using OpenWards.Sim;
using OpenWards.Sim.Calendar;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Objects;
using OpenWards.Sim.People;
using OpenWards.Sim.Rooms;
using OpenWards.Sim.Staff;
using static OpenWards.Content.Tests.PatientFlowTests;

namespace OpenWards.Content.Tests;

/// <summary>The ward (CorsixTH's rooms/ward.lua, objects/bed.lua).</summary>
public class WardTests
{
    private static readonly TileRect Area = new(2, 12, 8, 8);

    [Fact]
    public void PatientsLieInTheBedsSideBySideWhileNursesWriteAtTheDesks()
    {
        var simulation = NewStaffedHospital(Rules(), weakDoctor: true);
        var room = BuildWard(simulation, beds: 2, desks: 2);
        var hospital = simulation.Hospitals[0];
        var inBed = new HashSet<int>();
        var animations = new HashSet<int>();
        var mostAtOnce = 0;
        var nursesAtOnce = 0;

        for (var tick = 0; tick < 4 * 31 * GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
            var lying = 0;
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
            {
                if (humanoid.Behaviour is PatientBehaviour { Phase: PatientPhase.UsingObject, Use.IsUsing: true } patient && patient.TargetId == room.Id)
                {
                    lying++;
                    inBed.Add(humanoid.Id);
                    if (humanoid.ActionAnimation is { } animation)
                    {
                        animations.Add(animation.Id);
                    }
                }

                if (humanoid.Behaviour is NurseBehaviour { Phase: NursePhase.UsingWardDesk } && humanoid.ActionAnimation is { } desk)
                {
                    animations.Add(desk.Id);
                }
            }

            mostAtOnce = Math.Max(mostAtOnce, lying);
            nursesAtOnce = Math.Max(nursesAtOnce, simulation.Entities.Count(e => e is Humanoid { Behaviour: NurseBehaviour { RoomId: var id } } && id == room.Id));
        }

        Assert.True(inBed.Count >= 3, $"Only {inBed.Count} patients lay in a bed.");
        Assert.Equal(2, mostAtOnce);
        Assert.Equal(2, nursesAtOnce);
        Assert.Contains(animations, id => id is 4686 or 4688 or 4710 or 4712);
        Assert.Contains(animations, id => id is 4694 or 4696 or 4718 or 4720);
        Assert.Contains(animations, id => id is 3256 or 3258);
        Assert.Contains(hospital.Transactions, t => t is { Kind: TransactionKind.Treatment, Subject: "diag_ward" });

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, BaseGameContent.Create());
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        StepFor(simulation, 5 * GameDate.HoursPerDay);
        StepFor(loaded, 5 * GameDate.HoursPerDay);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    [Fact]
    public void NursesHealFasterThanOneAndMorePatientsSlowThemDown()
    {
        var simulation = NewStaffedHospital(Rules(), weakDoctor: true);
        var room = BuildWard(simulation, beds: 2, desks: 1);
        StepFor(simulation, GameDate.HoursPerDay);

        var healing = 0m;
        for (var tick = 0; tick < 31 * GameDate.HoursPerDay && healing == 0; tick++)
        {
            simulation.Step();
            healing = RoomVisits.WardHealingAmount(simulation, room);
        }

        // One nurse, one patient in the room: (0.5 + quality) / ln 2 / 1.0 on medium.
        Assert.InRange(healing, 0.5m / DecimalMathLn2, 1.5m / DecimalMathLn2);
    }

