// Patients dropping litter: where a patient may drop it, after CorsixTH CorsixTH/Lua/entities/humanoids/patient.lua
// (Patient:setTile) and the object search it uses (World:findObjectNear, CorsixTH/Src/th_pathfind.cpp object_visitor).
// When they drop it follows the community's logic specification for OpenWards (see PatientNeeds).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Pathfinding;

namespace OpenWards.Sim.People;

/// <summary>
/// A patient drops a can, banana, paper or bottle when <see cref="PatientNeeds"/> says so: on a free floor tile of their
/// hospital outside rooms, and with a bin within 8 steps only one time in 20.
/// </summary>
internal static class Littering
{
    /// <summary>How far away a bin keeps a patient from littering (<c>findObjectNear(self, "bin", 8)</c>).</summary>
    private const int BinDistance = 8;

    private const string BinType = "bin";

    /// <summary>The patient drops litter where they stand, if they may.</summary>
    /// <returns>Whether they dropped it.</returns>
    internal static bool Drop(Simulation simulation, Humanoid humanoid)
    {
        var map = simulation.Map;
        var tile = humanoid.Tile;
        if (!map.Contains(tile.X, tile.Y) || humanoid.HospitalPlayer < 0)
        {
            return false;
        }

        var cell = map[tile.X, tile.Y];
        if (cell.RoomId != 0
            || AnyObjectAt(simulation, tile)
            || !cell.Has(TileFlags.Buildable)
            || !Disasters.Epidemics.IsInHospital(simulation, humanoid.HospitalPlayer, tile)
            || (IsBinNear(simulation, tile) && simulation.Random.Next(100) >= 5))
        {
            return false;
        }

        var trash = (LitterType)simulation.Random.NextInclusive(1, 4);
        Litter.Drop(simulation, humanoid.HospitalPlayer, trash, tile, simulation.Random.NextInclusive(0, 1) == 1);
        if (humanoid.HospitalPlayer < simulation.Hospitals.Count)
        {
            // hospital_littered, for the need_handyman_litter advice.
            simulation.Hospitals[humanoid.HospitalPlayer].Littered = true;
        }

        return true;
    }

    /// <summary>Whether anything stands on the tile, as CorsixTH's <c>World:getObjects</c> knows objects by their own tile.</summary>
    private static bool AnyObjectAt(Simulation simulation, TilePosition tile)
    {
        // A rat hole in a north or west wall is an object on its tile too.
        foreach (var hospital in simulation.Hospitals)
        {
            if (hospital.Ratholes.Any(hole => hole.IsVisible && hole.Tile == tile))
            {
                return true;
            }
        }

        foreach (var entity in simulation.Entities)
        {
            var at = entity switch
            {
                RoomObject placed => placed.Tile == tile,
                RoomDoor door => door.Tile == tile,
                EntranceDoor door => door.Tile == tile,
                Litter litter => !litter.IsRemoved && litter.Tile == tile,
                _ => false,
            };
            if (at)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>World:findObjectNear(self, "bin", 8)</c>, by CorsixTH's object visitor.</summary>
    private static bool IsBinNear(Simulation simulation, TilePosition start)
    {
        var bins = new HashSet<TilePosition>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is RoomObject { ObjectType: BinType } bin)
            {
                bins.Add(bin.Tile);
            }
        }

        return bins.Count > 0 && ObjectVisitor.Visit(simulation.Map, start, BinDistance, bins.Contains);
    }
}
