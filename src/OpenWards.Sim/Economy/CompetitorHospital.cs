// The place of the computer opponents in the game follows CorsixTH CorsixTH/Lua/hospitals/ai_hospital.lua (AIHospital)
// and CorsixTH/Lua/world.lua (World:World, World:spawnPatient). What they do follows the community's description of the
// original game (the game logic specification's "Rival hospitals"), not CorsixTH, whose opponents do nothing.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Diseases;
using OpenWards.Sim.Import;
using OpenWards.Sim.People;
using OpenWards.Sim.Serialization;
using OpenWards.Sim.Staff;

namespace OpenWards.Sim.Economy;

/// <summary>
/// A computer opponent a level names (<c>computer[n].Playing</c> and <c>.Name</c> in the level files).
/// </summary>
/// <param name="Number">CorsixTH's competitor number: the level files' index plus one, which picks the default name in
/// <c>competitor_names</c>.</param>
/// <param name="Name">The name the level gives it, which in CorsixTH's base_config.lua every index has.</param>
public readonly record struct PlannedCompetitor(int Number, string Name);

/// <summary>
/// How a computer opponent plays: how skilled and quick it is at deciding, how many staff it keeps for its patients,
/// how much it cares for their comfort and how sure of a diagnosis it wants to be before it guesses the treatment.
/// </summary>
/// <param name="Skill">The spread of the time between its decisions (<c>rng % Skill</c>), at least 1.</param>
/// <param name="StaffLevels">Which column of the staff tables it hires by, 0 (fewest staff) to 9 (most).</param>
/// <param name="Speed">How long it takes over a decision, 1 to 100: the lower, the quicker.</param>
/// <param name="Comfort">Which divisor of its patients it keeps benches, drinks machines, plants and extinguishers for, 0 (fewest) to 4 (most).</param>
/// <param name="GuessAt">The percentage of a diagnosis from which it guesses the treatment rather than send the patient home.</param>
/// <remarks>
/// The specification names a sixth trait, <c>Luck</c>, which none of its rules use, so it is left out. The level files
/// give the opponents only names, so the traits are drawn when the level starts (<see cref="Draw"/>).
/// </remarks>
public readonly record struct CompetitorPersonality(int Skill, int StaffLevels, int Speed, int Comfort, int GuessAt)
{
    /// <summary>The traits of an opponent at random, within the ranges this game chose.</summary>
    internal static CompetitorPersonality Draw(SimRandom random) => new(
        Skill: random.NextInclusive(10, 40),
        StaffLevels: random.NextInclusive(2, 7),
        Speed: random.NextInclusive(30, 70),
        Comfort: random.NextInclusive(0, 4),
        GuessAt: random.NextInclusive(60, 90));

    /// <summary>The same traits held to what the rules can use.</summary>
    internal CompetitorPersonality Held() => new(
        Math.Max(1, Skill),
        Math.Clamp(StaffLevels, 0, 9),
        Math.Clamp(Speed, 1, 100),
        Math.Clamp(Comfort, 0, CompetitorHospital.ComfortDivisors.Length - 1),
        Math.Clamp(GuessAt, 0, 100));
}

/// <summary>What a computer opponent does next.</summary>
public enum CompetitorActionKind : byte
{
    /// <summary>Hire a member of staff: of the class given, or the first class it has too few of.</summary>
    Hire = 0,

    /// <summary>Build a room: the one given, or the first one missing that it can pay for.</summary>
    Build = 1,

    /// <summary>Buy objects: those given, or the benches, drinks machines, plants and extinguishers (and land) it lacks.</summary>
    Buy = 2,

    /// <summary>Borrow when short of money, or pay back when it has plenty.</summary>
    Loan = 3,

    /// <summary>Open the hospital to patients.</summary>
    Open = 4,
}

/// <summary>One action of a computer opponent's scenario.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Target">The staff class (as a number), room or object it is about; empty for the general ones.</param>
/// <param name="Count">How many objects to buy.</param>
public readonly record struct CompetitorAction(CompetitorActionKind Kind, string Target = "", int Count = 0);

/// <summary>A member of a computer opponent's staff, who has no place on the map: their class, skill and wage.</summary>
public readonly record struct CompetitorStaff(StaffClass Class, int Skill, int Wage);

/// <summary>
/// A computer opponent's hospital (CorsixTH's <c>AIHospital</c>), as the community's description of the original game has
/// it: it plays a scenario and has no map. Every so often it decides on an action and then carries it out: it hires
/// staff from the candidates every hospital hires from, builds rooms, buys benches and the like, buys land and borrows.
/// Its patients live through a quick simulation of their stay as they come (<see cref="Admit"/>), whose outcomes go into
/// the month they happen in and reach the hospital every five days. Its reputation, value and the year's raise of its
/// owner's salary go by the same rules as the player's.
/// </summary>
/// <remarks>
/// Departs from CorsixTH, whose opponents build nothing, hire no one and only count the patients sent to them. Departs
/// from the original as the specification describes it in three ways: an opponent is paid for treatments too (in the
/// original only for diagnoses, which leaves the opponents implausibly poor on the year's charts), its land is compared
/// with the player's every time it buys objects (the original never looks again), and it borrows and pays back by the
/// player's rules (which the specification leaves open). The traits are drawn at random (<see cref="CompetitorPersonality"/>),
/// rooms are built from the lists of diagnosis and treatment rooms in turn, and staff need no specialities.
/// </remarks>
public sealed class CompetitorHospital
{
    /// <summary>The longest name read back from a save.</summary>
    private const int MaxNameBytes = 64;

    /// <summary>The most staff, rooms, objects and months read back from a save.</summary>
    private const int MaxEntries = 1 << 16;

    /// <summary>The opponents decide once in this many ticks.</summary>
    public const int DecisionInterval = 25;

    /// <summary>How many failed actions wait to be tried again.</summary>
    public const int RetryPlaces = 8;

    /// <summary>The days of the simulated stay between two rooms, and between two transfers of the month's outcomes.</summary>
    public const int StepDays = 5;

    /// <summary>What a drink earns.</summary>
    public const int DrinkIncome = 20;

    /// <summary>One patient in this many buys a drink when there are machines enough.</summary>
    public const int DrinkChance = 5;

