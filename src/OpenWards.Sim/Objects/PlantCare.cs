// The hospital's plant care score, as in the original Theme Hospital: the events and amounts, the ceiling and the
// shares follow the game expert's answer of 27.11.2026 (the developers' notes, checked against the original's saves).

using OpenWards.Sim.Entities;

namespace OpenWards.Sim.Objects;

/// <summary>
/// The hospital's score for looking after its plants, which the adviser judges. It is a running count, never worked
/// out again from the plants: a plant put down adds <see cref="PlantWorth"/> (the ceiling being the plants there were
/// before it, so the first adds nothing), each frame a plant droops takes <see cref="DroopLoss"/> off (with no floor),
/// watering adds more the drier the plant was, and a plant taken away takes what it was still worth. Only putting a
/// plant down and watering keep the score within 0 and <see cref="PlantWorth"/> for each plant.
/// </summary>
public static class PlantCare
{
    /// <summary>What a plant is worth at most.</summary>
    public const int PlantWorth = 55;

    /// <summary>What each frame of drooping (at 320, 240, 160 and 80 water) takes off.</summary>
    public const int DroopLoss = 11;

    /// <summary>The fewest plants the adviser judges the score with.</summary>
    public const int FewestJudged = 3;

    /// <summary>What a plant with <paramref name="water"/> is still worth: 0 just watered, up to 55 when long dry, reversed.</summary>
    /// <remarks>The original's <c>⌊(w + 40) / 8⌋</c>: 55 at 400 water, 0 at −40.</remarks>
    public static int Worth(int water) => (water - PlantState.LowestWater) / 8;

    /// <summary>What watering a plant with <paramref name="water"/> adds: from 0 (just watered) to 55 (at −40).</summary>
    public static int WateringGain(int water) => PlantWorth - Worth(water);

    /// <summary>The plants of <paramref name="player"/>'s hospital, in the corridors and in the rooms.</summary>
    public static int Plants(Simulation simulation, int player)
    {
        var count = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is RoomObject { Plant: not null, IsDestroyed: false } placed && Owner(simulation, placed) == player)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The score as a share of the most it could be (<see cref="PlantWorth"/> for each plant), or null with no plants. It
    /// may be below 0 or above 1: the score has no floor while plants droop, and a dry plant taken away lowers the
    /// ceiling more than the score.
    /// </summary>
    public static decimal? Share(int score, int plants) => plants > 0 ? score / (decimal)(PlantWorth * plants) : null;

    /// <summary>The score for an old game or a save without one: each plant but the first full, less every frame drooped.</summary>
    public static int Estimate(Simulation simulation, int player)
    {
        int plants = 0, frames = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is RoomObject { Plant: { } plant, IsDestroyed: false } placed && Owner(simulation, placed) == player)
            {
                plants++;
                frames += plant.State;
            }
        }

        return plants == 0 ? 0 : Math.Clamp((PlantWorth * (plants - 1)) - (DroopLoss * frames), 0, PlantWorth * plants);
    }

    /// <summary>A plant has been put down (it is among the entities already): it adds its worth up to the plants before it.</summary>
    internal static void OnPlaced(Simulation simulation, RoomObject plant)
    {
        if (HospitalOf(simulation, plant) is { } hospital)
        {
            var before = Plants(simulation, hospital.Player) - 1;
            hospital.PlantCare = Math.Clamp(hospital.PlantCare + PlantWorth, 0, PlantWorth * Math.Max(0, before));
        }
    }

    /// <summary>A plant has drooped a frame.</summary>
    internal static void OnDrooped(Simulation simulation, RoomObject plant)
    {
        if (HospitalOf(simulation, plant) is { } hospital)
        {
            hospital.PlantCare -= DroopLoss;
        }
    }

    /// <summary>A plant is about to be watered: it adds more the drier it is, up to the hospital's ceiling.</summary>
    internal static void OnWatered(Simulation simulation, RoomObject plant)
    {
        if (plant.Plant is { } state && HospitalOf(simulation, plant) is { } hospital)
        {
            var plants = Plants(simulation, hospital.Player);
            hospital.PlantCare = Math.Clamp(hospital.PlantCare + WateringGain(state.Water), 0, PlantWorth * plants);
        }
    }

    /// <summary>A plant is being taken away (sold or picked up to move it): what it was still worth comes off.</summary>
    internal static void OnRemoved(Simulation simulation, RoomObject plant)
    {
        if (plant.Plant is { } state && HospitalOf(simulation, plant) is { } hospital)
        {
            hospital.PlantCare -= Worth(state.Water);
        }
    }

    private static int Owner(Simulation simulation, RoomObject placed)
    {
        var map = simulation.Map;
        return map.Contains(placed.Tile.X, placed.Tile.Y) ? map.GetParcelOwner(map[placed.Tile.X, placed.Tile.Y].Parcel) - 1 : -1;
    }

    private static Economy.Hospital? HospitalOf(Simulation simulation, RoomObject placed)
    {
        var owner = Owner(simulation, placed);
        return owner >= 0 && owner < simulation.Hospitals.Count ? simulation.Hospitals[owner] : null;
    }
}
