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

    public void Reset() => _samples.Clear();

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

/// <summary>
/// Strict continuous reference-temperature stability gate. Every sample in the uninterrupted dwell
/// must remain inside target tolerance and the complete window must satisfy range, standard-deviation
/// and linear-drift limits. Any violation resets the dwell and the newest valid sample may start a
/// fresh window.
/// </summary>
public sealed class TemperatureStabilityDetector
{
    // Arithmetic slack only: ten million times smaller than a 0.001 °C reading.
    // Prevent subtraction/regression rounding from rejecting an inclusive limit.
    public static bool IsWithinLimit(double value, double limit) =>
        double.IsFinite(value) && double.IsFinite(limit) && value <= limit + 1e-10;

    private static readonly TimeSpan ShortTermDriftWindow = TimeSpan.FromMinutes(2);
    private readonly TimeSpan _requiredDuration;
    private readonly double _toleranceC;
    private readonly double _maxDriftCPerMinute;
    private readonly double _maxRangeC;
    private readonly double _maxStdDevC;
    private readonly List<(DateTimeOffset Timestamp, double Value)> _window = new();
    private int _stableScoreSeconds;
    private int _displayedStableScoreSeconds;
    private double _lastAverageDeltaC;
    private double _lastNormalizedChangeCPerMinute;
    private bool _isStable;

    public TemperatureStabilityDetector(
        TimeSpan requiredDuration,
        double toleranceC,
        double maxDriftCPerMinute,
        double maxRangeC = 0.03,
        double maxStdDevC = 0.01)
    {
        _requiredDuration = requiredDuration < TimeSpan.Zero ? TimeSpan.Zero : requiredDuration;
        _toleranceC = Math.Abs(toleranceC);
        _maxDriftCPerMinute = Math.Max(0, maxDriftCPerMinute);
        _maxRangeC = Math.Max(0, maxRangeC);
        _maxStdDevC = Math.Max(0, maxStdDevC);
    }

    /// <summary>Validated uninterrupted stability time in seconds.</summary>
    public int StableScoreSeconds => _stableScoreSeconds;
    public string? LastResetReason { get; private set; }

    /// <summary>
    /// Stability time shown to the operator; identical to the authoritative continuous dwell.
    /// </summary>
    public int DisplayedStableScoreSeconds => _displayedStableScoreSeconds;

    /// <summary>Configured uninterrupted seconds required before the temperature gate opens.</summary>
    public int RequiredStableScoreSeconds => (int)Math.Ceiling(_requiredDuration.TotalSeconds);

    /// <summary>Mean |T - first sample| of the current uninterrupted window.</summary>
    public double LastAverageDeltaC => _lastAverageDeltaC;

    /// <summary>Linear-regression drift of the current uninterrupted window in °C/min.</summary>
    public double LastNormalizedChangeCPerMinute => _lastNormalizedChangeCPerMinute;

    public StabilityMetrics Add(DateTimeOffset timestamp, double value, double target)
    {
        bool toleranceOk = IsWithinLimit(Math.Abs(value - target), _toleranceC);
        if (!toleranceOk || !double.IsFinite(value))
        {
            ResetWindow();
            LastResetReason = $"{timestamp:HH:mm:ss} · neplatná teplota alebo odchýlka od cieľa {Math.Abs(value - target):F4} / {_toleranceC:F4} °C";
            return BuildMetrics(new[] { (timestamp, value) }, false);
        }

        _window.Add((timestamp, value));
        // Once enough history exists, retain the shortest suffix that still spans the configured
        // dwell. This keeps the gate continuously supervised without making harmless multi-hour
        // reference movement accumulate forever after the gate has opened.
        while (_requiredDuration > TimeSpan.Zero &&
               _window.Count > 2 &&
               timestamp - _window[1].Timestamp >= _requiredDuration)
        {
            _window.RemoveAt(0);
        }
        StabilityMetrics candidate = BuildMetrics(_window, false);
        bool rangeOk = _maxRangeC <= 0 || IsWithinLimit(candidate.Range, _maxRangeC);
        bool stdDevOk = _maxStdDevC <= 0 || IsWithinLimit(candidate.StandardDeviation, _maxStdDevC);
        bool driftOk = _maxDriftCPerMinute <= 0 || IsWithinLimit(Math.Abs(candidate.SlopePerMinute), _maxDriftCPerMinute);

        if (!rangeOk || !stdDevOk || !driftOk)
        {
            // A failed sample invalidates the complete dwell. Keep only the newest valid sample as
            // the possible beginning of a fresh uninterrupted stability window.
            ResetWindow();
            LastResetReason = $"{timestamp:HH:mm:ss} · " + string.Join(" · ", new[]
            {
                !rangeOk ? $"rozsah {candidate.Range:F4} > {_maxRangeC:F4} °C" : null,
                !stdDevOk ? $"σ {candidate.StandardDeviation:F4} > {_maxStdDevC:F4} °C" : null,
                !driftOk ? $"drift {Math.Abs(candidate.SlopePerMinute):F4} > {_maxDriftCPerMinute:F4} °C/min" : null
            }.Where(reason => reason is not null));
            _window.Add((timestamp, value));
            // Return the rejected metrics for this refresh so the operator can see exactly which
            // criterion caused the reset; the next sample is evaluated from the fresh window.
            return candidate with { WindowDuration = TimeSpan.Zero, IsStable = false };
        }

        TimeSpan continuousDuration = _window.Count > 1 ? timestamp - _window[0].Timestamp : TimeSpan.Zero;
        _stableScoreSeconds = Math.Min(RequiredStableScoreSeconds, Math.Max(0, (int)Math.Floor(continuousDuration.TotalSeconds)));
        _displayedStableScoreSeconds = _stableScoreSeconds;
        _lastAverageDeltaC = _window.Average(sample => Math.Abs(sample.Value - _window[0].Value));
        _lastNormalizedChangeCPerMinute = candidate.SlopePerMinute;
        _isStable = continuousDuration >= _requiredDuration;
        return BuildMetrics(_window, _isStable);
    }