    /// <summary>The care divisor while there are fewer patients than <see cref="FewPatients"/>.</summary>
    public const int FewPatientsCare = 20;

    /// <summary>Below this many patients in the hospital the care divisor is <see cref="FewPatientsCare"/>.</summary>
    public const int FewPatients = 6;

    /// <summary>The lowest the chances of leaving and dying go as a patient's stay goes on.</summary>
    public const int LowestChance = 5;

    /// <summary>The owner's salary at the start, as the player's.</summary>
    public const long StartSalary = Hospital.StartPlayerSalary;

    /// <summary>The benches its scenario starts with.</summary>
    public const int StartBenches = 4;

    /// <summary>The objects it keeps one of for so many patients, by <see cref="CompetitorPersonality.Comfort"/>.</summary>
    internal static readonly int[] ComfortDivisors = [20, 15, 10, 7, 5];

    /// <summary>The objects it keeps for its patients' comfort, in the order it buys them.</summary>
    public static readonly IReadOnlyList<string> ComfortObjects = [DrinksMachine, Bench, Plant, Extinguisher];

    /// <summary>The diagnosis rooms it builds, in order.</summary>
    public static readonly IReadOnlyList<string> DiagnosisRooms =
        ["general_diag", "x_ray", "cardiogram", "scanner", "psych", "blood_machine", "ultrascan", "ward"];

    /// <summary>The treatment rooms it builds, in order.</summary>
    public static readonly IReadOnlyList<string> TreatmentRooms =
    [
        "pharmacy", "psych", "ward", "operating_theatre", "inflation", "electrolysis", "slack_tongue", "fracture_clinic",
        "hair_restoration", "jelly_vat", "decontamination", "dna_fixer",
    ];

    /// <summary>What it does first, once.</summary>
    public static readonly IReadOnlyList<CompetitorAction> StartScenario =
    [
        new(CompetitorActionKind.Hire, StaffClassName(StaffClass.Receptionist)),
        new(CompetitorActionKind.Buy, ReceptionDesk, 1),
        new(CompetitorActionKind.Buy, Bench, StartBenches),
        new(CompetitorActionKind.Build, PatientBehaviour.FirstRoom),
        new(CompetitorActionKind.Open),
        new(CompetitorActionKind.Hire, StaffClassName(StaffClass.Doctor)),
    ];

    /// <summary>What it does over and over after <see cref="StartScenario"/>.</summary>
    public static readonly IReadOnlyList<CompetitorAction> Cycle =
    [
        new(CompetitorActionKind.Hire),
        new(CompetitorActionKind.Build),
        new(CompetitorActionKind.Buy),
        new(CompetitorActionKind.Loan),
    ];

    private const string ReceptionDesk = "reception_desk";
    private const string Bench = "bench";
    private const string DrinksMachine = "drinks_machine";
    private const string Plant = "plant";
    private const string Extinguisher = "extinguisher";

    /// <summary>Staff of a class it keeps one of for so many patients, by <see cref="CompetitorPersonality.StaffLevels"/>.</summary>
    private static readonly int[] DoctorsAndNursesPer = [55, 45, 36, 28, 21, 15, 10, 6, 3, 1];

    private static readonly int[] HandymenPer = [10, 9, 8, 7, 6, 5, 4, 3, 2, 1];

    private static readonly int[] ReceptionistsPer = [40, 36, 32, 28, 24, 20, 16, 12, 8, 4];

    /// <summary>The decision and the four kinds of action take this long, in tenths of the opponent's pace.</summary>
    private const int DecideDelay = 33;

    private readonly List<CompetitorStaff> _staff = [];
    private readonly List<string> _rooms = [];
    private readonly SortedDictionary<string, int> _objects = new(StringComparer.Ordinal);
    private readonly List<CompetitorAction> _retries = [];
    private readonly List<MonthOutcomes> _months = [];

    /// <param name="number">CorsixTH's competitor number (the level files' index plus one).</param>
    /// <param name="name">Its name.</param>
    /// <param name="reputation">Its reputation at the start.</param>
    /// <param name="balance">Its money at the start.</param>
    /// <param name="tiles">The tiles of its plot, as big as the one the players start on.</param>
    /// <param name="personality">How it plays; by default in the middle of every range.</param>
    public CompetitorHospital(int number, string name, int reputation = Hospital.AverageReputation, long balance = 0, int tiles = 0, CompetitorPersonality? personality = null)
    {
        Number = number;
        Name = name;
        ReputationVisits = Hospital.StartReputationVisits;
        ReputationScore = (long)Math.Clamp(reputation, Hospital.MinReputation, Hospital.MaxReputation) * ReputationVisits;
        Balance = balance;
        Tiles = Math.Max(0, tiles);
        Personality = (personality ?? new CompetitorPersonality(25, 5, 50, 2, 75)).Held();
    }

    /// <summary>CorsixTH's competitor number (the level files' index plus one).</summary>
    public int Number { get; }

    /// <summary>Its name, as the level gives it.</summary>
    public string Name { get; }

    /// <summary>How it plays.</summary>
    public CompetitorPersonality Personality { get; private set; }

    /// <summary>Its reputation, from 0 to 1000, as the player's: <see cref="ReputationScore"/> over <see cref="ReputationVisits"/>. It is also its reputation for every disease.</summary>
    public int Reputation => Hospital.ReputationOf(ReputationScore, ReputationVisits);

    /// <summary>The points of its patients' outcomes times <see cref="Hospital.ReputationPerPoint"/>, as <see cref="Hospital.ReputationScore"/>.</summary>
    public long ReputationScore { get; private set; }

    /// <summary>Patients counted in its reputation, as <see cref="Hospital.ReputationVisits"/>.</summary>
    public int ReputationVisits { get; private set; }

    /// <summary>Patients sent to it so far, whether they came or not (CorsixTH's <c>spawn_attempts</c>).</summary>
    public long SpawnAttempts { get; internal set; }

    /// <summary>Patients so far (<c>num_visitors</c>).</summary>
    public int Visitors { get; internal set; }

    /// <summary>Patients this year (<c>num_visitors_ty</c>), reset at the end of each year.</summary>
    public int VisitorsThisYear { get; internal set; }

    /// <summary>Its money.</summary>
    public long Balance { get; private set; }

    /// <summary>What it has borrowed.</summary>
    public long Loan { get; private set; }

