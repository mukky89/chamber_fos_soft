namespace VotschVc3.Core.Calibration;

/// <summary>
/// Historical, profile-scoped timing analysis derived from persisted run summaries.
/// Run summaries remain the single source of truth; this projection is rebuilt whenever
/// history changes so older runs automatically contribute without a data migration.
/// </summary>
public sealed record CalibrationProfileStatistics(
    Guid ProfileId,
    int UsageCount,
    int CompletedCount,
    int UnfinishedCount,
    DateTimeOffset? LastUsedAt,
    TimeSpan? LastCompletedDuration,
    TimeSpan? AverageDuration,
    TimeSpan? MedianDuration,
    TimeSpan? MinimumDuration,
    TimeSpan? MaximumDuration,
    IReadOnlyList<CalibrationPlateauStatistics> Plateaus)
{
    public static CalibrationProfileStatistics Empty(Guid profileId) =>
        new(profileId, 0, 0, 0, null, null, null, null, null, null, Array.Empty<CalibrationPlateauStatistics>());
}

public sealed record CalibrationPlateauStatistics(
    int PlateauIndex,
    double TargetTemperatureC,
    int SampleCount,
    TimeSpan AverageDuration,
    TimeSpan MedianDuration,
    TimeSpan MinimumDuration,
    TimeSpan MaximumDuration);

public static class CalibrationProfileStatisticsAnalyzer
{
    public static CalibrationProfileStatistics Analyze(IEnumerable<CalibrationRunRecord> history, Guid profileId)
    {
        ArgumentNullException.ThrowIfNull(history);

        CalibrationRunRecord[] runs = history
            .Where(run => run.ProfileId == profileId)
            .OrderByDescending(run => run.StartedAt)
            .ToArray();
        if (runs.Length == 0) return CalibrationProfileStatistics.Empty(profileId);

        CalibrationRunRecord[] completed = runs
            .Where(IsCompleted)
            .Where(run => run.CompletedAt > run.StartedAt)
            .ToArray();
        TimeSpan[] durations = completed
            .Select(run => run.CompletedAt!.Value - run.StartedAt)
            .ToArray();

        IReadOnlyList<CalibrationPlateauStatistics> plateaus = completed
            .SelectMany(run => run.Plateaus)
            .Where(plateau => plateau.CompletedAt > plateau.StartedAt)
            .GroupBy(plateau => plateau.PlateauIndex)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                CalibrationPlateauResult[] samples = group.ToArray();
                TimeSpan[] times = samples.Select(item => item.CompletedAt - item.StartedAt).ToArray();
                return new CalibrationPlateauStatistics(
                    group.Key,
                    samples.Average(item => item.TargetTemperatureC),
                    times.Length,
                    Average(times),
                    Median(times),
                    times.Min(),
                    times.Max());
            })
            .ToArray();

        CalibrationRunRecord? latestCompleted = completed.FirstOrDefault();
        return new CalibrationProfileStatistics(
            profileId,
            runs.Length,
            completed.Length,
            runs.Length - completed.Length,
            runs[0].StartedAt,
            latestCompleted?.CompletedAt - latestCompleted?.StartedAt,
            durations.Length == 0 ? null : Average(durations),
            durations.Length == 0 ? null : Median(durations),
            durations.Length == 0 ? null : durations.Min(),
            durations.Length == 0 ? null : durations.Max(),
            plateaus);
    }

    private static bool IsCompleted(CalibrationRunRecord run) =>
        run.State is CalibrationRunState.Completed or CalibrationRunState.CompletedWithWarnings;

    private static TimeSpan Average(IReadOnlyCollection<TimeSpan> values) =>
        TimeSpan.FromTicks((long)values.Average(value => value.Ticks));

    private static TimeSpan Median(IReadOnlyCollection<TimeSpan> values)
    {
        long[] ordered = values.Select(value => value.Ticks).OrderBy(value => value).ToArray();
        int middle = ordered.Length / 2;
        return TimeSpan.FromTicks(ordered.Length % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] / 2) + (ordered[middle] / 2));
    }
}
