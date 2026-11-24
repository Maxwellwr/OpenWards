using OpenWards.Sim.Calendar;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Disasters;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Rooms;
using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Tests;

/// <summary>Rat holes and rats, as the logic specification has them, and the rats' running (entities/rat.lua).</summary>
public class RatTests
{
    private const int Size = 16;

    [Fact]
    public void ACleanHospitalGetsNoHolesAndNoRats()
    {
        var simulation = NewSimulation();
        for (var day = 0; day < 200; day++)
        {
            StepDay(simulation);
        }

        Assert.Empty(simulation.Hospitals[0].Ratholes);
        Assert.Empty(simulation.Entities.OfType<Rat>());
    }

    [Fact]
    public void WithTwoHolesARatComesNowAndThenAndRunsBetweenThem()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        hospital.AddRathole(new Rathole(new TilePosition(3, 2), Direction.North, 1));
        hospital.AddRathole(new Rathole(new TilePosition(12, 2), Direction.North, 1));
        Rat? rat = null;
        for (var day = 0; day < 400 && rat is null; day++)
        {
            StepDay(simulation);
            rat = simulation.Entities.OfType<Rat>().FirstOrDefault();
        }

        Assert.NotNull(rat);
        Assert.NotEqual(rat!.Origin.Tile, rat.Target.Tile);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());

        // It runs a tile every four ticks and is gone once it is in the other hole.
        var seen = new List<TilePosition>();
        for (var tick = 0; tick < 400 && simulation.FindEntity(rat.Id) is not null; tick++)
        {
            simulation.Step();
            restored.Step();
            if (rat.Phase == RatPhase.Running)
            {
                seen.Add(rat.From);
            }
        }

        Assert.Null(simulation.FindEntity(rat.Id));
        // Found a day after it came, it may be on its way already: the tiles left are the end of its path.
        var left = seen.Distinct().ToList();
        Assert.NotEmpty(left);
        Assert.Equal(rat.Path.Skip(rat.Path.Count - left.Count - 1).Take(left.Count), left);
        Assert.Equal(rat.Target.Tile, rat.Path[^1]);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void LitterOpensAHoleInTheFirstWallFromTheTileLitteredLast()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];

        // Litter on 12 tiles, (13, 8) last; litter on litter adds no tile.
        for (var x = 2; x < 14; x++)
        {
            Litter.Drop(simulation, 0, LitterType.Paper, new TilePosition(x, 8), isMirrored: false);
        }

        Litter.Drop(simulation, 0, LitterType.Puke, new TilePosition(5, 8), isMirrored: false);
        Assert.Equal(12, Rats.LitteredTiles(simulation, 0));
        Assert.Equal(new TilePosition(13, 8), hospital.LastLitterTile);

        // Northwards the outside wall at y = 2 is met; the other ways leave the hospital's land first.
        Assert.Equal(new Rathole(new TilePosition(13, 2), Direction.North, 1), Rats.FindRatholeWall(simulation, 0, new TilePosition(13, 8), Direction.North));
        Assert.Null(Rats.FindRatholeWall(simulation, 0, new TilePosition(13, 8), Direction.East));
        Assert.Null(Rats.FindRatholeWall(simulation, 0, new TilePosition(13, 8), Direction.South));
        Assert.Null(Rats.FindRatholeWall(simulation, 0, new TilePosition(13, 8), Direction.West));

        // At 12 % a day, in one of four ways, the hole comes within a year; it is never made twice.
        for (var day = 0; day < 365 && hospital.Ratholes.Count == 0; day++)
        {
            StepDay(simulation);
        }

        Assert.Equal([new Rathole(new TilePosition(13, 2), Direction.North, 1)], hospital.Ratholes);

        // Building over a hole closes it.
        Rats.RemoveRatholesAt(simulation, new TilePosition(13, 2));
        Assert.Empty(hospital.Ratholes);
    }

    [Fact]
    public void ADoorIsNoPlaceForAHole()
    {
        var simulation = NewSimulation();
        simulation.Map[7, 2].Set(TileFlags.TallNorth, true);
        Assert.Null(Rats.FindRatholeWall(simulation, 0, new TilePosition(7, 9), Direction.North));
        Assert.NotNull(Rats.FindRatholeWall(simulation, 0, new TilePosition(8, 9), Direction.North));
    }

    [Fact]
    public void HolesCloseByThemselvesNowAndThen()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        for (var x = 2; x < 14; x++)
        {
            hospital.AddRathole(new Rathole(new TilePosition(x, 2), Direction.North, 1));
        }

        // A hole a day in 400: some of the 12 are gone after five years, not all.
        for (var day = 0; day < 5 * 365; day++)
        {
            StepDay(simulation);
        }

        Assert.InRange(hospital.Ratholes.Count, 2, 11);
    }

    [Fact]
    public void AShotRatLeavesADeadRatAndShotsAreCounted()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        hospital.AddRathole(new Rathole(new TilePosition(3, 2), Direction.North, 1));
        hospital.AddRathole(new Rathole(new TilePosition(12, 2), Direction.North, 1));
        var rat = Rat.Spawn(simulation, 0, hospital.Ratholes[0], hospital.Ratholes)!;
        for (var tick = 0; tick < Rat.LeaveHoleTicks + 9; tick++)
        {
            simulation.Step();
        }

        Assert.Equal(RatPhase.Running, rat.Phase);
        simulation.Schedule(0, new ShootRatCommand(rat.Id));
        simulation.Step();

        Assert.Null(simulation.FindEntity(rat.Id));
        var dead = Assert.Single(simulation.Entities.OfType<Litter>());
        Assert.Equal(LitterType.DeadRat, dead.Type);
        Assert.Equal(2, dead.Tile.Y);
        Assert.Equal(dead.Tile, hospital.LastLitterTile);
        Assert.Equal(1, Rats.LitteredTiles(simulation, 0));

        // A shot at a rat that is gone misses, but counts.
        simulation.Schedule(0, new ShootRatCommand(rat.Id));
        simulation.Step();
        Assert.Equal((2, 1), (hospital.RatShotsThisYear, hospital.RatsKilledThisYear));
    }

    [Fact]
    public void TheHolesInARoomsWallsCloseWhenItIsTakenOutOfUse()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        var room = new Room(99, "gp", new TileRect(5, 5, 3, 3), [.. Enumerable.Repeat(new OriginalTile(1, TileFlags.Passable), 9)]);

        // Holes in the corridor tiles round the room, in the walls towards it, and two that are not.
        hospital.AddRathole(new Rathole(new TilePosition(6, 4), Direction.South, 1));
        hospital.AddRathole(new Rathole(new TilePosition(5, 8), Direction.North, 1));
        hospital.AddRathole(new Rathole(new TilePosition(4, 7), Direction.East, 1));
        hospital.AddRathole(new Rathole(new TilePosition(8, 5), Direction.West, 1));
        hospital.AddRathole(new Rathole(new TilePosition(6, 4), Direction.North, 1));
        hospital.AddRathole(new Rathole(new TilePosition(3, 2), Direction.North, 1));

        // Hospital:removeRatholesAroundRoom.
        Rats.RemoveRatholesAroundRoom(simulation, room);

        Assert.Equal([new Rathole(new TilePosition(6, 4), Direction.North, 1), new Rathole(new TilePosition(3, 2), Direction.North, 1)], hospital.Ratholes);
    }

    [Fact]
    public void A_debug_rat_comes_out_of_new_holes_nearest_the_middle_of_the_view()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        // The north wall's tiles (x, 2) have their top corners at (32·(x − 2), 16·(x + 2)): x = 2 to 12 are in view, and
        // x = 7 and 8 nearest its middle (160, 150).
        var command = new MakeDebugRatCommand(0, 0, 320, 300);
        using (var wire = new MemoryStream())
        {
            command.Write(new SimWriter(wire));
            wire.Position = 0;
            Assert.Equal(command, Command.Read(new SimReader(wire)));
        }

        simulation.Schedule(0, command);
        simulation.Step();

        Assert.Equal([new Rathole(new TilePosition(7, 2), Direction.North, 1), new Rathole(new TilePosition(8, 2), Direction.North, 1)], hospital.Ratholes);
        var rat = Assert.Single(simulation.Entities.OfType<Rat>());
        Assert.Equal(new TilePosition(7, 2), rat.Origin.Tile);

        // With two holes in view already, the next rat comes out of one of them, and no hole is added.
        simulation.Schedule(0, command);
        simulation.Step();
        Assert.Equal(2, hospital.Ratholes.Count);
        Assert.Equal(2, simulation.Entities.OfType<Rat>().Count());

        // A view with no corridor wall in it makes nothing.
        simulation.Schedule(0, new MakeDebugRatCommand(2000, 2000, 2100, 2100));
        simulation.Step();
        Assert.Equal(2, simulation.Entities.OfType<Rat>().Count());
    }

    private static void StepDay(Simulation simulation)
    {
        for (var tick = 0; tick < GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
        }
    }

    /// <summary>A hospital of 12 by 12 floor tiles (2 to 13) whose north edge is an outside wall.</summary>
    private static Simulation NewSimulation()
    {
        var map = new LevelMap(Size, Size, playerCount: 1);
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                ref var tile = ref map[x, y];
                tile.Floor = 1;
                tile.Set(TileFlags.Passable, true);
                if (x is >= 2 and < 14 && y is >= 2 and < 14)
                {
                    tile.Parcel = 1;
                    tile.Set(TileFlags.Buildable | TileFlags.Hospital, true);
                    if (y == 2)
                    {
                        tile.NorthWall = 90;
                    }
                }
            }
        }

        map.UpdatePathfinding();
        return new Simulation(map, seed: 4);
    }
}
