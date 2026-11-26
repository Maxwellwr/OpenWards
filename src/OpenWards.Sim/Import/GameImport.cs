using OpenWards.Sim.Calendar;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Rooms;
using OpenWards.Sim.Staff;

namespace OpenWards.Sim.Import;

/// <summary>A room of an imported save.</summary>
/// <param name="RoomType">Id of the room's definition.</param>
/// <param name="Area">The room's tiles.</param>
/// <param name="Door">Where its door is.</param>
/// <param name="MaxQueue">The longest its door queue may grow, if known.</param>
public sealed record ImportedRoom(string RoomType, TileRect Area, DoorPlacement Door, int? MaxQueue = null);

/// <summary>An object of an imported save, in a room or in a corridor.</summary>
/// <param name="ObjectType">Id of the object's definition.</param>
/// <param name="Tile">Tile it stands on.</param>
/// <param name="Orientation">Way it faces.</param>
/// <param name="Strength">Its strength, for machines.</param>
/// <param name="TimesUsed">How many times it has been used, for machines.</param>
/// <param name="PlantWater">A plant's water in the original's terms (<see cref="Objects.PlantState.OriginalFullWater"/> when watered), if known.</param>
public sealed record ImportedObject(string ObjectType, TilePosition Tile, Direction Orientation, int Strength, int TimesUsed, int? PlantWater = null);

/// <summary>A member of staff of an imported save.</summary>
/// <param name="Profile">Who they are (<see cref="StaffProfile.Import"/>).</param>
/// <param name="Tile">Where they stand.</param>
/// <param name="Happiness">Morale, from 0 to 1.</param>
/// <param name="Fatigue">Tiredness, from 0 to 1.</param>
/// <param name="Discontent">How long they have been unhappy (<see cref="Humanoid.Discontent"/>), if known.</param>
/// <param name="RaiseRequests">How many times they have asked for money, if known.</param>
/// <param name="HiredHoursAgo">How many hours ago they were hired or last given a raise, if known.</param>
/// <param name="HandymanShares">A handyman's shares of cleaning, watering and repairs in percent, if known.</param>
public sealed record ImportedStaff(
    StaffProfile Profile,
    TilePosition Tile,
    decimal Happiness,
    decimal Fatigue,
    int? Discontent = null,
    int? RaiseRequests = null,
    long? HiredHoursAgo = null,
    (int Cleaning, int Watering, int Repairing)? HandymanShares = null);

/// <summary>A patient of an imported save; they start again from reception with what they have.</summary>
/// <param name="DiseaseId">Their disease.</param>
/// <param name="Tile">Where they stand.</param>
/// <param name="Health">How well they are, from 0 to 1.</param>
/// <param name="Happiness">From 0 to 1.</param>
/// <param name="Thirst">From 0 (not thirsty) to 1.</param>
/// <param name="Warmth">From 0 to 1.</param>
/// <param name="DiagnosisProgress">How certain the diagnosis is, in thousandths.</param>
/// <param name="Wallet">The money they brought, if known.</param>
/// <param name="InsuranceCompany">The insurance company (1 to 3) that pays for them, 0 for none, or null to draw one.</param>
/// <param name="DiagnosisDifficulty">How hard their diagnosis is, if known.</param>
/// <param name="Bowel">How much they need the toilet, if known.</param>
/// <param name="Nausea">How sick they feel, if known.</param>
/// <param name="ContagiousStrength">How contagious they are, if known.</param>
/// <param name="Diagnosed">Whether they have been diagnosed: they head for treatment.</param>
/// <param name="SeenByGp">Whether the GP has seen them: they go back to the GP rather than to reception.</param>
/// <param name="IsEmergency">Whether they came with the emergency under way (<see cref="ImportedGame.Emergency"/>).</param>
public sealed record ImportedPatient(
    string DiseaseId,
    TilePosition Tile,
    decimal Health,
    decimal Happiness,
    decimal Thirst,
    decimal Warmth,
    int DiagnosisProgress,
    int? Wallet = null,
    int? InsuranceCompany = null,
    int? DiagnosisDifficulty = null,
    int? Bowel = null,
    int? Nausea = null,
    int? ContagiousStrength = null,
    bool Diagnosed = false,
    bool SeenByGp = false,
    bool IsEmergency = false);

/// <summary>Where an imported save's VIP visit stands.</summary>
/// <param name="State">The visit's state, as <see cref="VipState"/>.</param>
/// <param name="Name">Which VIP (0 to 10).</param>
/// <param name="ArrivalInHours">Hours from the save's moment to the VIP's arrival (negative once they have come).</param>
/// <param name="RoomsLeft">Rooms the VIP in the hospital has still to see.</param>
/// <param name="Tile">Where the VIP in the hospital stands, if they are.</param>
public sealed record ImportedVip(VipState State, int Name, long ArrivalInHours, int RoomsLeft, TilePosition? Tile);

