namespace VotschVc3.Core.Calibration;

public sealed record StabilityMetrics(
    int Count,
    double Mean,
    double Median,
    double Minimum,
    double Maximum,
    double Range,
    double StandardDeviation,
    double SlopePerMinute,
    TimeSpan WindowDuration,
    bool IsStable);

public sealed class RollingStabilityDetector
{
    private readonly int _requiredSamples;
    private readonly double _maxRangePm;
    private readonly double _maxStdDevPm;
    private readonly double _maxDriftPmPerMinute;
    private readonly Queue<(DateTimeOffset Timestamp, double Value)> _samples = new();

    public RollingStabilityDetector(
        int requiredSamples,
        double maxRangePm,
        double maxStdDevPm,
        double maxDriftPmPerMinute)
    {
        if (requiredSamples < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredSamples));
        }

        _requiredSamples = requiredSamples;
        _maxRangePm = Math.Max(0, maxRangePm);
        _maxStdDevPm = Math.Max(0, maxStdDevPm);
        _maxDriftPmPerMinute = Math.Max(0, maxDriftPmPerMinute);
    }

    public int Count => _samples.Count;
    public int RequiredSamples => _requiredSamples;

    public IReadOnlyList<(DateTimeOffset Timestamp, double Value)> Samples => _samples.ToArray();

    public StabilityMetrics Add(DateTimeOffset timestamp, double wavelengthNm)
    {
        _samples.Enqueue((timestamp, wavelengthNm));
        while (_samples.Count > _requiredSamples)
        {
            _samples.Dequeue();
        }

        return Evaluate();
    }

    public StabilityMetrics Evaluate()
    {
        var data = _samples.ToArray();
        if (data.Length == 0)
        {
            return new StabilityMetrics(0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, false);
        }

        double[] values = data.Select(x => x.Value).ToArray();
        double mean = values.Average();
        double[] ordered = values.OrderBy(x => x).ToArray();
        double median = ordered.Length % 2 == 0
            ? (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2d
            : ordered[ordered.Length / 2];
        double min = ordered[0];
        double max = ordered[^1];
        double range = max - min;
        double variance = values.Sum(v => Math.Pow(v - mean, 2)) / values.Length;
        double stdDev = Math.Sqrt(variance);
        double slopeNmPerMinute = CalculateSlopePerMinute(data);
        TimeSpan duration = data.Length > 1 ? data[^1].Timestamp - data[0].Timestamp : TimeSpan.Zero;

        double rangePm = range * 1000d;
        double stdDevPm = stdDev * 1000d;
        double driftPmPerMinute = Math.Abs(slopeNmPerMinute * 1000d);

        bool enough = data.Length >= _requiredSamples;
        bool rangeOk = _maxRangePm <= 0 || rangePm <= _maxRangePm;
        bool stdOk = _maxStdDevPm <= 0 || stdDevPm <= _maxStdDevPm;
        bool driftOk = _maxDriftPmPerMinute <= 0 || driftPmPerMinute <= _maxDriftPmPerMinute;

        return new StabilityMetrics(
            data.Length,
            mean,
            median,
            min,
            max,
            rangePm,
            stdDevPm,
            slopeNmPerMinute * 1000d,
            duration,
            enough && rangeOk && stdOk && driftOk);
    }

    private static double CalculateSlopePerMinute((DateTimeOffset Timestamp, double Value)[] data)
    {
        if (data.Length < 2)
        {
            return 0;
        }

        DateTimeOffset origin = data[0].Timestamp;
        double[] x = data.Select(p => (p.Timestamp - origin).TotalMinutes).ToArray();
        double[] y = data.Select(p => p.Value).ToArray();
        double xMean = x.Average();
        double yMean = y.Average();
        double numerator = 0;
        double denominator = 0;

        for (int i = 0; i < data.Length; i++)
        {
            double dx = x[i] - xMean;
            numerator += dx * (y[i] - yMean);
            denominator += dx * dx;
        }

        return denominator <= double.Epsilon ? 0 : numerator / denominator;
    }
}

public sealed class TemperatureStabilityDetector
{
    private readonly TimeSpan _requiredDuration;
    private readonly double _toleranceC;
    private readonly double _maxDriftCPerMinute;
    private readonly Queue<(DateTimeOffset Timestamp, double Value)> _samples = new();

