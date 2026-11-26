using OpenWards.Sim.Economy;

namespace OpenWards.Sim.Tests;

/// <summary>The network game's missions, after the maintainer's specification ("Сетевые задания").</summary>
public class NetMissionTests
{
    /// <summary>
    /// The specification's net_missions.csv: level, row, mission, value, month, months to do it. Level 34's row 5 is
    /// given twice in its file; this is the second, which counts.
    /// </summary>
    private static readonly (int Level, int Row, int Code, int Value, int Month, int TimeToDo)[] Missions =
    [
        (20, 0, 3, 1, 2, 4),
        (20, 1, 2, 1, 7, 8),
        (20, 2, 4, 15, 16, 4),
        (20, 3, 8, 1, 24, 12),
        (22, 0, 1, 1, 1, 9),
        (22, 1, 7, 500, 11, 6),
        (22, 2, 1, 1, 17, 9),
        (22, 3, 7, 500, 27, 6),
        (22, 4, 8, 1, 34, 9),
        (23, 0, 3, 1, 2, 4),
        (23, 1, 2, 1, 7, 9),
        (23, 2, 4, 10, 17, 4),
        (23, 3, 8, 1, 22, 8),
        (24, 0, 2, 1, 2, 4),
        (24, 1, 3, 1, 7, 9),
        (24, 2, 4, 15, 17, 6),
        (24, 3, 9, 1, 24, 12),
        (30, 0, 3, 1, 2, 6),
        (30, 1, 2, 1, 9, 8),
        (30, 2, 4, 15, 18, 4),
        (30, 3, 8, 1, 23, 12),
        (30, 4, 9, 1, 36, 12),
        (32, 0, 1, 1, 1, 9),
        (32, 1, 7, 500, 10, 6),
        (32, 2, 1, 1, 17, 9),
        (32, 3, 7, 500, 26, 6),
        (32, 4, 1, 1, 33, 9),
        (32, 5, 8, 1, 43, 6),
        (33, 0, 3, 1, 2, 4),
        (33, 1, 2, 1, 7, 8),
        (33, 2, 4, 10, 16, 4),
        (33, 3, 5, 10000, 21, 4),
        (33, 4, 6, 100000, 26, 4),
        (33, 5, 8, 1, 31, 12),
        (34, 0, 3, 1, 2, 4),
        (34, 1, 2, 1, 7, 8),
        (34, 2, 4, 15, 16, 4),
        (34, 3, 5, 10000, 21, 4),
        (34, 4, 7, 600, 26, 4),
        (34, 5, 8, 1, 44, 8),
        (40, 0, 3, 1, 2, 6),
        (40, 1, 2, 1, 9, 8),
        (40, 2, 4, 10, 18, 5),
        (40, 3, 7, 600, 23, 4),
        (40, 4, 8, 1, 28, 12),
        (40, 5, 9, 1, 41, 8),
        (40, 6, 8, 1, 50, 6),
        (42, 0, 1, 1, 1, 9),
        (42, 1, 7, 500, 10, 6),
        (42, 2, 1, 1, 17, 9),
        (42, 3, 7, 500, 26, 6),
        (42, 4, 1, 1, 33, 9),
        (42, 5, 8, 1, 43, 6),
        (43, 0, 3, 1, 2, 4),
        (43, 1, 2, 1, 7, 8),
        (43, 2, 4, 10, 16, 4),
        (43, 3, 5, 10000, 21, 4),
        (43, 4, 6, 100000, 26, 4),
        (43, 5, 8, 1, 31, 12),
        (43, 6, 9, 1, 44, 8),
        (43, 7, 7, 1, 53, 6),
        (44, 0, 3, 1, 2, 3),
        (44, 1, 2, 1, 6, 8),
        (44, 2, 4, 15, 15, 4),
        (44, 3, 5, 10000, 20, 4),
        (44, 4, 7, 600, 25, 4),
        (44, 5, 9, 1, 30, 12),
        (44, 6, 8, 1, 43, 8),
        (44, 7, 9, 1, 52, 6),
    ];

