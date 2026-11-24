// Training settings, ported from CorsixTH CorsixTH/Lua/base_config.lua (gbv.TrainingRate, gbv.AbilityThreshold,
// gbv.TrainingValue), as the level files may override them.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Staff;

/// <summary>How fast doctors learn in the training room.</summary>
/// <param name="Rate">What a student's skill gains from teaching (<c>gbv.TrainingRate</c>, 1 to 255).</param>
/// <param name="SurgeonThreshold">The points past which a student gains surgery (<c>gbv.AbilityThreshold[0]</c>).</param>
/// <param name="PsychiatristThreshold">The same for psychiatry (<c>gbv.AbilityThreshold[1]</c>).</param>
/// <param name="ResearcherThreshold">The same for research (<c>gbv.AbilityThreshold[2]</c>).</param>
/// <param name="ProjectorValue">The points towards an ability a lesson at the projector is worth (<c>gbv.TrainingValue[0]</c>).</param>
/// <param name="SkeletonValue">The same for a lesson with the skeleton (<c>gbv.TrainingValue[1]</c>).</param>
/// <param name="BookcaseValue">The same for a lesson with the books (<c>gbv.TrainingValue[2]</c>).</param>
public sealed record TrainingRules(
    int Rate,
    int SurgeonThreshold,
    int PsychiatristThreshold,
    int ResearcherThreshold,
    int ProjectorValue,
    int SkeletonValue,
    int BookcaseValue)
{
    /// <summary>
    /// For a level that sets nothing: base_config.lua's rate and values, and the logic specification's threshold of 150 for
    /// every ability (CorsixTH's are 75, 60 and 45).
    /// </summary>
    public static TrainingRules Default { get; } = new(40, 150, 150, 150, 10, 15, 20);

    /// <summary>The points past which a student gains <paramref name="specialism"/>.</summary>
    public int Threshold(Specialism specialism) => specialism switch
    {
        Specialism.Surgeon => SurgeonThreshold,
        Specialism.Psychiatrist => PsychiatristThreshold,
        _ => ResearcherThreshold,
    };

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Rate);
        writer.WriteInt32(SurgeonThreshold);
        writer.WriteInt32(PsychiatristThreshold);
        writer.WriteInt32(ResearcherThreshold);
        writer.WriteInt32(ProjectorValue);
        writer.WriteInt32(SkeletonValue);
        writer.WriteInt32(BookcaseValue);
    }

    internal static TrainingRules Read(SimReader reader) =>
        new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
}
