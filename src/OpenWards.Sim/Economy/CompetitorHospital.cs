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
/// A computer opponent a level names (<c>computer[i].Playing</c> 1 in the level files), with its name and traits.
/// </summary>
/// <param name="Number">The level files' index plus one.</param>
/// <param name="Name">Its name: the level's <c>computer[i].Name</c>, else the i-th of the original's names.</param>
/// <param name="Personality">Its traits, from <c>computer[i]</c>; the defaults where the level does not say.</param>
public readonly record struct PlannedCompetitor(int Number, string Name, CompetitorPersonality? Personality = null);

/// <summary>
/// How a computer opponent plays, as the level files give it (<c>computer[i].Skill</c> and so on): the difficulty's
/// base file has a table of twelve, which a level's own file replaces for the three that play.
/// </summary>
/// <param name="Skill">The spread of the time between its actions (<c>rng % Skill</c>), 1 to 5.</param>
/// <param name="StaffLevels">Which column of the staff tables it hires by, 0 (fewest staff) to 9 (most).</param>
/// <param name="Speed">How long it takes over an action, 1 to 100: the lower, the quicker.</param>
/// <param name="Comfort">Which column of the comfort objects' divisors it buys by, 0 (fewest) to 9 (most).</param>
/// <param name="GuessAt">The percentage of a diagnosis from which it guesses the treatment rather than send the patient home.</param>
/// <remarks>The level files' <c>Luck</c> is used by none of the rules, so it is not kept.</remarks>
public readonly record struct CompetitorPersonality(int Skill, int StaffLevels, int Speed, int Comfort, int GuessAt)
{
    /// <summary>The traits of an opponent whose level does not give them.</summary>
    public static CompetitorPersonality Default { get; } = new(3, 5, 50, 5, 75);

    /// <summary>The same traits held to what the rules can use.</summary>
    internal CompetitorPersonality Held() => new(
        Math.Max(1, Skill),
        Math.Clamp(StaffLevels, 0, 9),
        Math.Clamp(Speed, 1, 100),
        Math.Clamp(Comfort, 0, 9),
        Math.Clamp(GuessAt, 0, 100));
}

/// <summary>What a computer opponent does.</summary>
public enum CompetitorActionKind : byte
{
    /// <summary>Hire a member of staff of a class, with an ability if one is asked for.</summary>
    Hire = 0,

    /// <summary>Build a room.</summary>
    Build = 1,

    /// <summary>Buy objects, one at a time.</summary>
    Buy = 2,

    /// <summary>The request for a loan, which takes its time and does nothing.</summary>
    Loan = 3,
}

