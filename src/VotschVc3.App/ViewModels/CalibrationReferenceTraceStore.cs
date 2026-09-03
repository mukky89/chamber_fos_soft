using System.Runtime.CompilerServices;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// Process-lifetime ring buffer for the WIKA reference assigned to each chamber.
/// It listens to the existing reference status bus, therefore every successful background,
/// manual or calibration read is captured without coupling recording to a button click.
/// </summary>
public sealed class CalibrationReferenceTraceStore
{
    private const int MaxPointsPerChamber = 2880; // 4 h at 5 s sampling; longer runs are decimated below.
    private static readonly Lazy<CalibrationReferenceTraceStore> LazyInstance = new(() => new CalibrationReferenceTraceStore());
    public static CalibrationReferenceTraceStore Instance => LazyInstance.Value;

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

        DateTimeOffset timestamp = snapshot.LastUpdated ?? DateTimeOffset.Now;
        lock (_gate)
        {
            if (!_traces.TryGetValue(e.ChamberId, out List<CalibrationReferenceTracePoint>? trace))
            {
                trace = new List<CalibrationReferenceTracePoint>();
                _traces[e.ChamberId] = trace;
            }

            if (trace.Count > 0)
            {
                CalibrationReferenceTracePoint last = trace[^1];
                // The status bus may publish the same sample more than once while connection labels
                // change. Keep one physical sample only.
                if (last.Timestamp == timestamp && Math.Abs(last.TemperatureC - temperature) < 0.000001) return;
            }

            trace.Add(new CalibrationReferenceTracePoint(
                timestamp,
                temperature,
                snapshot.PortName,
                snapshot.Channel));

            if (trace.Count > MaxPointsPerChamber)
            {
                // Keep the newest half at full resolution and decimate the older half. This keeps
                // the graph responsive during very long calibrations without losing the trend.
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
        lock (_gate) _traces.Remove(chamberId);
    }
}

public sealed record CalibrationReferenceTracePoint(
    DateTimeOffset Timestamp,
    double TemperatureC,
    string PortName,
    string Channel);
