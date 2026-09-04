using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationProfileStatisticsTests
{
    [Fact]
    public void Analyze_groups_completed_runs_and_plateaus_by_profile()
    {
        Guid profileId = Guid.NewGuid();
        DateTimeOffset start = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var history = new[]
        {
            Run(profileId, start, 120, CalibrationRunState.Completed, 40, 60),
            Run(profileId, start.AddDays(1), 180, CalibrationRunState.CompletedWithWarnings, 80, 100),
            Run(profileId, start.AddDays(2), 30, CalibrationRunState.Aborted, 10),
            Run(Guid.NewGuid(), start.AddDays(3), 999, CalibrationRunState.Completed, 999),
        };

        CalibrationProfileStatistics result = CalibrationProfileStatisticsAnalyzer.Analyze(history, profileId);

        Assert.Equal(3, result.UsageCount);
        Assert.Equal(2, result.CompletedCount);
        Assert.Equal(1, result.UnfinishedCount);
        Assert.Equal(start.AddDays(2), result.LastUsedAt);
        Assert.Equal(TimeSpan.FromMinutes(180), result.LastCompletedDuration);
        Assert.Equal(TimeSpan.FromMinutes(150), result.AverageDuration);
        Assert.Equal(TimeSpan.FromMinutes(150), result.MedianDuration);
        Assert.Equal(2, result.Plateaus.Count);
        Assert.Equal(TimeSpan.FromMinutes(60), result.Plateaus[0].AverageDuration);
        Assert.Equal(2, result.Plateaus[0].SampleCount);
    }

    [Fact]
    public void Analyze_returns_empty_projection_when_profile_has_no_history()
    {
        Guid profileId = Guid.NewGuid();

        CalibrationProfileStatistics result = CalibrationProfileStatisticsAnalyzer.Analyze(Array.Empty<CalibrationRunRecord>(), profileId);

        Assert.Equal(profileId, result.ProfileId);
        Assert.Equal(0, result.UsageCount);
        Assert.Empty(result.Plateaus);
        Assert.Null(result.MedianDuration);
    }

    private static CalibrationRunRecord Run(
        Guid profileId,
        DateTimeOffset startedAt,
        double totalMinutes,
        CalibrationRunState state,
        params double[] plateauMinutes)
    {
        var run = new CalibrationRunRecord
        {
            ProfileId = profileId,
            StartedAt = startedAt,
            CompletedAt = startedAt.AddMinutes(totalMinutes),
            State = state,
        };
        DateTimeOffset plateauStart = startedAt;
        for (int index = 0; index < plateauMinutes.Length; index++)
        {
            run.Plateaus.Add(new CalibrationPlateauResult
            {
                PlateauIndex = index,
                TargetTemperatureC = -40 + (index * 10),
                StartedAt = plateauStart,
                CompletedAt = plateauStart.AddMinutes(plateauMinutes[index]),
            });
            plateauStart = run.Plateaus[^1].CompletedAt;
        }

        return run;
    }
}
