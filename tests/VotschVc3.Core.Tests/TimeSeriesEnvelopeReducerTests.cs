using VotschVc3.Core.Charting;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class TimeSeriesEnvelopeReducerTests
{
    [Fact]
    public void Reduce_PreservesWholeTimeRangeAndThermalSteps()
    {
        var source = Enumerable.Range(0, 12000)
            .Select(index => new Sample(index, index < 4000 ? -40 : index < 8000 ? 0 : 40))
            .ToArray();

        IReadOnlyList<Sample> reduced = TimeSeriesEnvelopeReducer.Reduce(source, sample => sample.Value, 3000);

        Assert.True(reduced.Count <= 3000);
        Assert.Equal(0, reduced[0].Index);
        Assert.Equal(11999, reduced[^1].Index);
        Assert.Contains(reduced, sample => sample.Value == -40);
        Assert.Contains(reduced, sample => sample.Value == 0);
        Assert.Contains(reduced, sample => sample.Value == 40);
    }

    [Fact]
    public void Reduce_PreservesShortMinimumAndMaximumSpikesInChronologicalOrder()
    {
        Sample[] source = Enumerable.Range(0, 10000).Select(index => new Sample(index, 10)).ToArray();
        source[2345] = new Sample(2345, -25);
        source[6789] = new Sample(6789, 55);

        IReadOnlyList<Sample> reduced = TimeSeriesEnvelopeReducer.Reduce(source, sample => sample.Value, 500);

        Assert.Contains(reduced, sample => sample.Index == 2345 && sample.Value == -25);
        Assert.Contains(reduced, sample => sample.Index == 6789 && sample.Value == 55);
        Assert.True(reduced.Zip(reduced.Skip(1), (left, right) => left.Index < right.Index).All(value => value));
    }

    private sealed record Sample(int Index, double Value);
}
