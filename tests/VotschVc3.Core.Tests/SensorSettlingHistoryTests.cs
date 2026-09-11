using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class SensorSettlingHistoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);
    private static CalibrationSetup Setup(int count = 1) => new()
    {
        Settings = new() { ChamberEntryEnabled = true },
        Mappings = Enumerable.Range(1, count).Select(i => new CalibrationSensorMapping
        { SerialNumber = "291877/0001", SensorName = "SC-01/T", Channel = "1.3", PeakId = "P" + i, Selected = true }).ToList()
    };
    private static void Complete(SensorSettlingRecorder recorder, CalibrationSetup setup, int seconds = 60)
    {
        recorder.Gates(Start.AddSeconds(10), 20, true, false, false, setup.Settings);
        recorder.Gates(Start.AddSeconds(30), 20, true, true, false, setup.Settings);
        foreach (var mapping in setup.Mappings)
        {
            recorder.Peak(Start.AddSeconds(30), mapping.Identity, true, false, false, false, CalibrationTargetState.Stabilizing);
            recorder.Peak(Start.AddSeconds(30 + seconds), mapping.Identity, true, true, false, false, CalibrationTargetState.Live);
            recorder.Peak(Start.AddSeconds(180), mapping.Identity, true, true, false, true, CalibrationTargetState.Stable);
        }
        recorder.Finish(Start.AddSeconds(180), "Dokončené");
    }
    [Fact]
    public void GateDurationsExcludeFinalSamplingAndKeepImmutableSensorSnapshot()
    {
        var run = new CalibrationRunRecord(); var setup = Setup();
        using var recorder = new SensorSettlingRecorder(run, setup, 0, 20, true, Start, () => { });
        Complete(recorder, setup);
        setup.Mappings[0].SensorName = "Changed";
        Assert.Equal(10, recorder.Attempt.Chamber.Seconds);
        Assert.Equal(20, recorder.Attempt.Wika.Seconds);
        Assert.Equal(60, recorder.Attempt.Peaks[0].Fbg.Seconds);
        Assert.Equal("SC-01/T", recorder.Attempt.Peaks[0].Sensor.SensorName);
    }
    [Fact]
    public void RetriedFbgQualificationSumsOnlyStabilizingIntervals()
    {
        var setup = Setup(); using var recorder = new SensorSettlingRecorder(new(), setup, 0, 20, true, Start, () => { });
        string key = setup.Mappings[0].Identity;
        recorder.Peak(Start, key, true, false, false, false, CalibrationTargetState.Stabilizing);
        recorder.Peak(Start.AddSeconds(10), key, true, true, false, false, CalibrationTargetState.Live);
        recorder.Peak(Start.AddSeconds(40), key, true, false, false, false, CalibrationTargetState.Stabilizing);
        recorder.Peak(Start.AddSeconds(60), key, true, true, false, true, CalibrationTargetState.Stable);
        Assert.Equal(30, recorder.Attempt.Peaks[0].Fbg.Seconds);
    }
    [Fact]
    public void CommonPhasesCountOnceAndFailedOrInterruptedRunsAreExcluded()
    {
        var run = new CalibrationRunRecord { State = CalibrationRunState.Completed }; var setup = Setup(2);
        using var recorder = new SensorSettlingRecorder(run, setup, 0, 20, true, Start, () => { });
        Complete(recorder, setup);
        var rows = SensorSettlingHistory.Rows(new[] { run, run });
        Assert.Equal(2, rows.Count);
        Assert.Equal((10d, 1), SensorSettlingHistory.Average(rows, "Komora"));
        Assert.Equal((20d, 1), SensorSettlingHistory.Average(rows, "WIKA"));
        Assert.Equal((60d, 2), SensorSettlingHistory.Average(rows, "FBG"));
        run.State = CalibrationRunState.Aborted;
        Assert.Equal(0, SensorSettlingHistory.Average(rows, "FBG").Count);
    }
    [Fact]
    public void DisabledPhaseIsMissingAndManualOverrideIsExcluded()
    {
        var setup = Setup(); setup.Settings.ChamberEntryEnabled = false;
        var run = new CalibrationRunRecord { State = CalibrationRunState.Completed };
        using var recorder = new SensorSettlingRecorder(run, setup, 0, 20, true, Start, () => { });
        recorder.Gates(Start.AddSeconds(5), 20, true, true, true, setup.Settings);
        Complete(recorder, setup);
        Assert.Null(recorder.Attempt.Chamber.DurationSeconds);
        Assert.Equal("Manuálne preskočené", recorder.Attempt.Wika.Status);
        Assert.Equal(0, SensorSettlingHistory.Average(SensorSettlingHistory.Rows([run]), "FBG").Count);
    }
    [Fact]
    public void CriteriaChangesCannotContaminateAverage()
    {
        var setup = Setup(); var run = new CalibrationRunRecord { State = CalibrationRunState.Completed };
        using var recorder = new SensorSettlingRecorder(run, setup, 0, 20, true, Start, () => { });
        setup.Settings.RequiredStableSamples = 100;
        Complete(recorder, setup);
        Assert.True(recorder.Attempt.CriteriaChanged);
        Assert.Equal(100, Assert.Single(recorder.Attempt.CriteriaChanges).Settings.RequiredStableSamples);
        Assert.Equal(50, recorder.Attempt.Criteria.RequiredStableSamples);
        Assert.Equal(0, SensorSettlingHistory.Average(SensorSettlingHistory.Rows([run]), "FBG").Count);
    }
    [Fact]
    public void HistorySurvivesReloadAndResumeWithoutDuplicatingOldAttempt()
    {
        string root = Path.Combine(Path.GetTempPath(), "sensor-history-" + Guid.NewGuid());
        try
        {
            var store = new CalibrationStore(root); var setup = Setup();
            var run = new CalibrationRunRecord { State = CalibrationRunState.Completed, ProfileName = "History" };
            using (var recorder = new SensorSettlingRecorder(run, setup, 0, 20, true, Start, () => store.SaveRun(run))) Complete(recorder, setup);
            var restored = Assert.Single(new CalibrationStore(root).LoadHistory());
            using (var retry = new SensorSettlingRecorder(restored, setup, 1, 30, true, Start.AddHours(1), () => store.SaveRun(restored)))
                retry.Finish(Start.AddHours(1).AddMinutes(1), "Timeout");
            store.SaveRun(restored); store.SaveRun(restored);
            var rows = SensorSettlingHistory.Rows(new CalibrationStore(root).LoadHistory());
            Assert.Equal(2, rows.Count);
            Assert.Equal(1, SensorSettlingHistory.Average(rows, "FBG").Count);
            Assert.Equal("SC-01/T", rows[0].Name);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void LegacyElapsedTimeIsNotMisrepresentedAsStabilization()
    {
        var run = new CalibrationRunRecord { Plateaus = [new() { Targets = [new()
            { SerialNumber = "SN", PeakId = "P1", StabilizationTime = TimeSpan.FromHours(1), Status = CalibrationTargetState.Stable }] }] };
        var row = Assert.Single(SensorSettlingHistory.Rows([run]));
        Assert.Equal("Neurčený", row.Name);
        Assert.Equal("SN", row.Peak.Sensor.SerialNumber);
        Assert.Null(row.Peak.Fbg.DurationSeconds);
    }
    [Fact]
    public void PendingRampAttemptIsReusedAndDoesNotCountRampAsStabilization()
    {
        var run = new CalibrationRunRecord(); var setup = Setup();
        var pending = SensorSettlingRecorder.CreatePending(run, setup, 0, 20, Start);
        Assert.Null(pending.Chamber.DurationSeconds);
        using var recorder = new SensorSettlingRecorder(run, setup, 0, 20, true, Start.AddMinutes(5), () => { }, pending);
        recorder.Gates(Start.AddMinutes(6), 20, true, false, false, setup.Settings);
        Assert.Single(run.SensorSettlingAttempts);
        Assert.Equal(60, pending.Chamber.Seconds);
        Assert.Equal(Start.AddMinutes(5), pending.Chamber.StartedAt);
    }
    [Fact]
    public void OverrideAfterReferenceRecoveryExcludesEarlierSuccessfulGate()
    {
        var setup = Setup(); var run = new CalibrationRunRecord { State = CalibrationRunState.Completed };
        using var recorder = new SensorSettlingRecorder(run, setup, 0, 20, true, Start, () => { });
        Complete(recorder, setup);
        recorder.Gates(Start.AddMinutes(4), 20, true, true, true, setup.Settings);
        Assert.Equal("Manuálne preskočené", recorder.Attempt.Wika.Status);
        Assert.Equal(0, SensorSettlingHistory.Average(SensorSettlingHistory.Rows([run]), "WIKA").Count);
    }
}
