using OpenWards.Sim.Calendar;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Import;
using OpenWards.Sim.Maps;

namespace OpenWards.Sim.Tests;

/// <summary>The player's own salary and the offer on winning (CorsixTH's player_salary and salary_offer).</summary>
public class PlayerSalaryTests
{
    [Fact]
    public void TheSalaryStaysTheSameAllYearAndRisesOnTheFirstOfJanuaryByTheScore()
    {
        // Balance at least 20,000: the starting 10,000 scores 50, a raise of 5 %.
        var simulation = NewSimulation(new LevelRules(10_000, [], [], scoreMaxIncrease: 300, endConditions: BalanceGoal(20_000)));
        var hospital = simulation.Hospitals[0];
        Assert.Equal(10_000, hospital.PlayerSalary);
        StepToNextMonth(simulation);
        Assert.Equal(10_000, hospital.PlayerSalary);

        while (simulation.Date.Year == 1)
        {
            simulation.Step();
        }

        Assert.Equal(10_500, hospital.PlayerSalary);

        // Rounded down: MAXWELL.H3's 69,935 became 74,131 (6 %) in MAXWELL.H4.
        Assert.Equal(74_131, Hospital.RaisedSalary(69_935, 6));
    }

    [Theory]
    [InlineData(0, 10_000)] // Up to 10: nothing.
    [InlineData(10, 10_000)]
    [InlineData(11, 10_500)]
    [InlineData(60, 10_500)]
    [InlineData(61, 10_600)]
    [InlineData(75, 10_700)]
    [InlineData(80, 10_800)]
    [InlineData(90, 10_900)]
    [InlineData(95, 11_000)]
    [InlineData(96, 15_000)] // Only a level won and played on reaches the 50 %.
    [InlineData(100, 15_000)]
    public void TheYearsRaiseFollowsTheTable(int score, long raised) =>
        Assert.Equal(raised, Hospital.RaisedSalary(10_000, score, Hospital.StartPlayerSalary, 300));

    [Fact]
    public void AboveTheCeilingTheRaiseIsSmall()
    {
        // Above three times the start (ScoreMaxInc 300): 2 % at 90, nothing at 80.
        Assert.Equal(30_001 * 102 / 100, Hospital.RaisedSalary(30_001, 90, 10_000, 300));
        Assert.Equal(30_001, Hospital.RaisedSalary(30_001, 80, 10_000, 300));
        Assert.Equal(30_000 * 109 / 100, Hospital.RaisedSalary(30_000, 90, 10_000, 300));

        // Above a million always, and never above 200 million.
        Assert.Equal(1_000_001 * 104 / 100, Hospital.RaisedSalary(1_000_001, 100, 1_000_000, 300));
        Assert.Equal(Hospital.MaxPlayerSalary, Hospital.RaisedSalary(Hospital.MaxPlayerSalary - 1, 100, Hospital.MaxPlayerSalary, 300));
    }

    [Fact]
    public void TheScoreIsTheBestGroupsMeanOfItsGoalsPercentages()
    {
        var conditions = new EndConditions(
            [
                new(EndCriterion.Reputation, 1, AtLeast: true, 600, 0, 500),
                new(EndCriterion.Balance, 1, AtLeast: true, 20_000, 0, 0),
                new(EndCriterion.PercentageKilled, 2, AtLeast: false, 10, 0, 0),
            ],
            []);
        decimal? Measure(EndCriterion criterion) => criterion switch
        {
            EndCriterion.Reputation => 600, // Met: 100.
            EndCriterion.Balance => 15_000, // 75.
            EndCriterion.PercentageKilled => 40, // At most 10: 10 × 100 / 40 = 25.
            _ => null,
        };

        Assert.Equal(87, conditions.Score(Measure));
        Assert.Equal(100, conditions.Score(c => c == EndCriterion.PercentageKilled ? 5 : 0));

        // A goal not met is always below 100, however close.
        Assert.Equal(99, new EndConditions([new(EndCriterion.Balance, 1, AtLeast: true, 20_000, 0, 0)], []).Score(_ => 19_999.9m));
        Assert.Equal(0, EndConditions.None.Score(_ => 1));
    }

