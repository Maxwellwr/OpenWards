using OpenWards.Sim.Commands;
using OpenWards.Sim.Definitions;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Objects;
using static OpenWards.Sim.Definitions.FootprintFlags;

namespace OpenWards.Sim.Tests;

/// <summary>The hospital's score for looking after its plants, as in the original.</summary>
public class PlantCareTests
{
    private static readonly ObjectDefinition Plant = new("plant", "Plant", 45, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, CompleteCell)]),
        [Direction.South] = new([new(0, 0, CompleteCell)]),
    }, CorridorObject: true);

    [Theory]
    [InlineData(400, 55, 0)]
    [InlineData(321, 45, 10)]
    [InlineData(320, 45, 10)]
    [InlineData(80, 15, 40)]
    [InlineData(0, 5, 50)]
    [InlineData(-39, 0, 55)]
    public void APlantIsWorthLessTheDrierItIsAndWateringGivesTheRest(int water, int worth, int gain)
    {
        Assert.Equal((worth, gain), (PlantCare.Worth(water), PlantCare.WateringGain(water)));
    }

    [Fact]
    public void EachPlantPutDownAddsItsWorthUpToThePlantsBeforeItSoTheFirstAddsNothing()
    {
        var simulation = NewSimulation();
        var scores = new List<int>();
        for (var x = 1; x <= 3; x++)
        {
            Place(simulation, new TilePosition(x, 1));
            scores.Add(simulation.Hospitals[0].PlantCare);
        }

        Assert.Equal([0, 55, 110], scores);
        Assert.Equal(3, PlantCare.Plants(simulation, 0));
    }

    [Fact]
    public void EachFrameDroopedTakesElevenOffWithNoFloor()
    {
        var simulation = NewSimulation();
        Place(simulation, new TilePosition(1, 1));
        var plant = Assert.Single(simulation.Entities.OfType<RoomObject>()).Plant!;
        plant.ImportOriginalWater(PlantState.FullWater - (4 * PlantState.WaterPerState) + 1);

        // Until the water has come to 80, the last frame.
        while (plant.State != PlantState.Wilted)
        {
            simulation.Step();
        }

        Assert.Equal(-PlantCare.DroopLoss, simulation.Hospitals[0].PlantCare);
    }

    [Fact]
    public void OnlyTheFramesTakeOffNotRunningDry()
    {
        var plant = new PlantState();
        var random = new SimRandom(5);
        var frames = 0;
        while (!plant.HasRunDry)
        {
            frames += plant.Droop(random) ? 1 : 0;
        }

        Assert.Equal(4, frames);
    }

    [Fact]
    public void WateringAddsMoreTheDrierThePlantWasUpToTheCeiling()
    {
        var simulation = NewSimulation();
        Place(simulation, new TilePosition(1, 1));
        Place(simulation, new TilePosition(2, 1));
        var plants = simulation.Entities.OfType<RoomObject>().ToList();
        Assert.Equal(55, simulation.Hospitals[0].PlantCare);

        plants[0].Plant!.ImportOriginalWater(160);
        PlantCare.OnWatered(simulation, plants[0]);
        Assert.Equal(55 + 30, simulation.Hospitals[0].PlantCare);

        plants[1].Plant!.ImportOriginalWater(-39);
        PlantCare.OnWatered(simulation, plants[1]);
        Assert.Equal(110, simulation.Hospitals[0].PlantCare);
    }

    [Fact]
    public void APlantSoldTakesWhatItWasStillWorthAndOneMovedIsPutDownAgainFreshlyWatered()
    {
        var simulation = NewSimulation();
        for (var x = 1; x <= 3; x++)
        {
            Place(simulation, new TilePosition(x, 1));
        }

        var plants = simulation.Entities.OfType<RoomObject>().ToList();
        plants[0].Plant!.ImportOriginalWater(80);
        simulation.Schedule(0, new SellCorridorObjectCommand(plants[0].Id));
        simulation.Step();
        Assert.Equal(110 - 15, simulation.Hospitals[0].PlantCare);

        plants[1].Plant!.ImportOriginalWater(240);
        simulation.Schedule(0, new MoveCorridorObjectCommand(plants[1].Id, new TilePosition(5, 5), Direction.South));
        simulation.Step();
        var moved = simulation.Entities.OfType<RoomObject>().Single(o => o.Tile == new TilePosition(5, 5));

        // 110 − 15 − 35 for the move, then +55 with the one plant left before it as the ceiling.
        Assert.Equal((55, PlantState.FullWater), (simulation.Hospitals[0].PlantCare, moved.Plant!.Water));
    }

    [Fact]
    public void SavesKeepTheScore()
    {
        var simulation = NewSimulation();
        Place(simulation, new TilePosition(1, 1));
        Place(simulation, new TilePosition(2, 1));
        simulation.Hospitals[0].PlantCare = 37;

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, simulation.Content);

        Assert.Equal(37, restored.Hospitals[0].PlantCare);
    }

    [Fact]
    public void WithoutAScoreItIsEstimatedFromThePlants()
    {
        var simulation = NewSimulation();
        for (var x = 1; x <= 3; x++)
        {
            Place(simulation, new TilePosition(x, 1));
        }

        var plants = simulation.Entities.OfType<RoomObject>().ToList();
        plants[0].Plant!.ImportOriginalWater(150);

        // 55 × 2 − 11 × 3 frames drooped.
        Assert.Equal(77, PlantCare.Estimate(simulation, 0));
        Assert.Equal(0, PlantCare.Estimate(NewSimulation(), 0));
    }

    [Fact]
    public void TheShareIsTheScoreOutOfFiftyFiveAPlant()
    {
        Assert.Equal(0.15m, PlantCare.Share(33, 4));
        Assert.Equal(-0.25m, PlantCare.Share(-55, 4));
        Assert.Equal(1.5m, PlantCare.Share(165, 2));
        Assert.Null(PlantCare.Share(10, 0));
    }

    private static void Place(Simulation simulation, TilePosition tile)
    {
        Assert.Equal(ObjectPlacementProblem.None, ObjectPlacer.Check(simulation, "plant", tile, Direction.South, 0));
        simulation.Schedule(0, new PlaceObjectCommand("plant", tile, Direction.South));
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
        return new Simulation(map, seed: 3, new GameContent([], [], [Plant]));
    }
}