/// <summary>An imported save's emergency under way.</summary>
/// <param name="DiseaseId">The emergency's disease.</param>
/// <param name="Victims">Patients brought in.</param>
/// <param name="Cured">Patients cured so far.</param>
/// <param name="Bonus">Bonus per patient cured.</param>
/// <param name="Percentage">Share of patients to cure, in percent.</param>
/// <param name="HoursLeft">Hours left on the watch.</param>
public sealed record ImportedEmergency(string DiseaseId, int Victims, int Cured, int Bonus, int Percentage, int HoursLeft);

/// <summary>A casebook entry of an imported save.</summary>
/// <param name="DiseaseId">The disease.</param>
/// <param name="Discovered">Whether it shows in the casebook.</param>
/// <param name="Effectiveness">Drug effectiveness in percent.</param>
/// <param name="Recoveries">Patients cured of it.</param>
/// <param name="Fatalities">Patients who died of it.</param>
/// <param name="TurnedAway">Patients turned away.</param>
/// <param name="MoneyEarned">Money it brought in.</param>
/// <param name="DrugCost">What one dose of its drug costs, if known.</param>
/// <param name="ReputationPoints">The points behind the disease's own reputation, if known.</param>
/// <param name="ReputationVisits">The visits behind the disease's own reputation, if known.</param>
public sealed record ImportedCase(
    string DiseaseId,
    bool Discovered,
    int Effectiveness,
    int Recoveries,
    int Fatalities,
    int TurnedAway,
    long MoneyEarned,
    int? DrugCost = null,
    int? ReputationPoints = null,
    int? ReputationVisits = null);

/// <summary>Research on an object type of an imported save.</summary>
/// <param name="ObjectType">The object's number in the level files (<c>thob</c>).</param>
/// <param name="Discovered">Whether it can be bought.</param>
/// <param name="Improvements">Improvements research has made to it.</param>
/// <param name="StartStrength">The strength of new ones.</param>
/// <param name="Cost">What a new one costs now.</param>
public sealed record ImportedObjectResearch(int ObjectType, bool Discovered, int Improvements, int StartStrength, int Cost);

/// <summary>A computer opponent of an imported save.</summary>
/// <param name="Name">Its name, which picks the level's opponent of that name.</param>
/// <param name="Reputation">Its reputation.</param>
/// <param name="Visitors">Its patients so far.</param>
/// <param name="VisitorsThisYear">Its patients this year, if known; else all of them.</param>
/// <param name="Money">Its money less its loan, if known.</param>
/// <param name="Value">Its value, if known.</param>
/// <param name="Cured">Its patients cured so far, if known.</param>
/// <param name="Deaths">Its patients who died so far, if known.</param>
/// <param name="Salary">Its owner's salary, if known.</param>
/// <param name="Tiles">The tiles of its plot, if known.</param>
/// <param name="Scenario">Where it had got in its scenario and what it has, if known; else its scenario starts afresh.</param>
public sealed record ImportedCompetitor(
    string Name,
    int Reputation,
    int Visitors,
    int? VisitorsThisYear = null,
    long? Money = null,
    long? Value = null,
    int? Cured = null,
    int? Deaths = null,
    long? Salary = null,
    int? Tiles = null,
    ImportedCompetitorScenario? Scenario = null);

/// <summary>A computer player's place in its scenario and what it has, from a save of the original.</summary>
/// <param name="IsOpen">Whether its hospital has opened.</param>
/// <param name="Position">Its scenario position as the original stores it: 0 to 8 the start, 12 to 15 the cycle.</param>
/// <param name="TicksToAction">Ticks until its next action; 0 or less when it is due.</param>
/// <param name="Staff">For each class, how many it employs and the sum of their skills (thousandths).</param>
/// <param name="Wages">The total of its staff's wages.</param>
/// <param name="PatientsInHospital">Its patients in the hospital, if known.</param>
/// <param name="Rooms">The room types it has built.</param>
/// <param name="Objects">Objects it has bought outside its rooms, by type.</param>
/// <param name="Queue">Its queue: for each of the eight places a hire of a class (with a specialism), a loan request or nothing.</param>
/// <param name="Pending">The action it waits to carry out when its time comes, if any; else it decides afresh then.</param>
public sealed record ImportedCompetitorScenario(
    bool IsOpen,
    int Position,
    long TicksToAction,
    IReadOnlyList<(StaffClass Class, int Count, int SkillSum)> Staff,
    long Wages,
    int? PatientsInHospital,
    IReadOnlyList<string> Rooms,
    IReadOnlyList<(string ObjectType, int Count)> Objects,
    IReadOnlyList<CompetitorAction?> Queue,
    CompetitorAction? Pending = null);

