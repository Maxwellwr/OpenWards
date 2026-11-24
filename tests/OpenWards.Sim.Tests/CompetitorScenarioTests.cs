using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Staff;

namespace OpenWards.Sim.Tests;

/// <summary>The computer opponents' scenario and their patients' quick stay, as the logic specification describes the original.</summary>
public class CompetitorScenarioTests
{
    [Fact]
    public void AnOpponentStartsByHiringBuildingAndOpening()
    {
        var simulation = NewSimulation(startCash: 100_000);
        var opponent = simulation.Competitors[0];
        Assert.False(opponent.IsOpen);
        Assert.Equal(100_000, opponent.Balance);

        // The six actions of the start, each decided on and then carried out, take a few weeks.
        var days = 0;
        while (days < 120 && (opponent.ScenarioStep < CompetitorHospital.StartScenario.Count || opponent.Pending is not null))
        {
            StepDay(simulation);
            days++;
        }

        Assert.InRange(days, 10, 60);
        Assert.True(opponent.IsOpen, $"retries {string.Join(", ", opponent.Retries)}, staff {simulation.AvailableStaff(StaffClass.Receptionist).Count}");
        Assert.Empty(opponent.Retries);
        Assert.True(opponent.HasRoom("gp"));
        Assert.Equal(1, opponent.ObjectCount("reception_desk"));
        Assert.Equal(CompetitorHospital.StartBenches, opponent.ObjectCount("bench"));
        Assert.Equal(1, opponent.StaffCount(StaffClass.Receptionist));
        Assert.Equal(1, opponent.StaffCount(StaffClass.Doctor));

        // The room, the desk and the benches, and 1.2 times each wage.
        var wages = opponent.Staff.Sum(member => (long)member.Wage * 6 / 5);
        Assert.Equal(100_000 - 2_000 - 500 - (4 * 100) - wages, opponent.Balance);
    }

    [Fact]
    public void ThePausesBetweenActionsGoByItsSpeedAndSkill()
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = new CompetitorHospital(1, "ORAC", personality: new CompetitorPersonality(Skill: 1, StaffLevels: 5, Speed: 40, Comfort: 2, GuessAt: 75));