    [Fact]
    public void WinningOffersFiveToNinePercentMoreUnlessTheHospitalCheated()
    {
        var offers = new HashSet<long>();
        for (var seed = 1UL; seed <= 40; seed++)
        {
            var simulation = new Simulation(new LevelMap(6, 6, playerCount: 1), seed: seed * 0x9E3779B97F4A7C15UL, rules: LevelRules.Free);
            var hospital = simulation.Hospitals[0];
            hospital.End(EndOutcome.Won, null, simulation.Random);
            offers.Add(hospital.SalaryOffer);
            Assert.Equal(1, hospital.SalaryOffers);
        }

        Assert.Subset(new HashSet<long> { 10_500, 10_600, 10_700, 10_800, 10_900 }, offers);
        Assert.Contains(10_700L, offers);

        // A hospital that cheated before gets no bonus. (The win cheat itself marks the hospital only afterwards, as in
        // CorsixTH.)
        var cheater = NewSimulation(LevelRules.Free);
        cheater.Schedule(0, new CheatCommand(CheatKind.Money));
        cheater.Schedule(0, new CheatCommand(CheatKind.WinLevel));
        cheater.Step();
        Assert.Equal(EndOutcome.Won, cheater.Hospitals[0].Outcome);
        Assert.Equal(10_000, cheater.Hospitals[0].SalaryOffer);
    }

    [Fact]
    public void TurnedDownTheOfferComesAgainAtAQuartersEndButNotTheFirst()
    {
        // Won at the end of March: the balance goal of 5,000 is met from the start.
        var simulation = NewSimulation(new LevelRules(10_000, [], [], endConditions: BalanceGoal(5_000)));
        var hospital = simulation.Hospitals[0];
        StepToMonth(simulation, 4);
        Assert.Equal(EndOutcome.Won, hospital.Outcome);
        Assert.Equal(1, hospital.SalaryOffers);

        simulation.Schedule(0, new DeclineSalaryOfferCommand());
        simulation.Step();
        Assert.Equal(0, hospital.SalaryOffer);
        Assert.True(hospital.IsSalaryOfferDeclined);

        // June's check is skipped, September's offers again, from the salary as it is then.
        StepToMonth(simulation, 7);
        Assert.Equal(1, hospital.SalaryOffers);
        StepToMonth(simulation, 10);
        Assert.Equal(2, hospital.SalaryOffers);
        Assert.InRange(hospital.SalaryOffer, 10_500, 10_900);

        // The year's raise of a winner playing on: 50 %.
        simulation.Schedule(0, new DeclineSalaryOfferCommand());
        simulation.Step();
        StepToMonth(simulation, 1);
        Assert.Equal(15_000, hospital.PlayerSalary);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(2, restored.Hospitals[0].SalaryOffers);
        Assert.True(restored.Hospitals[0].SkipsNextSalaryOffer);
    }

    [Fact]
    public void ABroughtSalaryIsAlsoTheCeilingsStart()
    {
        var simulation = NewSimulation(LevelRules.Free);
        var hospital = simulation.Hospitals[0];
        hospital.BringSalary(25_000);
        Assert.Equal(25_000, hospital.PlayerSalary);
        Assert.Equal(25_000, hospital.StartSalary);
    }

    [Fact]
    public void AnOpponentsYearsRaiseGoesByItsScore()
    {
        // MULTIVAC on level 4: 11,236 on 31 December, 12,247 (9 %) on 1 January.
        var opponent = new CompetitorHospital(1, "MULTIVAC");
        opponent.Import(new ImportedCompetitor("MULTIVAC", 532, 169, Value: 51_660, Salary: 11_236));
        opponent.OnEndYear(90, 300);
        Assert.Equal(12_247, opponent.PlayerSalary);
        Assert.Equal(532, opponent.Measure(EndCriterion.Reputation));
    }

    [Fact]
    public void LevelThreeRaisesTheSalaryAtTheEndOfTheThirdYear()
    {
        // No goals: the score is 0 and the year's raise nothing, but level 3's third year adds 8,000 to 20,000.
        var simulation = NewSimulation(new LevelRules(10_000, [], [], levelNumber: 3));
        var hospital = simulation.Hospitals[0];
        while (simulation.Date.Year < 3 || simulation.Date.Month < 12 || simulation.Date.Day < 31)
        {
            simulation.Step();
        }

        var before = hospital.PlayerSalary;
        StepToNextMonth(simulation);
        Assert.InRange(hospital.PlayerSalary - before, 8000, 20_000);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(3, restored.Rules.LevelNumber);
    }

    private static EndConditions BalanceGoal(int balance) =>
        new([new(EndCriterion.Balance, 1, AtLeast: true, balance, 0, 0)], []);

    private static void StepToMonth(Simulation simulation, int month)
    {
        do
        {
            StepToNextMonth(simulation);
        }
        while (simulation.Date.Month != month);
    }

    private static void StepToNextMonth(Simulation simulation)
    {
        var month = simulation.Date.Month;
        while (simulation.Date.Month == month)
        {
            simulation.Step();
        }
    }

    private static Simulation NewSimulation(LevelRules rules) => new(new LevelMap(6, 6, playerCount: 1), seed: 5, rules: rules);
}
