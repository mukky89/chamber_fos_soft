namespace VotschVc3.Core.Calibration;

/// <summary>One-time rolling chamber prerequisite before collecting WIKA stability.</summary>
public sealed record ChamberEntryStatus(bool Enabled, bool IsOpen, double? DeviationC, double? RangeC, double? DriftCPerMinute, double WindowSeconds, double RequiredSeconds, double ToleranceC, double RangeLimitC, double DriftLimitCPerMinute, DateTimeOffset EvaluatedAt);

public sealed class ChamberEntryGate
{
    private readonly Queue<(DateTimeOffset Time, double Value)> _samples = new();
    public ChamberEntryStatus? Status { get; private set; }
    public bool IsOpen { get; private set; }
    public string Detail { get; private set; } = "Čaká na meranie komory.";
    public bool Add(DateTimeOffset time, double value, double target, CalibrationProfileSettings settings)
    {
        if (IsOpen) return true;
        Status = new(settings.ChamberEntryEnabled, false, double.IsFinite(value) ? Math.Abs(value - target) : null,
            null, null, 0, Math.Clamp(settings.ChamberEntryStableSeconds, 1, 3600),
            Math.Abs(settings.ChamberEntryToleranceC), Math.Abs(settings.ChamberEntryRangeC),
            Math.Abs(settings.ChamberEntryDriftCPerMinute), time);
        if (!settings.ChamberEntryEnabled) return true;
        double seconds = Math.Clamp(settings.ChamberEntryStableSeconds, 1, 3600);
        if (!double.IsFinite(value) || Math.Abs(value - target) > Math.Abs(settings.ChamberEntryToleranceC))
        {
            _samples.Clear();
            Detail = $"Komora {value:F3} °C mimo cieľa ±{settings.ChamberEntryToleranceC:F2} °C; WIKA stabilita ešte nezačala.";
            return false;
        }
        if (_samples.Count > 0 && time - _samples.Last().Time > TimeSpan.FromSeconds(Math.Max(90, settings.SampleAcquisitionIntervalSeconds * 3)))
            _samples.Clear();
        _samples.Enqueue((time, value));
        while (_samples.Count > 2 && time - _samples.ElementAt(1).Time >= TimeSpan.FromSeconds(seconds)) _samples.Dequeue();
        var samples = _samples.ToArray();
        double elapsed = (time - samples[0].Time).TotalSeconds;
        double meanX = samples.Average(p => (p.Time - samples[0].Time).TotalMinutes);
        double meanY = samples.Average(p => p.Value);
        double variance = samples.Sum(p => Math.Pow((p.Time - samples[0].Time).TotalMinutes - meanX, 2));
        double drift = variance > 0 ? samples.Sum(p => ((p.Time - samples[0].Time).TotalMinutes - meanX) * (p.Value - meanY)) / variance : 0;
        double range = samples.Max(p => p.Value) - samples.Min(p => p.Value);
        IsOpen = elapsed >= seconds && range <= Math.Abs(settings.ChamberEntryRangeC) && Math.Abs(drift) <= Math.Abs(settings.ChamberEntryDriftCPerMinute);
        Status = Status with { IsOpen = IsOpen, RangeC = samples.Length > 1 ? range : null, DriftCPerMinute = samples.Length > 1 ? Math.Abs(drift) : null, WindowSeconds = Math.Min(elapsed, seconds) };
        Detail = $"Komora: okno {Math.Min(elapsed, seconds):F0}/{seconds:F0} s · rozsah {range:F3}/{settings.ChamberEntryRangeC:F3} °C · drift {Math.Abs(drift):F3}/{settings.ChamberEntryDriftCPerMinute:F3} °C/min. " +
            (IsOpen ? "Vstupná podmienka splnená; začína nové okno WIKA." : "WIKA stabilita ešte nezačala.");
        return IsOpen;
    }
}
