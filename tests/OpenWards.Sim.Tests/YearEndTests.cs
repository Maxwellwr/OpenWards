using OpenWards.Sim.Definitions;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Objects;
using OpenWards.Sim.Rooms;
using static OpenWards.Sim.Definitions.FootprintFlags;

namespace OpenWards.Sim.Tests;

/// <summary>The hospital's value, the year's counts and the trophies and awards of the year end.</summary>
public class YearEndTests
{
    private static readonly ObjectDefinition Desk = new("desk", "Desk", 1, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, CompleteCell)]),
    });

    private static readonly ObjectDefinition Bench = new("bench", "Bench", 4, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, CompleteCell)]),
    });

    [Fact]
    public void TheValueIsTheLandTheEquipmentTheRoomsAndTheReputation()
    {
        var map = TestMaps.FromRows("....", "....", "....");
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var x = 0; x < map.Width; x++)
        {
            map[x, 1].Parcel = 1;
            map[x, 2].Parcel = 1;
            map[x, 1].Set(TileFlags.Buildable | TileFlags.Hospital, true);
            map[x, 2].Set(TileFlags.Buildable | TileFlags.Hospital, true);
        }

        var content = new GameContent([], [], [Desk, Bench]);
        var rules = new LevelRules(10_000, [KeyValuePair.Create("gp", 2500)], [KeyValuePair.Create("desk", 300), KeyValuePair.Create("bench", 100)], landCostPerTile: 30);
        var simulation = new Simulation(map, seed: 1, content, rules);
        var hospital = simulation.Hospitals[0];

        // 8 tiles of land, 10,000 for reputation 500 and 10,000.
        Assert.Equal((8 * 30) + 20_000, hospital.Value);

        // Money spent or taken in does not change it by itself.
        hospital.Spend(1, 1000, TransactionKind.Wages, string.Empty);
        hospital.Receive(1, 100, TransactionKind.Treatment, "diag_gp");
        HospitalValue.Refresh(simulation);
        Assert.Equal(20_240, hospital.Value);

        // A desk counts at what it cost; a bench does not count.
        var desk = ObjectPlacer.Place(simulation, Desk, new TilePosition(0, 1), Direction.North);
        ObjectPlacer.Place(simulation, Bench, new TilePosition(2, 1), Direction.North);
        Assert.Equal(300, desk.PurchasePrice);
        HospitalValue.Refresh(simulation);
        Assert.Equal(20_540, hospital.Value);

        // A built room counts at the room's own price, until it is wrecked.
        var room = simulation.AddEntity(id => new Room(id, "gp", new TileRect(0, 1, 2, 2), [.. Enumerable.Repeat(new OriginalTile(1, TileFlags.Passable), 4)]) { IsPaid = true });
        HospitalValue.Refresh(simulation);
        Assert.Equal(23_040, hospital.Value);
        room.IsCrashed = true;
        HospitalValue.Refresh(simulation);
        Assert.Equal(20_540, hospital.Value);

        // An object that goes takes off what it cost.
        desk.IsDestroyed = true;
        HospitalValue.Refresh(simulation);
        Assert.Equal(20_240, hospital.Value);

        // Reputation counts in steps of 50: 1,000 for each.
        var famous = new Simulation(TestMaps.FromRows("...."), seed: 1, rules: new LevelRules(0, [], [], startReputation: 749));
        Assert.Equal((749 / 50 * 1000) + 10_000, famous.Hospitals[0].Value);

        // The price paid is saved with the object.
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, content);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(20_240, restored.Hospitals[0].Value);
    }

    [Fact]
    public void AGoodYearWinsEveryTrophyAndAward()
    {
        var simulation = NewSimulation(players: 1);
        var hospital = simulation.Hospitals[0];
        hospital.SetReputation(600);
        hospital.Value = 200_000;
        hospital.VisitorsThisYear = 70;
        hospital.CuredThisYear = 60;
        hospital.Cured = 60;
        hospital.NotCuredThisYear = 2;
        hospital.RatsKilledThisYear = 30;
        hospital.RatShotsThisYear = 40;
        hospital.Vips.ThisYear = 2;
        hospital.Vips.ScoreSumThisYear = 20;
        hospital.SodasSold = 150;
        hospital.PopulationPercent = 60;
        hospital.EmergenciesThisYear = 2;
        hospital.EmergencyVictimsThisYear = 10;
        hospital.EmergencySavedThisYear = 10;
        hospital.ResearchDistributedThisYear = 3000;
        for (var month = 0; month < 12; month++)
        {
            hospital.RecordAwardMonth(new AwardMonth(600, 90, 80, 90, 10));
        }

        var awards = YearEndAwards.Judge(simulation, hospital);

        Assert.Equal(
            [
                YearEndAwardKind.ManyCuredTrophy, YearEndAwardKind.ConsistentReputation, YearEndAwardKind.HappyStaffTrophy,
                YearEndAwardKind.HappyVips, YearEndAwardKind.NoDeathsTrophy, YearEndAwardKind.RatsKilledTrophy,
                YearEndAwardKind.RatAccuracyTrophy, YearEndAwardKind.HealthyPlantsTrophy, YearEndAwardKind.DrinksSoldTrophy,
                YearEndAwardKind.HighReputation, YearEndAwardKind.ManyCures, YearEndAwardKind.FewDeaths, YearEndAwardKind.HighPopulationShare,
                YearEndAwardKind.CuresOverDeaths, YearEndAwardKind.HappyPatients, YearEndAwardKind.HighValue, YearEndAwardKind.HappyStaff,
                YearEndAwardKind.Clean, YearEndAwardKind.ShortWaits, YearEndAwardKind.EmergenciesSaved, YearEndAwardKind.GoodResearch,
            ],
            awards.Select(a => a.Kind));
        Assert.All(awards.Where(a => YearEndAwards.IsTrophy(a.Kind)), a => Assert.True(a.Money > 0 != a.Reputation > 0));
        Assert.Equal(30, awards.Single(a => a.Kind == YearEndAwardKind.RatsKilledTrophy).Detail);
        Assert.Equal(75, awards.Single(a => a.Kind == YearEndAwardKind.RatAccuracyTrophy).Detail);

        // Trophy money and award money are paid separately; the reputation rises by the percentages.
        hospital.SetYearAwards(awards);
        hospital.OnEndYear(100);
        Assert.Equal(new Transaction(100, TransactionKind.TrophyBonus, string.Empty, 6000 + 2000 + 10_000 + 5000 + 1000, 34_000), hospital.Transactions[1]);
        Assert.Equal(new Transaction(100, TransactionKind.AwardBonus, string.Empty, 2000 + 2000 + 3000 + 4000, 45_000), hospital.Transactions[0]);
        Assert.True(hospital.Reputation > 600);

        // The year's counts start afresh.
        Assert.Equal((0, 0, 0, 0), (hospital.CuredThisYear, hospital.DeathsThisYear, hospital.NotCuredThisYear, hospital.VisitorsThisYear));
        Assert.Equal((0, 0, 0, 0), (hospital.RatsKilledThisYear, hospital.EmergenciesThisYear, hospital.ResearchDistributedThisYear, hospital.SodasSold));
        Assert.Empty(hospital.AwardMonths);
    }

    [Fact]
    public void TheWorstHospitalPaysTwiceAndOneAheadOfAllTheRestGetsThreeTimes()
    {
        var simulation = NewSimulation(players: 2);
        var bad = simulation.Hospitals[0];
        bad.SetReputation(150);
        bad.Value = 10_000;
        bad.VisitorsThisYear = 10;
        (bad.CuredThisYear, bad.Cured, bad.DeathsThisYear, bad.Deaths) = (1, 1, 30, 30);
        bad.PopulationPercent = 5;

        var good = simulation.Hospitals[1];
        good.SetReputation(600);
        good.Value = 200_000;
        good.VisitorsThisYear = 70;
        (good.CuredThisYear, good.Cured, good.DeathsThisYear, good.Deaths) = (60, 60, 5, 5);
        good.PopulationPercent = 95;

        Assert.Equal(
            [
                new YearEndAward(YearEndAwardKind.PoorReputation, -6000, 0),
                new YearEndAward(YearEndAwardKind.FewCures, -6000, 0),
                new YearEndAward(YearEndAwardKind.ManyDeaths, -10_000, 0),
                new YearEndAward(YearEndAwardKind.LowPopulationShare, 0, -10),
                new YearEndAward(YearEndAwardKind.DeathsOverCures, -4000, 0),
                new YearEndAward(YearEndAwardKind.LowValue, 0, -12),
                new YearEndAward(YearEndAwardKind.Clean, 0, 6),
            ],
            YearEndAwards.Judge(simulation, bad));

        var awards = YearEndAwards.Judge(simulation, good);
        Assert.Contains(new YearEndAward(YearEndAwardKind.HighReputation, 6000, 0), awards);
        Assert.Contains(new YearEndAward(YearEndAwardKind.ManyCures, 6000, 0), awards);
        Assert.Contains(new YearEndAward(YearEndAwardKind.FewDeaths, 6000, 0), awards); // fewest deaths: twice
        Assert.Contains(new YearEndAward(YearEndAwardKind.HighPopulationShare, 0, 12), awards);
        Assert.Contains(new YearEndAward(YearEndAwardKind.HighValue, 0, 9), awards);
    }

    [Fact]
    public void AReputationAtItsLimitTakesMoneyInstead()
    {
        var simulation = NewSimulation(players: 1);
        var hospital = simulation.Hospitals[0];
        hospital.SetReputation(Hospital.MaxReputation);
        hospital.SetYearAwards([new(YearEndAwardKind.HappyStaffTrophy, 0, 5), new(YearEndAwardKind.HappyStaff, 0, 4)]);

        hospital.OnEndYear(100);

        Assert.Equal(new Transaction(100, TransactionKind.TrophyBonus, string.Empty, 5000, 15_000), hospital.Transactions[1]);
        Assert.Equal(new Transaction(100, TransactionKind.AwardBonus, string.Empty, 2500, 17_500), hospital.Transactions[0]);
        Assert.Equal([new(YearEndAwardKind.HappyStaffTrophy, 5000, 0), new YearEndAward(YearEndAwardKind.HappyStaff, 2500, 0)], hospital.LastYearAwards);

        hospital.SetReputation(Hospital.MinReputation);
        hospital.SetYearAwards([new(YearEndAwardKind.UnhappyStaff, 0, -10)]);
        hospital.OnEndYear(200);
        Assert.Equal(new Transaction(200, TransactionKind.AwardBonus, string.Empty, -2500, 15_000), hospital.Transactions[0]);
    }

    [Fact]
    public void FewPatientsWinNothing()
    {
        var simulation = NewSimulation(players: 1);
        var hospital = simulation.Hospitals[0];
        hospital.VisitorsThisYear = 3;
        hospital.SodasSold = 500;
        Assert.Empty(YearEndAwards.Judge(simulation, hospital));

        // Six patients are enough for the trophies, not for the awards of level 2 (more than six).
        hospital.VisitorsThisYear = 6;
        Assert.Equal([YearEndAwardKind.NoDeathsTrophy, YearEndAwardKind.DrinksSoldTrophy], YearEndAwards.Judge(simulation, hospital).Select(a => a.Kind));
    }

    [Fact]
    public void ResearchIsJudgedOnlyWhenMoreThanAHundredPointsWereDistributed()
    {
        var simulation = NewSimulation(players: 1, AwardRules.With([new(AwardRules.NewTechPoor, 500)]));
        var hospital = simulation.Hospitals[0];
        hospital.VisitorsThisYear = 10;

        // No research handed out: neither award nor penalty, even below NewTechPoor.
        hospital.ResearchDistributedThisYear = 100;
        Assert.DoesNotContain(YearEndAwards.Judge(simulation, hospital), a => a.Kind is YearEndAwardKind.PoorResearch or YearEndAwardKind.GoodResearch);

        hospital.ResearchDistributedThisYear = 101;
        Assert.Contains(YearEndAwards.Judge(simulation, hospital), a => a.Kind == YearEndAwardKind.PoorResearch);

        hospital.ResearchDistributedThisYear = 2001;
        Assert.Contains(YearEndAwards.Judge(simulation, hospital), a => a.Kind == YearEndAwardKind.GoodResearch);
    }

    [Fact]
    public void AMonthMeasuresTheStaffPatientsPlantsAndWaiting()
    {
        var simulation = NewSimulation(players: 1);
        var month = YearEndAwards.MeasureMonth(simulation, simulation.Hospitals[0]);
        Assert.Equal(new AwardMonth(Hospital.AverageReputation, -1, -1, -1, -1), month);
    }

    [Fact]
    public void TheLevelSetsThePrizes()
    {
        var prices = AwardRules.With([new(AwardRules.CuresPoor, 0), new(AwardRules.RatKillsAbsolute, 3), new("NotASetting", 1)]);
        Assert.Equal(0, prices[AwardRules.CuresPoor]);
        Assert.Equal(3, prices[AwardRules.RatKillsAbsolute]);
        Assert.Equal(150_000, prices[AwardRules.HospValueAward]);
        Assert.Equal(25, prices[AwardRules.TrophyMayor]);
    }

    [Fact]
    public void TheSimulationJudgesTheYearAtItsEndAndSavesKeepIt()
    {
        var map = TestMaps.FromRows("....", "....");
        var original = new Simulation(map, seed: 7, rules: new LevelRules(1000, [], []));
        var hospital = original.Hospitals[0];
        while (original.Date.Year == 1)
        {
            original.Step();
        }

        // An empty hospital that nobody came to wins and pays nothing; the year's months were noted and are cleared.
        Assert.Empty(hospital.LastYearAwards);
        Assert.Empty(hospital.AwardMonths);
        Assert.Equal(1, hospital.InflationYears);
        Assert.DoesNotContain(hospital.Transactions, t => t.Kind is TransactionKind.AwardBonus or TransactionKind.TrophyBonus);
        while (original.Date.Month < 3)
        {
            original.Step();
        }

        Assert.Equal(2, hospital.AwardMonths.Count);

        using var stream = new MemoryStream();
        original.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(hospital.LastYearAwards, restored.Hospitals[0].LastYearAwards);
        Assert.Equal(20_000, restored.Hospitals[0].Value);
        Assert.Equal(AwardRules.Default[AwardRules.CuresPenalty], restored.Rules.Awards[AwardRules.CuresPenalty]);
    }

    /// <summary>A level 2 town of 4 × 2 tiles, the first player's plot the top row (and the second's the bottom one).</summary>
    private static Simulation NewSimulation(int players, AwardRules? awards = null)
    {
        var map = new LevelMap(4, 2, players);
        map.ParcelCount = players + 1;
        for (var player = 1; player <= players; player++)
        {
            map.SetParcelOwner(player, player);
        }

        for (var x = 0; x < map.Width; x++)
        {
            map[x, 0].Parcel = 1;
            map[x, 1].Parcel = (ushort)(players > 1 ? 2 : 1);
            map[x, 0].Set(TileFlags.Passable | TileFlags.Hospital, true);
            map[x, 1].Set(TileFlags.Passable | TileFlags.Hospital, true);
        }

        return new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], [], levelNumber: 2, awards: awards));
    }
}
