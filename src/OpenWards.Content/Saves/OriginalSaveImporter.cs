using OpenWards.Content.Levels;
using OpenWards.Formats;
using OpenWards.Sim;
using OpenWards.Sim.Calendar;
using OpenWards.Sim.Definitions;
using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Import;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Research;
using OpenWards.Sim.Rooms;
using OpenWards.Sim.Staff;

namespace OpenWards.Content.Saves;

/// <summary>
/// Starts a game from a save of the original Theme Hospital (<see cref="OriginalSaveGame"/>): the save's level is
/// started as usual and the player's hospital rebuilt on it (<see cref="GameImport"/>).
/// </summary>
public static class OriginalSaveImporter
{
    /// <summary>
    /// Objects not bought and placed by the player: entrance doors (the level's map places them), the second halves of
    /// two-part objects (op_sink2, operating_table_b; placed with their first half), litter, the helicopter and ratholes.
    /// </summary>
    private static readonly byte[] SkippedTypes = [58, 59, 34, 12, 62, 63, 64];

    /// <summary>The radiation shield: both halves carry its type; the first one in the save is placed.</summary>
    private const byte RadiationShield = 28;

    /// <summary>Starts a simulation from <paramref name="save"/> with the level files of <paramref name="install"/>.</summary>
    /// <exception cref="InvalidDataException">The save names no level of the campaign.</exception>
    public static (Simulation Simulation, ImportReport Report) Start(ThemeHospitalInstall install, OriginalSaveGame save, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(save);
        if (save.Level is < 1 or > 12)
        {
            throw new InvalidDataException($"The save is of level {save.Level}, not one of the campaign's 12.");
        }

        var level = LevelMapLoader.LoadOriginal(install, $"LEVEL.L{save.Level}", DifficultyOf(save.Difficulty));
        var simulation = level.StartSimulation(seed);
        var report = GameImport.Apply(simulation, ToImportedGame(save, simulation));
        return (simulation, report);
    }

    /// <summary>The difficulty the save's level files are named after.</summary>
    public static Difficulty DifficultyOf(string name) => name.ToUpperInvariant() switch
    {
        "EASY" => Difficulty.Easy,
        "HARD" => Difficulty.Hard,
        _ => Difficulty.Medium,
    };

    /// <summary>What of <paramref name="save"/>'s player hospital is rebuilt, in the terms of <paramref name="simulation"/>'s content.</summary>
    public static ImportedGame ToImportedGame(OriginalSaveGame save, Simulation simulation)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(simulation);
        var content = simulation.Content;
        var hospital = save.Hospitals[0];

        var rooms = new List<ImportedRoom>();
        foreach (var room in save.Rooms.Where(r => r.Width > 1 && r.Height > 1))
        {
            if (content.Rooms.FirstOrDefault(r => r.LevelConfigId == room.Type) is { } definition)
            {
                var area = new TileRect(room.Origin.X, room.Origin.Y, room.Width, room.Height);
                rooms.Add(new ImportedRoom(definition.Id, area, DoorOf(room), room.MaxQueueSize));
            }
        }

        var objects = new List<ImportedObject>();
        foreach (var entity in save.Entities.Where(e => e.Kind == 6 && e.TileX != 0 && !SkippedTypes.Contains(e.Type)))
        {
            if (entity.Type == RadiationShield && objects.Any(o => o.ObjectType == "radiation_shield"
                && Math.Abs(o.Tile.X - entity.TileX) + Math.Abs(o.Tile.Y - entity.TileY) == 1))
            {
                continue;
            }

            if (content.Objects.FirstOrDefault(o => o.ObjectType == entity.Type && !o.Id.EndsWith("_b", StringComparison.Ordinal)) is { } definition)
            {
                objects.Add(new ImportedObject(
                    definition.Id,
                    new TilePosition(entity.TileX, entity.TileY),
                    DirectionOf(entity.Direction),
                    entity.Strength,
                    entity.TimesUsed,
                    definition.Id == "plant" ? (short)entity.PlantWater : null));
            }
        }

        var prices = new List<(string, int)>();
        for (var index = 1; index < OriginalSaveHospital.DiseaseCount; index++)
        {
            var record = hospital.Disease(index);
            if (record.ChargePercent > 0 && DiseaseCatalog.All.FirstOrDefault(d => d.ExpertiseId == index && !d.IsPseudo) is { } disease)
            {
                prices.Add((disease.Id, record.ChargePercent / 100));
            }
        }

