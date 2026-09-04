using System.Runtime.CompilerServices;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// Process-lifetime ring buffer for the WIKA reference assigned to each chamber.
/// It listens to the existing reference status bus, therefore every successful background,
/// manual or calibration read is captured without coupling recording to a button click.
/// </summary>
public sealed class CalibrationReferenceTraceStore
{
    private const int MaxPointsPerChamber = 14400; // 4 h near 1 Hz; longer runs are decimated below.
    private static readonly Lazy<CalibrationReferenceTraceStore> LazyInstance = new(() => new CalibrationReferenceTraceStore());
    public static CalibrationReferenceTraceStore Instance => LazyInstance.Value;

    public event EventHandler? Changed;
    private readonly Dictionary<Guid, DateTimeOffset> _runStarts = new();
    private readonly HashSet<Guid> _activeRuns = new();

    public void BeginRun(Guid chamberId, DateTimeOffset started)
    {
        lock (_gate) { _traces[chamberId] = new(); _runStarts[chamberId] = started; _activeRuns.Add(chamberId); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void EndRun(Guid chamberId) { lock (_gate) _activeRuns.Remove(chamberId); }
    public DateTimeOffset? GetRunStart(Guid chamberId)
    { lock (_gate) return _runStarts.TryGetValue(chamberId, out var start) ? start : null; }

    /// <summary>
    /// Returns the chamber id when exactly one calibration trace is active.
    /// This is a safe UI fallback for older dashboard callers that did not explicitly
    /// pass ReferenceChamberId yet. If multiple runs are active, no guess is made.
    /// </summary>
    public Guid? GetSingleActiveChamberId()
    {
        lock (_gate)
        {
            return _activeRuns.Count == 1 ? _activeRuns.First() : null;
        }
    }

    public void AppendRunSample(Guid chamberId, CalibrationReferenceTracePoint point)
    {
        lock (_gate)
        {
            if (!_activeRuns.Contains(chamberId) || !double.IsFinite(point.TemperatureC)) return;
            if (point.Timestamp < _runStarts[chamberId]) return;
            _traces[chamberId].Add(point); // Retain every exact sample consumed by the calibration runner.
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private readonly object _gate = new();
    private readonly Dictionary<Guid, List<CalibrationReferenceTracePoint>> _traces = new();

    private CalibrationReferenceTraceStore()
    {
        CalibrationReferenceStatusStore.Instance.Changed += OnReferenceChanged;
    }

    [ModuleInitializer]
    internal static void StartAtAssemblyLoad()
    {
        _ = Instance;
    }

    private void OnReferenceChanged(object? sender, CalibrationReferenceChangedEventArgs e)
    {
        CalibrationReferenceSnapshot snapshot = CalibrationReferenceStatusStore.Instance.GetSnapshot(e.ChamberId);
        if (!snapshot.IsConnected || snapshot.TemperatureC is not { } temperature || !double.IsFinite(temperature)) return;

        DateTimeOffset timestamp = snapshot.LastUpdate ?? DateTimeOffset.Now;
        lock (_gate)
        {
            if (_runStarts.ContainsKey(e.ChamberId)) return; // Run traces are written only by the five-second sampler, then frozen.
            if (!_traces.TryGetValue(e.ChamberId, out List<CalibrationReferenceTracePoint>? trace))
            {
                trace = new List<CalibrationReferenceTracePoint>();
                _traces[e.ChamberId] = trace;
            }

            if (trace.Count > 0)
            {
                CalibrationReferenceTracePoint last = trace[^1];
                if (last.Timestamp == timestamp && Math.Abs(last.TemperatureC - temperature) < 0.000001) return;
            }

            trace.Add(new CalibrationReferenceTracePoint(
                timestamp,
                temperature,
                snapshot.PortName,
                snapshot.Channel));

            if (trace.Count > MaxPointsPerChamber)
            {
                int oldCount = trace.Count / 2;
                var compact = trace.Take(oldCount).Where((_, index) => index % 2 == 0)
                    .Concat(trace.Skip(oldCount))
                    .ToList();
                _traces[e.ChamberId] = compact;
            }
        }
    }

    public IReadOnlyList<CalibrationReferenceTracePoint> GetTrace(Guid chamberId)
    {
        lock (_gate)
        {
            return _traces.TryGetValue(chamberId, out List<CalibrationReferenceTracePoint>? trace)
                ? trace.ToArray()
                : Array.Empty<CalibrationReferenceTracePoint>();
        }
    }

    public void Clear(Guid chamberId)
    {
        lock (_gate) { _traces.Remove(chamberId); _runStarts.Remove(chamberId); _activeRuns.Remove(chamberId); }
    }
}

public sealed record CalibrationReferenceTracePoint(
    DateTimeOffset Timestamp,
    double TemperatureC,
    string PortName,
    string Channel);