/// <summary>
/// The state of a hospital taken from a save of another game (the original Theme Hospital), to be rebuilt on a
/// simulation started on the same level.
/// </summary>
public sealed record ImportedGame
{
    /// <summary>The player's own salary, or null to keep the level's.</summary>
    public long? PlayerSalary { get; init; }

    /// <summary>Year of the level, from 1.</summary>
    public int Year { get; init; } = 1;

    /// <summary>Month of the year, from 1.</summary>
    public int Month { get; init; } = 1;

    /// <summary>Day of the month, from 1.</summary>
    public int Day { get; init; } = 1;

    /// <summary>The player's name, which the hospital goes by, if known.</summary>
    public string? PlayerName { get; init; }

    public long Balance { get; init; }

    public long Loan { get; init; }

    /// <summary>Parcels the player owns, besides those the level starts with.</summary>
    public IReadOnlyList<int> OwnedParcels { get; init; } = [];

    public IReadOnlyList<ImportedRoom> Rooms { get; init; } = [];

    public IReadOnlyList<ImportedObject> Objects { get; init; } = [];

    /// <summary>Policy values as <see cref="HospitalPolicies"/> keeps them; missing ones are left as they are.</summary>
    public IReadOnlyList<(PolicyKind Kind, int Value)> Policies { get; init; } = [];

    /// <summary>Treatment prices in percent of the standard price, by disease id.</summary>
    public IReadOnlyList<(string DiseaseId, int Percent)> Prices { get; init; } = [];

    /// <summary>The hospital's staff; the level's starting staff are let go for them.</summary>
    public IReadOnlyList<ImportedStaff> Staff { get; init; } = [];

    public int Visitors { get; init; }

    public int Cured { get; init; }

    public int Deaths { get; init; }

    /// <summary>Patients this year, for the year's awards; null for all of them.</summary>
    public int? VisitorsThisYear { get; init; }

    /// <summary>Patients cured this year, for the year's awards; null for all of them.</summary>
    public int? CuredThisYear { get; init; }

    /// <summary>Patients who died this year, for the year's awards; null for all of them.</summary>
    public int? DeathsThisYear { get; init; }

    /// <summary>Patients who left uncured this year, for the year's awards; null for none.</summary>
    public int? NotCuredThisYear { get; init; }

    /// <summary>The lowest reputation this year, for the year's awards; null for today's.</summary>
    public int? LowestReputationThisYear { get; init; }

    /// <summary>Reputation, or null to keep the level's.</summary>
    public int? Reputation { get; init; }

    /// <summary>
    /// The points and visits behind the reputation (reputation = <see cref="Hospital.ReputationPerPoint"/> × points /
    /// visits), or null to weigh <see cref="Reputation"/> against the visitors.
    /// </summary>
    public (int Points, int Visits)? ReputationRecord { get; init; }

    /// <summary>What each insurance company (1 to 3) owes, by months from now (0 = this month's end).</summary>
    public IReadOnlyList<(int Company, int MonthsAhead, long Amount)> InsuranceOwed { get; init; } = [];

    /// <summary>The price level of the last month's end (100 = expected prices), or null to keep the level's.</summary>
    public int? PriceLevel { get; init; }

    /// <summary>The VIPs who visited this year and the sum of their scores (lower is better), or null for none.</summary>
    public (int Visits, int ScoreSum)? VipsThisYear { get; init; }

    /// <summary>How many days ago the last VIP came, or null to count from the import.</summary>
    public int? DaysSinceVip { get; init; }

    /// <summary>The litter on the floor.</summary>
    public IReadOnlyList<(TilePosition Tile, LitterType Type)> Litter { get; init; } = [];

    /// <summary>The rat holes: the corridor tile each opens onto and the wall it is in.</summary>
    public IReadOnlyList<(TilePosition Tile, Direction Wall)> Ratholes { get; init; } = [];

    /// <summary>The research points distributed to items this year, for the year's award, or null for none.</summary>
    public int? ResearchDistributedThisYear { get; init; }

    /// <summary>Where the VIP visit stands, or null to keep the level's.</summary>
    public ImportedVip? Vip { get; init; }

    /// <summary>The emergency under way, or null for none; its patients are those marked <see cref="ImportedPatient.IsEmergency"/>.</summary>
    public ImportedEmergency? Emergency { get; init; }

