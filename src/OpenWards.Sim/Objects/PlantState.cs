// Plants drooping without water, as in the original Theme Hospital: the water, its loss, the frames and the call for
// watering follow the game expert's answer of 26.11.2026 (the developers' notes, checked against the original's saves).
// The pleasing factor for staff comes from CorsixTH CorsixTH/Lua/objects/plant.lua (isPleasingFactor).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Objects;

/// <summary>
/// How thirsty a plant is. A watered plant has <see cref="FullWater"/> water. Every <see cref="DroopInterval"/> ticks it
/// loses 1 with a chance of one in two, whatever the warmth or the game speed: about 1.56 a day, so from watered to dry
/// takes about 256 days. It droops a frame at 320, 240, 160 and 80 water. It never dies: below 0 the water runs on to
/// <see cref="LowestWater"/>, then starts again from 0 with a more urgent call for watering.
/// </summary>
public sealed class PlantState
{
    /// <summary>The water of a freshly watered plant.</summary>
    public const int FullWater = 400;

    /// <summary>How much water each frame of drooping lasts.</summary>
    public const int WaterPerState = 80;

    /// <summary>Where the water of a long dry plant turns back to 0.</summary>
    public const int LowestWater = -40;

    /// <summary>How many ticks there are between the plants' draws for losing water.</summary>
    public const int DroopInterval = 16;

    /// <summary>How urgent the call for watering is once the water has run down to <see cref="LowestWater"/>.</summary>
    public const int WrappedPriority = 10;

    /// <summary>The healthy state.</summary>
    public const int Healthy = 0;

    /// <summary>The most drooping state, which looks dead (<c>phases</c> − 1).</summary>
    public const int Wilted = Phases - 1;

    /// <summary>How many states (animation frames) there are.</summary>
    public const int Phases = 5;

    /// <summary>The water, from <see cref="LowestWater"/> + 1 to <see cref="FullWater"/>.</summary>
    public int Water { get; private set; } = FullWater;

    /// <summary>Whether the water has run down to <see cref="LowestWater"/> since the plant was last watered.</summary>
    public bool HasRunDry { get; private set; }

    /// <summary>The state, from <see cref="Healthy"/> to <see cref="Wilted"/>: one more at each 80 water lost.</summary>
    public int State => Water > FullWater - WaterPerState ? Healthy : Math.Min(Wilted, (FullWater - Water) / WaterPerState);

    /// <summary>Whether a handyman should water it: from the first drooping frame on.</summary>
    public bool NeedsWatering => State != Healthy;

    /// <summary>How urgent watering it is: 1 to 4 by the frame, more once it has run dry; 0 when it needs no water.</summary>
    public int WateringPriority => HasRunDry ? WrappedPriority : State;

    /// <summary>Whether patients see it withered: only once its water is below 0, whatever the frame.</summary>
    public bool IsWithered => Water < 0;

    /// <summary>How pleasing the plant is to staff, 1 (most drooping) to 5 (healthy) (CorsixTH's <c>isPleasingFactor</c>).</summary>
    public int PleasingFactor => Phases - State;

    /// <summary>One of the plants' draws every <see cref="DroopInterval"/> ticks: one in two loses a drop of water.</summary>
    /// <returns>Whether it drooped a frame (its water came to 320, 240, 160 or 80).</returns>
    internal bool Droop(SimRandom random)
    {
        if (random.Next(2) != 0)
        {
            return false;
        }

        Water--;
        if (Water <= LowestWater)
        {
            Water = 0;
            HasRunDry = true;
            return false;
        }

        return Water is > 0 and < FullWater && Water % WaterPerState == 0;
    }

    /// <summary>
    /// An original save's plant water (400 when watered, below 0 when long dry), as it is: the original's water is this
    /// one. Whether it had already run dry once the save does not tell, so it has not.
    /// </summary>
    internal void ImportOriginalWater(int water)
    {
        Water = water <= LowestWater ? 0 : Math.Min(water, FullWater);
        HasRunDry = false;
    }

    /// <summary>Watered, the plant is healthy again with full water (the original's handyman sets 400 and the first frame).</summary>
    internal void Restore()
    {
        Water = FullWater;
        HasRunDry = false;
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Water);
        writer.WriteBool(HasRunDry);
    }

    internal static PlantState Read(SimReader reader)
    {
        if (reader.Version is not 0 and < 119)
        {
            // Before format 119 a plant had a state 0 to 4 and the days left until the next one, out of 64.
            var state = reader.ReadInt32();
            if (state is < Healthy or > Wilted)
            {
                throw new InvalidDataException($"Plant state {state} is out of range.");
            }

            var daysLeft = reader.ReadDecimal();
            var lost = (int)Math.Round(WaterPerState * (1 - (Math.Clamp(daysLeft, 0m, 64m) / 64m)));
            return new PlantState { Water = FullWater - (state * WaterPerState) - Math.Min(lost, WaterPerState - 1) };
        }

        var water = reader.ReadInt32();
        if (water is <= LowestWater or > FullWater)
        {
            throw new InvalidDataException($"Plant water {water} is out of range.");
        }

        return new PlantState { Water = water, HasRunDry = reader.ReadBool() };
    }
}
