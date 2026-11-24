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
    public void AGoodYearWinsTrophiesAndAwards()
    {
        var hospital = new Hospital(0, 0, reputation: 600, value: 200_000);
        hospital.CuredThisYear = 60;
        hospital.VisitorsThisYear = 70;

        hospital.OnEndYear(100, new SimRandom(4), AwardRules.Default);

        var kinds = hospital.LastYearAwards.Select(a => a.Kind).ToList();
        Assert.Equal(
            [
                YearEndAwardKind.ConsistentReputation, YearEndAwardKind.AllCuredTrophy, YearEndAwardKind.HighReputation,
                YearEndAwardKind.HighValue, YearEndAwardKind.ManyCures, YearEndAwardKind.AllCures, YearEndAwardKind.CuresOverDeaths,
            ],
            kinds);
        var valueAward = hospital.LastYearAwards.Single(a => a.Kind == YearEndAwardKind.HighValue);
        Assert.Equal(0, valueAward.Reputation % 3);
        Assert.InRange(valueAward.Reputation, 3, 45);

        // Trophy money and award money are paid separately; the reputation rises by the value award.
        Assert.Equal(new Transaction(100, TransactionKind.TrophyBonus, string.Empty, 2000 + 20_000, 22_000), hospital.Transactions[1]);
        Assert.Equal(new Transaction(100, TransactionKind.AwardBonus, string.Empty, 2000 + 2000 + 5000 + 4000, 35_000), hospital.Transactions[0]);
        Assert.InRange(hospital.Reputation, 600, 645);

        // The year's counts start afresh and prices rise.
        Assert.Equal((0, 0, 0, 0), (hospital.CuredThisYear, hospital.DeathsThisYear, hospital.NotCuredThisYear, hospital.VisitorsThisYear));
        Assert.Equal(1, hospital.InflationYears);
    }

    [Fact]
    public void ABadYearIsFined()
    {
        var hospital = new Hospital(0, 0, reputation: 150, value: 10_000);
        hospital.CuredThisYear = 1;
        hospital.DeathsThisYear = 30;

        hospital.OnEndYear(100, new SimRandom(4), AwardRules.Default);

        Assert.Equal(
            [
                YearEndAwardKind.PoorReputation, YearEndAwardKind.LowValue, YearEndAwardKind.FewCures,
                YearEndAwardKind.ManyDeaths, YearEndAwardKind.DeathsOverCures,
            ],
            hospital.LastYearAwards.Select(a => a.Kind));
        Assert.Equal(new Transaction(100, TransactionKind.AwardBonus, string.Empty, -3000 - 3000 - 5000 - 4000, -15_000), Assert.Single(hospital.Transactions));
        // The value penalty lowers the reputation, though so far below 500 a loss counts only now and then.
        Assert.InRange(hospital.LastYearAwards.Single(a => a.Kind == YearEndAwardKind.LowValue).Reputation, -90, -6);
        Assert.InRange(hospital.Reputation, 60, 150);
    }

    [Fact]
    public void SellingMoreCansThanTheLevelAsksWinsATrophyEvenWithoutCures()
    {
        var hospital = new Hospital(0, 0, reputation: 600, value: 200_000);
        hospital.SodasSold = AwardRules.Default[AwardRules.CansofCoke] + 1;

        hospital.OnEndYear(100, new SimRandom(4), AwardRules.Default);

        var trophy = hospital.LastYearAwards.Single(a => a.Kind == YearEndAwardKind.DrinksSoldTrophy);
        Assert.Equal(AwardRules.Default[AwardRules.CansofCokeBonus], trophy.Money);
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.TrophyBonus, Amount: 1000 });
        Assert.Equal(0, hospital.SodasSold);
    }

    [Fact]
    public void OneCureIsNotEnoughForATrophy()
    {
        var hospital = new Hospital(0, 0, reputation: 600, value: 200_000);
        hospital.CuredThisYear = 1;

        hospital.OnEndYear(100, new SimRandom(4), AwardRules.Default);

        // CorsixTH needs more than one cure for anything but the fines; with one cure there are fewer than ten.
        Assert.Equal([YearEndAwardKind.FewCures], hospital.LastYearAwards.Select(a => a.Kind));
    }

    [Fact]
    public void ADipInReputationLosesTheConsistentReputationTrophy()
    {
        var random = new SimRandom(4);
        var prices = AwardRules.With([new(AwardRules.Reputation, 500)]);
        var hospital = new Hospital(0, 0, reputation: 504, value: 100_000);
        hospital.CountPatient(null, visit: true, points: 0);
        hospital.ShiftReputation(21);
        Assert.Equal((501, 480), (hospital.Reputation, hospital.LowestReputationThisYear)); // ⌊10,080 / 21⌋, then +21
        hospital.CuredThisYear = 20;
        hospital.NotCuredThisYear = 1;

        hospital.OnEndYear(100, random, prices);

        Assert.Equal(
            [YearEndAwardKind.NoDeathsTrophy, YearEndAwardKind.HighReputation, YearEndAwardKind.FewDeaths, YearEndAwardKind.CuresOverDeaths],
            hospital.LastYearAwards.Select(a => a.Kind));
        Assert.Equal(hospital.Reputation, hospital.LowestReputationThisYear);
    }

    [Fact]
    public void TheLevelSetsThePrizes()
    {
        var prices = AwardRules.With([new(AwardRules.CuresPoor, 0), new(AwardRules.HospValuePoor, 0), new("NotASetting", 1)]);
        var hospital = new Hospital(0, 0, value: 20_000);
        hospital.DeathsThisYear = 1;

        hospital.OnEndYear(100, new SimRandom(4), prices);

        // Without the fines for few cures and a low value only the one for no cures per death is left.
        Assert.Equal([YearEndAwardKind.DeathsOverCures], hospital.LastYearAwards.Select(a => a.Kind));
        Assert.Equal(150_000, prices[AwardRules.HospValueAward]);
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

        // An empty hospital worth 20,000 cures nobody and is worth too little.
        Assert.Equal([YearEndAwardKind.LowValue, YearEndAwardKind.FewCures], hospital.LastYearAwards.Select(a => a.Kind));
        Assert.Equal(1, hospital.InflationYears);
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.AwardBonus, Amount: -3000 });

        using var stream = new MemoryStream();
        original.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(hospital.LastYearAwards, restored.Hospitals[0].LastYearAwards);
        Assert.Equal(20_000, restored.Hospitals[0].Value);
        Assert.Equal(AwardRules.Default[AwardRules.CuresPenalty], restored.Rules.Awards[AwardRules.CuresPenalty]);
    }
}