    /// <summary>
    /// The level's planned emergencies used up (the next planned one among them) and the hours until the next, or null
    /// to keep the level's plan.
    /// </summary>
    public (int Used, long? HoursToNext)? NextEmergency { get; init; }

    /// <summary>
    /// The level's planned earthquakes used up, the hours until the next and its strength; without a next one the
    /// level's plan stays.
    /// </summary>
    public (int Used, long? HoursToNext, int Size)? NextEarthquake { get; init; }

    /// <summary>The rats killed and shots fired this year, or null for none.</summary>
    public (int Killed, int Shots)? RatsThisYear { get; init; }

    /// <summary>Hospital value, or null to keep what rebuilding cost.</summary>
    public long? Value { get; init; }

    /// <summary>The graphs' monthly figures, oldest first.</summary>
    public IReadOnlyList<MonthStatistics> Months { get; init; } = [];

    /// <summary>The bank statement, oldest first.</summary>
    public IReadOnlyList<Transaction> Statement { get; init; } = [];

    /// <summary>The patients in the hospital.</summary>
    public IReadOnlyList<ImportedPatient> Patients { get; init; } = [];

    /// <summary>The casebook: what is known of each disease, by disease id.</summary>
    public IReadOnlyList<ImportedCase> Casebook { get; init; } = [];

    /// <summary>Research shares in percent, by area; missing areas are left as they are.</summary>
    public IReadOnlyList<(Research.ResearchCategory Category, int Share)> ResearchShares { get; init; } = [];

    /// <summary>Research on object types: what is discovered and how far machines are improved.</summary>
    public IReadOnlyList<ImportedObjectResearch> ObjectResearch { get; init; } = [];

    /// <summary>Research points put into each expertise (<c>#expertise[n]</c>) towards its next step.</summary>
    public IReadOnlyList<(int Expertise, int Points)> ExpertiseResearch { get; init; } = [];

    /// <summary>Research points not yet shared out among the areas, or null to keep none.</summary>
    public int? ResearchPool { get; init; }

    /// <summary>The computer opponents, matched to the level's by name, else in order.</summary>
    public IReadOnlyList<ImportedCompetitor> Competitors { get; init; } = [];

    /// <summary>Radiator heat in tenths, or null to keep the level's.</summary>
    public int? RadiatorHeat { get; init; }
}

/// <summary>What could not be rebuilt from an imported save.</summary>
/// <param name="Problems">One line for each room or object left out.</param>
public sealed record ImportReport(IReadOnlyList<string> Problems);

/// <summary>
/// Rebuilds an imported hospital on a simulation freshly started on the save's level: the player buys the parcels,
/// builds and furnishes the rooms and places the corridor objects through the usual commands, so the result holds
/// together as if it had been played; the money, date and settings are then set to the save's.
/// </summary>
public static class GameImport
{
    /// <summary>Rebuilds <paramref name="game"/> for player 0 of <paramref name="simulation"/>, which must not have run yet.</summary>
    public static ImportReport Apply(Simulation simulation, ImportedGame game)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(game);
        const int player = 0;
        var problems = new List<string>();
        var hospital = simulation.Hospitals[player];
        if (game.PlayerName is { Length: > 0 } name)
        {
            hospital.Name = name;
        }

        // Enough money to build everything again; the save's own balance is set at the end.
        hospital.ImportFinances(long.MaxValue / 4, 0);

        foreach (var parcel in game.OwnedParcels)
        {
            if (simulation.Map.GetParcelOwner(parcel) == 0)
            {
                simulation.SetParcelOwner(parcel, player + 1);
            }
        }

        var placed = new HashSet<ImportedObject>();
        foreach (var room in game.Rooms)
        {
            hospital.DiscoverRoom(room.RoomType);
            new BuildRoomCommand(new RoomPlan(room.RoomType, room.Area, room.Door)).Execute(simulation, player);
            var built = simulation.Entities.OfType<Room>().LastOrDefault(r => r.Area == room.Area && r.RoomType == room.RoomType);
            if (built is null)
            {
                problems.Add($"room {room.RoomType} at {room.Area} could not be built");
                continue;
            }

            // The objects the room needs go first, small ones before big ones: the original let objects stand closer
            // together than OpenWards's footprints allow, so whichever comes later may not fit; better an extra object
            // left out than the room unfinished.
            var needed = simulation.Content.FindRoom(room.RoomType)?.NeededObjects;
            foreach (var obj in game.Objects.Where(o => room.Area.Contains(o.Tile)).OrderBy(o => needed?.ContainsKey(o.ObjectType) == true ? 0 : 1).ThenBy(o => FootprintSize(simulation, o)))
            {
                if (Place(simulation, obj, player))
                {
                    placed.Add(obj);
                }
                else
                {
                    problems.Add($"{obj.ObjectType} at {obj.Tile} facing {obj.Orientation} in room {room.RoomType} could not be placed: {Objects.ObjectPlacer.Check(simulation, obj.ObjectType, obj.Tile, obj.Orientation, player)}");
                }
            }

            new ConfirmRoomCommand(built.Id).Execute(simulation, player);
            if (room.MaxQueue is >= 0 and <= People.HumanoidQueue.MaxMaxSize and var maxQueue && built.DoorQueue is { } queue)
            {
                queue.ChangeMaxSize(maxQueue - queue.MaxSize);
            }
            if (!built.IsActive)
            {
                problems.Add($"room {room.RoomType} at {room.Area} could not be finished");
            }
        }