    /// <summary>Its value, as the player's (<see cref="HospitalValue"/>): land, equipment, rooms, reputation and 10,000.</summary>
    public long Value { get; private set; }

    /// <summary>Patients cured so far.</summary>
    public int Cured { get; private set; }

    /// <summary>Patients who died so far.</summary>
    public int Deaths { get; private set; }

    /// <summary>Patients who went home without a cure so far.</summary>
    public int NotCured { get; private set; }

    /// <summary>Patients cured this year, for the year's awards.</summary>
    public int CuredThisYear { get; private set; }

    /// <summary>Patients who died this year, for the year's awards.</summary>
    public int DeathsThisYear { get; private set; }

    /// <summary>New patients this month, for the shares of the town's patients.</summary>
    public int VisitorsThisMonth { get; private set; }

    /// <summary>Its share of the town's new patients last month, in percent (<see cref="YearEndAwards.OnEndMonth"/>).</summary>
    public int PopulationPercent { get; internal set; }

    /// <summary>Its owner's salary, the "Salary" of the year's charts.</summary>
    public long PlayerSalary { get; private set; } = StartSalary;

    /// <summary>The tiles of its plot.</summary>
    public int Tiles { get; private set; }

    /// <summary>Whether it has opened to patients.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Patients in the hospital now: those whose stay has not reached the hospital's books yet.</summary>
    public int PatientsInHospital { get; private set; }

    /// <summary>Its staff, in the order they were hired.</summary>
    public IReadOnlyList<CompetitorStaff> Staff => _staff;

    /// <summary>Its rooms, in the order they were built.</summary>
    public IReadOnlyList<string> Rooms => _rooms;

    /// <summary>Its objects outside the rooms' own, by type.</summary>
    public IReadOnlyDictionary<string, int> Objects => _objects;

    /// <summary>The failed actions waiting to be tried again, first first.</summary>
    public IReadOnlyList<CompetitorAction> Retries => _retries;

    /// <summary>The tick of its next decision or action.</summary>
    public long NextActionTick { get; private set; }

    /// <summary>The action decided on, carried out at <see cref="NextActionTick"/>; null while it is to decide.</summary>
    public CompetitorAction? Pending { get; private set; }

    /// <summary>How far its scenario has got: the actions of <see cref="StartScenario"/>, then of <see cref="Cycle"/> over and over.</summary>
    public int ScenarioStep { get; private set; }

    /// <summary>Whether the last decision was the scenario's, so that the next one tries again a failed action, if any.</summary>
    private bool _retryNext;

    /// <summary>Whether the last room it looked for was a diagnosis room, so that it looks for a treatment room next.</summary>
    private bool _treatmentNext;

    /// <summary>What its rooms' equipment cost, for its value.</summary>
    private long _equipmentValue;

    /// <summary>The wages of its staff in a month.</summary>
    public long Wages
    {
        get
        {
            long wages = 0;
            foreach (var member in _staff)
            {
                wages += member.Wage;
            }

            return wages;
        }
    }

    /// <summary>Sets the reputation, keeping the visits it weighs against (tests).</summary>
    internal void SetReputation(int reputation) =>
        ReputationScore = (long)Math.Clamp(reputation, Hospital.MinReputation, Hospital.MaxReputation) * ReputationVisits;

    /// <summary>Takes on a member of staff without paying for them (tests).</summary>
    internal void AddStaff(CompetitorStaff member) => _staff.Add(member);

    /// <summary>Builds a room without paying for it (tests).</summary>
    internal void AddRoom(string roomType) => _rooms.Add(roomType);

    /// <summary>Opens the hospital (tests).</summary>
    internal void Open() => IsOpen = true;

    /// <summary>Its staff of a class.</summary>
    public int StaffCount(StaffClass staffClass) => _staff.Count(member => member.Class == staffClass);

    /// <summary>The average skill of its staff of a class, 0 without any.</summary>
    public int AverageSkill(StaffClass staffClass)
    {
        long skill = 0;
        var count = 0;
        foreach (var member in _staff)
        {
            if (member.Class == staffClass)
            {
                skill += member.Skill;
                count++;
            }
        }

        return count == 0 ? 0 : (int)(skill / count);
    }

    /// <summary>How many objects of a type it has outside its rooms.</summary>
    public int ObjectCount(string objectType) => _objects.GetValueOrDefault(objectType);

    /// <summary>Whether it has a room of this type.</summary>
    public bool HasRoom(string roomType) => _rooms.Contains(roomType);

    /// <summary>
    /// The staff of a class it wants: one for every so many patients in the hospital, by its
    /// <see cref="CompetitorPersonality.StaffLevels"/> (55 down to 1 for doctors and nurses, 10 to 1 for handymen, 40 to 4
    /// for receptionists).
    /// </summary>
    public int WantedStaff(StaffClass staffClass)
    {
        var per = staffClass switch
        {
            StaffClass.Doctor or StaffClass.Nurse => DoctorsAndNursesPer,
            StaffClass.Handyman => HandymenPer,
            _ => ReceptionistsPer,
        };
        return PatientsInHospital / per[Personality.StaffLevels];
    }

    /// <summary>The objects of a comfort type it wants: one for every so many patients, by its <see cref="CompetitorPersonality.Comfort"/>.</summary>
    public int WantedObjects() => PatientsInHospital / ComfortDivisors[Personality.Comfort];

    /// <summary>A new opponent with the level's money, the players' plot and traits drawn for it.</summary>
    /// <param name="planned">The level's opponent.</param>
    /// <param name="rules">The level's settings.</param>
    /// <param name="random">The opponents' random numbers.</param>
    /// <param name="tiles">The tiles of the plot the players start on.</param>
    internal static CompetitorHospital Create(PlannedCompetitor planned, LevelRules rules, SimRandom random, int tiles)
    {
        var competitor = new CompetitorHospital(planned.Number, planned.Name, rules.StartReputation, rules.StartCash, tiles, CompetitorPersonality.Draw(random));
        competitor.NextActionTick = competitor.Delay(random, DecideDelay);
        return competitor;
    }

