// Rat holes and rats. The daily holes and rats follow the logic specification (the original game); the debug rat, the
// walls of a tile and closing holes when building are ported from CorsixTH CorsixTH/Lua/hospital.lua
// (getWallsAround, addRathole, removeRatholeXY, removeRatholesAroundRoom, makeDebugRat, findRatholeTiles,
// isRatholeInView).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;

namespace OpenWards.Sim.Disasters;

/// <summary>
/// Rats and rat holes, as the logic specification describes the original game. Holes come from litter: each day, with
/// a chance of one percent for each littered tile of the hospital, a hole opens in the first wall met going in a random
/// direction from the tile littered last. One time in 400 a random hole closes by itself, and one day in ten, with two
/// holes or more, a rat runs from one to another. Rats affect nobody; the player can shoot them, and the year's shots
/// and kills are counted for the trophies. Building over a hole closes it; handymen do not.
/// </summary>
/// <remarks>
/// Departs from CorsixTH, whose holes follow the littered fraction of the corridors and whose rats come by the
/// hospital's area with a chance of 5 % plus half that fraction.
/// </remarks>
public static class Rats
{
    /// <summary>The most rat holes a hospital has at a time.</summary>
    public const int MaxRatholes = 40;

    /// <summary>A hole closes by itself one day in <see cref="RatholeCloseOdds"/> / <see cref="RatholeCloseChance"/>.</summary>
    private const int RatholeCloseChance = 10;

    private const int RatholeCloseOdds = 4000;

    /// <summary>The daily chance, in percent, of a rat when there are two holes or more.</summary>
    private const int RatChancePercent = 10;

    /// <summary>The end of a hospital's day, after the heating: holes open, close, and a rat may run.</summary>
    internal static void OnEndDay(Simulation simulation, Hospital hospital)
    {
        var random = simulation.Random;

        // 1. A new hole, with a chance of one percent per littered tile.
        var littered = LitteredTiles(simulation, hospital.Player);
        if (littered > 0 && hospital.LastLitterTile is { } start && hospital.Ratholes.Count < MaxRatholes
            && random.Next(100) < littered)
        {
            var direction = (Direction)random.Next(4);
            if (FindRatholeWall(simulation, hospital.Player, start, direction) is { } hole)
            {
                hospital.AddRathole(hole);
            }
        }

        // 2. A random hole may close by itself.
        if (hospital.Ratholes.Count > 0 && random.Next(RatholeCloseOdds) < RatholeCloseChance)
        {
            var hole = hospital.Ratholes[random.Next(hospital.Ratholes.Count)];
            hospital.RemoveRatholes(other => other == hole);
        }

        // 3. With two holes or more, a rat may run out of one to another.
        if (hospital.Ratholes.Count >= 2 && random.Next(100) < RatChancePercent)
        {
            var origin = hospital.Ratholes[random.Next(hospital.Ratholes.Count)];
            Rat.Spawn(simulation, hospital.Player, origin, hospital.Ratholes);
        }
    }

