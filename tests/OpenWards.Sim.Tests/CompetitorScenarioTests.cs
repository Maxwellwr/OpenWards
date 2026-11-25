using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Import;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Staff;

namespace OpenWards.Sim.Tests;

/// <summary>The computer opponents' scenario and their patients' quick stay, as the logic specification describes the original.</summary>
public class CompetitorScenarioTests
{
    private const int Cycle = CompetitorHospital.StartSteps;

    [Fact]
    public void AnOpponentStartsByHiringBuildingAndOpening()
    {
        var simulation = NewSimulation(startCash: 100_000);
        var opponent = simulation.Competitors[0];
        Assert.False(opponent.IsOpen);
        Assert.Equal(new CompetitorPersonality(2, 4, 30, 1, 70), opponent.Personality);

        var days = 0;
        while (days < 120 && (opponent.ScenarioStep < Cycle || opponent.Pending is not null))
        {
            StepDay(simulation);
            days++;
        }

        Assert.InRange(days, 5, 60);
        Assert.True(opponent.IsOpen);
        Assert.Equal(["gp", "psych"], opponent.Rooms);
        Assert.Equal(1, opponent.ObjectCount("reception_desk"));
        Assert.Equal(CompetitorHospital.StartBenches, opponent.ObjectCount("bench"));
        Assert.Equal(1, opponent.StaffCount(StaffClass.Receptionist));
        Assert.Equal(1, opponent.StaffCount(StaffClass.Doctor));

        // The start's rooms ask for no hire.
        Assert.All(opponent.Queue, Assert.Null);

        // The rooms, the desk and the benches, and 1.2 times each wage.
        var wages = opponent.Staff.Sum(member => (long)member.Wage * 6 / 5);
        Assert.Equal(100_000 - 2_000 - 3_000 - 500 - (4 * 100) - wages, opponent.Balance);
    }

    [Fact]
    public void TheWaitBeforeAnActionGoesByItsSpeedAndSkill()
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = new CompetitorHospital(1, "ORAC", personality: new CompetitorPersonality(Skill: 1, StaffLevels: 5, Speed: 40, Comfort: 2, GuessAt: 75));

        // The first decision is due at once: a hire of the start, ⌊(40 + rng % 5) × 25 / 10⌋ ticks ahead.
        opponent.Think(simulation);
        Assert.Equal(CompetitorAction.HireOf(StaffClass.Receptionist) with { FromStart = true }, opponent.Pending);
        Assert.InRange(opponent.NextActionTick - simulation.Tick, 100, 110);