/// <summary>An action of a computer opponent, or a task waiting in its queue (a hire for a room or a loan request).</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Target">The staff class (as a number), room or object it is about.</param>
/// <param name="Count">How many objects are left to buy.</param>
/// <param name="Ability">For a hire, the ability the candidate must have, if any.</param>
/// <param name="Slot">For a task from the queue, its place there; otherwise -1.</param>
/// <param name="FromStart">Whether the start of the scenario gave it, which lengthens its wait.</param>
public readonly record struct CompetitorAction(CompetitorActionKind Kind, string Target = "", int Count = 0, Specialism? Ability = null, int Slot = -1, bool FromStart = false)
{
    /// <summary>A hire of the class.</summary>
    public static CompetitorAction HireOf(StaffClass staffClass, Specialism? ability = null) =>
        new(CompetitorActionKind.Hire, ((int)staffClass).ToString(System.Globalization.CultureInfo.InvariantCulture), Ability: ability);

    /// <summary>The class of a hire.</summary>
    public StaffClass StaffClass =>
        (StaffClass)Math.Clamp(int.TryParse(Target, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0, 0, (int)StaffClass.Receptionist);
}

/// <summary>A member of a computer opponent's staff, who has no place on the map: their class, skill and wage.</summary>
public readonly record struct CompetitorStaff(StaffClass Class, int Skill, int Wage);

/// <summary>
/// A computer opponent's hospital (CorsixTH's <c>AIHospital</c>), as the community's description of the original game has
/// it: it plays a scenario and has no map. Every so often it decides on an action and, after a while, carries it out: it
/// hires staff from the candidates every hospital hires from, builds rooms and buys benches and the like. Its patients live
/// through a quick simulation of their stay as they come (<see cref="Admit"/>), whose outcomes go into the month they
/// happen in and reach the hospital every five days. Its reputation, value and the year's raise of its owner's salary go
/// by the same rules as the player's.
/// </summary>
/// <remarks>
/// Departs from CorsixTH, whose opponents build nothing, hire no one and only count the patients sent to them. Departs
/// from the original as the specification describes it in two ways: an opponent is paid for treatments too (in the
/// original only for diagnoses, which leaves the opponents implausibly poor on the year's charts), and an ordinary hire
/// looks for no ability (in the original it keeps the ability the last hire for a room asked for, which can leave an
/// opponent without handymen for years). It never borrows and never buys land, as in the original.
/// </remarks>
public sealed class CompetitorHospital
{
    /// <summary>The longest name read back from a save.</summary>
    private const int MaxNameBytes = 64;

    /// <summary>The most staff, rooms, objects and months read back from a save.</summary>
    private const int MaxEntries = 1 << 16;

    /// <summary>The opponents act once in this many ticks, and a decision that gave nothing to do waits this long.</summary>
    public const int DecisionInterval = 25;

    /// <summary>The places of the queue of tasks.</summary>
    public const int QueuePlaces = 8;

    /// <summary>The days of the simulated stay between two rooms, and between two transfers of the month's outcomes.</summary>
    public const int StepDays = 5;

    /// <summary>What a drink earns.</summary>
    public const int DrinkIncome = 20;

    /// <summary>One patient in this many buys a drink when there are machines enough.</summary>
    public const int DrinkChance = 5;

    /// <summary>A drinks machine is enough for fewer than this many patients in the hospital.</summary>
    public const int DrinksPerPatients = 10;

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

    /// <summary>The most objects of a type bought for one step of the scenario.</summary>
    public const int MostObjects = 4;

    /// <summary>The steps of the start of the scenario; the cycle comes after them.</summary>
    public const int StartSteps = 9;

    /// <summary>The steps of the cycle: staff, a room, objects and the loan.</summary>
    public const int CycleSteps = 4;

    /// <summary>The diagnosis rooms it builds, in order.</summary>
    public static readonly IReadOnlyList<string> DiagnosisRooms =
        ["general_diag", "x_ray", "cardiogram", "scanner", "psych", "blood_machine", "ultrascan", "ward"];

    /// <summary>The treatment rooms it builds, in order, once there is no diagnosis room left to build.</summary>
    public static readonly IReadOnlyList<string> TreatmentRooms =
    [
        "pharmacy", "psych", "ward", "operating_theatre", "inflation", "electrolysis", "slack_tongue", "fracture_clinic",
        "hair_restoration", "jelly_vat", "decontamination", "dna_fixer",
    ];

    /// <summary>The objects it keeps for its patients, in the order it looks at them, each one for so many patients by <see cref="CompetitorPersonality.Comfort"/>.</summary>
    public static readonly IReadOnlyList<(string ObjectType, int[] PatientsPer)> ComfortObjects =
    [
        (DrinksMachine, [10, 9, 8, 7, 6, 5, 4, 3, 2, 1]),
        (Bench, [5, 4, 4, 3, 3, 2, 2, 2, 1, 1]),
        (Plant, [32, 28, 24, 20, 16, 10, 7, 4, 2, 1]),
        (Extinguisher, [32, 28, 24, 20, 16, 10, 7, 4, 2, 1]),
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

    /// <summary>The classes it looks for staff it lacks in, in order.</summary>
    private static readonly StaffClass[] StaffOrder = [StaffClass.Nurse, StaffClass.Doctor, StaffClass.Handyman, StaffClass.Receptionist];

    /// <summary>The wait after an action, before the next decision, in tenths of the opponent's pace.</summary>
    private const int DecideDelay = 33;

    private readonly List<CompetitorStaff> _staff = [];
    private readonly List<string> _rooms = [];
    private readonly SortedDictionary<string, int> _objects = new(StringComparer.Ordinal);
    private readonly CompetitorAction?[] _queue = new CompetitorAction?[QueuePlaces];
    private readonly List<MonthOutcomes> _months = [];

    /// <param name="number">The level files' index plus one.</param>
    /// <param name="name">Its name.</param>
    /// <param name="reputation">Its reputation at the start.</param>
    /// <param name="balance">Its money at the start.</param>
    /// <param name="tiles">The tiles of its plot, as big as the one the players start on.</param>
    /// <param name="personality">How it plays; by default <see cref="CompetitorPersonality.Default"/>.</param>
    public CompetitorHospital(int number, string name, int reputation = Hospital.AverageReputation, long balance = 0, int tiles = 0, CompetitorPersonality? personality = null)
    {
        Number = number;
        Name = name;
        ReputationVisits = Hospital.StartReputationVisits;
        ReputationScore = (long)Math.Clamp(reputation, Hospital.MinReputation, Hospital.MaxReputation) * ReputationVisits;
        Balance = balance;
        Tiles = Math.Max(0, tiles);
        Personality = (personality ?? CompetitorPersonality.Default).Held();
    }

    /// <summary>The level files' index plus one.</summary>
    public int Number { get; }

    /// <summary>Its name.</summary>
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

    /// <summary>Its money; it never borrows.</summary>
    public long Balance { get; private set; }

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

    /// <summary>The tiles of its plot, as big as the players' at the start; it buys no more.</summary>
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

    /// <summary>The queue's places, empty or with a task: a hire for a room or a loan request.</summary>
    public IReadOnlyList<CompetitorAction?> Queue => _queue;

    /// <summary>The tick of its next decision or action.</summary>
    public long NextActionTick { get; private set; }

    /// <summary>The action decided on, carried out at <see cref="NextActionTick"/>; null while it is to decide.</summary>
    public CompetitorAction? Pending { get; private set; }

    /// <summary>How far its scenario has got: the <see cref="StartSteps"/> steps of the start, then the <see cref="CycleSteps"/> of the cycle over and over.</summary>
    public int ScenarioStep { get; private set; }

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

    /// <summary>Puts the scenario at a step (tests).</summary>
    internal void SetScenarioStep(int step) => ScenarioStep = Math.Clamp(step, 0, StartSteps + CycleSteps - 1);

    /// <summary>Counts a patient in the hospital without a stay (tests).</summary>
    internal void AddPatientsInHospital(int patients) => PatientsInHospital += patients;

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
    public int WantedObjects(string objectType)
    {
        foreach (var (type, per) in ComfortObjects)
        {
            if (string.Equals(type, objectType, StringComparison.Ordinal))
            {
                return PatientsInHospital / per[Personality.Comfort];
            }
        }

        return 0;
    }

    /// <summary>A new opponent with the level's money, the players' plot and the level's traits for it.</summary>
    /// <param name="planned">The level's opponent.</param>
    /// <param name="rules">The level's settings.</param>
    /// <param name="tiles">The tiles of the plot the players start on.</param>
    internal static CompetitorHospital Create(PlannedCompetitor planned, LevelRules rules, int tiles) =>
        new(planned.Number, planned.Name, rules.StartReputation, rules.StartCash, tiles, planned.Personality);

    /// <summary>
    /// Once every <see cref="DecisionInterval"/> ticks, when the time has come: a decision takes the scenario's next step,
    /// which may give it an action; the action is carried out after a wait by its kind, and the next decision comes 33
    /// tenths of its pace later. A step that gives nothing to do is followed by the next one in
    /// <see cref="DecisionInterval"/> ticks. A wait is <c>⌊clamp(Speed − rng % Skill, 1, 100) × delay / 10⌋</c> ticks, with
    /// another <c>rng % 5</c> before the clamp for an action of the start; the delay is 25 for a hire, 37 for a room, 10
    /// for an object and 50 for a loan request. Objects are bought one at a time, each an action of its own.
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
            if (Decide(simulation) is { } decided)
            {
                Pending = decided;
                NextActionTick = simulation.Tick + Delay(random, ActionDelay(decided.Kind), decided.FromStart);
            }
            else
            {
                NextActionTick = simulation.Tick + DecisionInterval;
            }

            return;
        }

        Carry(simulation, action);
        if (action is { Kind: CompetitorActionKind.Buy, Count: > 1 })
        {
            Pending = action with { Count = action.Count - 1 };
            NextActionTick = simulation.Tick + Delay(random, ActionDelay(action.Kind), action.FromStart);
            return;
        }

        Pending = null;
        NextActionTick = simulation.Tick + Delay(random, DecideDelay, fromStart: false);
    }

    /// <summary>
    /// The scenario's next step. The start: a receptionist, the reception desk, four benches, the receptionist again if
    /// that hire failed, the GP's office, opening, a doctor, the first diagnosis room it can build, then until it has
    /// both a receptionist and a doctor the hire of whichever is missing. Then over and over: staff, a room, objects and
    /// the loan.
    /// </summary>
    internal CompetitorAction? Decide(Simulation simulation)
    {
        var step = ScenarioStep;
        if (step < StartSteps)
        {
            if (step != StartSteps - 1)
            {
                ScenarioStep++;
            }

            return step switch
            {
                0 => Start(CompetitorAction.HireOf(StaffClass.Receptionist)),
                1 => Start(new(CompetitorActionKind.Buy, ReceptionDesk, 1)),
                2 => Start(new(CompetitorActionKind.Buy, Bench, StartBenches)),
                3 => StaffCount(StaffClass.Receptionist) == 0 ? Start(CompetitorAction.HireOf(StaffClass.Receptionist)) : null,
                4 => Start(new(CompetitorActionKind.Build, PatientBehaviour.FirstRoom)),
                5 => OpenHospital(),
                6 => Start(CompetitorAction.HireOf(StaffClass.Doctor)),
                7 => ChooseRoom(simulation, DiagnosisRooms, forTheCycle: false) is { } room ? Start(room) : null,
                _ => WaitForStartStaff(),
            };
        }

        ScenarioStep = StartSteps + ((step - StartSteps + 1) % CycleSteps);
        return (step - StartSteps) switch
        {
            0 => ChooseStaff(),
            1 => ChooseRoom(simulation, DiagnosisRooms, forTheCycle: true) ?? (NothingToBuild(simulation, DiagnosisRooms) ? ChooseRoom(simulation, TreatmentRooms, forTheCycle: true) : null),
            2 => ChooseObjects(),
            _ => TakeQueued(CompetitorActionKind.Loan),
        };

        static CompetitorAction Start(CompetitorAction action) => action with { FromStart = true };
    }

    private CompetitorAction? OpenHospital()
    {
        IsOpen = true;
        return null;
    }

    private CompetitorAction? WaitForStartStaff()
    {
        if (StaffCount(StaffClass.Receptionist) == 0)
        {
            return CompetitorAction.HireOf(StaffClass.Receptionist) with { FromStart = true };
        }

        if (StaffCount(StaffClass.Doctor) == 0)
        {
            return CompetitorAction.HireOf(StaffClass.Doctor) with { FromStart = true };
        }

        ScenarioStep = StartSteps;
        return null;
    }

    /// <summary>
    /// The staff step: a hire for a room from the queue (the one in the lowest place), else one of the first class it has
    /// fewer of than it wants (nurses, doctors, handymen, receptionists).
    /// </summary>
    private CompetitorAction? ChooseStaff()
    {
        if (TakeQueued(CompetitorActionKind.Hire) is { } queued)
        {
            return queued;
        }

        foreach (var staffClass in StaffOrder)
        {
            if (StaffCount(staffClass) < WantedStaff(staffClass))
            {
                return CompetitorAction.HireOf(staffClass);
            }
        }

        return null;
    }

    /// <summary>The task of the kind in the lowest place of the queue, as an action that knows its place.</summary>
    private CompetitorAction? TakeQueued(CompetitorActionKind kind)
    {
        for (var slot = 0; slot < QueuePlaces; slot++)
        {
            if (_queue[slot] is { } task && task.Kind == kind)
            {
                if (kind == CompetitorActionKind.Loan)
                {
                    _queue[slot] = null;
                    return task with { Slot = -1 };
                }

                return task with { Slot = slot };
            }
        }

        return null;
    }

    /// <summary>Puts a task in the lowest free place of the queue; dropped when the queue is full.</summary>
    private void Enqueue(CompetitorAction task)
    {
        for (var slot = 0; slot < QueuePlaces; slot++)
        {
            if (_queue[slot] is null)
            {
                _queue[slot] = task with { Slot = -1, FromStart = false };
                return;
            }
        }
    }

    /// <summary>The rooms of a list it has not got whose objects research has made available to the player.</summary>
    private IEnumerable<string> Buildable(Simulation simulation, IReadOnlyList<string> rooms) =>
        rooms.Where(room => !HasRoom(room) && simulation.Hospitals.Count > 0 && simulation.Hospitals[0].IsRoomDiscovered(room));

    private bool NothingToBuild(Simulation simulation, IReadOnlyList<string> rooms) => !Buildable(simulation, rooms).Any();

    /// <summary>
    /// The first room of the list it has not got, can build and can pay for. In the cycle the hire for it goes into the
    /// queue (a nurse for the pharmacy, ward and fracture clinic, a psychiatrist for psychiatry, a surgeon for the operating
    /// theatre, any doctor for the rest); and when there were rooms to build but all too dear, a loan request does.
    /// </summary>
    private CompetitorAction? ChooseRoom(Simulation simulation, IReadOnlyList<string> rooms, bool forTheCycle)
    {
        var any = false;
        foreach (var room in Buildable(simulation, rooms))
        {
            any = true;
            if (RoomPrice(simulation, room).Price > Balance)
            {
                continue;
            }

            if (forTheCycle)
            {
                Enqueue(room switch
                {
                    "pharmacy" or "ward" or "fracture_clinic" => CompetitorAction.HireOf(StaffClass.Nurse),
                    "psych" => CompetitorAction.HireOf(StaffClass.Doctor, Specialism.Psychiatrist),
                    "operating_theatre" => CompetitorAction.HireOf(StaffClass.Doctor, Specialism.Surgeon),
                    _ => CompetitorAction.HireOf(StaffClass.Doctor),
                });
            }

            return new CompetitorAction(CompetitorActionKind.Build, room);
        }

        if (any && forTheCycle)
        {
            Enqueue(new CompetitorAction(CompetitorActionKind.Loan));
        }

        return null;
    }

    /// <summary>The first comfort object it has fewer of than it wants: as many as it lacks, at most four.</summary>
    private CompetitorAction? ChooseObjects()
    {
        foreach (var (objectType, _) in ComfortObjects)
        {
            var lacking = WantedObjects(objectType) - ObjectCount(objectType);
            if (lacking > 0)
            {
                return new CompetitorAction(CompetitorActionKind.Buy, objectType, Math.Min(MostObjects, lacking));
            }
        }

        return null;
    }

    private static int ActionDelay(CompetitorActionKind kind) => kind switch
    {
        CompetitorActionKind.Hire => 25,
        CompetitorActionKind.Build => 37,
        CompetitorActionKind.Buy => 10,
        _ => 50,
    };

    private int Delay(SimRandom random, int delay, bool fromStart)
    {
        var pace = Personality.Speed - random.Next(Personality.Skill);
        if (fromStart)
        {
            pace += random.Next(5);
        }

        return Math.Clamp(pace, 1, 100) * delay / 10;
    }

    /// <summary>Carries out an action; false when it could not.</summary>
    internal bool Carry(Simulation simulation, CompetitorAction action)
    {
        switch (action.Kind)
        {
            case CompetitorActionKind.Hire:
                var hired = Hire(simulation, action.StaffClass, action.Ability);
                if (action.Slot is >= 0 and < QueuePlaces)
                {
                    // A hire from the queue leaves it when it succeeds, and moves to the free place furthest back when it fails.
                    var task = _queue[action.Slot];
                    _queue[action.Slot] = null;
                    if (!hired && task is not null)
                    {
                        for (var slot = QueuePlaces - 1; slot >= 0; slot--)
                        {
                            if (_queue[slot] is null)
                            {
                                _queue[slot] = task;
                                break;
                            }
                        }
                    }
                }

                return hired;
            case CompetitorActionKind.Build:
                return Build(simulation, action.Target);
            case CompetitorActionKind.Buy:
                return BuyOne(simulation, action.Target);
            default:
                return true;
        }
    }

    /// <summary>
    /// Hires the first candidate of the class, with the ability if one is asked for, in the order of the candidates every
    /// hospital hires from: 1.2 times the wage comes off its money, and the wage is paid at the end of every month. With
    /// less money than that, or none, the hire fails; it tries no other candidate.
    /// </summary>
    private bool Hire(Simulation simulation, StaffClass staffClass, Specialism? ability)
    {
        var candidates = simulation.AvailableStaff(staffClass);
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (ability is { } needed && !candidate.Has(needed))
            {
                continue;
            }

            var cost = (long)candidate.Wage * 6 / 5;
            if (Balance < 1 || Balance < cost)
            {
                return false;
            }

            simulation.TakeAvailableStaff(staffClass, i);
            Balance -= cost;
            _staff.Add(new CompetitorStaff(staffClass, candidate.Skill, candidate.Wage));
            return true;
        }

        return false;
    }

    /// <summary>What a room costs it: the room and the objects it needs, and of those what counts towards its value.</summary>
    private static (long Price, long Equipment) RoomPrice(Simulation simulation, string roomType)
    {
        var room = simulation.Content.Rooms.FirstOrDefault(r => string.Equals(r.Id, roomType, StringComparison.Ordinal));
        long equipment = 0;
        long objects = 0;
        if (room is not null)
        {
            foreach (var (objectId, count) in LevelRules.Needed(room))
            {
                var cost = (long)count * ObjectCost(simulation, objectId);
                objects += cost;
                if (HospitalValue.Counts(objectId))
                {
                    equipment += cost;
                }
            }
        }

        return (simulation.Rules.RoomCost(roomType) + objects, equipment);
    }

    /// <summary>Builds a room, if it has the money: its cost and the objects it needs.</summary>
    private bool Build(Simulation simulation, string roomType)
    {
        if (HasRoom(roomType))
        {
            return true;
        }

        var (price, equipment) = RoomPrice(simulation, roomType);
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

    /// <summary>Buys one object outside the rooms, if it has the money; otherwise it goes without.</summary>
    private bool BuyOne(Simulation simulation, string objectType)
    {
        var price = ObjectCost(simulation, objectType);
        if (Balance < price)
        {
            return false;
        }

        Balance -= price;
        _objects[objectType] = ObjectCount(objectType) + 1;
        return true;
    }

    private static int ObjectCost(Simulation simulation, string objectType) =>
        simulation.Hospitals.Count > 0 ? simulation.Hospitals[0].Research.ObjectCost(simulation.Rules, objectType) : simulation.Rules.ObjectCost(objectType);

    /// <summary>
    /// A patient sent to it, who lives through the stay at once, without a place on the map (the specification's "Rival's
    /// patient"):
    /// <list type="number">
    /// <item>The patients in the hospital grow by one, and the care divisor is worked out: 20 with fewer than 6, else
    /// <c>100 − ⌊P / (10 × (rooms + 1))⌋ − ⌊P / (10 × (staff + 1))⌋ − ⌊P / (10 × (benches + 1))⌋</c>, from 0 to 100.</item>
    /// <item>Without a GP's office the patient goes home.</item>
    /// <item>Then a room every five days: the GP's, then the disease's diagnosis rooms it has. Each charges its fee and
    /// diagnoses as for the player, with the average skill of its staff of the room's class instead of the doctor's
    /// score; while drinks machines × 10 are more than the patients in the hospital, a patient in five buys a drink. The patient leaves when <c>rng % N = 0</c> (N
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
        var enoughDrinks = ObjectCount(DrinksMachine) * DrinksPerPatients > PatientsInHospital;
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

    /// <summary>The end of a month: the month's outcomes, then the staff's wages.</summary>
    internal void OnEndMonth(Simulation simulation, int monthOfGame)
    {
        Transfer(simulation, monthOfGame);
        Balance -= Wages;

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
    /// charts show of it, and, when the save gives it, where it had got in its scenario: whether it is open, its staff
    /// (as many as it had of each class, each with the average skill and an even share of the wages), its rooms, the
    /// objects it bought, its queue of hires, when it acts next and the action it waits to carry out then.
    /// </summary>
    internal void Import(ImportedCompetitor state, Simulation? simulation = null)
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

        if (state.Scenario is { } scenario && simulation is not null)
        {
            ImportScenario(simulation, scenario);
        }
    }

    /// <summary>
    /// The scenario step for the original's scenario position, the number of its next record: 1 to 9 are the steps of
    /// the start (step = position − 1), 12 to 15 the four steps of the cycle (staff, room, objects, loan); 0 is the
    /// first step and anything else starts the cycle.
    /// </summary>
    internal static int ScenarioStepOf(int position) => position switch
    {
        <= 0 => 0,
        <= StartSteps => position - 1,
        >= 12 => StartSteps + ((position - 12) % CycleSteps),
        _ => StartSteps,
    };

    private void ImportScenario(Simulation simulation, ImportedCompetitorScenario scenario)
    {
        IsOpen = scenario.IsOpen;
        ScenarioStep = ScenarioStepOf(scenario.Position);
        Pending = scenario.Pending;
        NextActionTick = simulation.Tick + Math.Max(0, scenario.TicksToAction);
        if (scenario.PatientsInHospital is { } patients)
        {
            PatientsInHospital = Math.Clamp(patients, 0, Math.Max(0, Visitors));
        }

        _staff.Clear();
        var members = scenario.Staff.Sum(s => Math.Max(0, s.Count));
        var wages = Math.Max(0, scenario.Wages);
        var paid = 0L;
        foreach (var (staffClass, count, skillSum) in scenario.Staff)
        {
            for (var i = 0; i < count; i++)
            {
                var wage = members == 0 ? 0 : wages / members;
                paid += wage;
                _staff.Add(new CompetitorStaff(staffClass, Math.Clamp(skillSum / count, 0, PatientCare.FullDiagnosis), (int)Math.Min(wage, int.MaxValue)));
            }
        }

        // What the even shares leave over goes to the first.
        if (_staff.Count > 0 && wages > paid)
        {
            _staff[0] = _staff[0] with { Wage = (int)Math.Min(_staff[0].Wage + wages - paid, int.MaxValue) };
        }

        _rooms.Clear();
        _equipmentValue = 0;
        foreach (var room in scenario.Rooms)
        {
            if (!HasRoom(room))
            {
                _rooms.Add(room);
                _equipmentValue += RoomPrice(simulation, room).Equipment;
            }
        }

        _objects.Clear();
        foreach (var (objectType, count) in scenario.Objects)
        {
            if (count > 0)
            {
                _objects[objectType] = ObjectCount(objectType) + count;
            }
        }

        for (var i = 0; i < QueuePlaces; i++)
        {
            _queue[i] = i < scenario.Queue.Count && scenario.Queue[i] is { } hire
                ? CompetitorAction.HireOf(hire.Class, hire.Ability)
                : null;
        }

        // A hire from the queue keeps its place until carried out; a loan request leaves the queue when taken.
        if (Pending is { } pending && (pending.Slot is < 0 or >= QueuePlaces || pending.Kind != CompetitorActionKind.Hire))
        {
            if (pending is { Kind: CompetitorActionKind.Loan, Slot: >= 0 and < QueuePlaces })
            {
                _queue[pending.Slot] = null;
            }

            Pending = pending with { Slot = -1 };
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
    /// What a win criterion measures for the computer player, for its level score: reputation, balance, the cured
    /// and killed shares of its visitors, cures and value; nothing for the rest.
    /// </summary>
    internal decimal? Measure(EndCriterion criterion) => criterion switch
    {
        EndCriterion.Reputation => Reputation,
        EndCriterion.Balance => Balance,
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
        writer.WriteInt64(_equipmentValue);
        writer.WriteBool(Pending is not null);
        if (Pending is { } pending)
        {
            WriteAction(writer, pending);
        }

        foreach (var task in _queue)
        {
            writer.WriteBool(task is not null);
            if (task is { } queued)
            {
                WriteAction(writer, queued);
            }
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
        writer.WriteByte(action.Ability is { } ability ? (byte)(ability + 1) : (byte)0);
        writer.WriteInt32(action.Slot);
        writer.WriteBool(action.FromStart);
    }

    private static CompetitorAction ReadAction(SimReader reader)
    {
        var kind = (CompetitorActionKind)Math.Min(reader.ReadByte(), (byte)CompetitorActionKind.Loan);
        var target = reader.ReadString(MaxNameBytes);
        var count = reader.ReadInt32();
        var ability = reader.ReadByte();
        var slot = reader.ReadInt32();
        return new CompetitorAction(kind, target, count, ability is > 0 and <= 3 ? (Specialism)(ability - 1) : null, slot is >= 0 and < QueuePlaces ? slot : -1, reader.ReadBool());
    }

    /// <param name="reader">The save.</param>
    /// <param name="startReputation">The level's starting reputation, for a game saved before opponents had one.</param>
    /// <param name="startCash">The level's starting money, for a game saved before opponents had any.</param>
    /// <param name="tiles">The tiles of the plot the players start on, for a game saved before opponents had a plot.</param>
    /// <param name="personality">The level's traits for it, for a game saved before opponents had them.</param>
    internal static CompetitorHospital Read(SimReader reader, int startReputation, long startCash, int tiles, CompetitorPersonality? personality = null)
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

        var competitor = new CompetitorHospital(number, name, reputation, startCash, tiles, personality)
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
        competitor.ScenarioStep = Math.Clamp(reader.ReadInt32(), 0, StartSteps + CycleSteps - 1);
        competitor._equipmentValue = Math.Max(0, reader.ReadInt64());
        competitor.Pending = reader.ReadBool() ? ReadAction(reader) : null;
        for (var slot = 0; slot < QueuePlaces; slot++)
        {
            competitor._queue[slot] = reader.ReadBool() ? ReadAction(reader) : null;
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