    /// <summary>
    /// Once every <see cref="DecisionInterval"/> ticks: when the time has come, it decides on its next action, or carries
    /// out the one it decided on. A decision takes the next action of its scenario, or every other time a failed one
    /// waiting to be tried again. An action that fails waits to be tried again, in one of <see cref="RetryPlaces"/>
    /// places. The time until the next is <c>⌊clamp(Speed − rng % Skill, 1, 100) × delay / 10⌋</c> ticks, the delay
    /// 33 for a decision and 25, 37, 10 and 50 for hiring, building, buying and borrowing.
    /// </summary>
    internal void Think(Simulation simulation)
    {
        if (simulation.Tick < NextActionTick)
        {
            return;
        }

        var random = simulation.CompetitorRandom;
        if (Pending is not { } action)
        {
            Pending = Decide();
            NextActionTick = simulation.Tick + Delay(random, DecideDelay);
            return;
        }

        if (!Carry(simulation, action) && _retries.Count < RetryPlaces && !_retries.Contains(action))
        {
            _retries.Add(action);
        }

        Pending = null;
        NextActionTick = simulation.Tick + Delay(random, ActionDelay(action.Kind));
    }

    private CompetitorAction Decide()
    {
        if (_retryNext && _retries.Count > 0)
        {
            _retryNext = false;
            var retry = _retries[0];
            _retries.RemoveAt(0);
            return retry;
        }

        _retryNext = true;
        var step = ScenarioStep;
        ScenarioStep = step + 1 < StartScenario.Count + Cycle.Count ? step + 1 : StartScenario.Count;
        return step < StartScenario.Count ? StartScenario[step] : Cycle[step - StartScenario.Count];
    }

    private static int ActionDelay(CompetitorActionKind kind) => kind switch
    {
        CompetitorActionKind.Hire => 25,
        CompetitorActionKind.Build => 37,
        CompetitorActionKind.Buy => 10,
        CompetitorActionKind.Loan => 50,
        _ => DecideDelay,
    };

    private int Delay(SimRandom random, int delay) =>
        Math.Clamp(Personality.Speed - random.Next(Personality.Skill), 1, 100) * delay / 10;

    /// <summary>Carries out an action; false when it could not.</summary>
    internal bool Carry(Simulation simulation, CompetitorAction action) => action.Kind switch
    {
        CompetitorActionKind.Hire => action.Target.Length == 0 ? HireWanted(simulation) : Hire(simulation, ParseClass(action.Target)),
        CompetitorActionKind.Build => action.Target.Length == 0 ? BuildNext(simulation) : Build(simulation, action.Target),
        CompetitorActionKind.Buy => action.Target.Length == 0 ? BuyComfort(simulation) : Buy(simulation, action.Target, action.Count),
        CompetitorActionKind.Loan => Borrow(simulation),
        CompetitorActionKind.Open => IsOpen = true,
        _ => false,
    };

    /// <summary>Hires one of the first class it has fewer of than it wants (doctors, nurses, handymen, receptionists).</summary>
    private bool HireWanted(Simulation simulation)
    {
        foreach (var staffClass in (StaffClass[])[StaffClass.Doctor, StaffClass.Nurse, StaffClass.Handyman, StaffClass.Receptionist])
        {
            if (StaffCount(staffClass) < WantedStaff(staffClass))
            {
                return Hire(simulation, staffClass);
            }
        }

        return true;
    }

    /// <summary>
    /// Hires the most skilled candidate of the class it can pay for, from the candidates every hospital hires from (the
    /// first of them at the same skill): 1.2 times the wage comes off its money, and the wage is paid at the end of every
    /// month.
    /// </summary>
    private bool Hire(Simulation simulation, StaffClass staffClass)
    {
        var candidates = simulation.AvailableStaff(staffClass);
        var best = -1;
        for (var i = 0; i < candidates.Count; i++)
        {
            if (Balance >= HireCost(candidates[i]) && (best < 0 || candidates[i].Skill > candidates[best].Skill))
            {
                best = i;
            }
        }

        if (best < 0)
        {
            return false;
        }

        var candidate = simulation.TakeAvailableStaff(staffClass, best);
        Balance -= HireCost(candidate);
        _staff.Add(new CompetitorStaff(staffClass, candidate.Skill, candidate.Wage));
        return true;
    }

    private static long HireCost(StaffProfile candidate) => (long)candidate.Wage * 6 / 5;

    /// <summary>
    /// Builds the first room it has not got that research has made available to the player and it can pay for: from the
    /// diagnosis rooms and the treatment rooms in turn, or from the other list when one has nothing to build.
    /// </summary>
    private bool BuildNext(Simulation simulation)
    {
        var first = _treatmentNext ? TreatmentRooms : DiagnosisRooms;
        var second = _treatmentNext ? DiagnosisRooms : TreatmentRooms;
        _treatmentNext = !_treatmentNext;
        var wanted = false;
        foreach (var list in (IReadOnlyList<string>[])[first, second])
        {
            foreach (var room in list)
            {
                if (HasRoom(room) || !simulation.Hospitals[0].IsRoomDiscovered(room))
                {
                    continue;
                }

                wanted = true;
                if (Build(simulation, room))
                {
                    return true;
                }
            }
        }

        return !wanted;
    }

    /// <summary>Builds a room, if it has the money: its cost and the objects it needs.</summary>
    private bool Build(Simulation simulation, string roomType)
    {
        if (HasRoom(roomType))
        {
            return true;
        }

        var room = simulation.Content.Rooms.FirstOrDefault(r => string.Equals(r.Id, roomType, StringComparison.Ordinal));
        long equipment = 0;
        long objects = 0;
        if (room is not null)
        {
            foreach (var (objectId, count) in LevelRules.Needed(room))
            {
                long cost = (long)count * ObjectCost(simulation, objectId);
                objects += cost;
                if (HospitalValue.Counts(objectId))
                {
                    equipment += cost;
                }
            }
        }

        var price = simulation.Rules.RoomCost(roomType) + objects;
        if (Balance < price)
        {
            return false;
        }

        Balance -= price;
        _equipmentValue += equipment;
        _rooms.Add(roomType);
        RefreshValue(simulation);
        return true;
    }

    /// <summary>Buys objects outside the rooms, if it has the money for all of them.</summary>
    private bool Buy(Simulation simulation, string objectType, int count)
    {
        var price = (long)Math.Max(0, count) * ObjectCost(simulation, objectType);
        if (count <= 0 || Balance < price)
        {
            return count <= 0;
        }

        Balance -= price;
        _objects[objectType] = ObjectCount(objectType) + count;
        return true;
    }