        // The first decision is due at once: then ⌊40 × 33 / 10⌋ ticks until the action, ⌊40 × 25 / 10⌋ after a hire.
        opponent.Think(simulation);
        Assert.Equal(new CompetitorAction(CompetitorActionKind.Hire, "3"), opponent.Pending);
        Assert.Equal(simulation.Tick + 132, opponent.NextActionTick);
    }

    [Fact]
    public void AFailedActionWaitsToBeTriedAgain()
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = new CompetitorHospital(1, "ORAC", personality: new CompetitorPersonality(1, 5, 1, 2, 75));

        // Without money it cannot hire a receptionist. The next decision tries again, and when that fails too the one
        // after takes the scenario's next action.
        Think(simulation, opponent, times: 2);
        Assert.Equal([new CompetitorAction(CompetitorActionKind.Hire, "3")], opponent.Retries);
        Think(simulation, opponent, times: 1);
        Assert.Equal(new CompetitorAction(CompetitorActionKind.Hire, "3"), opponent.Pending);
        Assert.Empty(opponent.Retries);
        Think(simulation, opponent, times: 2);
        Assert.Equal(new CompetitorAction(CompetitorActionKind.Buy, "reception_desk", 1), opponent.Pending);
        Assert.Single(opponent.Retries);
    }

    [Theory]
    [InlineData(StaffClass.Doctor, 0, 100, 1)]
    [InlineData(StaffClass.Doctor, 9, 30, 30)]
    [InlineData(StaffClass.Handyman, 5, 30, 6)]
    [InlineData(StaffClass.Receptionist, 0, 30, 0)]
    public void ItWantsStaffForItsPatients(StaffClass staffClass, int staffLevels, int patients, int wanted)
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = new CompetitorHospital(1, "ORAC", personality: new CompetitorPersonality(10, staffLevels, 50, 2, 75));
        opponent.Open();
        for (var i = 0; i < patients; i++)
        {
            opponent.Admit(simulation, disease: null);
        }

        // A patient sent home without a GP's office stays in the books until the outcomes reach the hospital.
        Assert.Equal(patients, opponent.PatientsInHospital);
        Assert.Equal(wanted, opponent.WantedStaff(staffClass));
    }

    [Fact]
    public void WithoutAGpsOfficeThePatientGoesHome()
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = simulation.Competitors[0];
        opponent.Open();
        var disease = AnyDisease(simulation);
        for (var i = 0; i < 20; i++)
        {
            opponent.Admit(simulation, disease);
        }

        Assert.Equal(500, opponent.Reputation);
        opponent.Transfer(simulation, simulation.Date.MonthOfGame);

        // Twenty visits without a point halve the reputation of 20 points for 20 visits.
        Assert.Equal(250, opponent.Reputation);
        Assert.Equal((20, 20, 0), (opponent.Visitors, opponent.NotCured, opponent.PatientsInHospital));
    }

    [Fact]
    public void AWellStaffedOpponentCuresAndIsPaidForDiagnosisAndTreatment()
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = simulation.Competitors[0];
        opponent.Open();
        opponent.AddRoom("gp");
        opponent.AddStaff(new CompetitorStaff(StaffClass.Doctor, 1000, 100));
        var disease = DiseaseCatalog.All.First(d => !d.IsPseudo && d.TreatmentRooms is ["psych"]);
        foreach (var room in disease.TreatmentRooms.Concat(disease.DiagnosisRooms))
        {
            opponent.AddRoom(room);
        }

        for (var i = 0; i < 200; i++)
        {
            opponent.Admit(simulation, disease);
        }

        // The stays end within two months.
        opponent.Transfer(simulation, simulation.Date.MonthOfGame + 2);
        Assert.Equal(200, opponent.Cured + opponent.Deaths + opponent.NotCured);
        Assert.InRange(opponent.Cured, 120, 200);
        Assert.True(opponent.Reputation > 700, $"reputation {opponent.Reputation}");
        Assert.True(opponent.Balance > opponent.Cured * simulation.Rules.Diseases.CurePrice(disease), $"balance {opponent.Balance}");
        Assert.Equal(0, opponent.PatientsInHospital);
    }

    [Fact]
    public void ItBorrowsWhenShortAndPaysWagesAndInterest()
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = simulation.Competitors[0];
        Assert.True(opponent.Carry(simulation, new CompetitorAction(CompetitorActionKind.Loan)));
        Assert.Equal(Hospital.LoanStep, opponent.Loan);
        Assert.Equal(Hospital.LoanStep, opponent.Balance);

        opponent.AddStaff(new CompetitorStaff(StaffClass.Nurse, 300, 60));
        opponent.OnEndMonth(simulation, 1);
        Assert.Equal(Hospital.LoanStep - 60 - (simulation.Rules.InterestRate * Hospital.LoanStep / 120_000), opponent.Balance);
    }

    [Fact]
    public void ItBuysLandToMatchThePlayer()
    {
        var simulation = NewSimulation(startCash: 10_000);
        var opponent = new CompetitorHospital(1, "ORAC", balance: 10_000, tiles: 10);
        Assert.True(opponent.Carry(simulation, new CompetitorAction(CompetitorActionKind.Buy)));
        Assert.Equal(18, opponent.Tiles);
        Assert.Equal(10_000 - (8 * simulation.Rules.LandCostPerTile), opponent.Balance);
    }

    [Fact]
    public void AnOpponentsScenarioAndPatientsAreSaved()
    {
        var simulation = NewSimulation(startCash: 60_000);
        simulation.Hospitals[0].Open();
        while (simulation.Date.MonthOfGame < 4)
        {
            simulation.Step();
        }

        var opponent = simulation.Competitors[0];
        Assert.True(opponent.IsOpen);
        Assert.True(opponent.Visitors > 0);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        for (var i = 0; i < 2000; i++)
        {
            simulation.Step();
            restored.Step();
        }

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(simulation.Competitors[0].Rooms, restored.Competitors[0].Rooms);
    }

    private static DiseaseDefinition AnyDisease(Simulation simulation) => simulation.Rules.Diseases.AvailableDiseases().First(d => !d.IsPseudo);

    private static void Think(Simulation simulation, CompetitorHospital opponent, int times)
    {
        for (var i = 0; i < times; i++)
        {
            while (simulation.Tick < opponent.NextActionTick)
            {
                simulation.Step();
            }

            opponent.Think(simulation);
        }
    }

    private static void StepDay(Simulation simulation)
    {
        var day = simulation.Date.Day;
        while (simulation.Date.Day == day)
        {
            simulation.Step();
        }
    }

    /// <summary>One player whose plot is 18 tiles, with the GP's office and psychiatry discovered, and one opponent.</summary>
    private static Simulation NewSimulation(long startCash)
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
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        map.UpdatePathfinding();
        var rules = new LevelRules(
            (int)startCash,
            [new("gp", 2_000), new("psych", 3_000)],
            [new("reception_desk", 500), new("bench", 100), new("plant", 50), new("drinks_machine", 300), new("extinguisher", 40)],
            competitors: [new PlannedCompetitor(4, "MULTIVAC")]);
        var simulation = new Simulation(map, seed: 2, rules: rules);
        simulation.Hospitals[0].DiscoverRoom("gp");
        simulation.Hospitals[0].DiscoverRoom("psych");
        return simulation;
    }
}
