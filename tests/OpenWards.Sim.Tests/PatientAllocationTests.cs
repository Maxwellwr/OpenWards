using OpenWards.Sim.Calendar;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Import;
using OpenWards.Sim.Maps;
using OpenWards.Sim.People;

namespace OpenWards.Sim.Tests;

/// <summary>Which hospital a new patient goes to.</summary>
public class PatientAllocationTests
{
    [Fact]
    public void HospitalsTakeTurnsWhetherOrNotThePatientComes()
    {
        var simulation = NewSimulation(players: 3);

        var chosen = new List<int>();
        for (var i = 0; i < 7; i++)
        {
            var player = PatientArrivals.ChooseHospital(simulation);
            chosen.Add(player);

            // Without a reception desk nobody comes, but the turn is used up.
            Assert.Null(simulation.Arrivals.SpawnPatient(simulation, player));
        }

        Assert.Equal([0, 1, 2, 0, 1, 2, 0], chosen);
        Assert.Equal([3L, 2L, 2L], simulation.Hospitals.Select(h => h.SpawnAttempts));

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(1, PatientArrivals.ChooseHospital(restored));
    }

    [Fact]
    public void ComputerOpponentsTakeTheirTurnsAndOnlyCountTheirPatients()
    {
        var rules = new LevelRules(0, [], [], competitors: [new PlannedCompetitor(13, "CORSIX"), new PlannedCompetitor(14, "ROUJIN")]);
        var simulation = NewSimulation(players: 1, rules);
        Assert.Equal(["CORSIX", "ROUJIN"], simulation.Competitors.Select(c => c.Name));
        foreach (var competitor in simulation.Competitors)
        {
            competitor.Open();
        }

        var chosen = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            var hospital = PatientArrivals.ChooseHospital(simulation);
            chosen.Add(hospital);
            Assert.Null(simulation.Arrivals.SpawnPatient(simulation, hospital));
        }

        Assert.Equal([0, 1, 2, 0, 1], chosen);
        Assert.Equal(2L, simulation.Hospitals[0].SpawnAttempts);
        Assert.Equal(0, simulation.Hospitals[0].Visitors);
        Assert.Equal([2, 1], simulation.Competitors.Select(c => c.Visitors));
        Assert.Equal([2L, 1L], simulation.Competitors.Select(c => c.SpawnAttempts));
        Assert.Empty(simulation.Entities.OfType<Entities.Humanoid>());

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal([2, 1], restored.Competitors.Select(c => c.VisitorsThisYear));

        // Hospital:onEndYear starts the year's count again.
        while (simulation.Date.Year == restored.Date.Year)
        {
            simulation.Step();
        }

