using OpenWards.Sim.Definitions;
using OpenWards.Sim.Economy;

namespace OpenWards.Sim.Tests;

/// <summary>The three insurance companies of the eight a hospital deals with.</summary>
public class InsuranceCompanyTests
{
    [Fact]
    public void AHospitalPicksThreeDifferentCompaniesAndKeepsThemInASave()
    {
        var seen = new HashSet<int>();
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var insurers = NewSimulation(seed).Hospitals[0].Insurers;
            Assert.Equal(Hospital.InsuranceCompanies, insurers.Distinct().Count());
            Assert.All(insurers, insurer => Assert.InRange(insurer, 1, Hospital.InsuranceCompanyNames));
            seen.UnionWith(insurers);
        }

        // Each of the eight is as likely.
        Assert.Equal(Hospital.InsuranceCompanyNames, seen.Count);

        var simulation = NewSimulation(7);
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, GameContent.Empty);
        Assert.Equal(simulation.Hospitals[0].Insurers, loaded.Hospitals[0].Insurers);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
    }

    private static Simulation NewSimulation(ulong seed)
    {
        var map = TestMaps.FromRows("....", "....", "....", "....");
        return new Simulation(map, seed);
    }
}