    /// <summary>
    /// Buys 1 to 4 of each comfort object it has fewer of than it wants, then land: as much as the player has more of, at
    /// the level's price per tile, if its money is above 0 and enough. False if it could not pay for something.
    /// </summary>
    private bool BuyComfort(Simulation simulation)
    {
        var random = simulation.CompetitorRandom;
        var paid = true;
        var wanted = WantedObjects();
        foreach (var objectType in ComfortObjects)
        {
            var have = ObjectCount(objectType);
            if (have < wanted)
            {
                paid &= Buy(simulation, objectType, Math.Min(random.NextInclusive(1, 4), wanted - have));
            }
        }

        var land = PlayerLand(simulation) - Tiles;
        if (land > 0)
        {
            var price = (long)land * simulation.Rules.LandCostPerTile;
            if (Balance > 0 && Balance >= price)
            {
                Balance -= price;
                Tiles += land;
                RefreshValue(simulation);
            }
            else
            {
                paid = false;
            }
        }

        return paid;
    }

    /// <summary>
    /// The player's rules of borrowing: when it is short of money it borrows <see cref="Hospital.LoanStep"/>s up to its
    /// limit (a third of its value and 10,000, at most 100,000); when it has plenty it pays a step back.
    /// </summary>
    private bool Borrow(Simulation simulation)
    {
        var limit = Math.Clamp((Value / 3) + Hospital.LoanAllowance, 0, Hospital.LoanCeiling);
        if (simulation.Rules.FreeBuild)
        {
            return true;
        }

        if (Balance < Hospital.LoanStep)
        {
            if (Loan + Hospital.LoanStep > limit)
            {
                return false;
            }

            while (Balance < Hospital.LoanStep && Loan + Hospital.LoanStep <= limit)
            {
                Loan += Hospital.LoanStep;
                Balance += Hospital.LoanStep;
            }
        }
        else if (Loan > 0 && Balance >= RepayAbove + Hospital.LoanStep)
        {
            Loan -= Hospital.LoanStep;
            Balance -= Hospital.LoanStep;
        }

        return true;
    }

    /// <summary>It pays a step of its loan back while that leaves it this much.</summary>
    public const int RepayAbove = 20_000;

    private static int ObjectCost(Simulation simulation, string objectType) =>
        simulation.Hospitals.Count > 0 ? simulation.Hospitals[0].Research.ObjectCost(simulation.Rules, objectType) : simulation.Rules.ObjectCost(objectType);

    /// <summary>The tiles the first player owns.</summary>
    private static int PlayerLand(Simulation simulation)
    {
        var map = simulation.Map;
        var land = 0;
        for (var parcel = 1; parcel < map.ParcelCount; parcel++)
        {
            if (map.GetParcelOwner(parcel) == 1)
            {
                land += map.ParcelTileCount(parcel);
            }
        }

        return land;
    }

    /// <summary>
    /// A patient sent to it, who lives through the stay at once, without a place on the map (the specification's "Rival's
    /// patient"):
    /// <list type="number">
    /// <item>The patients in the hospital grow by one, and the care divisor is worked out: 20 with fewer than 6, else
    /// <c>100 − ⌊P / (10 × (rooms + 1))⌋ − ⌊P / (10 × (staff + 1))⌋ − ⌊P / (10 × (benches + 1))⌋</c>, from 0 to 100.</item>
    /// <item>Without a GP's office the patient goes home.</item>
    /// <item>Then a room every five days: the GP's, then the disease's diagnosis rooms it has. Each charges its fee and
    /// diagnoses as for the player, with the average skill of its staff of the room's class instead of the doctor's
    /// score; with drinks machines enough a patient in five buys a drink. The patient leaves when <c>rng % N = 0</c> (N
    /// then one less, not below 5) and dies when <c>rng % health = 0</c> (health then one less, not below 5). At a full
    /// diagnosis the treatment follows.</item>
    /// <item>When the rooms run out first: below <see cref="CompetitorPersonality.GuessAt"/> percent of a diagnosis the
    /// patient goes home; otherwise dies when <c>rng % 100 &gt; percent</c>, else is treated.</item>
    /// <item>Without the treatment rooms the patient goes home. A drug fails, and the patient dies, when
    /// <c>rnd(0, 99) ≥</c> the player's effectiveness of the drug; other treatments always work.</item>
    /// <item>Everything (money, the reputation's points and visit, the outcome) goes into the month it happens in.</item>
    /// </list>
    /// </summary>
    internal void Admit(Simulation simulation, DiseaseDefinition? disease)
    {
        SpawnAttempts++;
        if (!IsOpen)
        {
            return;
        }

        Visitors++;
        VisitorsThisYear++;
        VisitorsThisMonth++;
        PatientsInHospital++;
        var random = simulation.CompetitorRandom;
        var rules = simulation.Rules.Diseases;
        var care = CareDivisor();
        var health = random.Triangular(200, PatientNeeds.MaxHealth);
        var hours = simulation.Hours;
        var step = 0;

        if (disease is null || !HasRoom(PatientBehaviour.FirstRoom))
        {
            Leave(hours, diagnosed: false);
            return;
        }

        var maxDifficulty = rules.MaxDiagDiff(disease.ExpertiseId);
        var difficulty = maxDifficulty >= 1 ? random.Triangular(1, maxDifficulty) : 0;
        var progress = 0;
        var drinks = ObjectCount(DrinksMachine);
        var enoughDrinks = drinks > 0 && drinks >= WantedObjects();
        var visits = new List<string> { PatientBehaviour.FirstRoom };
        visits.AddRange(disease.DiagnosisRooms.Where(room => !string.Equals(room, PatientBehaviour.FirstRoom, StringComparison.Ordinal) && HasRoom(room)));
        foreach (var room in visits)
        {
            step++;
            var at = hours + ((long)step * StepDays * Calendar.GameDate.HoursPerDay);
            var month = Month(at);
            month.Money += DiagnosisFee(room, rules);
            var bonus = string.Equals(room, PatientBehaviour.FirstRoom, StringComparison.Ordinal) ? 0 : DiagnosisBonuses.Bonus(disease.Id, room);
            var gain = AverageSkill(RoomStaff(room)) + 250 - random.Triangular(0, 500) + bonus - difficulty;
            if (gain > 0)
            {
                progress = Math.Min(progress + gain, PatientCare.FullDiagnosis);
            }

            if (enoughDrinks && random.Next(DrinkChance) == 0)
            {
                month.Money += DrinkIncome;
            }

            if (random.Next(care) == 0)
            {
                Leave(at, diagnosed: progress >= PatientCare.FullDiagnosis);
                return;
            }

            care = Math.Max(LowestChance, care - 1);
            if (random.Next(health) == 0)
            {
                Die(at, diagnosed: progress >= PatientCare.FullDiagnosis);
                return;
            }

            health = Math.Max(LowestChance, health - 1);
            if (progress >= PatientCare.FullDiagnosis)
            {
                break;
            }
        }

        var treatedAt = hours + ((long)(step + 1) * StepDays * Calendar.GameDate.HoursPerDay);
        var full = progress >= PatientCare.FullDiagnosis;
        if (!full)
        {
            var percent = progress * 100 / PatientCare.FullDiagnosis;
            if (percent < Personality.GuessAt)
            {
                Leave(treatedAt, diagnosed: false);
                return;
            }

            if (random.Next(100) > percent)
            {
                Die(treatedAt, diagnosed: false);
                return;
            }
        }

        if (disease.TreatmentRooms.Count == 0 || !disease.TreatmentRooms.All(HasRoom))
        {
            Leave(treatedAt, full);
            return;
        }

        if (Research.ResearchDepartment.IsDrugDisease(disease)
            && simulation.Hospitals.Count > 0
            && random.Triangular(0, 99) >= simulation.Hospitals[0].PeekCase(disease, rules).CureEffectiveness)
        {
            Die(treatedAt, full);
            return;
        }

        var outcome = Month(treatedAt);
        outcome.Money += ExpectedPrice(rules.CurePrice(disease));
        outcome.Points += (full ? 1 : 0) + 1;
        outcome.Visits++;
        outcome.Cured++;
        outcome.Leaving++;

    }

