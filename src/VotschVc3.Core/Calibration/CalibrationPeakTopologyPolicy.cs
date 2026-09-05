namespace VotschVc3.Core.Calibration;

/// <summary>
/// Keeps the operator-approved peak topology immutable while a calibration run is active.
/// Live measurements may update existing rows, but they must not add rows to a bound WPF grid.
/// </summary>
public static class CalibrationPeakTopologyPolicy
{
    public static IReadOnlyList<string> SelectNewSources(
        IEnumerable<string> knownSources,
        IEnumerable<string> measuredSources,
        bool calibrationIsRunning)
    {
        if (calibrationIsRunning) return Array.Empty<string>();

        var known = new HashSet<string>(knownSources, StringComparer.OrdinalIgnoreCase);
        return measuredSources
            .Where(source => !string.IsNullOrWhiteSpace(source) && known.Add(source))
            .ToArray();
    }
}