    public TemperatureStabilityDetector(TimeSpan requiredDuration, double toleranceC, double maxDriftCPerMinute)
    {
        _requiredDuration = requiredDuration < TimeSpan.Zero ? TimeSpan.Zero : requiredDuration;
        _toleranceC = Math.Abs(toleranceC);
        _maxDriftCPerMinute = Math.Max(0, maxDriftCPerMinute);
    }

    public StabilityMetrics Add(DateTimeOffset timestamp, double value, double target)
    {
        bool currentInsideTolerance = Math.Abs(value - target) <= _toleranceC;

        // Temperature stability is a CONTINUOUS in-tolerance interval. As soon as the reference
        // leaves the configured target band, the previous stability history must no longer block a
        // later recovery. The old implementation retained requiredDuration + 1 minute of history and
        // required every retained sample to be inside tolerance. That could report "not stable" even
        // though the current WIKA value, stable duration and drift shown in the UI were already valid.
        if (!currentInsideTolerance)
        {
            _samples.Clear();
            _samples.Enqueue((timestamp, value));
            return Evaluate(target);
        }

        // The one out-of-tolerance diagnostic sample retained above is discarded on the first valid
        // sample. From this point the window contains only the current continuous accepted interval.
        if (_samples.Count > 0 && _samples.Any(x => Math.Abs(x.Value - target) > _toleranceC))
        {
            _samples.Clear();
        }

        _samples.Enqueue((timestamp, value));
        TrimToRequiredWindow(timestamp);
        return Evaluate(target);
    }

    private void TrimToRequiredWindow(DateTimeOffset timestamp)
    {
        if (_samples.Count <= 1)
        {
            return;
        }

        if (_requiredDuration <= TimeSpan.Zero)
        {
            while (_samples.Count > 1)
            {
                _samples.Dequeue();
            }
            return;
        }

        DateTimeOffset cutoff = timestamp - _requiredDuration;

        // Keep exactly one anchor sample at/before the cutoff plus all newer samples. Keeping that
        // anchor avoids an off-by-one polling problem (for example 1 Hz samples would otherwise keep
        // producing a 59.x s window for a required 60 s duration), without retaining an extra minute
        // of stale history as the previous implementation did.
        while (_samples.Count > 1 && _samples.ElementAt(1).Timestamp <= cutoff)
        {
            _samples.Dequeue();
        }
    }

    private StabilityMetrics Evaluate(double target)
    {
        var data = _samples.ToArray();
        if (data.Length == 0)
        {
            return new StabilityMetrics(0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, false);
        }

        double[] values = data.Select(x => x.Value).ToArray();
        double mean = values.Average();
        double[] ordered = values.OrderBy(x => x).ToArray();
        double median = ordered.Length % 2 == 0
            ? (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2d
            : ordered[ordered.Length / 2];
        double min = ordered[0];
        double max = ordered[^1];
        double variance = values.Sum(v => Math.Pow(v - mean, 2)) / values.Length;
        double stdDev = Math.Sqrt(variance);
        TimeSpan duration = data.Length > 1 ? data[^1].Timestamp - data[0].Timestamp : TimeSpan.Zero;
        double slope = CalculateSlopePerMinute(data);

        bool durationOk = duration >= _requiredDuration;
        bool toleranceOk = data.All(x => Math.Abs(x.Value - target) <= _toleranceC);
        bool driftOk = _maxDriftCPerMinute <= 0 || Math.Abs(slope) <= _maxDriftCPerMinute;

        return new StabilityMetrics(
            data.Length,
            mean,
            median,
            min,
            max,
            max - min,
            stdDev,
            slope,
            duration,
            durationOk && toleranceOk && driftOk);
    }

    private static double CalculateSlopePerMinute((DateTimeOffset Timestamp, double Value)[] data)
    {
        if (data.Length < 2)
        {
            return 0;
        }

        DateTimeOffset origin = data[0].Timestamp;
        double[] x = data.Select(p => (p.Timestamp - origin).TotalMinutes).ToArray();
        double xMean = x.Average();
        double yMean = data.Average(p => p.Value);
        double numerator = 0;
        double denominator = 0;

        for (int i = 0; i < data.Length; i++)
        {
            double dx = x[i] - xMean;
            numerator += dx * (data[i].Value - yMean);
            denominator += dx * dx;
        }

        return denominator <= double.Epsilon ? 0 : numerator / denominator;
    }
}