        foreach (var obj in game.Objects.Where(o => !placed.Contains(o) && !game.Rooms.Any(r => r.Area.Contains(o.Tile))))
        {
            if (!Place(simulation, obj, player))
            {
                problems.Add($"{obj.ObjectType} at {obj.Tile} facing {obj.Orientation} could not be placed: {Objects.ObjectPlacer.Check(simulation, obj.ObjectType, obj.Tile, obj.Orientation, player)}");
            }
        }

        // The level's starting staff make way for the save's.
        foreach (var starter in simulation.Entities.OfType<Humanoid>().Where(h => h.Profile is not null && h.Employer == player).ToList())
        {
            StaffEmployment.Detach(simulation, starter);
            simulation.RemoveEntity(starter.Id);
        }

        var hired = new List<(Humanoid Staff, long HoursAgo)>();
        foreach (var member in game.Staff)
        {
            var staff = StaffHiring.Place(simulation, member.Profile, member.Tile, player);
            staff.Happiness = Math.Clamp(member.Happiness, 0m, 1m);
            staff.Fatigue = Math.Clamp(member.Fatigue, 0m, 1m);
            if (member.Discontent is >= 0 and var discontent)
            {
                staff.Discontent = discontent;
            }

            if (member.RaiseRequests is >= 0 and var requests)
            {
                staff.RaiseRequestCount = Math.Min(requests, StaffEmployment.DefiniteQuitRequest - 1);
            }

            if (member.HiredHoursAgo is >= 0 and var ago)
            {
                hired.Add((staff, ago));
            }

            if (member.HandymanShares is { } shares && staff.Behaviour is People.HandymanBehaviour handyman)
            {
                handyman.ImportShares(shares.Cleaning, shares.Watering, shares.Repairing);
            }
        }

        foreach (var (tile, type) in game.Litter)
        {
            if (simulation.Map.Contains(tile.X, tile.Y) && simulation.Map[tile.X, tile.Y].RoomId == 0)
            {
                Entities.Litter.Drop(simulation, player, type, tile, isMirrored: false);
            }
        }

        foreach (var (tile, wall) in game.Ratholes.Take(Disasters.Rats.MaxRatholes))
        {
            if (simulation.Map.Contains(tile.X, tile.Y) && simulation.Map[tile.X, tile.Y].RoomId == 0)
            {
                hospital.AddRathole(new Rathole(tile, wall, simulation.Map[tile.X, tile.Y].Parcel));
            }
        }

        foreach (var (kind, value) in game.Policies)
        {
            hospital.Policies.Set(kind, value);
        }

        foreach (var (diseaseId, percent) in game.Prices)
        {
            new SetTreatmentPriceCommand(diseaseId, percent).Execute(simulation, player);
        }

        if (game.RadiatorHeat is { } heat)
        {
            hospital.SetRadiatorHeat(heat);
        }