    public void Reset() { ResetWindow(); LastResetReason = null; }

    private void ResetWindow()
    {
        _window.Clear();
        _stableScoreSeconds = 0;
        _displayedStableScoreSeconds = 0;
        _lastAverageDeltaC = 0;
        _lastNormalizedChangeCPerMinute = 0;
        _isStable = false;
    }

    private StabilityMetrics BuildMetrics(
        IReadOnlyList<(DateTimeOffset Timestamp, double Value)> samples,
        bool isStable)
    {
        if (samples.Count == 0)
        {
            return new StabilityMetrics(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                _lastNormalizedChangeCPerMinute,
                TimeSpan.FromSeconds(_displayedStableScoreSeconds),
                isStable);
        }

        double[] values = samples.Select(x => x.Value).ToArray();
        double mean = values.Average();
        double[] ordered = values.OrderBy(x => x).ToArray();
        double median = ordered.Length % 2 == 0
            ? (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2d
            : ordered[ordered.Length / 2];
        double min = ordered[0];
        double max = ordered[^1];
        double variance = values.Sum(v => Math.Pow(v - mean, 2)) / values.Length;
        double stdDev = Math.Sqrt(variance);
        double fullWindowSlope = CalculateSlopePerMinute(samples);
        DateTimeOffset shortTermStart = samples[^1].Timestamp - ShortTermDriftWindow;
        IReadOnlyList<(DateTimeOffset Timestamp, double Value)> shortTermSamples = samples
            .Where(sample => sample.Timestamp >= shortTermStart)
            .ToArray();
        double shortTermSlope = CalculateSlopePerMinute(shortTermSamples);
        double effectiveSlope = Math.Abs(shortTermSlope) > Math.Abs(fullWindowSlope)
            ? shortTermSlope
            : fullWindowSlope;

        return new StabilityMetrics(
            values.Length,
            mean,
            median,
            min,
            max,
            max - min,
            stdDev,
            effectiveSlope,
            TimeSpan.FromSeconds(_displayedStableScoreSeconds),
            isStable);
    }

    private static double CalculateSlopePerMinute(
        IReadOnlyList<(DateTimeOffset Timestamp, double Value)> samples)
    {
        if (samples.Count < 2)
            return 0;

        DateTimeOffset origin = samples[0].Timestamp;
        double meanMinutes = samples.Average(sample => (sample.Timestamp - origin).TotalMinutes);
        double meanValue = samples.Average(sample => sample.Value);
        double numerator = 0;
        double denominator = 0;
        foreach ((DateTimeOffset timestamp, double value) in samples)
        {
            double centeredMinutes = (timestamp - origin).TotalMinutes - meanMinutes;
            numerator += centeredMinutes * (value - meanValue);
            denominator += centeredMinutes * centeredMinutes;
        }

        return denominator <= double.Epsilon ? 0 : numerator / denominator;
    }
}
