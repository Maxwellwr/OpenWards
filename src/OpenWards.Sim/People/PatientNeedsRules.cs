// The settings are the original level files' gbv.LeaveMax, Unhappy, BowelFull, BowelOverflows, VomitLimit, DrinkHappy,
// ToiletHappy, LitterDrop and LitterRandom, as the community's logic specification for OpenWards describes them.
// LeaveMax and Unhappy default to the specification's values for each difficulty; the other defaults are OpenWards' own,
// for levels whose files give none.

using OpenWards.Sim.Economy;
using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.People;

/// <summary>
/// What a level's patients put up with (the level files' <c>gbv</c> settings read by <see cref="PatientNeeds"/>): how
/// full their count of grievances may get before they leave, and the limits of their mood, bowels, nausea and litter.
/// </summary>
/// <param name="LeaveMax">A patient waiting leaves once their leave count is above this (<c>gbv.LeaveMax</c>).</param>
/// <param name="Unhappy">Below this mood (0 to 100) a patient's leave count grows faster (<c>gbv.Unhappy</c>).</param>
/// <param name="BowelFull">Above this the patient wants the toilet (<c>gbv.BowelFull</c>).</param>
/// <param name="BowelOverflows">From this on the patient may do it on the floor (<c>gbv.BowelOverflows</c>).</param>
/// <param name="VomitLimit">From this nausea on the patient may be sick (<c>gbv.VomitLimit</c>).</param>
/// <param name="DrinkHappy">What a can from the drinks machine adds to the mood (<c>gbv.DrinkHappy</c>).</param>
/// <param name="ToiletHappy">What the toilet adds to the mood (<c>gbv.ToiletHappy</c>).</param>
/// <param name="LitterDrop">Above this litter count the patient may drop litter (<c>gbv.LitterDrop</c>).</param>
/// <param name="LitterRandom">One check in this many the patient may drop litter anyway (<c>gbv.LitterRandom</c>); 0 never.</param>
public sealed record PatientNeedsRules(
    int LeaveMax,
    int Unhappy,
    int BowelFull,
    int BowelOverflows,
    int VomitLimit,
    int DrinkHappy,
    int ToiletHappy,
    int LitterDrop,
    int LitterRandom)
{
    /// <summary>OpenWards' default for <c>gbv.BowelFull</c>.</summary>
    public const int DefaultBowelFull = 50;

    /// <summary>OpenWards' default for <c>gbv.BowelOverflows</c>.</summary>
    public const int DefaultBowelOverflows = 80;

    /// <summary>OpenWards' default for <c>gbv.VomitLimit</c>.</summary>
    public const int DefaultVomitLimit = 50;

    /// <summary>OpenWards' default for <c>gbv.DrinkHappy</c>.</summary>
    public const int DefaultDrinkHappy = 20;

    /// <summary>OpenWards' default for <c>gbv.ToiletHappy</c>.</summary>
    public const int DefaultToiletHappy = 20;

    /// <summary>OpenWards' default for <c>gbv.LitterDrop</c>.</summary>
    public const int DefaultLitterDrop = 5;

    /// <summary>OpenWards' default for <c>gbv.LitterRandom</c>.</summary>
    public const int DefaultLitterRandom = 100;

    /// <summary>The settings of a medium level.</summary>
    public static readonly PatientNeedsRules Default = ForDifficulty(Difficulty.Medium);

    /// <summary>
    /// The specification's <c>LeaveMax</c> and <c>Unhappy</c> for the difficulty (easy 250 and 30, medium 150 and 50, hard
    /// 100 and 50) with OpenWards' defaults for the rest.
    /// </summary>
    public static PatientNeedsRules ForDifficulty(Difficulty difficulty) => new(
        difficulty switch { Difficulty.Easy => 250, Difficulty.Hard => 100, _ => 150 },
        difficulty == Difficulty.Easy ? 30 : 50,
        DefaultBowelFull,
        DefaultBowelOverflows,
        DefaultVomitLimit,
        DefaultDrinkHappy,
        DefaultToiletHappy,
        DefaultLitterDrop,
        DefaultLitterRandom);

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(LeaveMax);
        writer.WriteInt32(Unhappy);
        writer.WriteInt32(BowelFull);
        writer.WriteInt32(BowelOverflows);
        writer.WriteInt32(VomitLimit);
        writer.WriteInt32(DrinkHappy);
        writer.WriteInt32(ToiletHappy);
        writer.WriteInt32(LitterDrop);
        writer.WriteInt32(LitterRandom);
    }

    internal static PatientNeedsRules Read(SimReader reader) => new(
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadInt32());
}
