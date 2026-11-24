using OpenWards.Sim.Commands;
using OpenWards.Sim.Definitions;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Rooms;
using OpenWards.Sim.Staff;

namespace OpenWards.Sim.Tests;

/// <summary>Staff tiring at work (CorsixTH's Staff:tick, isTiring, checkIfNeedRest and Staff:tickDay).</summary>
public class TirednessTests
{
    private static readonly RoomDefinition Office = new("office", "Office", 3, 18, new WallSet(82, 83, 90, 91));
    private static readonly RoomDefinition StaffRoom = new("staff_room", "Staff Room", 3, 18, new WallSet(82, 83, 90, 91));

    [Fact]
    public void WorkTiresByWorkLightEveryUpdateButNotIdleOrInTheStaffRoomAndReceptionistsNever()
    {
        var simulation = NewSimulation();
        AddRoom(simulation, "office", new TileRect(1, 1, 3, 3), active: false);
        AddRoom(simulation, "staff_room", new TileRect(4, 1, 3, 3), active: false);
        var idle = Hire(simulation, StaffClass.Doctor, new TilePosition(6, 6));
        var handyman = Hire(simulation, StaffClass.Handyman, new TilePosition(6, 5));
        var resting = Hire(simulation, StaffClass.Nurse, new TilePosition(5, 2));
        var working = Hire(simulation, StaffClass.Doctor, new TilePosition(2, 2));
        var receptionist = Hire(simulation, StaffClass.Receptionist, new TilePosition(7, 6));
        SetBehaviour(working, new People.DoctorBehaviour(), "Phase", People.DoctorPhase.WalkingToRoom);
        handyman.Behaviour = new People.HandymanBehaviour();
        SetBehaviour(handyman, handyman.Behaviour, "Phase", People.HandymanPhase.WalkingToLitter);
        foreach (var humanoid in new[] { idle, handyman, resting, working, receptionist })
        {
            humanoid.Fatigue = 0m;
        }

        // Ten updates of WorkLight 1: 10 thousandths, whatever the work.
        for (var update = 0; update < 10; update++)
        {
            People.PatientNeeds.Modify(simulation);
        }

        Assert.Equal(0.01m, working.Fatigue);
        Assert.Equal(0.01m, handyman.Fatigue);
        Assert.Equal(0m, idle.Fatigue);
        Assert.Equal(0m, resting.Fatigue);
        Assert.Equal(0m, receptionist.Fatigue);
    }

    [Fact]
    public void MoodMovesOnlyPastTheThresholds()
    {
        var simulation = NewSimulation(new TirednessRules(600, 700, 800, 300));
        var nurse = Hire(simulation, StaffClass.Nurse, new TilePosition(6, 6));

        foreach (var (fatigue, happiness) in new[] { (0.2m, 0.51m), (0.3m, 0.5m), (0.6m, 0.5m), (0.8m, 0.5m), (0.801m, 0.49m) })
        {
            nurse.Fatigue = fatigue;
            nurse.Happiness = 0.5m;
            Tiredness.Modify(simulation, nurse);
            Assert.Equal(happiness, nurse.Happiness);
        }
    }

    [Fact]
    public void AnOverworkedDoctorCracksUpAndComesRoundRestingInTheStaffRoom()
    {
        var simulation = NewSimulation(new TirednessRules(600, 700, 800, 300));
        AddRoom(simulation, "staff_room", new TileRect(4, 1, 3, 3), active: false);
        var doctor = Hire(simulation, StaffClass.Doctor, new TilePosition(6, 6));
        var layer = doctor.Layers[Humanoid.StaffLayer];

        // One update in 1,000 past CrackUpTired.
        var updates = 0;
        while (!doctor.IsCrazy && updates < 20_000)
        {
            doctor.Fatigue = 0.9m;
            Tiredness.Modify(simulation, doctor);
            updates++;
        }

        Assert.True(doctor.IsCrazy);
        Assert.Equal(doctor.Profile!.Layer5 + 4, doctor.Layers[Humanoid.StaffLayer]);

        // Still very tired in the staff room, they stay crazy; rested, they are themselves again.
        doctor.PlaceAt(new TilePosition(5, 2));
        doctor.Fatigue = 0.75m;
        Tiredness.Modify(simulation, doctor);
        Assert.True(doctor.IsCrazy);
        doctor.Fatigue = 0.5m;
        Tiredness.Modify(simulation, doctor);
        Assert.False(doctor.IsCrazy);
        Assert.Equal(layer, doctor.Layers[Humanoid.StaffLayer]);
    }