        var emergencyPatients = new List<Humanoid>();
        foreach (var imported in game.Patients)
        {
            // Someone sitting on a bench stands up next to it.
            if (Diseases.DiseaseCatalog.Find(imported.DiseaseId) is not { } disease || NearestPassable(simulation.Map, imported.Tile) is not { } tile)
            {
                problems.Add($"patient with {imported.DiseaseId} at {imported.Tile} could not be placed");
                continue;
            }

            var patient = simulation.AddEntity(id => Humanoid.CreatePatient(id, tile, simulation.Random, disease.Look, player));
            var behaviour = new People.PatientBehaviour { Health = (int)(Math.Clamp(imported.Health, 0.001m, 1m) * People.PatientNeeds.MaxHealth) };
            var insurer = imported.InsuranceCompany is { } company && company is >= 0 and <= Hospital.InsuranceCompanies
                ? company
                : People.PatientCare.ChooseInsuranceCompany(simulation);
            behaviour.SetDisease(disease, insurer, simulation.Rules.Needs);
            behaviour.Wallet = imported.Wallet is >= 0 and var wallet ? wallet : People.PatientCare.NewPatientMoney(simulation);
            if (imported.DiagnosisDifficulty is > 0 and var difficulty)
            {
                behaviour.DiagnosisDifficulty = difficulty;
            }

            if (imported.Bowel is >= 0 and var bowel)
            {
                behaviour.Bowel = bowel;
            }

            if (imported.Nausea is >= 0 and var nausea)
            {
                behaviour.Nausea = nausea;
            }

            if (imported.ContagiousStrength is > 0 and < 255 and var contagious)
            {
                behaviour.ContagiousStrength = contagious;
            }
            behaviour.DiagnosisProgress = Math.Clamp(imported.DiagnosisProgress, 0, 1000);

            // The save's visitors, which the reputation is weighed against, count them already.
            behaviour.IsCountedInReputation = true;
            behaviour.DrinkStock = People.PatientNeeds.MaxPoints - (int)(Math.Clamp(imported.Thirst, 0m, 1m) * People.PatientNeeds.MaxPoints);
            patient.Behaviour = behaviour;
            patient.Happiness = Math.Clamp(imported.Happiness, 0m, 1m);
            patient.Warmth = Math.Clamp(imported.Warmth, 0m, 1m);
            if (imported.IsEmergency && game.Emergency is not null)
            {
                emergencyPatients.Add(patient);
            }
            else
            {
                behaviour.ResumeImportedRoute(imported.Diagnosed, imported.SeenByGp);
            }
        }

        foreach (var entry in game.Casebook)
        {
            if (Diseases.DiseaseCatalog.Find(entry.DiseaseId) is { } disease)
            {
                var book = hospital.CaseOf(disease, simulation.Rules.Diseases);
                book.Discovered |= entry.Discovered;
                // Every drug is there from the start; disease byte +28 says whether the disease can be treated now, not
                // whether its drug is known.
                if (Research.ResearchDepartment.IsDrugDisease(disease) && entry.Effectiveness > 0)
                {
                    // The original keeps 0 for diseases not cured by drugs, which are always fully effective.
                    book.CureEffectiveness = Math.Min(entry.Effectiveness, DiseaseCase.FullEffectiveness);
                    if (entry.DrugCost is > 0 and var cost)
                    {
                        book.DrugCost = cost;
                    }
                }

                book.Recoveries = entry.Recoveries;
                book.Fatalities = entry.Fatalities;
                book.TurnedAway = entry.TurnedAway;
                book.MoneyEarned = entry.MoneyEarned;
            }
        }

        hospital.Research.Import(simulation, hospital, game.ObjectResearch, game.ExpertiseResearch, game.ResearchPool);
        foreach (var (category, share) in game.ResearchShares)
        {
            hospital.Research.Area(category).Share = Math.Clamp(share, 0, 100);
        }

        hospital.ImportFinances(game.Balance, game.Loan);
        hospital.ImportRecords(game.Visitors, game.Cured, game.Deaths, game.Reputation ?? hospital.Reputation, game.Value ?? hospital.Value, game.Months, game.Statement);
        if (game.ReputationRecord is { Points: var repPoints, Visits: > 0 and var repVisits })
        {
            hospital.ImportReputation((long)repPoints * Hospital.ReputationPerPoint, repVisits);
        }

        // After the records, which weigh each disease's reputation by the hospital's when the save does not tell it.
        foreach (var entry in game.Casebook)
        {
            if (entry is { ReputationPoints: { } points, ReputationVisits: > 0 and var visits } && Diseases.DiseaseCatalog.Find(entry.DiseaseId) is { } disease)
            {
                var book = hospital.CaseOf(disease, simulation.Rules.Diseases);
                book.ReputationVisits = visits;
                book.ReputationScore = Math.Clamp((long)points * Hospital.ReputationPerPoint, (long)Hospital.MinReputation * visits, (long)Hospital.MaxReputation * visits);
            }
        }

        hospital.ImportInsuranceOwed(game.InsuranceOwed);
        if (game.PriceLevel is { } priceLevel)
        {
            hospital.ImportPriceLevel(priceLevel);
        }

        hospital.ImportYear(
            game.VisitorsThisYear ?? game.Visitors,
            game.CuredThisYear ?? game.Cured,
            game.DeathsThisYear ?? game.Deaths,
            game.NotCuredThisYear ?? 0,
            game.LowestReputationThisYear ?? hospital.Reputation);
        if (game.PlayerSalary is > 0 and var salary)
        {
            // The level's starting salary is not in the save: the year's raise measures its ceiling from today's.
            hospital.BringSalary(salary);
        }

