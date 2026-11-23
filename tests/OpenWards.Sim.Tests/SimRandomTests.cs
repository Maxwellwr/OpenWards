namespace OpenWards.Sim.Tests;

public class SimRandomTests
{
    [Fact]
    public void MatchesPcg32ReferenceOutput()
    {
        // First outputs of pcg32-demo from the PCG reference implementation (seed 42, sequence 54).
        var random = new SimRandom(42, 54);

        uint[] expected = [0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e];
        var actual = expected.Select(_ => random.NextUInt32()).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RestoredStateContinuesTheSameSequence()
    {
        var original = new SimRandom(1234, 7);
        original.NextUInt32();
        original.NextUInt32();

        var restored = SimRandom.FromState(original.State, original.Increment);

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(original.NextUInt32(), restored.NextUInt32());
        }
    }

    [Fact]
    public void FromStateRejectsEvenIncrement()
    {
        Assert.Throws<ArgumentException>(() => SimRandom.FromState(1, 2));
    }

    [Fact]
    public void NextStaysInRangeAndCoversIt()
    {
        var random = new SimRandom(99);
        var counts = new int[6];

        for (var i = 0; i < 6000; i++)
        {
            counts[random.Next(6)]++;
        }

        Assert.All(counts, count => Assert.InRange(count, 850, 1150));
    }

    [Fact]
    public void NextInclusiveHandlesBoundsAndFullRange()
    {
        var random = new SimRandom(5);

        for (var i = 0; i < 1000; i++)
        {
            Assert.InRange(random.NextInclusive(-3, 3), -3, 3);
        }

        Assert.Equal(17, random.NextInclusive(17, 17));
        random.NextInclusive(int.MinValue, int.MaxValue);
    }

    [Fact]
    public void ChanceRespectsExtremes()
    {
        var random = new SimRandom(8);

        for (var i = 0; i < 100; i++)
        {
            Assert.False(random.Chance(0, 4));
            Assert.True(random.Chance(4, 4));
        }
    }

    [Fact]
    public void RejectsInvalidArguments()
    {
        var random = new SimRandom(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInclusive(2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Chance(1, 0));
    }

    [Fact]
    public void TriangularAddsTwoUniformDrawsAndStopsShortOfAnOddRangesTop()
    {
        var random = new SimRandom(99, 3);
        var counts = new int[6];
        const int draws = 90_000;
        for (var i = 0; i < draws; i++)
        {
            var value = random.Triangular(55, 60);
            Assert.InRange(value, 55, 59);
            counts[value - 55]++;
        }

        // Weights 1 : 2 : 3 : 2 : 1 out of 9.
        int[] weights = [1, 2, 3, 2, 1];
        for (var i = 0; i < weights.Length; i++)
        {
            Assert.InRange(counts[i], (draws * weights[i] / 9) - 600, (draws * weights[i] / 9) + 600);
        }
    }

    [Fact]
    public void TriangularOfAnEvenRangeReachesBothEnds()
    {
        var random = new SimRandom(5);
        var seen = new HashSet<int>();
        for (var i = 0; i < 10_000; i++)
        {
            var value = random.Triangular(0, 4);
            Assert.InRange(value, 0, 4);
            seen.Add(value);
        }

        Assert.Equal(5, seen.Count);
        Assert.Equal(7, new SimRandom(1).Triangular(7, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Triangular(2, 1));
    }
}
