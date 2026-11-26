using OpenWards.Sim.Commands;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.People;
using OpenWards.Sim.Serialization;
using OpenWards.Sim.Staff;

namespace OpenWards.Sim.Tests;

/// <summary>
/// Firing staff, promotion, pay, discontent and requests for money, after the community's logic specification
/// (the medium settings: Unhappy 50, Happy 75, ResignMax 150, PromoDoc 6, PromoCon 12, SalaryTooLow −10, SalaryTooHigh 20).
/// </summary>
public class StaffEmploymentTests
{
    private const int Month = StaffEmployment.Month;

    [Fact]
    public void FiredStaffGetAMonthsWageAndLeave()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        var hospital = simulation.Hospitals[0];
        var balance = hospital.Balance;
        var reputation = hospital.Reputation;

        Run(simulation, new FireStaffCommand(doctor.Id));

        Assert.True(doctor.IsFired);
        Assert.IsType<LeavingBehaviour>(doctor.Behaviour);
        Assert.Equal(balance - doctor.Profile!.Wage, hospital.Balance);
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.Severance, Subject: "Doctor" } && t.Amount == -doctor.Profile.Wage);

        // Unlike CorsixTH, firing does not touch the reputation.
        Assert.Equal(reputation, hospital.Reputation);

        // Firing again does nothing, and the fired no longer tire or mind the temperature.
        Run(simulation, new FireStaffCommand(doctor.Id));
        Assert.Equal(balance - doctor.Profile.Wage, hospital.Balance);
        doctor.Happiness = 0.5m;
        Feelings.OnEndDay(simulation, doctor);
        Assert.Equal(0.5m, doctor.Happiness);
    }

    [Fact]
    public void OnlyTheirOwnHospitalCanFireThem()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);

        simulation.Schedule(1, new FireStaffCommand(doctor.Id));
        simulation.Step();

        Assert.False(doctor.IsFired);
    }

    [Fact]
    public void DiscontentGrowsWhileUnhappyAndFallsWhileHappy()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);

        doctor.Happiness = 0.49m;
        StaffEmployment.Modify(simulation, doctor);
        StaffEmployment.Modify(simulation, doctor);
        Assert.Equal(2, doctor.Discontent);

        // Between Unhappy and Happy it stays.
        doctor.Happiness = 0.6m;
        StaffEmployment.Modify(simulation, doctor);
        Assert.Equal(2, doctor.Discontent);

        doctor.Happiness = 0.76m;
        for (var i = 0; i < 5; i++)
        {
            StaffEmployment.Modify(simulation, doctor);
        }

        Assert.Equal(0, doctor.Discontent);
    }

    [Fact]
    public void TheDiscontentCountsInTheNeedsUpdateAndHireAtTheHour()
    {
        var simulation = NewSimulation();
        for (var i = 0; i < 20; i++)
        {
            simulation.Step();
        }

        var doctor = Hire(simulation, StaffClass.Doctor);
        Assert.Equal(simulation.Hours - 1, doctor.HiredAt);
        doctor.Happiness = 0.1m;
        while (!PatientNeeds.IsDue(simulation.Hours))
        {
            simulation.Step();
        }

        simulation.Step();
        Assert.Equal(1, doctor.Discontent);
    }

    [Fact]
    public void OnlyStaffDiscontentLongEnoughAndSixMonthsInAsk()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        doctor.Happiness = 0.1m;
        doctor.Discontent = 150;

        // Just hired: no request, however discontent.
        StaffEmployment.Modify(simulation, doctor);
        Assert.Equal(0, doctor.RaiseRequest);
        Assert.Equal(151, doctor.Discontent);

        // Nine months in, the request comes for sure.
        doctor.HiredAt = simulation.Hours - (9 * Month) - 1;
        StaffEmployment.Modify(simulation, doctor);
        Assert.NotEqual(RaiseReason.None, doctor.RaiseReason);
        Assert.Equal(1, doctor.RaiseRequestCount);
        Assert.Equal(StaffEmployment.QuittingDelay, doctor.QuittingIn);

        // While a request waits, nobody asks again.
        var request = doctor.RaiseRequest;
        StaffEmployment.Modify(simulation, doctor);
        Assert.Equal(request, doctor.RaiseRequest);
        Assert.Equal(1, doctor.RaiseRequestCount);
    }

    [Fact]
    public void NobodyAsksWhileResting()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        doctor.Happiness = 0.1m;
        doctor.Discontent = 200;
        doctor.HiredAt = simulation.Hours - (9 * Month) - 1;
        doctor.Behaviour = new People.RestingBehaviour(roomId: 0, parcel: 0);

        // The discontent still counts, but the request waits for the rest to end.
        StaffEmployment.Modify(simulation, doctor);
        Assert.Equal(RaiseReason.None, doctor.RaiseReason);
        Assert.Equal(201, doctor.Discontent);
    }

    [Fact]
    public void ServiceAskedForIsSixToNineMonths()
    {
        var earliest = long.MaxValue;
        for (var seed = 1; seed <= 40; seed++)
        {
            var simulation = NewSimulation(seed);
            var doctor = Hire(simulation, StaffClass.Doctor);
            doctor.Discontent = 1000;
            doctor.Happiness = 0.1m;
            var served = 6L * Month;
            while (served <= 10L * Month)
            {
                doctor.HiredAt = simulation.Hours - served;
                StaffEmployment.Modify(simulation, doctor);
                if (doctor.RaiseReason != RaiseReason.None)
                {
                    break;
                }

                served += Month / 10;
            }

            Assert.InRange(served, 6L * Month, (9L * Month) + (Month / 10));
            earliest = Math.Min(earliest, served);
        }

        // Six months and a little: most draws ask for six months.
        Assert.True(earliest <= (6L * Month) + (Month / 10));
    }

    [Fact]
    public void UnderpaidStaffAskForTheShortfallAndATenthAsARise()
    {
        var simulation = NewSimulation();
        var doctor = Unhappy(simulation);
        var expected = doctor.Profile!.FairWage(simulation.Rules.Staff);
        doctor.Profile.Wage = expected - 30;

        StaffEmployment.Modify(simulation, doctor);

        Assert.Equal(RaiseReason.Underpaid, doctor.RaiseReason);
        Assert.Equal(30 + (expected / 10), doctor.RaiseRequest);
    }

    [Fact]
    public void OthersAskForATenthToAThirdMostlyAsAPayment()
    {
        var reasons = new List<RaiseReason>();
        for (var seed = 1; seed <= 60; seed++)
        {
            foreach (var tired in new[] { false, true })
            {
                var simulation = NewSimulation(seed);
                var doctor = Unhappy(simulation);
                var wage = doctor.Profile!.Wage;
                doctor.Fatigue = tired ? 0.75m : 0.2m;

                StaffEmployment.Modify(simulation, doctor);

                Assert.InRange(doctor.RaiseRequest, Math.Max(1, wage * 10 / 100), wage * 30 / 100);
                reasons.Add(doctor.RaiseReason);
                if (!tired)
                {
                    Assert.Equal(RaiseReason.Payment, doctor.RaiseReason);
                }
                else
                {
                    Assert.Contains(doctor.RaiseReason, new[] { RaiseReason.TiredRise, RaiseReason.TiredPayment });
                }
            }
        }

        // Very tired, a rise one time in four.
        var tiredRises = reasons.Count(r => r == RaiseReason.TiredRise);
        Assert.InRange(tiredRises, 5, 30);
    }

    [Fact]
    public void FromTheSixthRequestNothingKeepsThem()
    {
        var simulation = NewSimulation();
        var doctor = Unhappy(simulation);
        doctor.RaiseRequestCount = 5;

        StaffEmployment.Modify(simulation, doctor);

        Assert.Equal(RaiseReason.DefiniteQuit, doctor.RaiseReason);
        Assert.Equal(6, doctor.RaiseRequestCount);
        var wage = doctor.Profile!.Wage;
        doctor.RaiseRequestCount = 1_000_000;

        // Agreeing gives nothing and counts as refusing.
        Run(simulation, new AnswerRaiseCommand(doctor.Id, Grant: true));

        Assert.Equal(wage, doctor.Profile.Wage);
        Assert.True(doctor.IsFired);
    }

    [Fact]
    public void AgreeingToARiseRaisesTheWageAndSettlesThem()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        var wage = doctor.Profile!.Wage;
        doctor.Happiness = 0.05m;
        doctor.Discontent = 200;
        doctor.RaiseRequestCount = 2;
        doctor.RaiseReason = RaiseReason.Underpaid;
        doctor.RaiseRequest = 40;
        doctor.QuittingIn = 500;

        Run(simulation, new AnswerRaiseCommand(doctor.Id, Grant: true));

        Assert.Equal(wage + 40, doctor.Profile.Wage);
        Assert.Equal(1m, doctor.Happiness);
        Assert.Equal(0, doctor.Discontent);
        Assert.Equal(2, doctor.RaiseRequestCount);
        Assert.Equal(0, doctor.RaiseRequest);
        Assert.Equal(RaiseReason.None, doctor.RaiseReason);
        Assert.Equal(-1, doctor.QuittingIn);
        Assert.False(doctor.IsFired);
    }

    [Fact]
    public void AgreeingToAPaymentPaysOnceAndLeavesTheWage()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        var wage = doctor.Profile!.Wage;
        var balance = simulation.Hospitals[0].Balance;
        doctor.RaiseReason = RaiseReason.TiredPayment;
        doctor.RaiseRequest = 40;
        doctor.QuittingIn = 500;

        Run(simulation, new AnswerRaiseCommand(doctor.Id, Grant: true));

        Assert.Equal(wage, doctor.Profile.Wage);
        Assert.Equal(balance - 40, simulation.Hospitals[0].Balance);
        Assert.Contains(simulation.Hospitals[0].Transactions, t => t.Kind == TransactionKind.PersonalBonus && t.Amount == -40);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1_000_000, true)]
    public void RefusingLetsThemStayOneTimeInRequestsPlusOne(int requests, bool quits)
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        var balance = simulation.Hospitals[0].Balance;
        doctor.Happiness = 0.3m;
        doctor.Discontent = 200;
        doctor.RaiseRequestCount = requests;
        doctor.RaiseReason = RaiseReason.Payment;
        doctor.RaiseRequest = 40;
        doctor.QuittingIn = 500;

        Run(simulation, new AnswerRaiseCommand(doctor.Id, Grant: false));

        Assert.Equal(quits, doctor.IsFired);
        Assert.Equal(0, doctor.RaiseRequest);
        Assert.Equal(quits ? 200 : 0, doctor.Discontent);
        Assert.Equal(0.3m, doctor.Happiness);

        // Quitting comes without severance.
        Assert.Equal(balance, simulation.Hospitals[0].Balance);
    }

    [Fact]
    public void RefusalsAreSurvivedAboutOneTimeInThreeOnTheSecondRequest()
    {
        var stayed = 0;
        for (var seed = 1; seed <= 90; seed++)
        {
            var simulation = NewSimulation(seed);
            var doctor = Hire(simulation, StaffClass.Doctor);
            doctor.RaiseRequestCount = 2;
            doctor.RaiseReason = RaiseReason.Payment;
            doctor.RaiseRequest = 40;
            Run(simulation, new AnswerRaiseCommand(doctor.Id, Grant: false));
            stayed += doctor.IsFired ? 0 : 1;
        }

        Assert.InRange(stayed, 15, 45);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnansweredRequestEndsInQuittingOrAgreementByPolicy(bool grant)
    {
        var simulation = NewSimulation();
        Run(simulation, new SetPolicyCommand(PolicyKind.GrantWageIncrease, grant ? 1 : 0));
        var doctor = Hire(simulation, StaffClass.Doctor);
        var wage = doctor.Profile!.Wage;
        var balance = simulation.Hospitals[0].Balance;
        doctor.RaiseReason = RaiseReason.Underpaid;
        doctor.RaiseRequest = 40;
        doctor.QuittingIn = 1;

        StaffEmployment.Tick(simulation, doctor);
        Assert.False(doctor.IsFired);
        StaffEmployment.Tick(simulation, doctor);

        Assert.Equal(!grant, doctor.IsFired);
        Assert.Equal(grant ? wage + 40 : wage, doctor.Profile.Wage);
        Assert.Equal(-1, doctor.QuittingIn);
        Assert.Equal(balance, simulation.Hospitals[0].Balance);
    }

    [Fact]
    public void JuniorsAndDoctorsArePromotedAtTheMonthsEndOnceSkilledAndLongEnoughIn()
    {
        var simulation = NewSimulation();
        var rules = simulation.Rules.Staff;
        var junior = Doctor(simulation, rules.DoctorThreshold + 1, junior: true);
        var doctor = Doctor(simulation, rules.ConsultantThreshold + 1, junior: false);
        var fresh = Doctor(simulation, rules.DoctorThreshold + 1, junior: true);
        var weak = Doctor(simulation, rules.DoctorThreshold, junior: true);
        junior.HiredAt = simulation.Hours - (6 * Month);
        doctor.HiredAt = simulation.Hours - (12 * Month);
        fresh.HiredAt = simulation.Hours - (6 * Month) + 1;
        weak.HiredAt = simulation.Hours - (24 * Month);

        StaffEmployment.OnEndMonth(simulation);

        Assert.False(junior.Profile!.IsJunior);
        Assert.False(junior.Profile.IsConsultant);
        Assert.True(doctor.Profile!.IsConsultant);
        Assert.True(fresh.Profile!.IsJunior);
        Assert.True(weak.Profile!.IsJunior);

        // One step a month: the new doctor is not yet a consultant.
        junior.Profile.TrainSkill(StaffProfile.MaxSkill);
        Assert.False(junior.Profile.IsConsultant);
    }

    [Fact]
    public void SkillAloneDoesNotPromote()
    {
        var simulation = NewSimulation();
        var junior = Doctor(simulation, 100, junior: true);

        junior.Profile!.TrainSkill(StaffProfile.MaxSkill);
        for (var i = 0; i < 10; i++)
        {
            junior.Profile.GainProcedureSkill();
        }

        Assert.True(junior.Profile.IsJunior);
        Assert.False(junior.Profile.IsConsultant);
    }

    [Fact]
    public void PayAgainstTheExpectedWageMovesTheMoodOnceAMonth()
    {
        var simulation = NewSimulation();
        var underpaid = Hire(simulation, StaffClass.Doctor);
        var paid = Hire(simulation, StaffClass.Nurse);
        underpaid.Profile!.Wage = underpaid.Profile.FairWage(simulation.Rules.Staff) - 1;
        paid.Profile!.Wage = paid.Profile.FairWage(simulation.Rules.Staff);
        underpaid.Happiness = 0.5m;
        paid.Happiness = 0.5m;

        StaffEmployment.OnEndMonth(simulation);

        Assert.Equal(0.4m, underpaid.Happiness);
        Assert.Equal(0.7m, paid.Happiness);

        // The day no longer weighs the pay.
        Feelings.OnEndDay(simulation, underpaid);
        Assert.True(underpaid.Happiness >= 0.39m);
    }

    [Fact]
    public void ARaiseFromTheStaffScreenAddsATenthAndAnswersARequest()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        var wage = doctor.Profile!.Wage;
        doctor.RaiseReason = RaiseReason.Underpaid;
        doctor.RaiseRequest = 40;
        doctor.QuittingIn = 300;

        Run(simulation, new IncreaseSalaryCommand(doctor.Id));

        Assert.Equal(Math.Min(wage + (wage / 10), simulation.Rules.MaxSalary), doctor.Profile.Wage);
        Assert.Equal(0, doctor.RaiseRequest);
        Assert.Equal(RaiseReason.None, doctor.RaiseReason);
        Assert.Equal(-1, doctor.QuittingIn);
    }

    [Fact]
    public void FiredStaffWalkOffAndAreNotPaid()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        Run(simulation, new FireStaffCommand(doctor.Id));
        var balance = simulation.Hospitals[0].Balance;

        for (var i = 0; i < 31 * 50 && simulation.Entities.Contains(doctor); i++)
        {
            simulation.Step();
        }

        Assert.DoesNotContain(doctor, simulation.Entities);
        Assert.DoesNotContain(simulation.Hospitals[0].Transactions, t => t.Kind == TransactionKind.Wages && t.Amount > 0);
        Assert.True(simulation.Hospitals[0].Balance >= balance - 1000);
    }

    [Fact]
    public void ABonusCostsATenthOfTheWageAndCheersUp()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        doctor.Happiness = 0.2m;
        var balance = simulation.Hospitals[0].Balance;

        Run(simulation, new PayBonusCommand(doctor.Id));

        Assert.Equal(balance - (doctor.Profile!.Wage / 10), simulation.Hospitals[0].Balance);
        Assert.Contains(simulation.Hospitals[0].Transactions, t => t.Kind == TransactionKind.PersonalBonus);
        Assert.True(doctor.Happiness >= 0.65m);
    }

    [Fact]
    public void TheCommandsAndSavesKeepItAll()
    {
        foreach (var command in new Command[] { new FireStaffCommand(7), new AnswerRaiseCommand(8, true), new PayBonusCommand(9), new IncreaseSalaryCommand(10) })
        {
            using var wire = new MemoryStream();
            command.Write(new SimWriter(wire));
            wire.Position = 0;
            Assert.Equal(command, Command.Read(new SimReader(wire)));
        }

        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor);
        doctor.RaiseRequest = 40;
        doctor.RaiseReason = RaiseReason.TiredPayment;
        doctor.RaiseRequestCount = 3;
        doctor.Discontent = 77;
        doctor.HiredAt = 12345;
        doctor.QuittingIn = 300;
        var nurse = Hire(simulation, StaffClass.Nurse);

        // Fired but not yet gone (the test map has no spawn points, so a step would see them out at once).
        StaffEmployment.Fire(simulation, nurse);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        var copy = (Humanoid)restored.FindEntity(doctor.Id)!;
        Assert.Equal(40, copy.RaiseRequest);
        Assert.Equal(RaiseReason.TiredPayment, copy.RaiseReason);
        Assert.Equal(3, copy.RaiseRequestCount);
        Assert.Equal(77, copy.Discontent);
        Assert.Equal(12345, copy.HiredAt);
        Assert.Equal(simulation.Rules.Staff.Employment, restored.Rules.Staff.Employment);
        Assert.True(((Humanoid)restored.FindEntity(nurse.Id)!).IsFired);
        Assert.IsType<LeavingBehaviour>(((Humanoid)restored.FindEntity(nurse.Id)!).Behaviour);
    }

    /// <summary>A doctor nine months in, discontent enough to ask at the next update.</summary>
    private static Humanoid Unhappy(Simulation simulation)
    {
        var doctor = Hire(simulation, StaffClass.Doctor);
        doctor.Profile!.Wage = doctor.Profile.FairWage(simulation.Rules.Staff);
        doctor.Happiness = 0.1m;
        doctor.Discontent = 150;
        doctor.HiredAt = simulation.Hours - (9 * Month) - 1;
        return doctor;
    }

    private static Humanoid Doctor(Simulation simulation, int skill, bool junior)
    {
        var profile = StaffProfile.Create(StaffClass.Doctor, junior ? 0 : simulation.Rules.Staff.DoctorThreshold + 1, simulation.Rules.Staff, simulation.Random);
        while (profile.Skill < skill)
        {
            profile.GainProcedureSkill();
        }

        return StaffHiring.Place(simulation, profile, new TilePosition(4, 4), 0);
    }

    private static Humanoid Hire(Simulation simulation, StaffClass staffClass)
    {
        Run(simulation, new HireStaffCommand(staffClass, 0, new TilePosition(4, 4)));
        return simulation.Entities.OfType<Humanoid>().Last();
    }

    private static void Run(Simulation simulation, Command command)
    {
        simulation.Schedule(0, command);
        simulation.Step();
    }

    private static Simulation NewSimulation(int seed = 1)
    {
        var map = new LevelMap(10, 10, playerCount: 2);
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                ref var tile = ref map[x, y];
                tile.Floor = 1;
                tile.Set(TileFlags.Passable, true);
                if (x is > 1 and < 8 && y is > 1 and < 8)
                {
                    tile.Parcel = 1;
                    tile.Set(TileFlags.Buildable | TileFlags.Hospital, true);
                }
            }
        }

        // Seeds spread out, so that statistical tests do not start from neighbouring states.
        return new Simulation(map, seed: (ulong)seed * 0x9E3779B97F4A7C15UL, rules: new LevelRules(10_000, [], []));
    }
}