    /// <summary>The care divisor N of a new patient.</summary>
    private int CareDivisor()
    {
        var patients = PatientsInHospital;
        if (patients < FewPatients)
        {
            return FewPatientsCare;
        }

        var care = 100 - (patients / (10 * (_rooms.Count + 1))) - (patients / (10 * (_staff.Count + 1))) - (patients / (10 * (ObjectCount(Bench) + 1)));
        return Math.Clamp(care, LowestChance, 100);
    }

    /// <summary>The fee of a diagnosis room, at the price patients expect at its reputation.</summary>
    private int DiagnosisFee(string room, DiseaseRules rules) =>
        DiseaseCatalog.Find(DiseaseCatalog.DiagnosisPrefix + room) is { } diagnosis ? ExpectedPrice(rules.CurePrice(diagnosis)) : 0;

    /// <summary>As <see cref="Hospital.ExpectedPrice"/>: the start price, raised by a reputation above 500.</summary>
    private int ExpectedPrice(int start) => (int)Math.Max(start, (long)start * Reputation / Hospital.AverageReputation);

    /// <summary>Whose skill counts in a diagnosis room: a nurse's on the ward, else a doctor's.</summary>
    private static StaffClass RoomStaff(string room) =>
        string.Equals(room, "ward", StringComparison.Ordinal) ? StaffClass.Nurse : StaffClass.Doctor;

    private void Leave(long at, bool diagnosed)
    {
        var month = Month(at);
        month.Points += diagnosed ? 1 : 0;
        month.Visits++;
        month.NotCured++;
        month.Leaving++;
    }

    private void Die(long at, bool diagnosed)
    {
        var month = Month(at);
        month.Points += diagnosed ? 0 : -1;
        month.Visits++;
        month.Deaths++;
        month.Leaving++;
    }

    /// <summary>The outcomes of the month of the game that the hour falls in.</summary>
    private MonthOutcomes Month(long hours)
    {
        var month = Calendar.GameDate.FromHours(hours).MonthOfGame;
        foreach (var outcomes in _months)
        {
            if (outcomes.Month == month)
            {
                return outcomes;
            }
        }

        var added = new MonthOutcomes { Month = month };
        var index = _months.FindIndex(outcomes => outcomes.Month > month);
        _months.Insert(index < 0 ? _months.Count : index, added);
        return added;
    }

    /// <summary>
    /// Every five days and at the end of the month the outcomes of the month so far (and of any before it) reach the
    /// hospital: its money, reputation and patients.
    /// </summary>
    internal void Transfer(Simulation simulation, int monthOfGame)
    {
        while (_months.Count > 0 && _months[0].Month <= monthOfGame)
        {
            var outcomes = _months[0];
            _months.RemoveAt(0);
            Balance += outcomes.Money;
            ReputationScore += (long)outcomes.Points * Hospital.ReputationPerPoint;
            ReputationVisits += outcomes.Visits;
            Cured += outcomes.Cured;
            CuredThisYear += outcomes.Cured;
            Deaths += outcomes.Deaths;
            DeathsThisYear += outcomes.Deaths;
            NotCured += outcomes.NotCured;
            PatientsInHospital = Math.Max(0, PatientsInHospital - outcomes.Leaving);
        }

        RefreshValue(simulation);
    }

    /// <summary>The end of a month: the month's outcomes, then the staff's wages and the interest on the loan and overdraft, as the player's.</summary>
    internal void OnEndMonth(Simulation simulation, int monthOfGame)
    {
        Transfer(simulation, monthOfGame);
        Balance -= Wages;
        var rate = simulation.Rules.InterestRate;
        Balance -= rate * Loan / 120_000;
        if (Balance < 0)
        {
            Balance -= rate * -Balance * 2 / 120_000;
        }

        VisitorsThisMonth = 0;
        RefreshValue(simulation);
    }

    /// <summary>
    /// Its value as the player's (<see cref="HospitalValue"/>): its plot at the level's price per tile, its rooms'
    /// equipment, the base price of its rooms, 1,000 per 50 of reputation and 10,000.
    /// </summary>
    internal void RefreshValue(Simulation simulation)
    {
        var value = ((long)Tiles * simulation.Rules.LandCostPerTile) + _equipmentValue
            + ((long)Reputation / HospitalValue.ReputationStep * HospitalValue.ValuePerReputationStep) + HospitalValue.BaseValue;
        foreach (var room in _rooms)
        {
            value += simulation.Rules.RoomCost(room);
        }

        Value = Math.Max(0, value);
    }

