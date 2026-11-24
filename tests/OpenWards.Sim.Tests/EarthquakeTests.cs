using OpenWards.Sim.Calendar;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Disasters;
using OpenWards.Sim.Economy;

namespace OpenWards.Sim.Tests;

/// <summary>CorsixTH's earthquakes (earthquake.lua).</summary>
public class EarthquakeTests
{
    [Fact]
    public void TheFirstPlannedEarthquakeIsPlannedAtTheStart()
    {
        var simulation = NewSimulation(new PlannedEarthquake(2, 2, 4));
        var earthquakes = simulation.Earthquakes;

        Assert.True(earthquakes.IsNextPlanned);
        Assert.False(earthquakes.IsActive);
        Assert.Equal(2, earthquakes.StartMonth);
        Assert.InRange(earthquakes.StartDay, 1, 28);
        Assert.Equal(4, earthquakes.Size);
        Assert.Equal(1, earthquakes.PlannedSoFar);
    }

    [Fact]
    public void ALevelWithoutEarthquakesPlansNone()
    {
        Assert.False(NewSimulation().Earthquakes.IsNextPlanned);
        Assert.False(NewSimulation(new PlannedEarthquake(1, 3, 0)).Earthquakes.IsNextPlanned);
    }

    [Fact]
    public void AWarningQuakeComesFiveToNineDaysBeforeTheMainOne()
    {
        var simulation = NewSimulation(new PlannedEarthquake(1, 1, 6), new PlannedEarthquake(3, 3, 2));
        var earthquakes = simulation.Earthquakes;
        StepUntilActive(simulation);
        // It starts at the end of its day.
        var day = GameDate.FromHours(simulation.Tick - 1);
        Assert.Equal(1, day.Month);
        Assert.Equal(earthquakes.StartDay, day.Day);
        Assert.Equal(GameDate.HoursPerDay - 1, day.Hour);

        var warning = earthquakes.WarningHours;
        var shakes = new List<EarthquakeShake>();
        while (earthquakes.IsActive)
        {
            simulation.Step();
            shakes.Add(earthquakes.Shake);
        }

        // The warning quake for 25 hours, quiet until the main quake 5 to 9 days later, then six rounds 16 hours apart.
        Assert.InRange(warning, 5 * GameDate.HoursPerDay, 9 * GameDate.HoursPerDay);
        Assert.Equal(0, warning % GameDate.HoursPerDay);
        Assert.Equal(warning + 97, shakes.Count);
        Assert.All(shakes[..25], shake => Assert.Equal(EarthquakeShake.Small, shake));
        Assert.All(shakes[25..warning], shake => Assert.Equal(EarthquakeShake.None, shake));
        Assert.All(shakes[warning..(warning + 48)], shake => Assert.Equal(EarthquakeShake.Small, shake));
        Assert.All(shakes[(warning + 48)..(warning + 64)], shake => Assert.Equal(EarthquakeShake.Large, shake));
        Assert.All(shakes[(warning + 64)..(warning + 96)], shake => Assert.Equal(EarthquakeShake.Small, shake));
        Assert.Equal(EarthquakeShake.None, shakes[^1]);

        // The next one is planned.
        Assert.True(earthquakes.IsNextPlanned);
        Assert.Equal(3, earthquakes.StartMonth);
        Assert.Equal(2, earthquakes.RemainingDamage);
        Assert.Equal(2, earthquakes.PlannedSoFar);
    }

    [Fact]
    public void AnEntryWhoseMonthsHavePassedIsSkipped()
    {
        var earthquakes = Earthquakes.Start([new PlannedEarthquake(0, 0, 5), new PlannedEarthquake(2, 4, 3)], new SimRandom(5));

        Assert.True(earthquakes.IsNextPlanned);
        Assert.InRange(earthquakes.StartMonth, 2, 4);
        Assert.Equal(3, earthquakes.Size);
        Assert.Equal(2, earthquakes.PlannedSoFar);
    }

    [Fact]
    public void TheCheatBringsAnEarthquakeTodayButNotDuringOne()
    {
        var simulation = NewSimulation();
        var earthquakes = simulation.Earthquakes;

        Run(simulation, CheatKind.Earthquake);
        Assert.True(simulation.Hospitals[0].Cheated);
        Assert.InRange(earthquakes.Size, 1, 6);
        Assert.Equal(earthquakes.Size, earthquakes.RemainingDamage);
        StepUntilActive(simulation);
        Assert.Equal(1, GameDate.FromHours(simulation.Tick - 1).Day);

        var other = NewSimulation(new PlannedEarthquake(1, 1, 3));
        StepUntilActive(other);
        Run(other, CheatKind.Earthquake);
        Assert.False(other.Hospitals[0].Cheated);
    }

    [Fact]
    public void TurningEarthquakesOffStopsTheShakingAndTheNextOne()
    {
        var simulation = NewSimulation(new PlannedEarthquake(1, 1, 3));
        var earthquakes = simulation.Earthquakes;
        StepUntilActive(simulation);
        simulation.Step();
        Assert.Equal(EarthquakeShake.Small, earthquakes.Shake);

        Run(simulation, CheatKind.ToggleEarthquakes);
        Assert.True(earthquakes.IsDisabled);
        Assert.Equal(EarthquakeShake.None, earthquakes.Shake);
        var timer = earthquakes.WarningTimer;
        simulation.Step();
        Assert.Equal(timer, earthquakes.WarningTimer);

        Run(simulation, CheatKind.ToggleEarthquakes);
        Assert.False(earthquakes.IsDisabled);
        Assert.True(simulation.Hospitals[0].Cheated);
    }

    [Fact]
    public void SavesKeepAnEarthquakeUnderWay()
    {
        var simulation = NewSimulation(new PlannedEarthquake(1, 1, 5), new PlannedEarthquake(4, 6, 1));
        StepUntilActive(simulation);
        for (var i = 0; i < 640; i++)
        {
            simulation.Step();
        }

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal([new PlannedEarthquake(1, 1, 5), new PlannedEarthquake(4, 6, 1)], restored.Rules.Earthquakes);
        for (var i = 0; i < 200; i++)
        {
            simulation.Step();
            restored.Step();
        }

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.False(restored.Earthquakes.IsActive);
    }

    private static void StepUntilActive(Simulation simulation)
    {
        for (var i = 0; i < GameDate.HoursPerDay * 62 && !simulation.Earthquakes.IsActive; i++)
        {
            simulation.Step();
        }

        Assert.True(simulation.Earthquakes.IsActive);
    }

    private static void Run(Simulation simulation, CheatKind cheat)
    {
        simulation.Schedule(0, new CheatCommand(cheat));
        simulation.Step();
    }

    private static Simulation NewSimulation(params PlannedEarthquake[] earthquakes) =>
        new(TestMaps.FromRows("....", "...."), seed: 3, rules: new LevelRules(1000, [], [], earthquakes: earthquakes));
}