        var staff = new List<ImportedStaff>();
        foreach (var entity in save.Entities.Where(e => e.Kind == 4 && e.TileX != 0))
        {
            // Type 4 is a doctor dressed for surgery.
            var staffClass = entity.Type is 4 ? StaffClass.Doctor : (StaffClass)Math.Min((int)entity.Type, 3);
            var flags = entity.StaffFlags;
            var (surgery, psychiatry, research) = entity.TrainingProgress;
            var name = new ImportedStaffName(entity.Initial, entity.NameStart, entity.NameEnd, entity.Description.Misc, entity.Description.Character);
            var profile = StaffProfile.Import(
                staffClass,
                entity.Skill,
                entity.AttentionToDetail * 100,
                psychiatrist: (flags & 64) != 0,
                surgeon: (flags & 32) != 0,
                researcher: (flags & 256) != 0,
                entity.Salary,
                (byte)entity.Layers[5],
                [surgery, psychiatry, research],
                name,
                simulation.Rules.Staff,
                simulation.Random);
            // Only the low 16 bits of the hiring tick are kept: the most recent tick that fits.
            var hiredAgo = (save.Tick - entity.HiredTick16) & 0xFFFF;
            var (cleaning, watering, repairing) = entity.HandymanShares;
            staff.Add(new ImportedStaff(
                profile,
                new TilePosition(entity.TileX, entity.TileY),
                entity.Morale / 100m,
                entity.Fatigue / 1000m,
                entity.Discontent,
                entity.RaiseRequests,
                Math.Min(hiredAgo, Math.Max(0, save.Tick)),
                (flags & 2) != 0 ? (cleaning, watering, repairing) : null));
        }

        var patients = new List<ImportedPatient>();
        foreach (var entity in save.Entities.Where(e => e.Kind == 2 && e.TileX != 0 && e.Disease is > 0 and < OriginalSaveHospital.DiseaseCount))
        {
            if (DiseaseCatalog.All.FirstOrDefault(d => d.ExpertiseId == entity.Disease && !d.IsPseudo) is not { } disease)
            {
                continue;
            }

            patients.Add(new ImportedPatient(
                disease.Id,
                new TilePosition(entity.TileX, entity.TileY),
                Math.Clamp(entity.Health, 1, 1000) / 1000m,
                entity.Happiness / 100m,
                1m - (entity.Thirst / 100m),
                entity.Warmth / 100m,
                entity.DiagnosisProgress,
                entity.Wallet,
                entity.InsurerBit switch { 1 => 1, 2 => 2, 4 => 3, _ => 0 },
                entity.DiagnosisDifficulty,
                entity.Bowel,
                entity.Nausea,
                entity.ContagiousStrength,
                Diagnosed: entity.DiagnosedDisease == entity.Disease,
                SeenByGp: entity.ProceduresDone > 0,
                IsEmergency: (entity.PatientFlags & EmergencyPatientFlag) != 0));
        }

        var casebook = new List<ImportedCase>();
        for (var index = 1; index < OriginalSaveHospital.DiseaseCount; index++)
        {
            var record = hospital.Disease(index);
            foreach (var disease in DiseaseCatalog.All.Where(d => d.ExpertiseId == index))
            {
                casebook.Add(new ImportedCase(disease.Id, record.Discovered, record.DrugEffectiveness, record.Recoveries, record.Fatalities, record.TurnedAway, record.MoneyEarned, record.DrugCost, record.ReputationPoints, record.ReputationVisits));
            }
        }

        var distribution = hospital.ResearchDistribution;
        var shares = new List<(ResearchCategory, int)>();
        for (var i = 0; i < distribution.Count && i < 5; i++)
        {
            shares.Add(((ResearchCategory)i, Math.Max(0, distribution[i])));
        }

        // The graphs keep the last 12 month ends, the newest last: the one of last December is 11 - month from the start.
        var yearStart = 11 - Math.Clamp(save.Month, 0, 11);
        var curesAtYearStart = save.Chart(OriginalSaveChart.Cures)[yearStart];
        var deathsAtYearStart = save.Chart(OriginalSaveChart.Deaths)[yearStart];
        var visitorsThisYear = Math.Clamp(hospital.Visitors - hospital.VisitorsAtYearStart, 0, hospital.Visitors);
        var curedThisYear = Math.Clamp(hospital.PeopleCured - curesAtYearStart, 0, hospital.PeopleCured);
        var deathsThisYear = Math.Clamp(hospital.Deaths - deathsAtYearStart, 0, hospital.Deaths);

