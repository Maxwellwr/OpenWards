// The level files' gbv.PromoDoc, PromoCon, ResignMax, Happy, Unhappy, SalaryTooLow and SalaryTooHigh, as the community's
// logic specification for OpenWards describes their use. The defaults are the medium difficulty file's (FULL00.SAM).

using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Staff;

/// <summary>How staff are promoted, how their mood follows their pay and how soon discontent makes them ask for money.</summary>
/// <param name="PromoDoc">Months after being hired before a junior may become a doctor (<c>gbv.PromoDoc</c>).</param>
/// <param name="PromoCon">Months after being hired before a doctor may become a consultant (<c>gbv.PromoCon</c>).</param>
/// <param name="ResignMax">Above this discontent a member of staff asks for money (<c>gbv.ResignMax</c>).</param>
/// <param name="Happy">Above this mood (0 to 100) discontent falls (<c>gbv.Happy</c>).</param>
/// <param name="Unhappy">Below this mood discontent grows (<c>gbv.Unhappy</c>).</param>
/// <param name="SalaryTooLow">What a month paid below the expected wage does to the mood (<c>gbv.SalaryTooLow</c>).</param>
/// <param name="SalaryTooHigh">What a month paid at least the expected wage does to the mood (<c>gbv.SalaryTooHigh</c>).</param>
public sealed record EmploymentRules(int PromoDoc, int PromoCon, int ResignMax, int Happy, int Unhappy, int SalaryTooLow, int SalaryTooHigh)
{
    /// <summary>The medium difficulty's settings.</summary>
    public static EmploymentRules Default { get; } = new(6, 12, 150, 75, 50, -10, 20);

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(PromoDoc);
        writer.WriteInt32(PromoCon);
        writer.WriteInt32(ResignMax);
        writer.WriteInt32(Happy);
        writer.WriteInt32(Unhappy);
        writer.WriteInt32(SalaryTooLow);
        writer.WriteInt32(SalaryTooHigh);
    }

    internal static EmploymentRules Read(SimReader reader) =>
        new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
}