        // Once the hospital opened, the year's patients went to it and the opponents, first in turn, then by reputation.
        Assert.Equal([0, 0], simulation.Competitors.Select(c => c.VisitorsThisYear));
        Assert.All(simulation.Competitors, c => Assert.True(c.Visitors > 2));
    }

    [Fact]
    public void AfterTheFirstMonthsTheBetterReputationDrawsMorePatients()
    {
        var simulation = NewSimulation(players: 1, ByReputation(randomChance: 0));
        var rival = simulation.Competitors[0];
        rival.SetReputation(Hospital.MaxReputation);

        // AllocIndRep 2 × reputation × 100 / 100: the overall reputation (AllocTotalRep) does not count.
        Assert.Equal(1000, PatientArrivals.Appeal(simulation, 0, disease: null));
        Assert.Equal(2000, PatientArrivals.Appeal(simulation, 1, disease: null));

        var toPlayer = Enumerable.Range(0, 4000).Count(_ => PatientArrivals.ChooseHospital(simulation) == 0);
        Assert.InRange(toPlayer, 1233, 1433);

        // The same reputations share them evenly.
        rival.SetReputation(simulation.Hospitals[0].Reputation);
        toPlayer = Enumerable.Range(0, 4000).Count(_ => PatientArrivals.ChooseHospital(simulation) == 0);
        Assert.InRange(toPlayer, 1900, 2100);
    }

    [Fact]
    public void ADearTreatmentPutsPatientsOffAndACheapOneDrawsThem()
    {
        var simulation = NewSimulation(players: 1, ByReputation(randomChance: 0));
        var hospital = simulation.Hospitals[0];
        var rules = simulation.Rules.Diseases;
        var disease = rules.AvailableDiseases().First(d => hospital.PeekCase(d, rules).Reputation is not null);

        Assert.Equal(1000, PatientArrivals.Appeal(simulation, 0, disease));

        // k = expected price × 100 / price: twice the price is worth half...
        hospital.CaseOf(disease, rules).PricePercent = 200;
        Assert.Equal(500, PatientArrivals.Appeal(simulation, 0, disease));

        // ...and half the price as much as half again, no more.
        hospital.CaseOf(disease, rules).PricePercent = 50;
        Assert.Equal(1500, PatientArrivals.Appeal(simulation, 0, disease));

        // A better reputation for the disease raises both prices alike, so k stays.
        hospital.CaseOf(disease, rules).PricePercent = 200;
        hospital.CaseOf(disease, rules).Reputation = 800;
        Assert.Equal(800, PatientArrivals.Appeal(simulation, 0, disease)); // 2 × 800 × 50 / 100

        // Choosing a hospital only peeks at the casebook.
        var other = rules.AvailableDiseases().First(d => !hospital.Casebook.ContainsKey(d.Id));
        _ = PatientArrivals.Appeal(simulation, 0, other);
        Assert.False(hospital.Casebook.ContainsKey(other.Id));
    }

    [Fact]
    public void NowAndThenAPatientGoesToAHospitalAtRandom()
    {
        var simulation = NewSimulation(players: 1, ByReputation(randomChance: 0));
        simulation.Hospitals[0].SetReputation(Hospital.MinReputation);
        Assert.Equal(0, simulation.Hospitals[0].Reputation);
        Assert.DoesNotContain(0, Enumerable.Range(0, 200).Select(_ => PatientArrivals.ChooseHospital(simulation)));

        // gbv.AllocRand 4: a quarter go at random, half of those to the player of two hospitals.
        simulation = NewSimulation(players: 1, ByReputation(randomChance: 4));
        simulation.Hospitals[0].SetReputation(Hospital.MinReputation);
        var toPlayer = Enumerable.Range(0, 4000).Count(_ => PatientArrivals.ChooseHospital(simulation) == 0);
        Assert.InRange(toPlayer, 400, 600);
    }

    [Fact]
    public void PatientsComeByReputationOnceTheDelayIsOver()
    {
        var rules = new LevelRules(0, [], [], competitors: [new PlannedCompetitor(13, "CORSIX")], allocation: new PatientAllocationRules(Delay: 1, 1, 2, 4));
        var simulation = NewSimulation(players: 1, rules);
        simulation.Competitors[0].Open();
        Assert.True(rules.Allocation.InTurnIn(simulation.Date.MonthOfGame));
        Assert.False(rules.Allocation.InTurnIn(simulation.Date.MonthOfGame + 1));

        // In the first month the hospitals take turns; then a much better opponent takes nearly all.
        Assert.Equal([0, 1, 0], Enumerable.Range(0, 3).Select(_ => Send(simulation)));
        while (simulation.Date.MonthOfGame == 1)
        {
            simulation.Step();
        }

        simulation.Hospitals[0].SetReputation(Hospital.MinReputation);
        var before = simulation.Competitors[0].Visitors;
        var toPlayer = Enumerable.Range(0, 400).Count(_ => Send(simulation) == 0);
        Assert.InRange(toPlayer, 20, 80);
        Assert.Equal(400 - toPlayer, simulation.Competitors[0].Visitors - before);

        int Send(Simulation game)
        {
            var hospital = PatientArrivals.ChooseHospital(game);
            game.Arrivals.SpawnPatient(game, hospital);
            return hospital;
        }
    }

    [Fact]
    public void AnImportedGameTakesTheOpponentsReputationsByName()
    {
        var rules = new LevelRules(0, [], [], competitors: [new PlannedCompetitor(4, "MULTIVAC"), new PlannedCompetitor(5, "HOLLY"), new PlannedCompetitor(6, "DEEP THOUGHT")]);
        var simulation = NewSimulation(players: 1, rules);

        // MAXWELL.H7, level 4 in September; the third has a name the level does not know, so takes the one left.
        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 9,
            Day = 25,
            Competitors = [new ImportedCompetitor("HOLLY", 644, 151), new ImportedCompetitor("MULTIVAC", 516, 137), new ImportedCompetitor("ORAC", 742, 145)],
        });

        Assert.Equal([(516, 137), (644, 151), (742, 145)], simulation.Competitors.Select(c => (c.Reputation, c.Visitors)));
    }

    /// <summary>One opponent, and patients by reputation from the start.</summary>
    private static LevelRules ByReputation(int randomChance) =>
        new(0, [], [], competitors: [new PlannedCompetitor(13, "CORSIX")], allocation: new PatientAllocationRules(Delay: 0, 1, 2, randomChance));

    [Theory]
    [InlineData(9, 15)]
    [InlineData(9, 1)]
    [InlineData(3, 10)]
    public void ImportedGameGetsTheSpawnRateOfTheMonthsSkipped(int month, int day)
    {
        // Played from the start: the calendar runs up to the first hour of the imported date, where the import puts it.
        var played = NewSimulation(players: 1);
        var target = new GameDate(1, month, day, 0).ToHours();
        while (played.Hours < target)
        {
            played.Step();
        }

        var imported = NewSimulation(players: 1);
        GameImport.Apply(imported, new ImportedGame { Month = month, Day = day });

        Assert.Equal(played.Date, imported.Date);
        Assert.Equal((played.Arrivals.SpawnRate, played.Arrivals.MonthlyIncrease), (imported.Arrivals.SpawnRate, imported.Arrivals.MonthlyIncrease));

        // The month's patients are spread over its days, not left to the level's first month.
        Assert.True(imported.Arrivals.SpawnDates.Sum() > played.Rules.Population[0].Change);
    }

    private static Simulation NewSimulation(int players, LevelRules? rules = null)
    {
        var map = new LevelMap(6, 6, players);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Floor = 1;
                map[x, y].Set(TileFlags.Passable, true);
            }
        }

        // A hospital tile, so that the map has spawn points.
        map[3, 3].Set(TileFlags.Hospital, true);
        map.UpdatePathfinding();
        return new Simulation(map, seed: 2, rules: rules);
    }
}