    /// <summary>
    /// The tiles of the hospital's land (its parcels, yard included) with litter on them that a handyman sweeps up:
    /// rubbish, puke, pee and dead rats.
    /// </summary>
    public static int LitteredTiles(Simulation simulation, int player)
    {
        var map = simulation.Map;
        var tiles = new HashSet<TilePosition>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is Litter { IsRemoved: false, IsCleanable: true } litter && IsOwnLand(map, player, litter.Tile))
            {
                tiles.Add(litter.Tile);
            }
        }

        return tiles.Count;
    }

    /// <summary>
    /// Litter was dropped on a tile: if the tile had no litter yet, the hospital whose land it is remembers it as the tile
    /// littered last, where new rat holes start from.
    /// </summary>
    internal static void OnLittered(Simulation simulation, TilePosition tile, bool wasClean)
    {
        var map = simulation.Map;
        if (!wasClean || !map.Contains(tile.X, tile.Y))
        {
            return;
        }

        var parcel = map[tile.X, tile.Y].Parcel;
        var owner = parcel > 0 && parcel < map.ParcelCount ? map.GetParcelOwner(parcel) - 1 : -1;
        if (owner >= 0 && owner < simulation.Hospitals.Count)
        {
            simulation.Hospitals[owner].LastLitterTile = tile;
        }
    }

    /// <summary>
    /// The player shoots at a rat: the year's shots go up by one, and if the rat is still there it is hit, the year's
    /// kills go up and a dead rat lies where it was (on the hospital's land).
    /// </summary>
    internal static void Shoot(Simulation simulation, int player, Rat? rat)
    {
        if (player < 0 || player >= simulation.Hospitals.Count)
        {
            return;
        }

        var hospital = simulation.Hospitals[player];
        hospital.RatShotsThisYear++;
        if (rat is not { IsGone: false } || rat.Player != player)
        {
            return;
        }

        hospital.RatsKilledThisYear++;
        rat.Shoot();
        var tile = rat.Phase == RatPhase.Running && rat.Timer * 2 <= Rat.TicksPerTile ? rat.To : rat.From;
        if (IsOwnLand(simulation.Map, player, tile))
        {
            // A dead rat may lie on other litter; it then adds no littered tile.
            var wasClean = !Litter.At(simulation, tile).Any(litter => litter.IsCleanable);
            simulation.AddEntity(id => new Litter(id, player, LitterType.DeadRat, tile, isMirrored: false));
            OnLittered(simulation, tile, wasClean);
        }
    }

    /// <summary>
    /// Going from <paramref name="start"/> towards <paramref name="direction"/> over the hospital's land, the first wall
    /// met: a hole in it if that wall is no door or window and the tile before it is a corridor tile inside the building.
    /// Leaving the hospital's land, or the map, first gives none.
    /// </summary>
    internal static Rathole? FindRatholeWall(Simulation simulation, int player, TilePosition start, Direction direction)
    {
        var map = simulation.Map;
        var tile = start;
        while (map.Contains(tile.X, tile.Y) && IsOwnLand(map, player, tile))
        {
            var cell = map[tile.X, tile.Y];
            var next = direction switch
            {
                Direction.North => new TilePosition(tile.X, tile.Y - 1),
                Direction.East => new TilePosition(tile.X + 1, tile.Y),
                Direction.South => new TilePosition(tile.X, tile.Y + 1),
                _ => new TilePosition(tile.X - 1, tile.Y),
            };
            var hasNeighbour = map.Contains(next.X, next.Y);
            var (wall, door) = direction switch
            {
                Direction.North => (cell.NorthWall, cell.Has(TileFlags.TallNorth)),
                Direction.West => (cell.WestWall, cell.Has(TileFlags.TallWest)),
                Direction.East when hasNeighbour => (map[next.X, next.Y].WestWall, map[next.X, next.Y].Has(TileFlags.TallWest)),
                Direction.South when hasNeighbour => (map[next.X, next.Y].NorthWall, map[next.X, next.Y].Has(TileFlags.TallNorth)),
                _ => ((ushort)0, false),
            };
            if (wall != 0)
            {
                if (door || simulation.Content.IsWindowWall(wall) || cell.RoomId != 0 || !cell.Has(TileFlags.Hospital))
                {
                    return null;
                }

                return new Rathole(tile, direction, cell.Parcel);
            }

            tile = next;
        }

        return null;
    }

    /// <summary>Whether the tile is on a parcel the hospital owns.</summary>
    private static bool IsOwnLand(LevelMap map, int player, TilePosition tile)
    {
        if (!map.Contains(tile.X, tile.Y))
        {
            return false;
        }

        var parcel = map[tile.X, tile.Y].Parcel;
        return parcel > 0 && parcel < map.ParcelCount && map.GetParcelOwner(parcel) == player + 1;
    }

    /// <summary>
    /// <c>Hospital:makeDebugRat</c>: the hospital gets rat holes in view until two are, nearest the view's centre first,
    /// and a rat comes out of the first new one, else of a random one in view. Nothing happens without a hole in view.
    /// </summary>
    internal static void MakeDebugRat(Simulation simulation, Hospital hospital, int left, int top, int right, int bottom)
    {
        var visible = hospital.Ratholes.Where(hole => IsInView(hole, left, top, right, bottom)).ToList();
        Rathole? emerge = null;
        foreach (var hole in FindRatholeTiles(simulation, hospital, Math.Max(0, 2 - visible.Count), left, top, right, bottom))
        {
            hospital.AddRathole(hole);
            visible.Add(hole);
            emerge ??= hole;
        }

        if (visible.Count == 0)
        {
            return;
        }

        var origin = emerge ?? visible[simulation.Random.NextInclusive(1, visible.Count) - 1];
        Rat.Spawn(simulation, hospital.Player, origin, hospital.Ratholes);
    }

    /// <summary><c>isRatholeInView</c>: a hole in a north or west wall whose tile's top corner is in view.</summary>
    private static bool IsInView(Rathole hole, int left, int top, int right, int bottom)
    {
        if (hole.Wall is not (Direction.North or Direction.West))
        {
            return false;
        }

        var (x, y) = ScreenPosition(hole.Tile);
        return x >= left && x <= right && y >= top && y <= bottom;
    }

    /// <summary><c>Map:WorldToScreen</c>: a tile's top corner in the map's pixels.</summary>
    private static (int X, int Y) ScreenPosition(TilePosition tile) => (32 * (tile.X - tile.Y), 16 * (tile.X + tile.Y));

    /// <summary>
    /// <c>Hospital:findRatholeTiles</c>: up to <paramref name="count"/> corridor tiles of the hospital in view with a
    /// north wall, or else a west one, without a hole in it yet, nearest the view's centre first.
    /// </summary>
    private static List<Rathole> FindRatholeTiles(Simulation simulation, Hospital hospital, int count, int left, int top, int right, int bottom)
    {
        var map = simulation.Map;
        var candidates = new List<(Rathole Hole, long Distance)>();
        if (count <= 0)
        {
            return [];
        }

        for (var x = 0; x < map.Width; x++)
        {
            for (var y = 0; y < map.Height; y++)
            {
                var tile = new TilePosition(x, y);
                var cell = map[x, y];
                if (!Epidemics.IsInHospital(simulation, hospital.Player, tile) || cell.RoomId != 0 || !cell.Has(TileFlags.Buildable))
                {
                    continue;
                }

                Rathole? pick = null;
                foreach (var wall in WallsAround(map, tile))
                {
                    if (wall.Wall == Direction.North)
                    {
                        pick = wall;
                        break;
                    }

                    if (wall.Wall == Direction.West && pick is null)
                    {
                        pick = wall;
                    }
                }

                if (pick is { } hole && IsInView(hole, left, top, right, bottom)
                    && !hospital.Ratholes.Any(other => other.Tile == hole.Tile && other.Wall == hole.Wall))
                {
                    // Twice the distance from the centre on each axis, to stay in whole numbers.
                    var (sx, sy) = ScreenPosition(tile);
                    long dx = (2L * sx) - left - right;
                    long dy = (2L * sy) - top - bottom;
                    candidates.Add((hole, (dx * dx) + (dy * dy)));
                }
            }
        }

        return candidates.OrderBy(candidate => candidate.Distance).Take(count).Select(candidate => candidate.Hole).ToList();
    }

    /// <summary>
    /// <c>World:prepareFootprintTilesForBuild</c> and <c>prepareRectangleTilesForBuild</c>'s
    /// <c>Hospital:removeRatholeXY</c>: the holes of a tile close when something is built there.
    /// </summary>
    internal static void RemoveRatholesAt(Simulation simulation, TilePosition tile)
    {
        foreach (var hospital in simulation.Hospitals)
        {
            hospital.RemoveRatholesAt(tile);
        }
    }

    /// <summary>
    /// <c>Hospital:removeRatholesAroundRoom</c>, from <c>Room:deactivate</c>: the holes in the room's walls close, those of
    /// the corridor tiles along its four sides in the wall towards the room.
    /// </summary>
    internal static void RemoveRatholesAroundRoom(Simulation simulation, Room room)
    {
        var area = room.Area;
        bool Facing(Rathole hole) =>
            (hole.Wall == Direction.South && hole.Tile.Y == area.Y - 1 && hole.Tile.X >= area.X && hole.Tile.X < area.Right)
            || (hole.Wall == Direction.North && hole.Tile.Y == area.Bottom && hole.Tile.X >= area.X && hole.Tile.X < area.Right)
            || (hole.Wall == Direction.East && hole.Tile.X == area.X - 1 && hole.Tile.Y >= area.Y && hole.Tile.Y < area.Bottom)
            || (hole.Wall == Direction.West && hole.Tile.X == area.Right && hole.Tile.Y >= area.Y && hole.Tile.Y < area.Bottom);
        foreach (var hospital in simulation.Hospitals)
        {
            hospital.RemoveRatholes(Facing);
        }
    }

    /// <summary>
    /// <c>Hospital:getWallsAround</c>: the walls of a tile, west, north, east and south, the east and south ones being
    /// the neighbours' west and north walls.
    /// </summary>
    internal static List<Rathole> WallsAround(LevelMap map, TilePosition tile)
    {
        var walls = new List<Rathole>();
        var cell = map[tile.X, tile.Y];
        if (cell.WestWall != 0)
        {
            walls.Add(new Rathole(tile, Direction.West, cell.Parcel));
        }

        if (cell.NorthWall != 0)
        {
            walls.Add(new Rathole(tile, Direction.North, cell.Parcel));
        }

        if (tile.X + 1 < map.Width && map[tile.X + 1, tile.Y].WestWall != 0)
        {
            walls.Add(new Rathole(tile, Direction.East, cell.Parcel));
        }

        if (tile.Y + 1 < map.Height && map[tile.X, tile.Y + 1].NorthWall != 0)
        {
            walls.Add(new Rathole(tile, Direction.South, cell.Parcel));
        }

        return walls;
    }
}