    /// <summary>
    /// An opponent of an imported save of the original: its reputation over its patients, its money and what the year's
    /// charts show of it. What it has built is not in the import, so its scenario starts afresh.
    /// </summary>
    internal void Import(ImportedCompetitor state)
    {
        Visitors = Math.Max(0, state.Visitors);
        ReputationVisits = Hospital.StartReputationVisits + Visitors;
        ReputationScore = (long)Math.Clamp(state.Reputation, Hospital.MinReputation, Hospital.MaxReputation) * ReputationVisits;
        VisitorsThisYear = state.VisitorsThisYear is { } thisYear ? Math.Clamp(thisYear, 0, Visitors) : Visitors;
        SpawnAttempts = Visitors;
        Balance = state.Money ?? Balance;
        Value = Math.Max(0, state.Value ?? Value);
        Cured = Math.Max(0, state.Cured ?? Cured);
        Deaths = Math.Max(0, state.Deaths ?? Deaths);
        PlayerSalary = state.Salary is > 0 and var salary ? salary : PlayerSalary;
        if (state.Tiles is > 0 and var tiles)
        {
            Tiles = tiles;
        }
    }

    /// <summary>The year end: its owner's salary rises as the player's does (<see cref="Hospital.RaisedSalary(long, int, long, int)"/>), and the year's patients start afresh.</summary>
    internal void OnEndYear(int score, int scoreMaxIncrease)
    {
        VisitorsThisYear = 0;
        CuredThisYear = 0;
        DeathsThisYear = 0;
        PlayerSalary = Hospital.RaisedSalary(PlayerSalary, score, StartSalary, scoreMaxIncrease);
    }

    /// <summary>
    /// What a win criterion measures for the computer player, for its level score: reputation, balance less loan, the cured
    /// and killed shares of its visitors, cures and value; nothing for the rest.
    /// </summary>
    internal decimal? Measure(EndCriterion criterion) => criterion switch
    {
        EndCriterion.Reputation => Reputation,
        EndCriterion.Balance => Balance - Loan,
        EndCriterion.PercentageCured => Visitors > 0 ? Cured * 100m / Visitors : 0,
        EndCriterion.NumCured => Cured,
        EndCriterion.PercentageKilled => Visitors > 0 ? Deaths * 100m / Visitors : 0,
        EndCriterion.Value => Value,
        _ => null,
    };

