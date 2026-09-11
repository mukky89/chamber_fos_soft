using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Calibration;

public sealed class SettlingPhase
{
    public List<SettlingInterval> Intervals { get; set; } = [];
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? ActiveSince { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public double Seconds { get; set; }
    public string Status { get; set; } = "N/A";
    public void Begin(DateTimeOffset now)
    {
        if (ActiveSince.HasValue) return;
        StartedAt ??= now;
        ActiveSince = now;
        EndedAt = null;
        Status = "Prebieha";
    }
    public void End(DateTimeOffset now, string status)
    {
        if (ActiveSince is { } start)
        {
            Seconds += Math.Max(0, (now - start).TotalSeconds);
            Intervals.Add(new(start, now, status));
        }
        ActiveSince = null;
        if (StartedAt.HasValue) { EndedAt = now; Status = status; }
    }
    [JsonIgnore] public double? DurationSeconds => StartedAt.HasValue ? Seconds : null;
}

public sealed record SettlingInterval(DateTimeOffset From, DateTimeOffset Until, string Status);

public sealed class SensorSettlingPeak
{
    public CalibrationSensorMapping Sensor { get; set; } = new();
    public SettlingPhase Fbg { get; set; } = new();
    public string Result { get; set; } = "Prebieha";
}

public sealed class SensorSettlingAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int PlateauIndex { get; set; }
    public double TargetTemperatureC { get; set; }
    public double? FromTemperatureC { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string Status { get; set; } = "Prerušené / nedokončené";
    public bool CriteriaChanged { get; set; }
    public CalibrationProfileSettings Criteria { get; set; } = new();
    public List<SettlingCriteriaChange> CriteriaChanges { get; set; } = [];
    public SettlingPhase Chamber { get; set; } = new();
    public SettlingPhase Wika { get; set; } = new();
    public List<SensorSettlingPeak> Peaks { get; set; } = [];
}

public sealed record SettlingCriteriaChange(DateTimeOffset At, CalibrationProfileSettings Settings);

/// <summary>Observes existing gate transitions only; never participates in calibration decisions.</summary>
public sealed class SensorSettlingRecorder : IDisposable
{
    private readonly Action _save;
    private readonly bool _reference;
    private string _latestCriteria;
    private bool _finished;
    private bool _dirty;
    public SensorSettlingAttempt Attempt { get; }
    public SensorSettlingRecorder(CalibrationRunRecord run, CalibrationSetup setup, int index,
        double target, bool reference, DateTimeOffset now, Action save, SensorSettlingAttempt? pending = null)
    {
        _save = save;
        _reference = reference;
        Attempt = pending ?? CreatePending(run, setup, index, target, now);
        _latestCriteria = JsonSerializer.Serialize(Attempt.Criteria);
        if (!reference || setup.Settings.ChamberEntryEnabled) Attempt.Chamber.Begin(now);
        if (reference && !setup.Settings.ChamberEntryEnabled) Attempt.Wika.Begin(now);
        Save();
    }
    public static SensorSettlingAttempt CreatePending(CalibrationRunRecord run, CalibrationSetup setup, int index, double target, DateTimeOffset now)
    {
        var attempt = new SensorSettlingAttempt
        {
            PlateauIndex = index, TargetTemperatureC = target, StartedAt = now,
            Criteria = JsonSerializer.Deserialize<CalibrationProfileSettings>(JsonSerializer.Serialize(setup.Settings))!,
            Peaks = setup.ActiveMappings.Where(m => m.Selected).Select(m => new SensorSettlingPeak
            { Sensor = JsonSerializer.Deserialize<CalibrationSensorMapping>(JsonSerializer.Serialize(m))! }).ToList()
        };
        run.SensorSettlingAttempts.Add(attempt);
        return attempt;
    }
    public void Gates(DateTimeOffset now, double actual, bool entryReady, bool stable, bool forced, CalibrationProfileSettings settings)
    {
        Attempt.FromTemperatureC ??= double.IsFinite(actual) ? actual : null;
        string currentCriteria = JsonSerializer.Serialize(settings);
        if (currentCriteria != _latestCriteria)
        {
            Attempt.CriteriaChanged = true;
            Attempt.CriteriaChanges.Add(new(now, JsonSerializer.Deserialize<CalibrationProfileSettings>(currentCriteria)!));
            _latestCriteria = currentCriteria;
            _dirty = true;
        }
        bool changed = false;
        if (_reference && entryReady && Attempt.Chamber.ActiveSince.HasValue)
        { Attempt.Chamber.End(now, "Úspešné"); changed = true; }
        if (_reference && entryReady && !Attempt.Wika.StartedAt.HasValue)
        { Attempt.Wika.Begin(now); changed = true; }
        var phase = _reference ? Attempt.Wika : Attempt.Chamber;
        if (forced && phase.Status != "Manuálne preskočené")
        { phase.End(now, "Manuálne preskočené"); changed = true; }
        if (stable && phase.ActiveSince.HasValue)
        { phase.End(now, forced ? "Manuálne preskočené" : "Úspešné"); changed = true; }
        if (changed) _dirty = true;
    }
    public void Peak(DateTimeOffset now, string identity, bool started, bool measuring, bool forced, bool terminal, CalibrationTargetState state)
    {
        var peak = Attempt.Peaks.First(p => p.Sensor.Identity == identity);
        string before = JsonSerializer.Serialize(peak.Fbg);
        if (started && !measuring && !terminal) peak.Fbg.Begin(now);
        if (measuring && peak.Fbg.ActiveSince.HasValue) peak.Fbg.End(now, forced ? "Timeout" : "Úspešné");
        if (terminal)
        {
            peak.Result = state.ToString();
            if (state != CalibrationTargetState.Stable) peak.Fbg.End(now, state.ToString());
        }
        if (before != JsonSerializer.Serialize(peak.Fbg)) _dirty = true;
    }
    public void Finish(DateTimeOffset now, string status)
    {
        if (_finished) return;
        _finished = true;
        Attempt.Status = status;
        Attempt.EndedAt = now;
        foreach (var phase in Attempt.Peaks.Select(p => p.Fbg).Prepend(Attempt.Wika).Prepend(Attempt.Chamber))
            if (phase.ActiveSince.HasValue) phase.End(now, status == "Dokončené" ? "Nedokončené" : status);
        Save();
    }
    private void Save()
        => TrySave(_save);
    public static void TrySave(Action save)
    {
        try { save(); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { VotschVc3.Core.Diagnostics.AppLog.Warn("Snímače", "Históriu ustálenia nemožno uložiť: " + ex.Message); }
    }
    public void Flush()
    {
        if (!_dirty) return;
        _dirty = false;
        Save();
    }
    public void Dispose() => Finish(DateTimeOffset.UtcNow, "Prerušené / chyba");
}

public sealed record SensorSettlingRow(CalibrationRunRecord Run, SensorSettlingAttempt Attempt, SensorSettlingPeak Peak)
{
    public string Name => string.IsNullOrWhiteSpace(Peak.Sensor.SensorName) ? "Neurčený" : Peak.Sensor.SensorName.Trim();
    public bool Accepted => Attempt.Status == "Dokončené" && !Attempt.CriteriaChanged && Peak.Result == nameof(CalibrationTargetState.Stable)
        && Attempt.Wika.Status != "Manuálne preskočené" && Attempt.Chamber.Status != "Manuálne preskočené"
        && Run.State is CalibrationRunState.Completed or CalibrationRunState.CompletedWithWarnings;
    public string Direction => Attempt.FromTemperatureC is not double start ? "N/A" :
        Attempt.TargetTemperatureC > start ? "Ohrev" : Attempt.TargetTemperatureC < start ? "Chladenie" : "Bez zmeny";
}

public static class SensorSettlingHistory
{
    public static IReadOnlyList<SensorSettlingRow> Rows(IEnumerable<CalibrationRunRecord> runs) => runs
        .GroupBy(r => r.RunId).Select(g => g.First())
        .SelectMany(r => Attempts(r).GroupBy(a => a.Id).Select(g => g.First())
            .SelectMany(a => a.Peaks.GroupBy(p => p.Sensor.SourceIdentity).Select(g => new SensorSettlingRow(r, a, g.First())))).ToArray();

