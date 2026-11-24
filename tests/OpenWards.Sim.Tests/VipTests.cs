using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.People;
using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Tests;

/// <summary>How a VIP scores the hospital, what the score brings, and where VIPs queue (CorsixTH's Queue:push).</summary>
public class VipTests
{
    [Fact]
    public void TheScoreDecidesTheGrantAndTheReputation()
    {
        int[] scores = [-9, -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 12];
        Assert.Equal([4000, 4000, 2000, 1500, 1200, 800, 400, 200, 0, 0, 0, 0], scores.Select(VipVisits.Grant));
        Assert.Equal([4, 4, 4, 4, 4, 2, 2, 2, 2, 2, -4, -4], scores.Select(VipVisits.ReputationPercent));
    }

    [Fact]
    public void EachScoreHasARemarkAndOlderRatingsComeBackAsScores()
    {
        Assert.Equal(1, new VipReport(1, -9, 0, 0, 0).Remark);
        Assert.Equal(9, new VipReport(1, VipVisits.PleasedScore, 0, 0, 0).Remark);
        Assert.Equal(11, new VipReport(1, VipVisits.PleasedScore + 1, 0, 0, 0).Remark);
        Assert.Equal(VipReport.RemarkCount, new VipReport(1, 30, 0, 0, 0).Remark);
        for (var score = -5; score <= 8; score++)
        {
            Assert.Equal(score, VipReport.ScoreOfRemark(new VipReport(1, score, 0, 0, 0).Remark));
        }
    }

    [Fact]
    public void AnEmptyHospitalScoresBadlyAndLitterOnElevenTilesMakesItWorse()
    {
        var map = TestMaps.FromRows("..................", "..................");
        var simulation = new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], []));

        // Clean (−1), fewer than three staff (+4), no doctors (+4), no rooms (+4).
        Assert.Equal(11, VipBehaviour.ScoreHospital(simulation, 0));

        for (var x = 0; x < 10; x++)
        {
            Litter.Drop(simulation, 0, LitterType.SodaCan, new TilePosition(x, 0), isMirrored: false);
        }

        // Two pieces on one tile count once.
        Litter.Drop(simulation, 0, LitterType.Paper, new TilePosition(0, 1), isMirrored: false);
        Litter.Drop(simulation, 0, LitterType.Paper, new TilePosition(0, 1), isMirrored: false);
        Assert.Equal(11, VipBehaviour.LitterTiles(simulation, 0));
        Assert.Equal(13, VipBehaviour.ScoreHospital(simulation, 0));
    }

    [Theory]
    [InlineData(11, 0)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    public void OlderSavesNamesMoveToTheListsOrderWithTheMinisterFirst(int saved, int now)
    {
        var visits = new VipVisits { State = VipState.Visiting, InvitedName = saved, Report = new VipReport(saved, 1, 200, 2, 99) };
        using var stream = new MemoryStream();
        visits.Write(new SimWriter(stream));
        stream.Position = 0;

        var read = VipVisits.Read(new SimReader(stream) { Version = VipVisits.NamesFromZeroVersion - 1 });

        Assert.Equal(now, read.InvitedName);
        Assert.Equal(now, read.Report!.Value.Name);
    }

    [Fact]
    public void AnOlderSavesNoVipBecomesNoName()
    {
        var visits = new VipVisits { InvitedName = 0 };
        using var stream = new MemoryStream();
        visits.Write(new SimWriter(stream));
        stream.Position = 0;

        Assert.Equal(VipVisits.NoName, VipVisits.Read(new SimReader(stream) { Version = VipVisits.NamesFromZeroVersion - 1 }).InvitedName);
        Assert.Equal(VipVisits.NoName, new VipVisits().InvitedName);
    }

    [Fact]
    public void TheTriangularDistributionStaysWithinItsBounds()
    {
        var random = new SimRandom(9);
        for (var i = 0; i < 1000; i++)
        {
            Assert.InRange(VipVisits.TriangularRandom(random, 0.75m, 1m, 1.25m), 0.75m, 1.25m);
        }
    }

    [Fact]
    public void AVipGoesAheadOfPatientsInAQueue()
    {
        var map = new LevelMap(10, 10, playerCount: 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Set(TileFlags.Passable, true);
            }
        }

        var simulation = new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], []));
        var queue = new HumanoidQueue(new TilePosition(5, 5), new TilePosition(5, 4), 20);
        var first = Patient(simulation);
        var second = Patient(simulation);
        queue.Push(simulation, first);
        queue.Push(simulation, second);

        var vip = simulation.AddEntity(id => Humanoid.CreateVip(id, new TilePosition(1, 1), 0));
        vip.Behaviour = new VipBehaviour(1);
        queue.Push(simulation, vip);
        var third = Patient(simulation);
        queue.Push(simulation, third);

        Assert.Equal([vip.Id, first.Id, second.Id, third.Id], queue.Members);
    }

    private static Humanoid Patient(Simulation simulation)
    {
        var patient = simulation.AddEntity(id => Humanoid.CreateRandomPatient(id, new TilePosition(1, 1), simulation.Random, 0));
        patient.Behaviour = new PatientBehaviour();
        return patient;
    }
}