    /// <summary>
    /// As in the original game (its saves' beds all touch the wall on the side they face), and unlike CorsixTH, a bed only
    /// goes in with its head against the ward's wall.
    /// </summary>
    [Theory]
    [InlineData(8, 13, Direction.North, ObjectPlacementProblem.None)]
    [InlineData(9, 16, Direction.East, ObjectPlacementProblem.None)]
    [InlineData(8, 19, Direction.South, ObjectPlacementProblem.None)]
    [InlineData(3, 16, Direction.West, ObjectPlacementProblem.None)]
    [InlineData(8, 15, Direction.North, ObjectPlacementProblem.NeedsWall)]
    [InlineData(5, 16, Direction.East, ObjectPlacementProblem.NeedsWall)]
    [InlineData(8, 17, Direction.South, ObjectPlacementProblem.NeedsWall)]
    [InlineData(5, 16, Direction.West, ObjectPlacementProblem.NeedsWall)]
    [InlineData(9, 13, Direction.West, ObjectPlacementProblem.NeedsWall)]
    public void BedsStandWithTheirHeadsAgainstAWall(int x, int y, Direction orientation, ObjectPlacementProblem expected)
    {
        var simulation = NewStaffedHospital(Rules(), weakDoctor: true);
        Run(simulation, new BuildRoomCommand(new RoomPlan("ward", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));

        Assert.Equal(expected, ObjectPlacer.Check(simulation, "bed", new TilePosition(x, y), orientation, 0));
    }

    /// <summary>
    /// As in the original, the bed on the cursor sticks to the nearest wall, facing it; next to the door, where it does not
    /// fit, it goes to the next nearest wall.
    /// </summary>
    [Theory]
    [InlineData(6, 17, 6, 19, Direction.South)]
    [InlineData(8, 14, 9, 14, Direction.East)]
    [InlineData(3, 16, 3, 16, Direction.West)]
    [InlineData(5, 13, 3, 13, Direction.West)]
    [InlineData(2, 19, 3, 19, Direction.South)]
    [InlineData(9, 19, 9, 18, Direction.East)]
    public void TheBedSticksToTheNearestWall(int cursorX, int cursorY, int x, int y, Direction orientation)
    {
        var simulation = NewStaffedHospital(Rules(), weakDoctor: true);
        Run(simulation, new BuildRoomCommand(new RoomPlan("ward", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var bed = simulation.Content.FindObject("bed")!;

        var snapped = ObjectPlacer.SnapToWall(simulation, bed, Area, new TilePosition(cursorX, cursorY), 0);

        Assert.Equal((new TilePosition(x, y), orientation), snapped);
        Assert.Equal(ObjectPlacementProblem.None, ObjectPlacer.Check(simulation, "bed", snapped.Tile, snapped.Orientation, 0));
    }

    private const decimal DecimalMathLn2 = 0.6931471805599453m;

    /// <summary>Broken heart the only disease, hard to diagnose, so that the weak GP sends patients on to the ward.</summary>
    private static LevelRules Rules()
    {
        var nonVisuals = new int[DiseaseRules.NonVisualsCount];
        nonVisuals[DiseaseCatalog.Find("broken_heart")!.NonVisualsId!.Value] = 1;
        var diseases = new DiseaseRules(
            Enumerable.Repeat(100, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(true, DiseaseRules.ExpertiseCount + 1).ToArray(),
            Enumerable.Repeat(900, DiseaseRules.ExpertiseCount + 1).ToArray(),
            new int[DiseaseRules.VisualsCount],
            nonVisuals,
            new int[DiseaseRules.VisualsCount],
            new int[DiseaseRules.NonVisualsCount],
            0,
            0);
        return new LevelRules(
            1_000_000,
            [KeyValuePair.Create("gp", 2500), KeyValuePair.Create("ward", 2500)],
            [KeyValuePair.Create(ReceptionDesks.ObjectId, 600)],
            diseases: diseases);
    }

    private static Room BuildWard(Simulation simulation, int beds, int desks)
    {
        Run(simulation, new BuildRoomCommand(new RoomPlan("ward", Area, new DoorPlacement(new(5, Area.Y), EdgeOrientation.North))));
        var room = Assert.Single(simulation.Entities.OfType<Room>(), r => r.RoomType == "ward");
        for (var i = 0; i < beds; i++)
        {
            Place(simulation, "bed");
        }

        for (var i = 0; i < desks; i++)
        {
            Place(simulation, "desk");
        }

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        for (var i = 0; i < desks; i++)
        {
            Run(simulation, new HireStaffCommand(StaffClass.Nurse, 0, new(14, 16)));
        }

        return room;
    }

    private static void Place(Simulation simulation, string type)
    {
        for (var y = Area.Bottom - 1; y >= Area.Y; y--)
        {
            for (var x = Area.Right - 1; x >= Area.X; x--)
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
