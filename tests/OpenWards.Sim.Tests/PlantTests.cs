using OpenWards.Sim.Commands;
using OpenWards.Sim.Definitions;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Objects;
using OpenWards.Sim.People;
using OpenWards.Sim.Staff;
using static OpenWards.Sim.Definitions.FootprintFlags;

namespace OpenWards.Sim.Tests;

/// <summary>Plants drooping without water, as in the original.</summary>
public class PlantTests
{
    private static readonly ObjectDefinition Plant = new("plant", "Plant", 45, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, CompleteCell)]),
        [Direction.South] = new([new(0, 0, CompleteCell)]),
    }, CorridorObject: true);

    private static readonly ObjectDefinition Bin = new("bin", "Bin", 50, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, OnlySide)]),
    }, CorridorObject: true);

    [Theory]
    [InlineData(400, PlantState.Healthy, 400)]
    [InlineData(321, PlantState.Healthy, 321)]
    [InlineData(320, 1, 320)]
    [InlineData(241, 1, 241)]
    [InlineData(240, 2, 240)]
    [InlineData(160, 3, 160)]
    [InlineData(80, PlantState.Wilted, 80)]
    [InlineData(-11, PlantState.Wilted, -11)]
    [InlineData(-40, PlantState.Wilted, 0)]
    public void AnOriginalSavesWaterIsKeptAndSetsTheFrame(int water, int state, int kept)
    {
        var plant = new PlantState();

        plant.ImportOriginalWater(water);

        Assert.Equal((state, kept), (plant.State, plant.Water));
    }

    [Fact]
    public void ItNeedsWateringFromTheFirstDroopingFrameAndIsWitheredOnlyBelowNoWater()
    {
        var plant = new PlantState();
        Assert.Equal((false, 0, false, 5), (plant.NeedsWatering, plant.WateringPriority, plant.IsWithered, plant.PleasingFactor));

        plant.ImportOriginalWater(320);
        Assert.Equal((true, 1, false), (plant.NeedsWatering, plant.WateringPriority, plant.IsWithered));

        plant.ImportOriginalWater(0);
        Assert.Equal((true, 4, false, 1), (plant.NeedsWatering, plant.WateringPriority, plant.IsWithered, plant.PleasingFactor));

        plant.ImportOriginalWater(-1);
        Assert.True(plant.IsWithered);
    }

    [Fact]
    public void BelowNoWaterItRunsDownToMinusFortyThenStartsAgainMoreUrgently()
    {
        var plant = new PlantState();
        plant.ImportOriginalWater(-39);
        var random = new SimRandom(7);

        while (!plant.HasRunDry)
        {
            plant.Droop(random);
        }

        Assert.Equal((0, PlantState.Wilted, PlantState.WrappedPriority), (plant.Water, plant.State, plant.WateringPriority));

        plant.Restore();
        Assert.Equal((PlantState.FullWater, false, PlantState.Healthy), (plant.Water, plant.HasRunDry, plant.State));
    }

    [Fact]
    public void APlacedPlantLosesAboutADropAndAHalfADayWhateverTheWarmthAndSavesKeepIt()
    {
        var simulation = NewSimulation();
        Place(simulation, "plant", new TilePosition(3, 3));
        var plant = Assert.Single(simulation.Entities.OfType<RoomObject>()).Plant;
        Assert.NotNull(plant);

        // Thirty days: one draw in two every 16 ticks of 50 a day, about 47 drops.
        for (var tick = 0; tick < 30 * 50; tick++)
        {
            simulation.Step();
        }

        Assert.InRange(PlantState.FullWater - plant.Water, 30, 64);
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, simulation.Content);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(plant.Water, Assert.Single(restored.Entities.OfType<RoomObject>()).Plant!.Water);
    }

    [Fact]
    public void FromWateredToDryTakesAbout256Days()
    {
        var plant = new PlantState();
        var random = new SimRandom(11);
        var draws = 0;
        while (plant.Water > 0)
        {
            plant.Droop(random);
            draws++;
        }

        // 800 draws on average, 16 ticks each, 50 ticks a day.
        Assert.InRange(draws * PlantState.DroopInterval / 50, 230, 282);
    }

    [Fact]
    public void OtherObjectsHaveNoPlantState()
    {
        var simulation = NewSimulation();
        Place(simulation, "bin", new TilePosition(3, 3), Direction.North);

        Assert.Null(Assert.Single(simulation.Entities.OfType<RoomObject>()).Plant);
    }

    [Fact]
    public void ADeadPlantMakesStaffUnhappy()
    {
        var simulation = NewSimulation();
        Place(simulation, "plant", new TilePosition(3, 3));
        var plant = Assert.Single(simulation.Entities.OfType<RoomObject>()).Plant!;
        plant.ImportOriginalWater(0);

        simulation.Schedule(0, new HireStaffCommand(StaffClass.Handyman, 0, new TilePosition(4, 4)));
        simulation.Step();
        var handyman = simulation.Entities.OfType<Humanoid>().Single();
        handyman.Happiness = 0.5m;
        handyman.Warmth = 0.3m;

        Feelings.OnEndDay(simulation, handyman);

        // The most drooping plant −0.003 + 1 × 0.001.
        Assert.Equal(0.5m - 0.002m, handyman.Happiness);
    }

    private static void Place(Simulation simulation, string type, TilePosition tile, Direction orientation = Direction.South)
    {
        Assert.Equal(ObjectPlacementProblem.None, ObjectPlacer.Check(simulation, type, tile, orientation, 0));
        simulation.Schedule(0, new PlaceObjectCommand(type, tile, orientation));
        simulation.Step();
    }

    private static Simulation NewSimulation()
    {
        var map = TestMaps.FromRows([.. Enumerable.Repeat("........", 8)]);
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Parcel = 1;
                map[x, y].Set(TileFlags.Buildable | TileFlags.Hospital | TileFlags.BuildableNorth | TileFlags.BuildableEast | TileFlags.BuildableSouth | TileFlags.BuildableWest, true);
            }
        }

        map.UpdatePathfinding();
        return new Simulation(map, seed: 3, new GameContent([], [], [Plant, Bin]));
    }
}
