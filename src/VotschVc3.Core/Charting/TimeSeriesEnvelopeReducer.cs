namespace VotschVc3.Core.Charting;

/// <summary>
/// Reduces a long time series without discarding its beginning. Each chronological bucket keeps
/// its minimum and maximum, so slow trends, steps and short spikes remain visible in a live chart.
/// </summary>
public static class TimeSeriesEnvelopeReducer
{
    public static IReadOnlyList<T> Reduce<T>(IReadOnlyList<T> source, Func<T, double> valueSelector, int maxPoints)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(valueSelector);
        if (maxPoints < 4) throw new ArgumentOutOfRangeException(nameof(maxPoints));
        if (source.Count <= maxPoints) return source.ToArray();

        int interiorCount = source.Count - 2;
        int bucketCount = Math.Max(1, (maxPoints - 2) / 2);
        double bucketSize = interiorCount / (double)bucketCount;
        var selected = new List<(int Index, T Value)>(maxPoints) { (0, source[0]) };

        for (int bucket = 0; bucket < bucketCount; bucket++)
        {
            int start = 1 + (int)Math.Floor(bucket * bucketSize);
            int end = bucket == bucketCount - 1
                ? source.Count - 1
                : 1 + (int)Math.Floor((bucket + 1) * bucketSize);
            if (end <= start) end = Math.Min(source.Count - 1, start + 1);

            int minIndex = start;
            int maxIndex = start;
            double min = valueSelector(source[start]);
            double max = min;
            for (int index = start + 1; index < end; index++)
            {
                double value = valueSelector(source[index]);
                if (value < min) { min = value; minIndex = index; }
                if (value > max) { max = value; maxIndex = index; }
            }

            selected.Add((minIndex, source[minIndex]));
            if (maxIndex != minIndex) selected.Add((maxIndex, source[maxIndex]));
        }

        selected.Add((source.Count - 1, source[^1]));
        return selected
            .GroupBy(item => item.Index)
            .Select(group => group.First())
            .OrderBy(item => item.Index)
            .Take(maxPoints)
            .Select(item => item.Value)
            .ToArray();
    }
}