        // The save does not count who left uncured: this year's patients neither cured, dead nor still in the hospital.
        var notCuredThisYear = Math.Max(0, visitorsThisYear - curedThisYear - deathsThisYear - patients.Count);

        // The lowest of the reputations at this year's month ends and today's.
        var lowestReputation = save.Chart(OriginalSaveChart.Reputation).Skip(yearStart + 1).Where(r => r > 0).Append(hospital.Reputation).Min();

        return new ImportedGame
        {
            PlayerName = hospital.Name,
            PlayerSalary = hospital.PlayerSalary,
            Staff = staff,
            Patients = patients,
            Visitors = hospital.Visitors,
            Cured = hospital.PeopleCured,
            Deaths = hospital.Deaths,
            VisitorsThisYear = visitorsThisYear,
            CuredThisYear = curedThisYear,
            DeathsThisYear = deathsThisYear,
            NotCuredThisYear = notCuredThisYear,
            LowestReputationThisYear = lowestReputation,
            Reputation = hospital.Reputation,
            ReputationRecord = hospital.ReputationVisits > 0 ? (hospital.ReputationPoints, hospital.ReputationVisits) : null,
            PriceLevel = hospital.PriceLevel,
            VipsThisYear = save.VipsThisYear,
            // OpenWards counts the days since the VIP was invited: the arrival less the journey.
            DaysSinceVip = hospital.VipArrivalTick is > 0 and var vip ? Math.Max(0, save.Tick - vip + VipVisits.TravelTicks) / 50 : null,
            Vip = VipOf(save, hospital),
            Emergency = hospital.EmergencyTimer > 0 && DiseaseCatalog.All.FirstOrDefault(d => d.ExpertiseId == hospital.EmergencyDisease && !d.IsPseudo) is { } emergencyDisease
                ? new ImportedEmergency(emergencyDisease.Id, hospital.EmergencyPatients, hospital.EmergencyCured, hospital.EmergencyBonus, hospital.EmergencyPassPercent, hospital.EmergencyTimer * 25)
                : null,
            NextEmergency = (save.EmergenciesUsed, save.NextEmergency is { Planned: true, Tick: var emergencyTick } ? Math.Max(0, emergencyTick - save.Tick) : null),
            NextEarthquake = (save.EarthquakesUsed, save.NextEarthquake is { Planned: true, Tick: var quakeTick } ? Math.Max(0, quakeTick - save.Tick) : null, save.NextEarthquake.Strength),
            ResearchDistributedThisYear = save.ResearchDistributedThisYear,
            RatsThisYear = save.RatsThisYear,
            Litter = LitterOf(save),
            Ratholes = Enumerable.Range(0, Math.Min(hospital.RatholeCount, OriginalSaveHospital.MaxRatholes))
                .Select(hospital.Rathole)
                .Select(hole => (new TilePosition(hole.X, hole.Y), (Direction)(hole.Wall & 3)))
                .ToList(),

            // The save keeps each company's debt by the calendar month it is paid in; OpenWards by months from now.
            InsuranceOwed =
            [
                .. from company in Enumerable.Range(0, 3)
                   from month in Enumerable.Range(0, 12)
                   let amount = hospital.InsuranceOwed(company, month)
                   where amount > 0
                   select (company + 1, (month - Math.Clamp(save.Month, 0, 11) + 12) % 12, (long)amount),
            ],
            Value = hospital.Value,

            // The computer players' blocks (kind 2), for the opponents' reputations and the year's charts.
            Competitors =
            [
                .. save.Hospitals.Select((h, player) => (h, player)).Where(p => p.h.Kind == 2).Select(p => new ImportedCompetitor(
                    p.h.Name,
                    p.h.Reputation,
                    p.h.Visitors,
                    VisitorsThisYear: p.h.Visitors - p.h.VisitorsAtYearStart,
                    Money: (long)p.h.Money - p.h.Loan,
                    Value: p.h.Value,
                    Cured: p.h.PeopleCured,
                    Deaths: p.h.Deaths,
                    Salary: p.h.PlayerSalary,
                    Tiles: p.h.Size,
                    Scenario: CompetitorScenario(save, p.player, content))),
            ],
            Months = Months(save),
            Statement = Statement(save, hospital.Money),
            Casebook = casebook,
            ResearchShares = shares,
            ObjectResearch =
            [
                .. Enumerable.Range(0, OriginalSaveHospital.ObjectTypeCount).Select(hospital.ObjectType)
                    .Select(o => new ImportedObjectResearch(o.Type, o.Available, o.Improvements, o.StartStrength, o.Cost)),
            ],
            ExpertiseResearch = [.. Enumerable.Range(1, OriginalSaveHospital.DiseaseCount - 1).Select(i => (i, hospital.ExpertiseResearch(i)))],
            ResearchPool = hospital.ResearchPoints,
            Year = save.Year + 1,
            Month = save.Month + 1,
            Day = Math.Max(1, save.Day),
            Balance = hospital.Money,
            Loan = hospital.Loan,
            OwnedParcels = [.. Enumerable.Range(1, OriginalSaveGame.MaxPlots - 1).Where(p => save.PlotOwner(p) == 1 && p < simulation.Map.ParcelCount)],
            Rooms = rooms,
            Objects = objects,
            Policies =
            [
                (PolicyKind.SendHome, hospital.SendHomePercent * 10),
                (PolicyKind.GuessCure, hospital.GuessCurePercent * 10),
                (PolicyKind.StopProcedure, hospital.StopProcedure),
                (PolicyKind.GoToStaffRoom, hospital.StaffRestLevel),
                (PolicyKind.StaffAllowedToMove, hospital.StaffLeaveRooms ? 1 : 0),
            ],
            Prices = prices,
            RadiatorHeat = hospital.HeatingLevel is > 0 and var heat ? Math.Clamp(heat, Hospital.MinRadiatorHeat, Hospital.MaxRadiatorHeat) : null,
        };
    }

    /// <summary>
    /// The original's object direction code in OpenWards's terms: 0 north, 1 east, 2 south, 3 west, as for the
    /// objects of level files. Some two-tile objects only fit another way; <see cref="GameImport"/> then tries the others.
    /// </summary>
    public static Direction DirectionOf(byte code) => (Direction)(code & 3);

    /// <summary>The graphs' figures of the months played, oldest first.</summary>
    private static List<MonthStatistics> Months(OriginalSaveGame save)
    {
        var series = Enum.GetValues<OriginalSaveChart>().ToDictionary(s => s, save.Chart);
        var months = new List<MonthStatistics>();
        for (var i = 0; i < 12; i++)
        {
            var month = new MonthStatistics(
                series[OriginalSaveChart.MoneyIn][i],
                series[OriginalSaveChart.MoneyOut][i],
                series[OriginalSaveChart.Wages][i],
                series[OriginalSaveChart.Balance][i],
                series[OriginalSaveChart.Visitors][i],
                series[OriginalSaveChart.Cures][i],
                series[OriginalSaveChart.Deaths][i],
                series[OriginalSaveChart.Reputation][i]);
            if (month != default)
            {
                months.Add(month);
            }
        }

        return months;
    }

    /// <summary>
    /// This year's bank statement lines, oldest first, as transactions; balances are worked back from today's. Lines of
    /// kinds without a counterpart are left out.
    /// </summary>
    private static List<Transaction> Statement(OriginalSaveGame save, long balance)
    {
        var lines = save.Statement.Select((line, order) => (line, order))
            .Where(item => item.line.Month <= save.Month && item.line.Day >= 1)
            .OrderBy(item => item.line.Month).ThenBy(item => item.line.Day).ThenBy(item => item.order)
            .Select(item => item.line)
            .ToList();
        var transactions = new List<Transaction>();
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (KindOf(line.Kind) is not { } kind)
            {
                continue;
            }

            var subject = kind == TransactionKind.Treatment
                ? DiseaseCatalog.All.FirstOrDefault(d => d.ExpertiseId == line.Detail)?.Id ?? string.Empty
                : string.Empty;
            var hour = new GameDate(1, line.Month + 1, line.Day, 0).ToHours();
            transactions.Insert(0, new Transaction(hour, kind, subject, line.Amount, balance));
            balance -= line.Amount;
        }

        return transactions;
    }

    private static TransactionKind? KindOf(byte kind) => kind switch
    {
        1 => TransactionKind.Wages,
        3 => TransactionKind.BuyObject,
        4 => TransactionKind.BuildRoom,
        7 => TransactionKind.Treatment,
        13 => TransactionKind.Drinks,
        16 => TransactionKind.Heating,
        17 => TransactionKind.Insurance,
        18 => TransactionKind.Loan,
        20 => TransactionKind.LoanInterest,
        22 => TransactionKind.DrugCost,
        23 => TransactionKind.Overdraft,
        27 => TransactionKind.PersonalBonus,
        34 => TransactionKind.AwardBonus,
        35 => TransactionKind.TrophyBonus,
        _ => null,
    };

    /// <summary>
    /// The door as a room plan wants it: on the north or west edge of the door's tile when the outside lies north or
    /// west, otherwise on the edge of the tile outside.
    /// </summary>
    /// <summary>
    /// The litter of the low nibble of each tile's extra byte: 1 to 3 are the three kinds of rubbish patients drop, 4 a
    /// dead rat, 5 puke, 6 pee, and 7 and 8 mark rat holes in the tile's north and west wall (the original developers'
    /// notes, as the maintainer's expert on the original reads them). The hospital's count of littered tiles is the tiles of 1 to 6 in every original save;
    /// the holes come from the hospital's list, which also holds those of east and south walls, unmarked on any tile.
    /// </summary>
    private static List<(TilePosition Tile, LitterType Type)> LitterOf(OriginalSaveGame save)
    {
        var litter = new List<(TilePosition, LitterType)>();
        for (var y = 0; y < OriginalSaveGame.MapSize; y++)
        {
            for (var x = 0; x < OriginalSaveGame.MapSize; x++)
            {
                LitterType? type = (save[x, y].Extra & 0x0F) switch
                {
                    >= 1 and <= 3 and var rubbish => (LitterType)rubbish,
                    4 => LitterType.DeadRat,
                    5 => LitterType.Puke,
                    6 => LitterType.Pee,
                    _ => null,
                };
                if (type is { } found)
                {
                    litter.Add((new TilePosition(x, y), found));
                }
            }
        }

        return litter;
    }

    /// <summary>The objects a computer player keeps count of outside its rooms.</summary>
    private static readonly string[] CompetitorObjects = ["reception_desk", "bench", "drinks_machine", "plant", "extinguisher"];

    /// <summary>
    /// Where a computer player had got in its scenario: its scenario position, when it acts next, whether it is open,
    /// its staff by class with their skills and wages, the rooms and objects of its lists and its queue of hires.
    /// </summary>
    private static ImportedCompetitorScenario CompetitorScenario(OriginalSaveGame save, int player, GameContent content)
    {
        var h = save.Hospitals[player];
        int[] counts = [h.Nurses, h.Doctors, h.Handymen, h.Receptionists];
        var staff = new List<(StaffClass, int, int)>();
        for (var i = 0; i < counts.Length; i++)
        {
            staff.Add(((StaffClass)i, counts[i], h.CompetitorSkillSum(i)));
        }

        var rooms = new List<string>();
        for (var type = 0; type < OriginalSaveGame.HospitalRoomTypes; type++)
        {
            if (save.HospitalRooms(player, type) > 0 && content.Rooms.FirstOrDefault(r => r.LevelConfigId == type) is { } room)
            {
                rooms.Add(room.Id);
            }
        }

        var objects = new List<(string, int)>();
        foreach (var id in CompetitorObjects)
        {
            if (content.FindObject(id) is { ObjectType: var type } && type < OriginalSaveGame.HospitalObjectTypes && save.HospitalObjects(player, type) is > 0 and var count)
            {
                objects.Add((id, count));
            }
        }

        return new ImportedCompetitorScenario(
            h.IsOpen,
            h.CompetitorScenarioPosition,
            (long)h.CompetitorNextTick - save.Tick,
            staff,
            h.CompetitorWages,
            h.CompetitorPatients,
            rooms,
            objects,
            [.. h.CompetitorQueue.Select(CompetitorTask)],
            CompetitorPending(h, content));
    }

    /// <summary>
    /// A place of a computer player's queue: type 1 a hire of a staff class (0 nurse, 1 doctor, 2 handyman,
    /// 3 receptionist) with the qualification bits it needs, type 4 a loan request; type 0 is a free place, its arguments
    /// left over from a task carried out.
    /// </summary>
    private static CompetitorAction? CompetitorTask((int Type, int Argument, int Qualification) task) => task switch
    {
        (1, >= 0 and <= 3, var bits) => CompetitorAction.HireOf((StaffClass)task.Argument, AbilityOf(bits)),
        (4, _, _) => new CompetitorAction(CompetitorActionKind.Loan),
        _ => null,
    };

    /// <summary>The specialism a hire needs by its qualification bits: 32 surgeon, 64 psychiatrist.</summary>
    private static Specialism? AbilityOf(int bits) =>
        (bits & 32) != 0 ? Specialism.Surgeon : (bits & 64) != 0 ? Specialism.Psychiatrist : null;

    /// <summary>
    /// The action a computer player waits to carry out: 1 a hire of a staff class (needing a specialism only when it came
    /// from the queue), 2 a room of a type, 3 objects of a type (how many are left), 4 a loan request; 0 or what is not
    /// known is none, and it decides afresh. One from the queue keeps its place there, the first of its type, so that it
    /// leaves the queue when carried out; an action of the start (scenario position up to 10) waits as the start's do.
    /// </summary>
    private static CompetitorAction? CompetitorPending(OriginalSaveHospital h, GameContent content)
    {
        var (kind, argument, second, fromQueue) = h.CompetitorPending;
        var fromStart = h.CompetitorScenarioPosition <= 10;
        var slot = -1;
        if (fromQueue)
        {
            var queue = h.CompetitorQueue;
            for (var i = 0; i < queue.Count && slot < 0; i++)
            {
                slot = queue[i].Type == kind ? i : -1;
            }
        }

        return kind switch
        {
            1 when argument <= 3 => CompetitorAction.HireOf((StaffClass)argument, fromQueue ? AbilityOf(second) : null) with { Slot = slot, FromStart = fromStart },
            2 when content.Rooms.FirstOrDefault(r => r.LevelConfigId == argument) is { } room => new CompetitorAction(CompetitorActionKind.Build, room.Id, FromStart: fromStart),
            3 when content.Objects.FirstOrDefault(o => o.ObjectType == argument && !o.Id.EndsWith("_b", StringComparison.Ordinal)) is { } item =>
                new CompetitorAction(CompetitorActionKind.Buy, item.Id, Math.Max(1, second), FromStart: fromStart),
            4 => new CompetitorAction(CompetitorActionKind.Loan, FromStart: fromStart),
            _ => null,
        };
    }

    /// <summary>Patient flag (+116) of those brought by the emergency helicopter.</summary>
    private const int EmergencyPatientFlag = 0x40;

    /// <summary>The VIP visit as the save leaves it: the state, who, when they arrive and, in the hospital, where they stand.</summary>
    private static ImportedVip? VipOf(OriginalSaveGame save, OriginalSaveHospital hospital)
    {
        if (hospital.VipState is < 1 or > 4)
        {
            return null;
        }

        TilePosition? tile = null;
        if (hospital.VipState == 3 && save.Entities.FirstOrDefault(e => e.Slot == hospital.VipSlot && e.Kind == VipKind) is { } vip)
        {
            tile = new TilePosition(vip.TileX, vip.TileY);
        }

        return new ImportedVip((VipState)hospital.VipState, hospital.VipName, (long)hospital.VipArrivalTick - save.Tick, hospital.VipRoomsLeft, tile);
    }

    /// <summary>The entity kind of a VIP.</summary>
    private const int VipKind = 20;

    private static DoorPlacement DoorOf(OriginalSaveRoom room)
    {
        var (dx, dy) = (room.Outside.X - room.Door.X, room.Outside.Y - room.Door.Y);
        return (dx, dy) switch
        {
            (0, -1) => new DoorPlacement(new TilePosition(room.Door.X, room.Door.Y), EdgeOrientation.North),
            (-1, 0) => new DoorPlacement(new TilePosition(room.Door.X, room.Door.Y), EdgeOrientation.West),
            (0, 1) => new DoorPlacement(new TilePosition(room.Outside.X, room.Outside.Y), EdgeOrientation.North),
            _ => new DoorPlacement(new TilePosition(room.Outside.X, room.Outside.Y), EdgeOrientation.West),
        };
    }
}