    [Fact]
    public void FatigueStopsAtOne()
    {
        var simulation = NewSimulation();
        var handyman = Hire(simulation, StaffClass.Handyman, new TilePosition(6, 6));
        SetBehaviour(handyman, handyman.Behaviour!, "Phase", People.HandymanPhase.WalkingToLitter);
        handyman.Fatigue = 0.9995m;

        Tiredness.Modify(simulation, handyman);

        Assert.Equal(1m, handyman.Fatigue);
    }

    [Fact]
    public void StaffMoreTiredThanThePolicyWaitForAStaffRoomWithoutLosingMood()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor, new TilePosition(6, 6));
        doctor.Happiness = 0.5m;

        // Exactly at the policy (Tired 600) is not past it.
        doctor.Fatigue = 0.6m;
        StaffEmployment.Tick(simulation, doctor);
        Assert.False(doctor.WaitingForStaffRoom);

        doctor.Fatigue = 0.601m;
        StaffEmployment.Tick(simulation, doctor);
        Assert.True(doctor.WaitingForStaffRoom);
        StaffEmployment.Tick(simulation, doctor);
        Assert.Equal(0.5m, doctor.Happiness);

        // Very tired, they look it.
        doctor.Fatigue = 0.7m;
        StaffEmployment.Tick(simulation, doctor);
        Assert.True(doctor.IsTiredMoodShown);
        Assert.Equal(0.5m, doctor.Happiness);
    }

    [Fact]
    public void NobodyWaitsWhenThereIsAStaffRoomAndBuildingOneEndsTheWait()
    {
        var simulation = NewSimulation();
        var doctor = Hire(simulation, StaffClass.Doctor, new TilePosition(6, 6));
        doctor.Fatigue = 0.65m;
        doctor.WaitingForStaffRoom = true;

        var staffRoom = AddRoom(simulation, "staff_room", new TileRect(1, 1, 3, 3), active: true);
        Tiredness.OnRoomActivated(simulation, staffRoom);
        Assert.False(doctor.WaitingForStaffRoom);

        StaffEmployment.Tick(simulation, doctor);
        Assert.False(doctor.WaitingForStaffRoom);
    }

    [Theory]
    [InlineData(8, 200, 8)] // Not more tired than half the RecoveryFactor of 450: all of it.
    [InlineData(8, 225, 8)]
    [InlineData(8, 226, 15)] // 8 × 450 / 226.
    [InlineData(8, 900, 4)]
    [InlineData(3, 1000, 3)] // At least RecoveryMinimum.
    [InlineData(60, 900, 30)]
    public void RestRecoversLessTheMoreTired(int value, int fatigue, int recovered) =>
        Assert.Equal(recovered, RestRules.Default.Recovery(value, fatigue));

    [Fact]
    public void RestStandingOrOnTheSofaComesEveryEightFramesOfOneOrTwoTicksBySkillAndFatigue()
    {
        var simulation = NewSimulation(new TirednessRules(600, 700, 800, 300));
        var doctor = Hire(simulation, StaffClass.Doctor, new TilePosition(6, 6));
        var staff = simulation.Rules.Staff;
        int Period(int skill, decimal fatigue)
        {
            typeof(StaffProfile).GetProperty(nameof(StaffProfile.Skill))!.SetValue(doctor.Profile, skill);
            doctor.Fatigue = fatigue;
            return Tiredness.RestPeriod(simulation, doctor);
        }

        // Up to DoctorThreshold: always 16.
        Assert.Equal((16, 16, 16), (Period(staff.DoctorThreshold, 0.5m), Period(staff.DoctorThreshold, 0.75m), Period(staff.DoctorThreshold, 0.9m)));

        // Between the thresholds: 8 until very tired.
        Assert.Equal((8, 8, 16, 16), (Period(staff.DoctorThreshold + 1, 0.7m), Period(staff.ConsultantThreshold - 1, 0.5m), Period(staff.DoctorThreshold + 1, 0.701m), Period(staff.DoctorThreshold + 1, 0.9m)));

        // Consultants: 8 until tired enough to crack up.
        Assert.Equal((8, 8, 16), (Period(staff.ConsultantThreshold, 0.75m), Period(staff.ConsultantThreshold, 0.8m), Period(staff.ConsultantThreshold, 0.801m)));
    }

    [Fact]
    public void TheLevelSetsWhenStaffCountAsTired()
    {
        var simulation = NewSimulation(new TirednessRules(300, 400, 500));
        Assert.True(Tiredness.IsVeryTired(simulation, 0.45m));
        Assert.False(Tiredness.IsCrackUpTired(simulation, 0.45m));
        Assert.Equal(2, TirednessRules.Unset.Rest.WorkLight);
        Assert.Equal(1, TirednessRules.Default.Rest.WorkLight);
    }

    [Fact]
    public void SavesKeepFatigueAndTheWait()
    {
        var simulation = NewSimulation(new TirednessRules(300, 400, 500));
        var handyman = Hire(simulation, StaffClass.Handyman, new TilePosition(6, 6));
        handyman.Fatigue = 0.35m;
        for (var i = 0; i < 3; i++)
        {
            simulation.Step();
        }

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        var copy = Assert.Single(restored.Entities.OfType<Humanoid>());
        Assert.Equal(handyman.Fatigue, copy.Fatigue);
        Assert.True(copy.WaitingForStaffRoom);
        Assert.Equal(400, restored.Rules.Tiredness.VeryTired);
    }

    private static void SetBehaviour(Humanoid humanoid, People.HumanoidBehaviour behaviour, string property, object value)
    {
        humanoid.Behaviour = behaviour;
        behaviour.GetType().GetProperty(property)!.SetValue(behaviour, value);
    }

    private static Room AddRoom(Simulation simulation, string roomType, TileRect area, bool active)
    {
        var room = simulation.AddEntity(id => new Room(id, roomType, area, [.. Enumerable.Repeat(default(OriginalTile), area.Width * area.Height)]));
        room.IsActive = active;
        for (var x = area.X; x < area.Right; x++)
        {
            for (var y = area.Y; y < area.Bottom; y++)
            {
                simulation.Map[x, y].RoomId = room.Id;
            }
        }

        return room;
    }

    private static Humanoid Hire(Simulation simulation, StaffClass staffClass, TilePosition tile)
    {
        // Hired in the corridor, then put where wanted: staff cannot be hired into a room.
        simulation.Schedule(0, new HireStaffCommand(staffClass, 0, new TilePosition(7, 7)));
        simulation.Step();
        var hired = simulation.Entities.OfType<Humanoid>().Last();
        hired.PlaceAt(tile);
        hired.Fatigue = 0m;
        hired.Happiness = 1m;
        return hired;
    }

    private static Simulation NewSimulation(TirednessRules? tiredness = null)
    {
        var map = TestMaps.FromRows("........", "........", "........", "........", "........", "........", "........", "........");
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Parcel = 1;
                map[x, y].Set(TileFlags.Buildable | TileFlags.Hospital, true);
            }
        }

        var content = new GameContent([Office, StaffRoom], [], []);
        return new Simulation(map, seed: 1, content, new LevelRules(10_000, [], [], tiredness: tiredness));
    }
}