    /// <summary>The specification's net_missions_timeline.csv: level, row, month given, deadline, month judged.</summary>
    private static readonly (int Level, int Row, int Given, int Deadline, int Judged)[] Timeline =
    [
        (20, 0, 3, 7, 9),
        (20, 1, 9, 17, 18),
        (20, 2, 18, 22, 24),
        (20, 3, 24, 36, 36),
        (22, 0, 3, 12, 12),
        (22, 1, 12, 18, 18),
        (22, 2, 18, 27, 27),
        (22, 3, 27, 33, 33),
        (22, 4, 36, 45, 45),
        (23, 0, 3, 7, 9),
        (23, 1, 9, 18, 18),
        (23, 2, 18, 22, 24),
        (23, 3, 24, 32, 33),
        (24, 0, 3, 7, 9),
        (24, 1, 9, 18, 18),
        (24, 2, 18, 24, 24),
        (24, 3, 24, 36, 36),
        (30, 0, 3, 9, 9),
        (30, 1, 9, 17, 18),
        (30, 2, 18, 22, 24),
        (30, 3, 24, 36, 36),
        (30, 4, 36, 48, 48),
        (32, 0, 3, 12, 12),
        (32, 1, 12, 18, 18),
        (32, 2, 18, 27, 27),
        (32, 3, 27, 33, 33),
        (32, 4, 33, 42, 42),
        (32, 5, 45, 51, 51),
        (33, 0, 3, 7, 9),
        (33, 1, 9, 17, 18),
        (33, 2, 18, 22, 24),
        (33, 3, 24, 28, 30),
        (33, 4, 30, 34, 36),
        (33, 5, 36, 48, 48),
        (34, 0, 3, 7, 9),
        (34, 1, 9, 17, 18),
        (34, 2, 18, 22, 24),
        (34, 3, 24, 28, 30),
        (34, 4, 30, 34, 36),
        (34, 5, 45, 53, 54),
        (40, 0, 3, 9, 9),
        (40, 1, 9, 17, 18),
        (40, 2, 18, 23, 24),
        (40, 3, 24, 28, 30),
        (40, 4, 30, 42, 42),
        (40, 5, 42, 50, 51),
        (40, 6, 51, 57, 57),
        (42, 0, 3, 12, 12),
        (42, 1, 12, 18, 18),
        (42, 2, 18, 27, 27),
        (42, 3, 27, 33, 33),
        (42, 4, 33, 42, 42),
        (42, 5, 45, 51, 51),
        (43, 0, 3, 7, 9),
        (43, 1, 9, 17, 18),
        (43, 2, 18, 22, 24),
        (43, 3, 24, 28, 30),
        (43, 4, 30, 34, 36),
        (43, 5, 36, 48, 48),
        (43, 6, 48, 56, 57),
        (43, 7, 57, 63, 63),
        (44, 0, 3, 6, 6),
        (44, 1, 6, 14, 15),
        (44, 2, 15, 19, 21),
        (44, 3, 21, 25, 27),
        (44, 4, 27, 31, 33),
        (44, 5, 33, 45, 45),
        (44, 6, 45, 53, 54),
        (44, 7, 54, 60, 60),
    ];

    public static TheoryData<int> Levels() => [.. Missions.Select(row => row.Level).Distinct()];

    [Theory]
    [MemberData(nameof(Levels))]
    public void MissionsAreGivenAndJudgedAtTheQuartersEndAsTheTimelineHasIt(int level)
    {
        var simulation = NewSimulation(Missions.Where(row => row.Level == level).Select(row => new NetMissionRule(row.Row, (NetCriterion)row.Code, row.Value, row.Month, row.TimeToDo)));
        var given = new List<(int Row, int Month, int Deadline)>();
        var judged = new List<(int Row, int Month)>();
        for (var month = 1; month <= 72; month++)
        {
            var before = simulation.LastNetMission;
            NetMission.OnEndMonth(simulation, month);
            if (!ReferenceEquals(before, simulation.LastNetMission) && simulation.LastNetMission is { } result)
            {
                judged.Add((result.Index, result.Month));
            }

            var mission = simulation.Hospitals[0].NetMission;
            if (mission.Current is { } row && (given.Count == 0 || given[^1].Row != row.Index))
            {
                given.Add((row.Index, month, mission.Deadline));
            }
        }

        var expected = Timeline.Where(row => row.Level == level).ToList();
        Assert.Equal(expected.Select(row => (row.Row, row.Given, row.Deadline)), given);
        Assert.Equal(expected.Select(row => (row.Row, row.Judged)), judged);
    }

