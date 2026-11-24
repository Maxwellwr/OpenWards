using OpenWards.Sim.Economy;
using OpenWards.Sim.Import;
using OpenWards.Sim.Maps;

namespace OpenWards.Sim.Tests;

/// <summary>What an imported save of the original brings of the players and the computer opponents for the year's charts.</summary>
public class CompetitorChartsTests
{
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
        Assert.Equal((50_852L, 91, 5, 11_236L, 520, 169, 90), (opponent.Balance, opponent.Cured, opponent.Deaths, opponent.PlayerSalary, opponent.Tiles, opponent.Visitors, opponent.VisitorsThisYear));
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