    private static string StaffClassName(StaffClass staffClass) => ((int)staffClass).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static StaffClass ParseClass(string target) =>
        (StaffClass)Math.Clamp(int.Parse(target, System.Globalization.CultureInfo.InvariantCulture), 0, (int)StaffClass.Receptionist);

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Number);
        writer.WriteString(Name);
        writer.WriteInt64(SpawnAttempts);
        writer.WriteInt32(Visitors);
        writer.WriteInt32(VisitorsThisYear);
        writer.WriteInt64(ReputationScore);
        writer.WriteInt32(ReputationVisits);
        writer.WriteInt64(Balance);
        writer.WriteInt64(Loan);
        writer.WriteInt64(Value);
        writer.WriteInt32(Cured);
        writer.WriteInt32(Deaths);
        writer.WriteInt32(NotCured);
        writer.WriteInt64(PlayerSalary);
        writer.WriteInt32(Tiles);
        writer.WriteInt32(CuredThisYear);
        writer.WriteInt32(DeathsThisYear);
        writer.WriteInt32(VisitorsThisMonth);
        writer.WriteInt32(PopulationPercent);
        writer.WriteInt32(Personality.Skill);
        writer.WriteInt32(Personality.StaffLevels);
        writer.WriteInt32(Personality.Speed);
        writer.WriteInt32(Personality.Comfort);
        writer.WriteInt32(Personality.GuessAt);
        writer.WriteBool(IsOpen);
        writer.WriteInt32(PatientsInHospital);
        writer.WriteInt64(NextActionTick);
        writer.WriteInt32(ScenarioStep);
        writer.WriteBool(_retryNext);
        writer.WriteBool(_treatmentNext);
        writer.WriteInt64(_equipmentValue);
        writer.WriteBool(Pending is not null);
        if (Pending is { } pending)
        {
            WriteAction(writer, pending);
        }

        writer.WriteInt32(_retries.Count);
        foreach (var retry in _retries)
        {
            WriteAction(writer, retry);
        }

        writer.WriteInt32(_staff.Count);
        foreach (var member in _staff)
        {
            writer.WriteByte((byte)member.Class);
            writer.WriteInt32(member.Skill);
            writer.WriteInt32(member.Wage);
        }

        writer.WriteInt32(_rooms.Count);
        foreach (var room in _rooms)
        {
            writer.WriteString(room);
        }

        writer.WriteInt32(_objects.Count);
        foreach (var (objectType, count) in _objects)
        {
            writer.WriteString(objectType);
            writer.WriteInt32(count);
        }

        writer.WriteInt32(_months.Count);
        foreach (var month in _months)
        {
            writer.WriteInt32(month.Month);
            writer.WriteInt64(month.Money);
            writer.WriteInt32(month.Points);
            writer.WriteInt32(month.Visits);
            writer.WriteInt32(month.Cured);
            writer.WriteInt32(month.Deaths);
            writer.WriteInt32(month.NotCured);
            writer.WriteInt32(month.Leaving);
        }
    }

    private static void WriteAction(SimWriter writer, CompetitorAction action)
    {
        writer.WriteByte((byte)action.Kind);
        writer.WriteString(action.Target);
        writer.WriteInt32(action.Count);
    }

    private static CompetitorAction ReadAction(SimReader reader) =>
        new((CompetitorActionKind)Math.Min(reader.ReadByte(), (byte)CompetitorActionKind.Open), reader.ReadString(MaxNameBytes), reader.ReadInt32());

    /// <param name="reader">The save.</param>
    /// <param name="startReputation">The level's starting reputation, for a game saved before opponents had one.</param>
    /// <param name="startCash">The level's starting money, for a game saved before opponents had any.</param>
    /// <param name="tiles">The tiles of the plot the players start on, for a game saved before opponents had a plot.</param>
    internal static CompetitorHospital Read(SimReader reader, int startReputation, long startCash, int tiles)
    {
        var number = reader.ReadInt32();
        var name = reader.ReadString(MaxNameBytes);
        var spawnAttempts = reader.ReadInt64();
        var visitors = reader.ReadInt32();
        var visitorsThisYear = reader.ReadInt32();
        if (reader.Version is 0 or >= 117)
        {
            return ReadScenario(reader, number, name, spawnAttempts, visitors, visitorsThisYear);
        }

        // Before version 117 an opponent was a model of its reputation and the year's charts; it keeps what the charts
        // showed, and its scenario starts now, as after an import.
        var withReputation = reader.Version >= 103;
        var reputation = withReputation ? reader.ReadInt32() : startReputation;
        if (withReputation)
        {
            _ = reader.ReadInt32(); // the standing
            _ = reader.ReadInt32(); // the start's reputation
        }

        var competitor = new CompetitorHospital(number, name, reputation, startCash, tiles)
        {
            SpawnAttempts = spawnAttempts,
            Visitors = visitors,
            VisitorsThisYear = visitorsThisYear,
        };
        competitor.ReputationVisits = Hospital.StartReputationVisits + Math.Max(0, visitors);
        competitor.ReputationScore = (long)Math.Clamp(reputation, Hospital.MinReputation, Hospital.MaxReputation) * competitor.ReputationVisits;

        if (reader.Version >= 104)
        {
            competitor.Balance = reader.ReadInt64();
            competitor.Value = reader.ReadInt64();
            competitor.Cured = reader.ReadInt32();
            competitor.Deaths = reader.ReadInt32();
            _ = reader.ReadInt32(); // the month's cures
            competitor.PlayerSalary = reader.ReadInt64();
            competitor.Tiles = Math.Max(0, reader.ReadInt32());
            _ = reader.ReadInt32(); // the shares of a patient cured and dead
            _ = reader.ReadInt32();
        }

        if (reader.Version >= 113)
        {
            competitor.CuredThisYear = Math.Max(0, reader.ReadInt32());
            competitor.DeathsThisYear = Math.Max(0, reader.ReadInt32());
            competitor.VisitorsThisMonth = Math.Max(0, reader.ReadInt32());
            competitor.PopulationPercent = Math.Clamp(reader.ReadInt32(), 0, 100);
        }

        return competitor;
    }

    private static CompetitorHospital ReadScenario(SimReader reader, int number, string name, long spawnAttempts, int visitors, int visitorsThisYear)
    {
        var competitor = new CompetitorHospital(number, name)
        {
            SpawnAttempts = spawnAttempts,
            Visitors = visitors,
            VisitorsThisYear = visitorsThisYear,
        };
        competitor.ReputationScore = reader.ReadInt64();
        competitor.ReputationVisits = Math.Max(1, reader.ReadInt32());
        competitor.Balance = reader.ReadInt64();
        competitor.Loan = Math.Max(0, reader.ReadInt64());
        competitor.Value = Math.Max(0, reader.ReadInt64());
        competitor.Cured = Math.Max(0, reader.ReadInt32());
        competitor.Deaths = Math.Max(0, reader.ReadInt32());
        competitor.NotCured = Math.Max(0, reader.ReadInt32());
        competitor.PlayerSalary = reader.ReadInt64();
        competitor.Tiles = Math.Max(0, reader.ReadInt32());
        competitor.CuredThisYear = Math.Max(0, reader.ReadInt32());
        competitor.DeathsThisYear = Math.Max(0, reader.ReadInt32());
        competitor.VisitorsThisMonth = Math.Max(0, reader.ReadInt32());
        competitor.PopulationPercent = Math.Clamp(reader.ReadInt32(), 0, 100);
        competitor.Personality = new CompetitorPersonality(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()).Held();
        competitor.IsOpen = reader.ReadBool();
        competitor.PatientsInHospital = Math.Max(0, reader.ReadInt32());
        competitor.NextActionTick = reader.ReadInt64();
        competitor.ScenarioStep = Math.Clamp(reader.ReadInt32(), 0, StartScenario.Count + Cycle.Count - 1);
        competitor._retryNext = reader.ReadBool();
        competitor._treatmentNext = reader.ReadBool();
        competitor._equipmentValue = Math.Max(0, reader.ReadInt64());
        competitor.Pending = reader.ReadBool() ? ReadAction(reader) : null;
        var retries = reader.ReadCount(RetryPlaces);
        for (var i = 0; i < retries; i++)
        {
            competitor._retries.Add(ReadAction(reader));
        }

        var staff = reader.ReadCount(MaxEntries);
        for (var i = 0; i < staff; i++)
        {
            var staffClass = (StaffClass)Math.Min(reader.ReadByte(), (byte)StaffClass.Receptionist);
            competitor._staff.Add(new CompetitorStaff(staffClass, reader.ReadInt32(), reader.ReadInt32()));
        }

        var rooms = reader.ReadCount(MaxEntries);
        for (var i = 0; i < rooms; i++)
        {
            competitor._rooms.Add(reader.ReadString(MaxNameBytes));
        }

        var objects = reader.ReadCount(MaxEntries);
        for (var i = 0; i < objects; i++)
        {
            var objectType = reader.ReadString(MaxNameBytes);
            competitor._objects[objectType] = Math.Max(0, reader.ReadInt32());
        }

        var months = reader.ReadCount(MaxEntries);
        for (var i = 0; i < months; i++)
        {
            competitor._months.Add(new MonthOutcomes
            {
                Month = reader.ReadInt32(),
                Money = reader.ReadInt64(),
                Points = reader.ReadInt32(),
                Visits = reader.ReadInt32(),
                Cured = reader.ReadInt32(),
                Deaths = reader.ReadInt32(),
                NotCured = reader.ReadInt32(),
                Leaving = reader.ReadInt32(),
            });
        }

        return competitor;
    }

    /// <summary>What happened to an opponent's patients in a month of the game, until it reaches the hospital.</summary>
    private sealed class MonthOutcomes
    {
        public int Month { get; init; }

        public long Money { get; set; }

        public int Points { get; set; }

        public int Visits { get; set; }

        public int Cured { get; set; }

        public int Deaths { get; set; }

        public int NotCured { get; set; }

        public int Leaving { get; set; }
    }
}