    [Fact]
    public void ARowGivenTwiceCountsAsTheLastAndEmptyRowsAreLeftOut()
    {
        var rules = new LevelRules(0, [], [], netMissions: [new(5, NetCriterion.NotLowestMoney, 1, 31, 12), new(2, NetCriterion.None, 0, 0, 0), new(5, NetCriterion.NotLowestReputation, 1, 44, 8)]);
        Assert.Equal([new NetMissionRule(5, NetCriterion.NotLowestReputation, 1, 44, 8)], rules.NetMissions);
    }

    [Fact]
    public void AMissionJustGivenSaysItsMonthsToDo()
    {
        // Given at the end of March, it is read in April, when three months have passed: the text says four.
        var simulation = NewSimulation([new(0, NetCriterion.BuyPlot, 1, 2, 4)]);
        NetMission.OnEndMonth(simulation, 3);
        var mission = simulation.Hospitals[0].NetMission;
        Assert.Equal(7, mission.Deadline);
        Assert.Equal(4, mission.MonthsLeft(3));
        Assert.Equal(1, mission.MonthsLeft(6));
    }

    [Fact]
    public void ThereAreNoMissionsForASingleHospital()
    {
        var simulation = NewSimulation([new(0, NetCriterion.BuyPlot, 1, 1, 3)], players: 1);
        NetMission.OnEndMonth(simulation, 3);
        Assert.Null(simulation.Hospitals[0].NetMission.Current);
    }

    [Fact]
    public void AnEventCountsOnlyForTheMissionBeingPlayedAndAfterItWasGiven()
    {
        var simulation = NewSimulation([new(0, NetCriterion.Emergency, 1, 1, 3), new(1, NetCriterion.BuyPlot, 1, 1, 3)]);
        var (first, second) = (simulation.Hospitals[0], simulation.Hospitals[1]);
        first.NetMission.Happened(NetCriterion.Emergency); // before the mission: does not count
        NetMission.OnEndMonth(simulation, 3);
        first.NetMission.Happened(NetCriterion.BuyPlot); // another mission's event
        second.NetMission.Happened(NetCriterion.Emergency);
        Assert.False(first.NetMission.Done);
        Assert.True(second.NetMission.Done);

        // Done early, the mission still waits for its deadline; then the next is given at once, not done.
        NetMission.OnEndMonth(simulation, 6);
        Assert.Equal(new NetMissionResult(0, NetCriterion.Emergency, 6, [false, true]), simulation.LastNetMission, ResultComparer.Instance);
        Assert.Equal((NetCriterion.BuyPlot, 9, false), (second.NetMission.Current?.Criterion, second.NetMission.Deadline, second.NetMission.Done));
    }

