using OpenWards.Sim.Entities;
using OpenWards.Sim.People;

namespace OpenWards.Sim.Economy;

/// <summary>
/// What a hospital is worth, as the logic specification describes the original game: its land, the room equipment it
/// paid for, the base price of each of its rooms, 1,000 for every 50 points of reputation, and 10,000; never below 0.
/// What machines are worn does not matter; a wrecked room and what stands in it count for nothing. The loan limit follows from it (<see cref="Hospital.MaxLoan"/>).
/// </summary>
/// <remarks>
/// Departs from CorsixTH, which adds up what was paid for land, rooms and objects from 20,000 at the start, takes off
/// what objects are sold for and leaves reputation out. Departs from the original too: it worked the value out only at
/// the end of the year and when the status window opened, and took a sold machine off at its current price, which
/// research may have lowered since it was bought. Here the value is kept up to date (after each command and at the end
/// of each day), and an object comes off at the price it was bought for.
/// </remarks>
public static class HospitalValue
{
    /// <summary>The reputation that is worth <see cref="ValuePerReputationStep"/>.</summary>
    public const int ReputationStep = 50;

    /// <summary>What <see cref="ReputationStep"/> points of reputation are worth: reputation 500 is worth 10,000.</summary>
    public const int ValuePerReputationStep = 1000;

    /// <summary>What every hospital is worth on top.</summary>
    public const int BaseValue = 10_000;

    /// <summary>
    /// Objects left out of the value: doors, filing cabinets and chairs, benches, drinks machines, sinks, plants, bins,
    /// toilets, fire extinguishers, radiators and reception desks (the specification's categories 0 and 4 to 7).
    /// Everything else, the equipment of rooms and of the staff room, counts.
    /// </summary>
    private static readonly HashSet<string> NotCounted = new(StringComparer.Ordinal)
    {
        "cabinet", "chair", "bench", "drinks_machine", "sink", "plant", "bin", "loo", "extinguisher", "radiator", "reception_desk",
    };

    /// <summary>Whether an object of this type counts towards a hospital's value.</summary>
    public static bool Counts(string objectType) => !NotCounted.Contains(objectType);

    /// <summary>What the hospital is worth now.</summary>
    public static long Of(Simulation simulation, Hospital hospital)
    {
        var map = simulation.Map;
        var owner = hospital.Player + 1;
        long land = 0;
        for (var parcel = 1; parcel < map.ParcelCount; parcel++)
        {
            if (map.GetParcelOwner(parcel) == owner)
            {
                land += map.ParcelTileCount(parcel);
            }
        }

        long value = (land * simulation.Rules.LandCostPerTile) + ((long)hospital.Reputation / ReputationStep * ValuePerReputationStep) + BaseValue;
        foreach (var entity in simulation.Entities)
        {
            switch (entity)
            {
                case RoomObject { IsDestroyed: false } placed when Counts(placed.ObjectType)
                    && map.Contains(placed.Tile.X, placed.Tile.Y)
                    && map.GetParcelOwner(map[placed.Tile.X, placed.Tile.Y].Parcel) == owner
                    && simulation.FindEntity(placed.RoomId) is not Room { IsCrashed: true }:
                    value += placed.PurchasePrice ?? hospital.Research.ObjectCost(simulation.Rules, placed.ObjectType);
                    break;
                case Room { IsPaid: true, IsCrashed: false } room when RoomVisits.Owner(simulation, room) == hospital.Player:
                    value += simulation.Rules.RoomCost(room.RoomType);
                    break;
            }
        }

        return Math.Max(0, value);
    }

    /// <summary>Brings every hospital's <see cref="Hospital.Value"/> up to date.</summary>
    internal static void Refresh(Simulation simulation)
    {
        foreach (var hospital in simulation.Hospitals)
        {
            hospital.Value = Of(simulation, hospital);
        }

        foreach (var competitor in simulation.Competitors)
        {
            competitor.RefreshValue(simulation);
        }
    }
}
