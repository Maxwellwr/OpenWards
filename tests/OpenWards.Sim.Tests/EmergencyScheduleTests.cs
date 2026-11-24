using OpenWards.Sim.Calendar;
using OpenWards.Sim.Disasters;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Maps;

namespace OpenWards.Sim.Tests;

/// <summary>When the level's planned emergencies come (CorsixTH's World:nextEmergency, computeNextEmergencyDates).</summary>
public class EmergencyScheduleTests
{
    [Fact]
    public void TheFirstPlannedEmergencyComesOnADayOfItsMonths()
    {
        var simulation = NewSimulation(new PlannedEmergency(0, 4, 5, 4, 6, 16, 75, 400), new PlannedEmergency(1, 8, 10, 3, 5, 32, 75, 500));
        var state = simulation.Hospitals[0].Emergencies;

        Assert.Equal(0, state.NextIndex);
        Assert.Equal(1, state.NextNumber);
        Assert.InRange(state.NextMonth, 4, 5);
        Assert.InRange(state.NextDay, 1, 31);

        Emergencies.NextEmergency(simulation, simulation.Hospitals[0]);
        Assert.Equal(1, state.NextIndex);
        Assert.InRange(state.NextMonth, 8, 10);

        Emergencies.NextEmergency(simulation, simulation.Hospitals[0]);
        Assert.Equal(-1, state.NextIndex);
        Assert.Equal(0, state.NextMonth);
    }

    [Fact]
    public void OneMissingNumberIsSteppedOverAndPastOnesSkipped()
    {
        // Level 3 has no fifth emergency; an emergency whose months are over never comes.
        var simulation = NewSimulation(new PlannedEmergency(0, 0, 0, 1, 1, 16, 75, 400), new PlannedEmergency(2, 3, 3, 1, 1, 16, 75, 400));
        var state = simulation.Hospitals[0].Emergencies;

        Assert.Equal(2, state.NextIndex);
        Assert.Equal(3, state.NextMonth);
    }

    [Fact]
    public void ALevelWithoutEmergenciesHasNone()
    {
        var state = NewSimulation().Hospitals[0].Emergencies;

        Assert.Equal(0, state.NextMonth);
        Assert.Null(state.Current);
    }

    [Fact]
    public void ALevelsRandomEmergencySettingsAreIgnored()
    {
        // The original game has no random emergencies: the planned one comes all the same.
        var map = new LevelMap(10, 10, playerCount: 1);
        var rules = new LevelRules(10_000, [], [], emergencies: [new PlannedEmergency(0, 4, 5, 4, 6, 16, 75, 400)], randomEmergencies: new RandomEmergencies(180, 30));
        var simulation = new Simulation(map, seed: 1, rules: rules);
        var state = simulation.Hospitals[0].Emergencies;

        Assert.Equal(0, state.NextIndex);
        Assert.InRange(state.NextMonth, 4, 5);

        // Without planned emergencies there is none at all.
        var none = new Simulation(new LevelMap(10, 10, playerCount: 1), seed: 1, rules: new LevelRules(10_000, [], [], randomEmergencies: new RandomEmergencies(30, 1)));
        Assert.Equal(0, none.Hospitals[0].Emergencies.NextMonth);
    }

    private static Simulation NewSimulation(params PlannedEmergency[] emergencies)
    {
        var map = new LevelMap(10, 10, playerCount: 1);
        return new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], [], emergencies: emergencies));
    }
}