    [Fact]
    public void TargetsAreCheckedAtTheDeadlineAndTheLowestIsTheOneBelowEveryoneElse()
    {
        var simulation = NewSimulation([], players: 3);
        var hospitals = simulation.Hospitals;
        hospitals[0].Cured = 10;
        hospitals[0].SetReputation(400);
        hospitals[1].SetReputation(400);
        hospitals[2].SetReputation(300);
        hospitals[2].Value = 100_000;

        Assert.True(NetMission.Passed(simulation, hospitals[0], new(0, NetCriterion.Cures, 10, 1, 3)));
        Assert.False(NetMission.Passed(simulation, hospitals[0], new(0, NetCriterion.Cures, 11, 1, 3)));
        Assert.True(NetMission.Passed(simulation, hospitals[0], new(0, NetCriterion.Money, 10_000, 1, 3)));
        Assert.False(NetMission.Passed(simulation, hospitals[0], new(0, NetCriterion.Money, 10_001, 1, 3)));
        Assert.True(NetMission.Passed(simulation, hospitals[2], new(0, NetCriterion.Value, 100_000, 1, 3)));
        Assert.False(NetMission.Passed(simulation, hospitals[1], new(0, NetCriterion.Value, 100_000, 1, 3)));
        Assert.True(NetMission.Passed(simulation, hospitals[0], new(0, NetCriterion.Reputation, 400, 1, 3)));
        Assert.False(NetMission.Passed(simulation, hospitals[2], new(0, NetCriterion.Reputation, 400, 1, 3)));

        // Level with someone is not the lowest; below everyone is.
        var notLowest = new NetMissionRule(0, NetCriterion.NotLowestReputation, 1, 1, 3);
        Assert.True(NetMission.Passed(simulation, hospitals[0], notLowest));
        Assert.True(NetMission.Passed(simulation, hospitals[1], notLowest));
        Assert.False(NetMission.Passed(simulation, hospitals[2], notLowest));

        hospitals[1].Borrow(0, asMuchAsAllowed: false);
        var notPoorest = new NetMissionRule(0, NetCriterion.NotLowestMoney, 1, 1, 3);
        Assert.True(NetMission.Passed(simulation, hospitals[1], notPoorest)); // the loan is taken off: level with the others
        Assert.True(NetMission.Passed(simulation, hospitals[2], notPoorest));
    }

    [Fact]
    public void CuresCountFromWhenTheMissionIsGivenAndFailingOnlyReportsIt()
    {
        var simulation = NewSimulation([new(0, NetCriterion.Cures, 5, 1, 3)]);
        simulation.Hospitals[0].Cured = 20;
        NetMission.OnEndMonth(simulation, 3);
        Assert.Equal(20, simulation.Hospitals[0].NetMission.CuredAtStart);
        simulation.Hospitals[0].Cured = 24;
        simulation.Hospitals[1].Cured = 5;
        NetMission.OnEndMonth(simulation, 6);

        Assert.Equal(new NetMissionResult(0, NetCriterion.Cures, 6, [false, true]), simulation.LastNetMission, ResultComparer.Instance);
        Assert.All(simulation.Hospitals, hospital => Assert.Equal(EndOutcome.None, hospital.Outcome));

        // Nobody did it: everyone plays on.
        Assert.True(new NetMissionResult(0, NetCriterion.Cures, 6, [false, false]).AllFailed);
        Assert.False(simulation.LastNetMission!.AllFailed);
    }

    [Fact]
    public void SavesKeepTheMissionsAndTheLastResult()
    {
        var simulation = NewSimulation([new(0, NetCriterion.Cures, 5, 1, 3), new(3, NetCriterion.Reputation, 600, 2, 4)]);
        NetMission.OnEndMonth(simulation, 3);
        NetMission.OnEndMonth(simulation, 6);
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(simulation.Rules.NetMissions, restored.Rules.NetMissions);
        var mission = restored.Hospitals[1].NetMission;
        Assert.Equal((3, 10, 0b1001), (mission.Current?.Index, mission.Deadline, mission.GivenRows));
        Assert.Equal(simulation.LastNetMission, restored.LastNetMission, ResultComparer.Instance);
    }

    private static Simulation NewSimulation(IEnumerable<NetMissionRule> missions, int players = 2)
    {
        var map = TestMaps.FromRows("....", "....");
        map.SetPlayerCount(players);
        return new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], [], netMissions: missions));
    }

    private sealed class ResultComparer : IEqualityComparer<NetMissionResult?>
    {
        public static readonly ResultComparer Instance = new();

        public bool Equals(NetMissionResult? x, NetMissionResult? y) =>
            x is null ? y is null : y is not null && (x.Index, x.Criterion, x.Month) == (y.Index, y.Criterion, y.Month) && x.Passed.SequenceEqual(y.Passed);

        public int GetHashCode(NetMissionResult? obj) => obj?.Index ?? 0;
    }
}
