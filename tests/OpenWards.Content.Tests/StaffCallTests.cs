using OpenWards.Sim;
using OpenWards.Sim.Calendar;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.People;
using OpenWards.Sim.Staff;
using static OpenWards.Content.Tests.PatientFlowTests;

namespace OpenWards.Content.Tests;

/// <summary>Rooms calling staff (CorsixTH's calls_dispatcher.lua: verifyStaffForRoom, getPriorityForRoom).</summary>
public class StaffCallTests
{
    [Fact]
    public void AnEmptyRoomCallsForADoctorOnlyOnceAPatientWantsIt()
    {
        var simulation = NewStaffedHospital();
        var second = BuildSecondGp(simulation);
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new(15, 15)));
        var doctor = simulation.Entities.OfType<Humanoid>().Last(h => h.Profile?.Class == StaffClass.Doctor);

        // Nobody wants the second office: its call waits, and the new doctor wanders about (the original's calls come
        // from patients sent to a room without its staff, and from staff leaving a room with a queue).
        StepFor(simulation, 2 * GameDate.HoursPerDay);
        Assert.Equal(0, second.StaffId);
        Assert.Equal(0, ((DoctorBehaviour)doctor.Behaviour!).RoomId);

        // A patient sent there: the room calls, and the free doctor goes.
        var first = simulation.Entities.OfType<Room>().First(r => r.RoomType == "gp" && r.Id != second.Id);
        Humanoid? patient = null;
        for (var hour = 0; hour < 60 * GameDate.HoursPerDay && patient is null; hour++)
        {
            simulation.Step();
            patient = simulation.Entities.OfType<Humanoid>().FirstOrDefault(h => h.Behaviour is PatientBehaviour { TargetId: var target } && target == first.Id);
        }

        Assert.NotNull(patient);
        Assert.Equal(0, second.StaffId);
        Run(simulation, new SendPatientToRoomCommand(patient.Id, second.Id));
        StepFor(simulation, 2);
        Assert.Equal(doctor.Id, second.StaffId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnIdleDoctorIsCalledAwayToARoomWithPatientsWaitingIfStaffMayMove(bool allowed)
    {
        var simulation = NewStaffedHospital();
        var first = simulation.Entities.OfType<Room>().Single();
        for (var hour = 0; hour < 10 * GameDate.HoursPerDay && !first.StaffPresent; hour++)
        {
            simulation.Step();
        }

        Assert.True(first.StaffPresent);
        var doctor = first.StaffId;
        var second = BuildSecondGp(simulation);
        Run(simulation, new SetPolicyCommand(PolicyKind.StaffAllowedToMove, allowed ? 1 : 0));

        // Every patient sent to the first office goes to the second instead, which has no doctor of its own.
        // Without the policy, the queue is given ten days for the doctor not to come.
        var waited = 0;
        for (var hour = 0; hour < 300 * GameDate.HoursPerDay && !second.StaffPresent && first.PatientId == 0 && waited < 10 * GameDate.HoursPerDay; hour++)
        {
            foreach (var patient in simulation.Entities.OfType<Humanoid>().Where(h => h.Behaviour is PatientBehaviour { TargetId: var target } && target == first.Id).ToList())
            {
                Run(simulation, new SendPatientToRoomCommand(patient.Id, second.Id));
            }

            simulation.Step();
            if (second.DoorQueue is { Count: > 0 })
            {
                waited++;
            }
        }

        Assert.True(second.DoorQueue is { Count: > 0 } || second.StaffPresent, "No patient reached the second office.");
        if (allowed)
        {
            Assert.True(second.StaffPresent, "The doctor stayed in the empty office.");
            Assert.Equal(doctor, second.StaffId);
            Assert.Equal(0, first.StaffId);
        }
        else
        {
            Assert.False(second.StaffPresent);
            Assert.Equal(doctor, first.StaffId);
        }
    }
}
