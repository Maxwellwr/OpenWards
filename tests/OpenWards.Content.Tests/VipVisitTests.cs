using OpenWards.Sim;
using OpenWards.Sim.Calendar;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.People;
using static OpenWards.Content.Tests.PatientFlowTests;

namespace OpenWards.Content.Tests;

/// <summary>VIP visits (the timer, the faxes, the walk and the score), in a hospital with a GP's office and reception.</summary>
public class VipVisitTests
{
    [Fact]
    public void AVipAsksStrictlyByTheTimerOnceThereIsADesk()
    {
        var rules = new LevelRules(1_000_000, [KeyValuePair.Create("gp", 2500)], [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)], mayorLaunch: 10);
        var simulation = NewHospital(rules);
        var vips = simulation.Hospitals[0].Vips;

        // No desk: the days count, but nobody asks.
        StepFor(simulation, 12 * GameDate.HoursPerDay);
        Assert.Equal(VipState.None, vips.State);
        Assert.True(vips.DaysSinceLast >= 10);

        // With a desk the VIP asks at the end of that day, and the timer starts again.
        PlaceReceptionDesk(simulation);
        StepFor(simulation, GameDate.HoursPerDay);
        Assert.True(vips.IsInvitationPending);
        Assert.InRange(vips.InvitedName, 0, VipVisits.NameCount - 1);
        Assert.InRange(vips.DaysSinceLast, 0, 1);

        // Refusing costs nothing, and the next VIP asks ten days after this one did.
        var (balance, reputation) = (simulation.Hospitals[0].Balance, simulation.Hospitals[0].Reputation);
        Run(simulation, new AnswerVipCommand(Accept: false));
        Assert.Equal(VipState.None, vips.State);
        Assert.Equal(0, vips.Count);
        Assert.Equal((balance, reputation), (simulation.Hospitals[0].Balance, simulation.Hospitals[0].Reputation));
        var asked = simulation.Tick;
        while (!vips.IsInvitationPending)
        {
            simulation.Step();
        }

        Assert.InRange(simulation.Tick - asked, 8 * GameDate.HoursPerDay, 10 * GameDate.HoursPerDay);
    }

    [Fact]
    public void TheFaxInvitesTheVipByItselfAndTheVipArrivesFifteenHundredTicksLater()
    {
        var simulation = NewStaffedHospital();
        var vips = simulation.Hospitals[0].Vips;

        Run(simulation, new CheatCommand(CheatKind.Vip));
        Assert.True(vips.IsInvitationPending);
        Assert.True(simulation.Hospitals[0].Cheated);

        StepFor(simulation, (VipVisits.InvitationDays * GameDate.HoursPerDay) - 2);
        Assert.True(vips.IsInvitationPending);

        StepFor(simulation, 2);
        Assert.Equal(VipState.Travelling, vips.State);
        var accepted = simulation.Tick;
        Assert.Equal(accepted - 1 + VipVisits.TravelTicks, vips.ArrivalTick);

        while (simulation.Tick < vips.ArrivalTick - 1)
        {
            simulation.Step();
            Assert.DoesNotContain(simulation.Entities, entity => entity is Humanoid { Behaviour: VipBehaviour });
        }

        StepFor(simulation, 2);
        Assert.Equal(VipState.Visiting, vips.State);
        Assert.Equal(1, vips.Count);
        var vip = Assert.Single(simulation.Entities.OfType<Humanoid>(), humanoid => humanoid.Behaviour is VipBehaviour);
        Assert.Equal(HumanoidType.Vip, vip.Type);
        Assert.True(vip.IsSlow);
    }

    [Fact]
    public void AnInvitedVipReportsAtReceptionLooksInAtTheRoomsAndLeavesAScore()
    {
        var simulation = NewStaffedHospital();
        var hospital = simulation.Hospitals[0];
        Run(simulation, new CheatCommand(CheatKind.Vip));
        Run(simulation, new AnswerVipCommand(Accept: true));
        while (hospital.Vips.State == VipState.Travelling)
        {
            simulation.Step();
        }

        var vip = simulation.Entities.OfType<Humanoid>().Single(humanoid => humanoid.Behaviour is VipBehaviour);
        var behaviour = (VipBehaviour)vip.Behaviour!;

        var phases = new HashSet<VipPhase>();
        var wasInQueue = false;
        for (var tick = 0; tick < 30 * GameDate.HoursPerDay && simulation.FindEntity(vip.Id) is not null; tick++)
        {
            simulation.Step();
            phases.Add(behaviour.Phase);
            if (Desk(simulation).Desk!.Queue.Contains(vip.Id))
            {
                wasInQueue = true;
                Assert.Equal(vip.Id, Desk(simulation).Desk!.Queue.Front);
            }
        }

        Assert.Null(simulation.FindEntity(vip.Id));
        Assert.True(wasInQueue);
        Assert.Contains(VipPhase.Waiting, phases);
        Assert.Contains(VipPhase.LookingAtRoom, phases);
        Assert.Contains(VipPhase.GoingHome, phases);
        Assert.Equal(1, behaviour.VisitedRooms);

        // Two staff (+4), clean (−1), one room (+1), and −3 to +2. The only doctor is skilled enough for a consultant but
        // not yet promoted (that takes gbv.PromoCon months), so the doctors count for nothing.
        Assert.InRange(behaviour.Score, 1, 6);
        Assert.Equal(VipState.Over, hospital.Vips.State);
        Assert.Equal(1, hospital.Vips.ThisYear);
        Assert.Equal(behaviour.Score, hospital.Vips.ScoreSumThisYear);
        Assert.Equal(behaviour.Score <= VipVisits.PleasedScore ? 1 : 0, hospital.Vips.PleasedThisYear);
        var report = Assert.NotNull(hospital.Vips.Report);
        Assert.Equal((behaviour.Score, VipVisits.Grant(behaviour.Score), VipVisits.ReputationPercent(behaviour.Score)), (report.Score, report.Grant, report.ReputationPercent));
    }

    [Fact]
    public void AVipVisitSurvivesASave()
    {
        var simulation = NewStaffedHospital();
        Run(simulation, new CheatCommand(CheatKind.Vip));
        Run(simulation, new AnswerVipCommand(Accept: true));
        StepFor(simulation, VipVisits.TravelTicks + (3 * GameDate.HoursPerDay));

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, BaseGameContent.Create());

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        for (var tick = 0; tick < 20 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            restored.Step();
        }

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
    }
}
