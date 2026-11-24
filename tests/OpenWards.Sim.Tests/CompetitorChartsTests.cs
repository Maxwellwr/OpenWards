using OpenWards.Sim.Economy;
using OpenWards.Sim.Import;
using OpenWards.Sim.Maps;

namespace OpenWards.Sim.Tests;

/// <summary>What the year's charts show of the computer opponents, fitted to saves of the original.</summary>
public class CompetitorChartsTests
{
    [Theory]

    // MAXWELL.H3, level 4 in December: MULTIVAC (532) 91 cured and 5 dead of 169, DEEP THOUGHT (763) 131 and none of 178.
    [InlineData(532, 169, 80, 100, 3, 8)]
    [InlineData(763, 178, 115, 135, 0, 0)]

    // MAXWELL.H6, level 2 in July: HAL (726) 61 cured and none dead of 77.
    [InlineData(726, 77, 50, 62, 0, 1)]
    public void AnOpponentCuresAndLosesPatientsByItsReputation(int reputation, int patients, int fewestCured, int mostCured, int fewestDeaths, int mostDeaths)
    {
        var opponent = new CompetitorHospital(1, "HAL", reputation, reputation);
        for (var i = 0; i < patients; i++)
        {
            opponent.CountNewPatient();
        }

        Assert.Equal(patients, opponent.Visitors);
        Assert.InRange(opponent.Cured, fewestCured, mostCured);
        Assert.InRange(opponent.Deaths, fewestDeaths, mostDeaths);
    }

    [Fact]
    public void AMonthPaysAnOpponentForItsCures()
    {
        var opponent = new CompetitorHospital(1, "HOLLY", reputation: 1000, standing: 700, balance: 50_000, tiles: 520);
        Assert.Equal(0, opponent.Value);
        Assert.Equal(Hospital.StartPlayerSalary, opponent.PlayerSalary);
        while (opponent.CuredThisMonth < 20)
        {
            opponent.CountNewPatient();
        }

        opponent.OnEndMonth(1, new SimRandom(3));

        Assert.Equal(50_000 + (20 * CompetitorHospital.CureIncome) - CompetitorHospital.MonthlyCosts, opponent.Balance);
        Assert.Equal((520 * CompetitorHospital.ValuePerTile) + (200 * CompetitorHospital.ValuePerStanding), opponent.Value);
        Assert.Equal(0, opponent.CuredThisMonth);

        // A month without cures costs its running costs. The salary stays the same all year.
        opponent.OnEndMonth(2, new SimRandom(3));
        Assert.Equal(50_000 + (20 * CompetitorHospital.CureIncome) - (2 * CompetitorHospital.MonthlyCosts), opponent.Balance);
        Assert.Equal(Hospital.StartPlayerSalary, opponent.PlayerSalary);
    }

    [Fact]
    public void AYearsEndRaisesAnOpponentsSalaryAndValue()
    {
        // MULTIVAC on level 4: 11,236 and 51,660 on 31 December, 12,247 and 53,690 on 1 January.
        var opponent = new CompetitorHospital(1, "MULTIVAC");
        opponent.Import(new ImportedCompetitor("MULTIVAC", 532, 169, Value: 51_660, Salary: 11_236));
        opponent.OnEndYear(90, 300);

        Assert.Equal(51_660 + CompetitorHospital.YearEndValueRise, opponent.Value);
        Assert.Equal(12_247, opponent.PlayerSalary);
        Assert.Equal(0, opponent.VisitorsThisYear);
    }

    [Fact]
    public void AnImportedGameKeepsThePlayersNameThroughASave()
    {
        var simulation = NewSimulation(new LevelRules(0, [], []));
        GameImport.Apply(simulation, new ImportedGame { Month = 12, Day = 25, PlayerName = "MAXWELL" });
        Assert.Equal("MAXWELL", simulation.Hospitals[0].Name);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        Assert.Equal("MAXWELL", Simulation.Load(stream).Hospitals[0].Name);
    }

    [Fact]
    public void AnOpponentStartsWithTheLevelsMoneyAndThePlayersPlotAndIsSaved()
    {
        var rules = new LevelRules(40_000, [], [], competitors: [new PlannedCompetitor(4, "MULTIVAC")]);
        var simulation = NewSimulation(rules);
        var opponent = simulation.Competitors[0];
        Assert.Equal(40_000, opponent.Balance);
        Assert.Equal(simulation.Map.ParcelTileCount(1), opponent.Tiles);
        for (var i = 0; i < 9; i++)
        {
            opponent.CountNewPatient();
        }

        while (simulation.Date.MonthOfGame == 1)
        {
            simulation.Step();
        }

        Assert.NotEqual(0, opponent.Value);
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream).Competitors[0];
        Assert.Equal(
            (opponent.Balance, opponent.Value, opponent.Cured, opponent.Deaths, opponent.CuredThisMonth, opponent.PlayerSalary, opponent.Tiles),
            (restored.Balance, restored.Value, restored.Cured, restored.Deaths, restored.CuredThisMonth, restored.PlayerSalary, restored.Tiles));
    }

    [Fact]
    public void AnImportedGameTakesWhatTheChartsShowOfTheOpponents()
    {
        var rules = new LevelRules(0, [], [], competitors: [new PlannedCompetitor(4, "MULTIVAC")]);
        var simulation = NewSimulation(rules);

        // MAXWELL.H3: MULTIVAC on level 4 in December.
        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 12,
            Day = 31,
            Competitors = [new ImportedCompetitor("MULTIVAC", 532, 169, VisitorsThisYear: 90, Money: 50_852, Value: 51_660, Cured: 91, Deaths: 5, Salary: 11_236, Tiles: 520)],
        });

        var opponent = simulation.Competitors[0];
        Assert.Equal((50_852L, 51_660L, 91, 5, 11_236L, 520, 169, 90), (opponent.Balance, opponent.Value, opponent.Cured, opponent.Deaths, opponent.PlayerSalary, opponent.Tiles, opponent.Visitors, opponent.VisitorsThisYear));
    }

    [Fact]
    public void AnImportedGameTakesThePlayersNameSalaryYearAndThisYearsCounts()
    {
        var simulation = NewSimulation(new LevelRules(0, [], []));

        // MAXWELL.H3: 25 December of the third year, 81 cured of whom 33 this year, 189 patients of whom 75 this year.
        GameImport.Apply(simulation, new ImportedGame
        {
            PlayerName = "MAXWELL",
            PlayerSalary = 69_935,
            Year = 3,
            Month = 12,
            Day = 25,
            Visitors = 189,
            Cured = 81,
            VisitorsThisYear = 75,
            CuredThisYear = 33,
            DeathsThisYear = 0,
        });

        var hospital = simulation.Hospitals[0];
        Assert.Equal((3, 12), (simulation.Date.Year, simulation.Date.Month));
        Assert.Equal(("MAXWELL", 69_935L), (hospital.Name, hospital.PlayerSalary));
        Assert.Equal((75, 33, 0), (hospital.VisitorsThisYear, hospital.CuredThisYear, hospital.DeathsThisYear));

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        Assert.Equal("MAXWELL", Simulation.Load(stream).Hospitals[0].Name);
    }

    private static Simulation NewSimulation(LevelRules rules)
    {
        var map = new LevelMap(6, 6, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Floor = 1;
                map[x, y].Set(TileFlags.Passable, true);
                map[x, y].Parcel = (ushort)(y < 3 ? 1 : 0);
            }
        }

        map[3, 1].Set(TileFlags.Hospital, true);
        map.UpdatePathfinding();
        return new Simulation(map, seed: 2, rules: rules);
    }
}
