using OpenWards.Sim;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Objects;
using OpenWards.Sim.People;
using OpenWards.Sim.Rooms;
using OpenWards.Sim.Staff;
using static OpenWards.Content.Tests.PatientFlowTests;

namespace OpenWards.Content.Tests;

/// <summary>
/// Where staff go once rested: nobody keeps their room; researchers, doctors from the training room and nurses look for
/// that kind of room (the game expert's answer of 26.11.2026).
/// </summary>
public class RestReturnTests
{
    private static readonly TileRect First = new(3, 3, 6, 6);
    private static readonly TileRect Second = new(12, 3, 6, 6);

    [Fact]
    public void ARestedResearcherGoesToTheDepartmentWithTheMostDesksLeftEvenWhenNoneIsFree()
    {
        var simulation = NewHospital();
        var first = BuildRoom(simulation, "research", First, "autopsy", "desk", "cabinet");
        var second = BuildRoom(simulation, "research", Second, "autopsy", "desk", "cabinet");
        var working = HireConsultant(simulation, new(15, 15));
        Run(simulation, new MoveStaffCommand(working.Id, new(First.X + 2, First.Y + 2)));
        Assert.Equal(first.Id, ((DoctorBehaviour)working.Behaviour!).RoomId);

        // The first department's desk is taken: the second, with its desk free, rather than the one they came from.
        var rested = HireConsultant(simulation, new(16, 15));
        SetFree(simulation, rested);
        Assert.True(RoomVisits.ReturnAfterRest(simulation, rested, RestReturn.Research));
        Assert.Equal(second.Id, ((DoctorBehaviour)rested.Behaviour!).RoomId);

        // Both desks taken: the first department on the tie, all the same.
        var third = HireConsultant(simulation, new(17, 15));
        SetFree(simulation, third);
        Assert.True(RoomVisits.ReturnAfterRest(simulation, third, RestReturn.Research));
        Assert.Equal(first.Id, ((DoctorBehaviour)third.Behaviour!).RoomId);
    }

    [Fact]
    public void ARestedNurseGoesToAWardEvenFromThePharmacy()
    {
        var simulation = NewHospital();
        var ward = BuildRoom(simulation, "ward", First, "bed", "desk");
        Run(simulation, new HireStaffCommand(StaffClass.Nurse, 0, new(15, 15)));
        var nurse = simulation.Entities.OfType<Humanoid>().Last(h => h.Profile?.Class == StaffClass.Nurse);

        // Free again after the rest (as RestingBehaviour leaves her), she looks for a ward rather than any room.
        SetFree(simulation, nurse);
        Assert.True(RoomVisits.ReturnAfterRest(simulation, nurse, RestReturn.Ward));
        Assert.Equal(ward.Id, ((NurseBehaviour)nurse.Behaviour!).RoomId);
    }

    [Fact]
    public void WithoutSuchARoomTheRestedAreFreeLikeAnyoneElse()
    {
        var simulation = NewHospital();
        var researcher = HireConsultant(simulation, new(15, 15));
        Run(simulation, new HireStaffCommand(StaffClass.Nurse, 0, new(16, 15)));
        var nurse = simulation.Entities.OfType<Humanoid>().Last(h => h.Profile?.Class == StaffClass.Nurse);

        Assert.False(RoomVisits.ReturnAfterRest(simulation, researcher, RestReturn.Research));
        Assert.False(RoomVisits.ReturnAfterRest(simulation, researcher, RestReturn.None));
        Assert.False(RoomVisits.ReturnAfterRest(simulation, nurse, RestReturn.Ward));
        Assert.Equal(0, ((DoctorBehaviour)researcher.Behaviour!).RoomId);
        Assert.Equal(0, ((NurseBehaviour)nurse.Behaviour!).RoomId);
    }

    /// <summary>Free for any room, as the end of a rest leaves them.</summary>
    private static void SetFree(Simulation simulation, Humanoid staff)
    {
        StaffEmployment.Detach(simulation, staff);
        staff.Behaviour = StaffHiring.NewBehaviour(staff.Profile!.Class);
    }

    private static Humanoid HireConsultant(Simulation simulation, TilePosition tile)
    {
        // The super doctor cheat makes the doctors for hire consultants with every ability, researchers among them.
        Run(simulation, new CheatCommand(CheatKind.ToggleSuperDoctor));
        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, tile));
        Run(simulation, new CheatCommand(CheatKind.ToggleSuperDoctor));
        var doctor = simulation.Entities.OfType<Humanoid>().Last(h => h.Profile?.Class == StaffClass.Doctor);
        Assert.True(doctor.Profile!.IsResearcher);
        return doctor;
    }

    private static Room BuildRoom(Simulation simulation, string roomType, TileRect area, params string[] objects)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan(roomType, area, new DoorPlacement(new(area.X + 2, area.Bottom), EdgeOrientation.North))));
        var room = simulation.Entities.OfType<Room>().Last(r => r.RoomType == roomType);
        foreach (var type in objects)
        {
            Place(simulation, type, area);
        }

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        return room;
    }

    private static void Place(Simulation simulation, string type, TileRect area)
    {
        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
            {
                foreach (var orientation in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
                {
                    if (ObjectPlacer.Check(simulation, type, new(x, y), orientation, 0) == ObjectPlacementProblem.None)
                    {
                        Run(simulation, new PlaceObjectCommand(type, new(x, y), orientation));
                        return;
                    }
                }
            }
        }

        Assert.Fail($"Found no place for {type}.");
    }
}