    private static IEnumerable<SensorSettlingAttempt> Attempts(CalibrationRunRecord run)
    {
        foreach (var attempt in run.SensorSettlingAttempts) yield return attempt;
        foreach (var plateau in run.Plateaus.Where(p => !run.SensorSettlingAttempts.Any(a => a.PlateauIndex == p.PlateauIndex)))
            yield return new SensorSettlingAttempt
            {
                PlateauIndex = plateau.PlateauIndex, TargetTemperatureC = plateau.TargetTemperatureC,
                StartedAt = plateau.StartedAt, EndedAt = plateau.CompletedAt, Status = "Starší záznam – časy N/A",
                Peaks = plateau.Targets.Select(t => new SensorSettlingPeak
                {
                    Result = t.Status.ToString(), Sensor = new CalibrationSensorMapping
                    { SerialNumber = t.SerialNumber, Channel = t.Channel, PeakId = t.PeakId, PeakLoggerDeviceSerialNumber = t.PeakLoggerDeviceSerialNumber }
                }).ToList()
            };
    }

    public static (double? Average, int Count) Average(IEnumerable<SensorSettlingRow> rows, string phase)
    {
        var eligible = rows.Where(r => r.Accepted);
        var phases = phase == "FBG" ? eligible.Select(r => r.Peak.Fbg) :
            eligible.GroupBy(r => (r.Run.RunId, r.Attempt.Id)).Select(g => phase == "WIKA" ? g.First().Attempt.Wika : g.First().Attempt.Chamber);
        var seconds = phases.Where(p => p.Status == "Úspešné" && p.DurationSeconds.HasValue).Select(p => p.Seconds).ToArray();
        return (seconds.Length == 0 ? null : seconds.Average(), seconds.Length);
    }
}
