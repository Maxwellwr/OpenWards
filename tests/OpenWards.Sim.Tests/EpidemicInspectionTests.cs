using OpenWards.Sim.Disasters;

namespace OpenWards.Sim.Tests;

/// <summary>The health inspector's verdict on a covered-up epidemic, by the infected patients he finds (the logic specification's table).</summary>
public class EpidemicInspectionTests
{
    [Fact]
    public void NobodyFoundAlwaysWorks()
    {
        var random = new SimRandom(1);
        Assert.All(Enumerable.Range(0, 200), _ => Assert.Equal(EpidemicOutcome.CoveredUp, Epidemics.Verdict(random, 0)));
    }

    [Theory]
    [InlineData(1, 200, 800, 0, 0)]
    [InlineData(2, 200, 800, 0, 0)]
    [InlineData(3, 50, 95, 855, 0)]
    [InlineData(5, 50, 95, 855, 0)]
    [InlineData(6, 20, 39, 941, 0)]
    [InlineData(9, 20, 39, 941, 0)]
    [InlineData(10, 20, 0, 0, 980)]
    [InlineData(40, 20, 0, 0, 980)]
    public void TheVerdictFollowsTheTable(int found, int success, int fine, int fineAndReputation, int evacuation)
    {
        const int Draws = 20_000;
        var random = new SimRandom((ulong)found);
        var counts = new Dictionary<EpidemicOutcome, int>();
        for (var i = 0; i < Draws; i++)
        {
            var outcome = Epidemics.Verdict(random, found);
            counts[outcome] = counts.GetValueOrDefault(outcome) + 1;
        }

        Near(counts, EpidemicOutcome.CoveredUp, success);
        Near(counts, EpidemicOutcome.Fined, fine);
        Near(counts, EpidemicOutcome.FinedWithReputation, fineAndReputation);
        Near(counts, EpidemicOutcome.Evacuated, evacuation);

        static void Near(Dictionary<EpidemicOutcome, int> counts, EpidemicOutcome outcome, int perThousand)
        {
            var count = counts.GetValueOrDefault(outcome);
            if (perThousand == 0)
            {
                Assert.Equal(0, count);
                return;
            }

            var expected = Draws * perThousand / 1000;
            var tolerance = Math.Max(Draws / 200, expected / 10);
            Assert.InRange(count, expected - tolerance, expected + tolerance);
        }
    }
}
