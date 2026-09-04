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

/// <summary>
/// Temperature-stability gate modelled after Auto_calibrator_Pali/SensTemp/CalibrationThread.py.
///
/// Pali does not require the reference probe to converge to the exact setpoint and does not use a
/// rolling linear-regression window. It evaluates temperature in groups of five samples:
/// - the latest reference value must be inside the configured target tolerance;
/// - the mean absolute change of the five samples against the current baseline must be small;
/// - a good block adds five stability points;
/// - a bad block changes the baseline and removes ten points (never below zero).
///
/// The original Pali setting for change is an absolute delta. chamber_fos_soft already stores the
/// equivalent limit as °C/min, so the same Pali five-sample delta is normalized by the average sample
/// age before it is compared with that existing limit. This preserves the configured units while
/// matching Pali's baseline/block/score behaviour.
/// </summary>
public sealed class TemperatureStabilityDetector
{
    private const int PaliBlockSize = 5;
    private const int PaliFailurePenaltyMultiplier = 2;

    private readonly TimeSpan _requiredDuration;
    private readonly double _toleranceC;
    private readonly double _maxDriftCPerMinute;
    private readonly List<(DateTimeOffset Timestamp, double Value)> _block = new(PaliBlockSize);

    private DateTimeOffset? _baselineTimestamp;
    private DateTimeOffset? _scoreTimestamp;
    private double _baselineValue;
    private int _stableScoreSeconds;
    private int _displayedStableScoreSeconds;
    private double _lastAverageDeltaC;
    private double _lastNormalizedChangeCPerMinute;
    private bool _isStable;

    public TemperatureStabilityDetector(TimeSpan requiredDuration, double toleranceC, double maxDriftCPerMinute)
    {
        _requiredDuration = requiredDuration < TimeSpan.Zero ? TimeSpan.Zero : requiredDuration;
        _toleranceC = Math.Abs(toleranceC);
        _maxDriftCPerMinute = Math.Max(0, maxDriftCPerMinute);
    }

    /// <summary>Pali-style accumulated stability score. Good 5-sample block: +5, bad block: -10.</summary>
    public int StableScoreSeconds => _stableScoreSeconds;

    /// <summary>
    /// Stability time shown to the operator. Between completed five-sample validation blocks it
    /// follows the real sample timestamps, while <see cref="StableScoreSeconds"/> remains the
    /// authoritative block-validated score used to open the calibration gate.
    /// </summary>
    public int DisplayedStableScoreSeconds => _displayedStableScoreSeconds;

    /// <summary>Configured score required before the temperature gate opens.</summary>
    public int RequiredStableScoreSeconds => (int)Math.Ceiling(_requiredDuration.TotalSeconds);

    /// <summary>Mean |T - baseline| of the last completed five-sample block.</summary>
    public double LastAverageDeltaC => _lastAverageDeltaC;

    /// <summary>The Pali block delta normalized to °C/min so the existing drift setting keeps its unit.</summary>
    public double LastNormalizedChangeCPerMinute => _lastNormalizedChangeCPerMinute;

