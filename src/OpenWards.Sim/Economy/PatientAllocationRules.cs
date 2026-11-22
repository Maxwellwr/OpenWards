// The settings and their defaults are the original level files' gbv.AllocDelay, AllocTotalRep, AllocIndRep and AllocRand,
// with the defaults of CorsixTH CorsixTH/Lua/base_config.lua.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Economy;

/// <summary>
/// How a level shares the town's patients between the hospitals (the level files' <c>gbv.Alloc…</c> settings): for the
/// first months in turn, then by reputation and price, now and then at random.
/// </summary>
/// <param name="Delay">Months during which the hospitals take turns (<c>gbv.AllocDelay</c>).</param>
/// <param name="TotalReputation">How much a hospital's reputation weighs (<c>gbv.AllocTotalRep</c>).</param>
/// <param name="DiseaseReputation">How much its reputation for the patient's disease weighs (<c>gbv.AllocIndRep</c>).</param>
/// <param name="RandomChance">
/// One patient in this many goes to a hospital chosen at random (<c>gbv.AllocRand</c>); 0 for none.
/// </param>
public sealed record PatientAllocationRules(int Delay, int TotalReputation, int DiseaseReputation, int RandomChance)
{
    /// <summary>base_config.lua's <c>gbv.AllocDelay</c>.</summary>
    public const int DefaultDelay = 3;

    /// <summary>base_config.lua's <c>gbv.AllocTotalRep</c>.</summary>
    public const int DefaultTotalReputation = 1;

    /// <summary>base_config.lua's <c>gbv.AllocIndRep</c>.</summary>
    public const int DefaultDiseaseReputation = 2;

    /// <summary>base_config.lua's <c>gbv.AllocRand</c>.</summary>
    public const int DefaultRandomChance = 4;

    /// <summary>base_config.lua's settings.</summary>
    public static readonly PatientAllocationRules Default = new(DefaultDelay, DefaultTotalReputation, DefaultDiseaseReputation, DefaultRandomChance);

    /// <summary>The hospitals always take turns, as CorsixTH shares patients.</summary>
    public static readonly PatientAllocationRules InTurn = new(int.MaxValue, DefaultTotalReputation, DefaultDiseaseReputation, DefaultRandomChance);

    /// <summary>Whether patients go to the hospitals in turn in this month of the game (from 1).</summary>
    public bool InTurnIn(int monthOfGame) => monthOfGame <= Delay;

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Delay);
        writer.WriteInt32(TotalReputation);
        writer.WriteInt32(DiseaseReputation);
        writer.WriteInt32(RandomChance);
    }

    internal static PatientAllocationRules Read(SimReader reader) => new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
}