        if (game.Year > 1 || game.Month > 1 || game.Day > 1)
        {
            simulation.SkipTo(new GameDate(Math.Max(1, game.Year), game.Month, game.Day, 0));
            simulation.Arrivals.CatchUp(simulation);
        }

        ImportYearCounters(hospital, game);
        ImportVip(simulation, hospital, game.Vip, problems);
        ImportEmergencies(simulation, hospital, game, emergencyPatients);
        if (game.NextEarthquake is { HoursToNext: >= 0 and var quakeHours } quake)
        {
            simulation.Earthquakes.Import(simulation.Rules.Earthquakes.Count, quake.Used, GameDate.FromHours(simulation.Hours + quakeHours), quake.Size);
        }

        // The calendar has moved on: hiring dates count back from the save's day.
        foreach (var (staff, ago) in hired)
        {
            staff.HiredAt = Math.Max(0, simulation.Hours - ago);
        }

        ImportCompetitors(simulation, game.Competitors);
        return new ImportReport(problems);
    }

    /// <summary>
    /// The save's VIP: invited, on the way (arriving as much later as in the save) or in the hospital with the rooms
    /// they have still to see; after the visit, nothing changes.
    /// </summary>
    private static void ImportVip(Simulation simulation, Hospital hospital, ImportedVip? imported, List<string> problems)
    {
        if (imported is null || imported.Name is < 0 or >= VipVisits.NameCount)
        {
            return;
        }

        var vips = hospital.Vips;
        switch (imported.State)
        {
            case VipState.Invited:
                vips.State = VipState.Invited;
                vips.InvitedName = imported.Name;
                vips.InvitationExpires = simulation.Tick + (VipVisits.InvitationDays * GameDate.HoursPerDay);
                break;
            case VipState.Travelling:
                vips.State = VipState.Travelling;
                vips.InvitedName = imported.Name;
                vips.ArrivalTick = simulation.Tick + Math.Max(0, imported.ArrivalInHours);
                break;
            case VipState.Visiting:
                if (imported.Tile is not { } at || NearestPassable(simulation.Map, at) is not { } tile)
                {
                    problems.Add($"the VIP at {imported.Tile} could not be placed");
                    return;
                }

                var vip = simulation.AddEntity(id => Humanoid.CreateVip(id, tile, hospital.Player));
                var behaviour = new People.VipBehaviour(imported.Name);
                behaviour.ResumeImportedVisit(imported.RoomsLeft);
                vip.Behaviour = behaviour;
                vips.State = VipState.Visiting;
                vips.InvitedName = imported.Name;
                vips.ArrivalTick = simulation.Tick + Math.Min(0, imported.ArrivalInHours);
                vips.Count++;
                break;
        }
    }

    /// <summary>
    /// The save's emergencies: the one under way with its patients and the time left on its watch, and the level's
    /// planned emergencies used up, the next on the day the save planned it.
    /// </summary>
    private static void ImportEmergencies(Simulation simulation, Hospital hospital, ImportedGame game, List<Humanoid> emergencyPatients)
    {
        var state = hospital.Emergencies;
        if (game.NextEmergency is { } next)
        {
            state.NextNumber = Math.Max(0, next.Used);
            var index = next.Used - 1;
            if (next.HoursToNext is >= 0 and var hours && simulation.Rules.Emergencies.Any(e => e.Index == index))
            {
                var date = GameDate.FromHours(simulation.Hours + hours);
                (state.NextIndex, state.NextMonth, state.NextDay) = (index, date.MonthOfGame, date.Day);
            }
            else
            {
                Disasters.Emergencies.NextEmergency(simulation, hospital);
            }
        }

        if (game.Emergency is not { } imported || emergencyPatients.Count == 0 || imported.HoursLeft <= 0)
        {
            return;
        }

        var emergency = new Disasters.Emergency(imported.DiseaseId, Math.Max(imported.Victims, emergencyPatients.Count), imported.Bonus, imported.Percentage)
        {
            IsAccepted = true,
            Cured = Math.Max(0, imported.Cured),
            WatchTimer = Math.Min(imported.HoursLeft / Disasters.HospitalEmergencies.WatchTickRate, Disasters.HospitalEmergencies.WatchSegments - 1),
            WatchTickTimer = Math.Max(1, imported.HoursLeft % Disasters.HospitalEmergencies.WatchTickRate),
        };
        state.Current = emergency;
        var number = 0;
        foreach (var patient in emergencyPatients)
        {
            emergency.PatientIds.Add(patient.Id);
            if (patient.Behaviour is People.PatientBehaviour behaviour)
            {
                behaviour.StartEmergency(simulation, patient, ++number);
            }
        }
    }

    /// <summary>The year's VIP visits and rats for the awards, and the days since the last VIP for the next.</summary>
    private static void ImportYearCounters(Hospital hospital, ImportedGame game)
    {
        if (game.VipsThisYear is { Visits: > 0 } vips)
        {
            hospital.Vips.ThisYear = vips.Visits;
            hospital.Vips.ScoreSumThisYear = vips.ScoreSum;

            // Only the sum is kept: everyone was pleased when it says so on average.
            hospital.Vips.PleasedThisYear = vips.ScoreSum <= VipVisits.PleasedScore * vips.Visits ? vips.Visits : 0;
        }

        if (game.DaysSinceVip is >= 0 and var days)
        {
            hospital.Vips.DaysSinceLast = days;
        }

        if (game.ResearchDistributedThisYear is > 0 and var research)
        {
            hospital.ResearchDistributedThisYear = research;
        }

        if (game.RatsThisYear is { } rats)
        {
            hospital.RatsKilledThisYear = Math.Max(0, rats.Killed);
            hospital.RatShotsThisYear = Math.Max(rats.Shots, hospital.RatsKilledThisYear);
        }
    }

    /// <summary>The save's opponents (reputations, patients and the year's charts) for the level's opponents: by name, the rest in order.</summary>
    private static void ImportCompetitors(Simulation simulation, IReadOnlyList<ImportedCompetitor> imported)
    {
        var left = simulation.Competitors.ToList();
        var unmatched = new List<ImportedCompetitor>();
        foreach (var competitor in imported)
        {
            var match = left.FirstOrDefault(c => string.Equals(c.Name, competitor.Name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                unmatched.Add(competitor);
                continue;
            }

            match.Import(competitor, simulation);
            left.Remove(match);
        }

        for (var i = 0; i < unmatched.Count && i < left.Count; i++)
        {
            left[i].Import(unmatched[i], simulation);
        }
    }

    /// <summary>Tiles an object takes up, so that small objects against the walls go before big ones in the middle.</summary>
    private static int FootprintSize(Simulation simulation, ImportedObject obj) =>
        simulation.Content.FindObject(obj.ObjectType)?.Orientations.Values.Select(o => o.Footprint.Count).DefaultIfEmpty(0).Max() ?? 0;

    /// <summary>The tile itself if it can be walked on, else the nearest such tile up to two tiles away, or null.</summary>
    private static TilePosition? NearestPassable(LevelMap map, TilePosition tile)
    {
        for (var radius = 0; radius <= 2; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var (x, y) = (tile.X + dx, tile.Y + dy);
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == radius && map.Contains(x, y) && map[x, y].Has(TileFlags.Passable))
                    {
                        return new TilePosition(x, y);
                    }
                }
            }
        }

        return null;
    }

    private static bool Place(Simulation simulation, ImportedObject obj, int player)
    {
        // The original's direction codes do not always match OpenWards's footprints: take the save's way if the object
        // fits so, else the opposite way, else any way it fits on the same tile.
        var orientation = obj.Orientation;
        var tile = obj.Tile;
        Direction[] tries = [obj.Orientation, (Direction)(((int)obj.Orientation + 2) % 4), (Direction)(((int)obj.Orientation + 3) % 4), (Direction)(((int)obj.Orientation + 1) % 4)];

        // Failing that, the object moves to a neighbouring tile: the original let use positions overlap, which
        // OpenWards's placement rules (CorsixTH's) do not.
        (int Dx, int Dy)[] offsets = [(0, 0), (0, 1), (1, 0), (0, -1), (-1, 0)];
        var found = false;
        foreach (var (dx, dy) in offsets)
        {
            foreach (var candidate in tries)
            {
                var at = new TilePosition(obj.Tile.X + dx, obj.Tile.Y + dy);
                if (Objects.ObjectPlacer.Check(simulation, obj.ObjectType, at, candidate, player) == Objects.ObjectPlacementProblem.None)
                {
                    (orientation, tile, found) = (candidate, at, true);
                    break;
                }
            }

            if (found)
            {
                break;
            }
        }

        var before = simulation.Entities.Count;
        new PlaceObjectCommand(obj.ObjectType, tile, orientation).Execute(simulation, player);
        var created = simulation.Entities.Skip(before).OfType<RoomObject>().FirstOrDefault(o => o.Tile == tile && o.ObjectType == obj.ObjectType);
        if (created is null)
        {
            return false;
        }

        if (obj.PlantWater is { } water && created.Plant is { } plant)
        {
            plant.ImportOriginalWater(water);
        }

        if (created.Machine is { } machine)
        {
            machine.Strength = obj.Strength;
            machine.TimesUsed = obj.TimesUsed;
        }

        return true;
    }
}