    public StabilityMetrics Add(DateTimeOffset timestamp, double value, double target)
    {
        // Pali captures prev_temp before entering the five-sample loop. The first sample therefore
        // establishes the baseline and is not itself one of the five evaluated samples.
        if (_baselineTimestamp is null)
        {
            _baselineTimestamp = timestamp;
            _scoreTimestamp = timestamp;
            _baselineValue = value;
            _block.Clear();
            _displayedStableScoreSeconds = 0;
            // A zero dwell time removes only the time requirement; it must not bypass
            // the target-tolerance gate on the very first reference sample.
            _isStable = _requiredDuration <= TimeSpan.Zero &&
                        (Math.Abs(value - target) < _toleranceC ||
                         (_toleranceC == 0 && Math.Abs(value - target) <= double.Epsilon));
            return BuildMetrics(new[] { (timestamp, value) }, _isStable);
        }

        _block.Add((timestamp, value));
        if (_block.Count < PaliBlockSize)
        {
            double partialAverageDelta = _block.Average(x => Math.Abs(x.Value - _baselineValue));
            double partialAverageAgeMinutes = _block.Average(x => Math.Max(0, (x.Timestamp - _baselineTimestamp.Value).TotalMinutes));
            double partialChangePerMinute = partialAverageAgeMinutes <= double.Epsilon
                ? 0
                : partialAverageDelta / partialAverageAgeMinutes;
            bool partialToleranceOk = Math.Abs(value - target) < _toleranceC ||
                                      (_toleranceC == 0 && Math.Abs(value - target) <= double.Epsilon);
            bool partialChangeOk = _maxDriftCPerMinute <= 0 || partialChangePerMinute < _maxDriftCPerMinute;
            int pendingSeconds = Math.Max(0, (int)Math.Floor((timestamp - (_scoreTimestamp ?? timestamp)).TotalSeconds));
            _displayedStableScoreSeconds = partialToleranceOk && partialChangeOk
                ? Math.Min(RequiredStableScoreSeconds, _stableScoreSeconds + pendingSeconds)
                : _stableScoreSeconds;
            return BuildMetrics(_block, _isStable);
        }

        DateTimeOffset baselineTimestamp = _baselineTimestamp.Value;
        double averageDelta = _block.Average(x => Math.Abs(x.Value - _baselineValue));
        double averageAgeMinutes = _block.Average(x => Math.Max(0, (x.Timestamp - baselineTimestamp).TotalMinutes));
        double normalizedChangePerMinute = averageAgeMinutes <= double.Epsilon
            ? 0
            : averageDelta / averageAgeMinutes;

        _lastAverageDeltaC = averageDelta;
        _lastNormalizedChangeCPerMinute = normalizedChangePerMinute;

        double latest = _block[^1].Value;
        bool toleranceOk = Math.Abs(latest - target) < _toleranceC ||
                           (_toleranceC == 0 && Math.Abs(latest - target) <= double.Epsilon);
        bool changeOk = _maxDriftCPerMinute <= 0 || normalizedChangePerMinute < _maxDriftCPerMinute;
        int elapsedScoreSeconds = Math.Max(1, (int)Math.Round(
            (timestamp - (_scoreTimestamp ?? timestamp)).TotalSeconds,
            MidpointRounding.AwayFromZero));
        _scoreTimestamp = timestamp;

        if (toleranceOk && changeOk)
        {
            // Pali sampled at roughly 1 Hz, where five samples also meant five seconds. Real CTH7000
            // reads take longer, so count the measured wall-clock interval instead of pretending
            // every completed block lasted exactly five seconds.
            _stableScoreSeconds = Math.Min(RequiredStableScoreSeconds, _stableScoreSeconds + elapsedScoreSeconds);
            _isStable = _stableScoreSeconds >= RequiredStableScoreSeconds;
        }
        else
        {
            // Pali sets prev_temp to the current temperature and subtracts i*2 from the stability
            // counter. A single disturbance therefore penalizes accumulated stability instead of
            // erasing the entire history.
            _baselineTimestamp = _block[^1].Timestamp;
            _baselineValue = latest;
            _stableScoreSeconds = Math.Max(
                0,
                _stableScoreSeconds - (elapsedScoreSeconds * PaliFailurePenaltyMultiplier));
            _isStable = false;
        }

        _displayedStableScoreSeconds = _stableScoreSeconds;

        StabilityMetrics result = BuildMetrics(_block, _isStable);
        _block.Clear();
        return result;
    }

    private StabilityMetrics BuildMetrics(
        IReadOnlyList<(DateTimeOffset Timestamp, double Value)> samples,
        bool isStable)
    {
        if (samples.Count == 0)
        {
            return new StabilityMetrics(
                0,
                _baselineValue,
                _baselineValue,
                _baselineValue,
                _baselineValue,
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

        return new StabilityMetrics(
            values.Length,
            mean,
            median,
            min,
            max,
            max - min,
            stdDev,
            _lastNormalizedChangeCPerMinute,
            TimeSpan.FromSeconds(_displayedStableScoreSeconds),
            isStable);
    }
}
