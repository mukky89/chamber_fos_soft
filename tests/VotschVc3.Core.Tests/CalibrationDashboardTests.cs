using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;
using Xunit;
namespace VotschVc3.Core.Tests;
public sealed class CalibrationDashboardTests
{
    [Fact] public void RampShowsReasonWithoutStartingCalibrationPoint_AndCanBeStopped()
    {
        var m = Model();
        m.ReportStartup("Čaká sa na pripojenie komory.");
        Assert.Contains("pripojenie", m.OperationDetail);
        m.Apply(Snapshot(CalibrationRunState.MovingToPlateau, -1) with
        { Message = "Nábeh / rampa. Do konca kroku zostáva 00:25:00." }, Start.AddMinutes(5));
        Assert.Contains("00:25:00", m.OperationDetail);
        Assert.Contains("ešte nevyhodnocuje", m.TemperatureStatus);
        Assert.All(m.Points, p => Assert.Equal("Pending", p.State));
        Assert.DoesNotContain(m.Activity, e => e.Message.Contains("bod 0"));
        m.End(CalibrationRunState.Aborted, "Zastavené počas rampy", Start.AddMinutes(6));
        Assert.Contains("Zastavené", m.OperationDetail);
    }
    private static readonly DateTimeOffset Start = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);
    private static CalibrationDashboardViewModel Model()
    {
        var m = new CalibrationDashboardViewModel();
        m.Configure("Test profile · rozsah -40…120 °C · 17 krokov · veľmi dlhý popis", "Komora", new[] { -40d, 0, 120 }, true, "Rules", toleranceC: 0.25, maxDriftCPerMinute: 0.1, profileCode: "P-0214");
        m.Begin(Start);
        return m;
    }
    private static CalibrationProgressSnapshot Snapshot(CalibrationRunState state, int index = 0, params CalibrationTargetProgress[] targets) =>
        new(state, index, 3, -40, -39.9, -40, targets.Count(t => t.State == CalibrationTargetState.Stable), targets.Length,
            TimeSpan.FromMinutes(10), targets, "", 60, 60, state != CalibrationRunState.WaitingForChamberStability);
    private static CalibrationTargetProgress Target(string phase, int samples, CalibrationTargetState state = CalibrationTargetState.Live) =>
        new("SN1", "CH1", "P1", 0, 1550, 12, 12, 0.1, 0.1, TimeSpan.FromSeconds(40), TimeSpan.FromMinutes(60), state, "",
            12, 12, samples, 5, Phase: phase);
    [Fact] public void StartedPlateauIsNotCompleted_AndEtaNeedsMeasuredPoint()
    {
        var m = Model(); m.Apply(Snapshot(CalibrationRunState.StabilizingSensors), Start.AddMinutes(5));
        Assert.Equal(0, m.OverallProgress); Assert.Equal("Po prvom bode", m.Eta);
        m.Apply(Snapshot(CalibrationRunState.PlateauCompleted), Start.AddMinutes(10));
        Assert.Equal(100d / 3, m.OverallProgress, 5); Assert.StartsWith("≈", m.Eta);
        Assert.Equal("Done", m.Points[0].State); Assert.Equal("Pending", m.Points[1].State);
    }
    [Fact] public void ParallelMeasurementDoesNotCountStabilitySamplesAsMeasurement()
    {
        var m = Model(); m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0, Target("Measuring", 2), Target("Stabilizing", 0, CalibrationTargetState.Stabilizing) with { PeakId = "P2" }), Start);
        Assert.Equal(2, m.Samples); Assert.Equal(10, m.RequiredSamples); Assert.Equal(20, m.SampleProgress);
        Assert.Equal("Active", m.Steps[4].State); Assert.Equal("Active", m.Steps[5].State);
        Assert.Equal("SN1|CH1|P1", m.ActivePeakKey);
    }
    [Fact] public void IndividualFbgChartsTrackEachPeakAndResetForNewRun()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Stabilizing", 0, CalibrationTargetState.Stabilizing) with { CurrentWavelengthNm = 1550.001 },
            Target("Stabilizing", 0, CalibrationTargetState.Stabilizing) with { PeakId = "P2", CurrentWavelengthNm = 1551.002 }), Start);
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Stabilizing", 0, CalibrationTargetState.Stabilizing) with { CurrentWavelengthNm = 1550.003 },
            Target("Stabilizing", 0, CalibrationTargetState.Stabilizing) with { PeakId = "P2", CurrentWavelengthNm = 1551.004 }), Start.AddSeconds(1));

        Assert.Equal(2, m.FbgStabilityCharts.Count);
        Assert.All(m.FbgStabilityCharts, chart => Assert.Equal(2, chart.ChartPoints.Count));
        Assert.Contains("P2", m.FbgStabilityCharts[1].Title);

        m.Begin(Start.AddHours(1));
        Assert.Empty(m.FbgStabilityCharts);
    }
    [Fact] public void TemperatureLossRegressesGatesWithoutCompletingPoint()
    {
        var m = Model(); m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0, Target("Measuring", 2)), Start);
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability, 0, Target("Temperature", 0, CalibrationTargetState.WaitingForTemperature)), Start.AddSeconds(1));
        Assert.Equal(0, m.Samples); Assert.Equal("Waiting", m.Steps[3].State); Assert.Equal("Pending", m.Steps[5].State);
        Assert.Equal(0, m.CompletedPoints);
        Assert.Contains("WAITING", m.TemperatureStatus); // 100% score alone is not gate approval.
        Assert.Equal("Teplota komory", m.TimelinePreviousTitle);
        Assert.Equal("WIKA referencia", m.TimelineCurrentTitle);
        Assert.Equal("Stabilita FBG", m.TimelineNextTitle);
    }
    [Fact] public void PauseAndFailureKeepProgressAndStopEta()
    {
        var m = Model(); m.Apply(Snapshot(CalibrationRunState.PlateauCompleted), Start.AddMinutes(10));
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 1), Start.AddMinutes(11));
        m.Pause(true, Start.AddMinutes(12)); Assert.Equal("Pozastavené", m.Eta);
        m.End(CalibrationRunState.Failed, "Timeout WIKA", Start.AddMinutes(13));
        var elapsed = m.Elapsed; m.Tick(Start.AddHours(1));
        Assert.Equal(elapsed, m.Elapsed); Assert.Equal("—", m.Eta); Assert.Equal(1, m.CompletedPoints);
        Assert.Equal("Error", m.Points[1].State); Assert.Contains("Timeout", m.Now);
    }
    [Fact] public void RepeatedTelemetryDoesNotFloodLog_AndNewRunClearsHistory()
    {
        var m = Model(); var s = Snapshot(CalibrationRunState.StabilizingSensors, 0, Target("Measuring", 2));
        m.Apply(s, Start); int count = m.Activity.Count;
        for (int i = 1; i < 100; i++) m.Apply(s, Start.AddSeconds(i));
        Assert.Equal(count, m.Activity.Count);
        m.Begin(Start.AddDays(1)); Assert.Single(m.Activity); Assert.Equal(0, m.Samples); Assert.Equal(0, m.CompletedPoints);
    }
    [Fact] public void ResolvedTemperatureMismatchClearsOnlyMatchingDashboardWarning()
    {
        var m = Model();
        m.Warn("CHYBA TEPLOTY: starý rozdiel", Start);

        m.ResolveWarning("CHYBA TEPLOTY:", "Teploty sú opäť v zhode.", Start.AddSeconds(5));

        Assert.Equal("Bez hlásených upozornení", m.Alert);
        Assert.Equal("Done", m.AlertTone);
        Assert.Contains(m.Activity, item => item.Level == "SUCCESS" && item.Message.Contains("opäť v zhode"));

        m.Warn("Peak P1: stratený signál", Start.AddSeconds(6));
        m.ResolveWarning("CHYBA TEPLOTY:", "Teploty sú opäť v zhode.", Start.AddSeconds(7));
        Assert.Contains("stratený signál", m.Alert);
    }
    [Fact] public void FailedTargetIsNotShownAsSuccessfulPlateau()
    {
        var m = Model(); m.Apply(Snapshot(CalibrationRunState.PlateauCompleted, 0, Target("Done", 0, CalibrationTargetState.Failed)), Start);
        Assert.Equal("Warning", m.Points[0].State);
        Assert.Contains(m.Activity, e => e.Level == "WARNING");
    }
    [Fact] public void StaleDataAndEmptyPlanAreExplicit()
    {
        var m = Model(); m.Apply(Snapshot(CalibrationRunState.StabilizingSensors), Start); m.Tick(Start.AddSeconds(20));
        Assert.Contains("15 s", m.Freshness);
        var empty = new CalibrationDashboardViewModel(); empty.Configure("Empty", "Chamber", Array.Empty<double>(), false, "");
        Assert.Equal(0, empty.OverallProgress); Assert.Equal("Skipped", empty.Steps[3].State);
    }
    [Fact] public void ChamberTemperatureKeepsExactSampleTimestamp_AndInitialReadingIsVisible()
    {
        var m = Model();
        var initialAt = Start.AddMilliseconds(125);
        m.ReportChamberTemperature(-39.75, initialAt);
        Assert.Equal(-39.75, m.ActualTemperature);
        Assert.Equal(initialAt, m.LastTemperatureSampleAt);

        var sampleAt = Start.AddMilliseconds(875);
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability) with { ActualTemperatureC = -39.5 }, sampleAt);
        Assert.Equal(-39.5, m.ActualTemperature);
        Assert.Equal(sampleAt, m.LastTemperatureSampleAt);
    }
    [Fact] public void DashboardExposesCurrentTargetForStabilityBand()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability) with { TargetTemperatureC = -40 }, Start);
        Assert.Equal(-40, m.TargetTemperatureC);
        Assert.Equal(0.25, m.StabilityToleranceC);
    }
    [Fact] public void WikaCardExposesLiveToleranceDriftAndTimeCriteria()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability) with
        {
            ReferenceTemperatureC = -40.2,
            TemperatureStableScoreSeconds = 35,
            RequiredTemperatureScoreSeconds = 600,
            TemperatureGateOpen = false,
            TemperatureDriftCPerMinute = 0.08,
        }, Start);

        Assert.Equal("Done", m.ReferenceToleranceTone);
        Assert.Contains("0,200", m.ReferenceToleranceLabel);
        Assert.Equal("Done", m.ReferenceDriftTone);
        Assert.Contains("0,080 / ≤ 0,100", m.ReferenceDriftLabel);
        Assert.Equal("Waiting", m.ReferenceTimeTone);
        Assert.Contains("35 / 600 s", m.ReferenceTimeLabel);
    }
    [Fact] public void ForceNextStepIsAvailableOnlyWhileWaitingWithAuthoritativeTemperature()
    {
        var m = Model();
        Assert.False(m.CanForceTemperatureGate);
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability), Start);
        Assert.True(m.CanForceTemperatureGate);
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors), Start.AddSeconds(1));
        Assert.False(m.CanForceTemperatureGate);
    }
    [Fact] public void HeaderUsesCompactProfileName_AndShowsPersistedRunId()
    {
        var m = Model();
        Assert.Equal("P-0214 · Test profile", m.Profile);
        Assert.Contains("veľmi dlhý popis", m.ProfileDescription);
        Assert.Equal("Pripravuje sa…", m.RunId);
        m.SetRunId("01-2026-09-04");
        Assert.Equal("01-2026-09-04", m.RunId);
    }
}