        // Carried out (it fails without money), then ⌊40 × 33 / 10⌋ until the next decision.
        Advance(simulation, opponent.NextActionTick);
        opponent.Think(simulation);
        Assert.Null(opponent.Pending);
        Assert.Equal(simulation.Tick + 132, opponent.NextActionTick);
    }

    [Fact]
    public void AStepWithNothingToDoWaitsADecisionInterval()
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = new CompetitorHospital(1, "ORAC");
        opponent.AddStaff(new CompetitorStaff(StaffClass.Receptionist, 300, 50));

        // The start's second try at a receptionist is not needed.
        opponent.SetScenarioStep(3);
        opponent.Think(simulation);
        Assert.Null(opponent.Pending);
        Assert.Equal(4, opponent.ScenarioStep);
        Assert.Equal(simulation.Tick + CompetitorHospital.DecisionInterval, opponent.NextActionTick);
    }

    [Fact]
    public void TheStartWaitsForBothItsReceptionistAndItsDoctor()
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = new CompetitorHospital(1, "ORAC");
        opponent.SetScenarioStep(Cycle - 1);
        Assert.Equal(CompetitorAction.HireOf(StaffClass.Receptionist) with { FromStart = true }, opponent.Decide(simulation));
        opponent.AddStaff(new CompetitorStaff(StaffClass.Receptionist, 300, 50));
        Assert.Equal(CompetitorAction.HireOf(StaffClass.Doctor) with { FromStart = true }, opponent.Decide(simulation));
        Assert.Equal(Cycle - 1, opponent.ScenarioStep);
        opponent.AddStaff(new CompetitorStaff(StaffClass.Doctor, 300, 50));
        Assert.Null(opponent.Decide(simulation));
        Assert.Equal(Cycle, opponent.ScenarioStep);
    }

    [Fact]
    public void ARoomQueuesTheHireItNeedsWhichTheStaffStepTakesFirst()
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = new CompetitorHospital(1, "ORAC", balance: 50_000);
        opponent.AddPatientsInHospital(100);

        // Psychiatry is a diagnosis room, and wants a psychiatrist.
        opponent.SetScenarioStep(Cycle + 1);
        Assert.Equal(new CompetitorAction(CompetitorActionKind.Build, "psych"), opponent.Decide(simulation));
        var psychiatrist = CompetitorAction.HireOf(StaffClass.Doctor, Specialism.Psychiatrist);
        Assert.Equal(psychiatrist, opponent.Queue[0]);

        // The staff step takes it before the staff it lacks (a nurse first for 100 patients).
        opponent.SetScenarioStep(Cycle);
        var hire = opponent.Decide(simulation);
        Assert.Equal(psychiatrist with { Slot = 0 }, hire);

        // Hired if a candidate is one; else it moves to the last place.
        var found = simulation.AvailableStaff(StaffClass.Doctor).Any(candidate => candidate.Has(Specialism.Psychiatrist));
        Assert.Equal(found, opponent.Carry(simulation, hire!.Value));
        Assert.Null(opponent.Queue[0]);
        Assert.Equal(found ? null : psychiatrist, opponent.Queue[CompetitorHospital.QueuePlaces - 1]);

        // Without a task in the queue, the staff it lacks: nurses first.
        if (!found)
        {
            opponent.Carry(simulation, opponent.Decide(simulation)!.Value);
        }

        opponent.SetScenarioStep(Cycle);
        Assert.Equal(CompetitorAction.HireOf(StaffClass.Nurse), opponent.Decide(simulation));
    }

    [Fact]
    public void TreatmentRoomsComeWhenDiagnosisChoseNothingAndADearRoomAsksForALoan()
    {
        var simulation = NewSimulation(startCash: 0);
        simulation.Hospitals[0].DiscoverRoom("pharmacy");
        var opponent = new CompetitorHospital(1, "ORAC", balance: 1_000);

        // Psychiatry is too dear: a loan request goes into the queue, and in the same step the pharmacy, which wants a nurse.
        opponent.SetScenarioStep(Cycle + 1);
        Assert.Equal(new CompetitorAction(CompetitorActionKind.Build, "pharmacy"), opponent.Decide(simulation));
        Assert.Equal(new CompetitorAction(CompetitorActionKind.Loan), opponent.Queue[0]);
        Assert.Equal(CompetitorAction.HireOf(StaffClass.Nurse), opponent.Queue[1]);

        // The loan step takes the request, which only takes its time.
        opponent.SetScenarioStep(Cycle + 3);
        Assert.Equal(new CompetitorAction(CompetitorActionKind.Loan), opponent.Decide(simulation));
        Assert.Null(opponent.Queue[0]);
        Assert.Equal(1_000, opponent.Balance);
    }

    [Fact]
    public void WhenEveryRoomIsTooDearBothListsAskForALoan()
    {
        var simulation = NewSimulation(startCash: 0);
        simulation.Hospitals[0].DiscoverRoom("pharmacy");
        var opponent = new CompetitorHospital(1, "ORAC", balance: 100);

        opponent.SetScenarioStep(Cycle + 1);
        Assert.Null(opponent.Decide(simulation));
        Assert.Equal(new CompetitorAction(CompetitorActionKind.Loan), opponent.Queue[0]);
        Assert.Equal(new CompetitorAction(CompetitorActionKind.Loan), opponent.Queue[1]);
    }

    [Fact]
    public void TheStartsFirstDiagnosisRoomAsksForALoanWhenTooDear()
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = new CompetitorHospital(1, "ORAC", balance: 100);

        // The start's room asks for no hire, but a loan if it cannot pay.
        opponent.SetScenarioStep(7);
        Assert.Null(opponent.Decide(simulation));
        Assert.Equal(new CompetitorAction(CompetitorActionKind.Loan), opponent.Queue[0]);
        Assert.All(opponent.Queue.Skip(1), Assert.Null);
    }

    [Fact]
    public void ItBuysTheFirstObjectItLacksOneAtATime()
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = new CompetitorHospital(1, "ORAC", balance: 700, personality: new CompetitorPersonality(1, 5, 1, 0, 75));
        opponent.AddPatientsInHospital(30);

        // At Comfort 0 a drinks machine for 10 patients: 3, bought one at a time.
        opponent.SetScenarioStep(Cycle + 2);
        var buy = opponent.Decide(simulation)!.Value;
        Assert.Equal(new CompetitorAction(CompetitorActionKind.Buy, "drinks_machine", 3), buy);
        Assert.True(opponent.Carry(simulation, buy));
        Assert.True(opponent.Carry(simulation, buy));

        // The third is too dear and is gone without.
        Assert.False(opponent.Carry(simulation, buy));
        Assert.Equal(2, opponent.ObjectCount("drinks_machine"));
        Assert.Equal(100, opponent.Balance);

        // With drinks machines enough, the benches: one for 5 patients, at most four at a step.
        opponent.AddPatientsInHospital(-25);
        opponent.SetScenarioStep(Cycle + 2);
        Assert.Equal(new CompetitorAction(CompetitorActionKind.Buy, "bench", 1), opponent.Decide(simulation));
    }

    [Fact]
    public void ItHiresTheFirstCandidateOrNone()
    {
        var simulation = NewSimulation(startCash: 0);
        var first = simulation.AvailableStaff(StaffClass.Nurse)[0];
        var count = simulation.AvailableStaff(StaffClass.Nurse).Count;
        var poor = new CompetitorHospital(1, "ORAC", balance: (first.Wage * 6 / 5) - 1);
        Assert.False(poor.Carry(simulation, CompetitorAction.HireOf(StaffClass.Nurse)));
        Assert.Equal(count, simulation.AvailableStaff(StaffClass.Nurse).Count);

        var rich = new CompetitorHospital(1, "ORAC", balance: 10_000);
        Assert.True(rich.Carry(simulation, CompetitorAction.HireOf(StaffClass.Nurse)));
        Assert.Equal(new CompetitorStaff(StaffClass.Nurse, first.Skill, first.Wage), rich.Staff[0]);
        Assert.Equal(10_000 - (first.Wage * 6 / 5), rich.Balance);
        Assert.Equal(count - 1, simulation.AvailableStaff(StaffClass.Nurse).Count);
    }

    [Theory]
    [InlineData(StaffClass.Doctor, 0, 100, 1)]
    [InlineData(StaffClass.Doctor, 9, 30, 30)]
    [InlineData(StaffClass.Handyman, 5, 30, 6)]
    [InlineData(StaffClass.Receptionist, 0, 30, 0)]
    public void ItWantsStaffForItsPatients(StaffClass staffClass, int staffLevels, int patients, int wanted)
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = new CompetitorHospital(1, "ORAC", personality: new CompetitorPersonality(1, staffLevels, 50, 2, 75));
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
        var disease = simulation.Rules.Diseases.AvailableDiseases().First(d => !d.IsPseudo);
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

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(6, 5)]
    [InlineData(9, 8)]
    [InlineData(12, Cycle)]
    [InlineData(13, Cycle + 1)]
    [InlineData(15, Cycle + 3)]
    [InlineData(10, Cycle)]
    public void TheOriginalsScenarioPositionIsAStep(int position, int step)
    {
        Assert.Equal(step, CompetitorHospital.ScenarioStepOf(position));
    }

    [Fact]
    public void AnImportedOpponentGoesOnWhereItWas()
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = simulation.Competitors[0];
        var scenario = new ImportedCompetitorScenario(
            IsOpen: true,
            Position: 13,
            TicksToAction: 97,
            Staff: [(StaffClass.Nurse, 0, 0), (StaffClass.Doctor, 2, 1500), (StaffClass.Handyman, 1, 400), (StaffClass.Receptionist, 1, 300)],
            Wages: 1001,
            PatientsInHospital: 12,
            Rooms: ["gp", "psych"],
            Objects: [("reception_desk", 1), ("bench", 6)],
            Queue: [null, CompetitorAction.HireOf(StaffClass.Doctor, Specialism.Surgeon)],
            Pending: new CompetitorAction(CompetitorActionKind.Buy, "bench", 2));

        opponent.Import(new ImportedCompetitor(opponent.Name, 500, 40, Money: 20_000, Scenario: scenario), simulation);

        Assert.True(opponent.IsOpen);
        Assert.Equal(Cycle + 1, opponent.ScenarioStep);
        Assert.Equal(new CompetitorAction(CompetitorActionKind.Buy, "bench", 2), opponent.Pending);
        Assert.Equal(simulation.Tick + 97, opponent.NextActionTick);
        Assert.Equal(12, opponent.PatientsInHospital);
        Assert.Equal(2, opponent.StaffCount(StaffClass.Doctor));
        Assert.Equal(750, opponent.AverageSkill(StaffClass.Doctor));
        Assert.Equal(1, opponent.StaffCount(StaffClass.Handyman));
        Assert.Equal(0, opponent.StaffCount(StaffClass.Nurse));
        Assert.Equal(1001, opponent.Wages);
        Assert.Equal(["gp", "psych"], opponent.Rooms);
        Assert.Equal(6, opponent.ObjectCount("bench"));
        Assert.Null(opponent.Queue[0]);
        Assert.Equal(CompetitorAction.HireOf(StaffClass.Doctor, Specialism.Surgeon), opponent.Queue[1]);
        Assert.Equal(20_000, opponent.Balance);

        // What it was waiting for is carried out as any action.
        Assert.True(opponent.Carry(simulation, opponent.Pending!.Value));
        Assert.Equal(7, opponent.ObjectCount("bench"));
    }

    [Fact]
    public void AnImportedHireFromTheQueueLeavesItWhenCarriedOut()
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = simulation.Competitors[0];
        var surgeon = CompetitorAction.HireOf(StaffClass.Doctor, Specialism.Surgeon);
        var scenario = new ImportedCompetitorScenario(
            true, 12, 0, [], 0, 0, ["gp"], [], [CompetitorAction.HireOf(StaffClass.Doctor, Specialism.Surgeon), null, null, CompetitorAction.HireOf(StaffClass.Nurse, null)],
            surgeon with { Slot = 0 });

        opponent.Import(new ImportedCompetitor(opponent.Name, 500, 10, Money: 0, Scenario: scenario), simulation);

        // It keeps its place; without money the hire fails and moves to the free place furthest back.
        Assert.Equal(surgeon with { Slot = 0 }, opponent.Pending);
        Assert.False(opponent.Carry(simulation, opponent.Pending!.Value));
        Assert.Null(opponent.Queue[0]);
        Assert.Equal(surgeon, opponent.Queue[CompetitorHospital.QueuePlaces - 1]);
        Assert.Equal(CompetitorAction.HireOf(StaffClass.Nurse), opponent.Queue[3]);
    }

    [Fact]
    public void AnImportedLoanRequestInTheQueueTakesItsTime()
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = simulation.Competitors[0];
        var loan = new CompetitorAction(CompetitorActionKind.Loan);
        var scenario = new ImportedCompetitorScenario(true, 15, 0, [], 0, 0, [], [], [null, loan], loan with { Slot = 3 });

        opponent.Import(new ImportedCompetitor(opponent.Name, 500, 10, Scenario: scenario), simulation);

        Assert.Equal(loan, opponent.Pending);
        Assert.Equal(loan, opponent.Queue[1]);

        // The loan step takes it out of the queue as an action of its own.
        opponent.SetScenarioStep(Cycle + 3);
        Assert.Equal(loan, opponent.Decide(simulation));
        Assert.All(opponent.Queue, Assert.Null);
    }

    [Fact]
    public void AnImportedOpponentInTheStartDecidesWhenItsActionIsDue()
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = simulation.Competitors[0];
        var scenario = new ImportedCompetitorScenario(false, 4, -500, [(StaffClass.Receptionist, 1, 300)], 400, null, [], [("reception_desk", 1), ("bench", 4)], []);

        opponent.Import(new ImportedCompetitor(opponent.Name, 500, 0, Money: 20_000, Scenario: scenario), simulation);

        // Overdue: it decides at once, and with its receptionist it skips the start's second try.
        Assert.False(opponent.IsOpen);
        Assert.Equal(simulation.Tick, opponent.NextActionTick);
        opponent.Think(simulation);
        Assert.Equal(4, opponent.ScenarioStep);
        Assert.Null(opponent.Pending);
    }

    [Fact]
    public void AMonthsEndPaysTheWagesAndNothingElse()
    {
        var simulation = NewSimulation(startCash: 0);
        var opponent = simulation.Competitors[0];
        opponent.AddStaff(new CompetitorStaff(StaffClass.Nurse, 300, 60));
        opponent.OnEndMonth(simulation, 1);
        Assert.Equal(-60, opponent.Balance);
        Assert.Equal(simulation.Map.ParcelTileCount(1), opponent.Tiles);
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
        Assert.Equal(opponent.Personality, restored.Competitors[0].Personality);
        Assert.Equal(simulation.Rules.Competitors, restored.Rules.Competitors);
        for (var i = 0; i < 2000; i++)
        {
            simulation.Step();
            restored.Step();
        }

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(simulation.Competitors[0].Rooms, restored.Competitors[0].Rooms);
    }

    private static void Advance(Simulation simulation, long tick)
    {
        while (simulation.Tick < tick)
        {
            simulation.Step();
        }
    }

    private static void Think(Simulation simulation, CompetitorHospital opponent, int times)
    {
        for (var i = 0; i < times; i++)
        {
            Advance(simulation, opponent.NextActionTick);
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
            [new("gp", 2_000), new("psych", 3_000), new("pharmacy", 900)],
            [new("reception_desk", 500), new("bench", 100), new("plant", 50), new("drinks_machine", 300), new("extinguisher", 40)],
            competitors: [new PlannedCompetitor(4, "MULTIVAC", new CompetitorPersonality(2, 4, 30, 1, 70))]);
        var simulation = new Simulation(map, seed: 2, rules: rules);
        simulation.Hospitals[0].DiscoverRoom("gp");
        simulation.Hospitals[0].DiscoverRoom("psych");
        return simulation;
    }
}
