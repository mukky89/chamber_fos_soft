using System.Globalization;
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
    [Fact] public void EtaBecomesUnknownInsteadOfPublishingFalseFinish_WhenCurrentPointExceedsEvidence()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.PlateauCompleted), Start.AddMinutes(10));
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability, 1) with { PlateauElapsed = TimeSpan.FromHours(4) }, Start.AddHours(4));

        Assert.Equal("Neurčitý", m.Eta);
        Assert.Equal("—", m.Finish);
        Assert.Contains("prekročil", m.EtaBasis);
    }
    [Fact] public void EtaUsesHistoricalPlateausAndIncludesConfiguredSetpointRamps()
    {
        var history = new[]
        {
            new CalibrationPlateauStatistics(0, -40, 3, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(40)),
            new CalibrationPlateauStatistics(1, 0, 3, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(18), TimeSpan.FromMinutes(30)),
        };
        var m = new CalibrationDashboardViewModel();
        m.Configure("Profil", "Komora", new[] { -40d, 0d }, true, "Rules",
            enableSetpointRamp: true, setpointRampCPerMinute: 1, historicalPlateaus: history);
        m.ReportChamberTemperature(20, Start);
        m.Begin(Start);
        m.ReportChamberTemperature(20, Start);
        m.Tick(Start);

        Assert.Equal("≈ 2 h 30 min", m.Eta);
        Assert.Contains("historické mediány", m.EtaBasis);
    }
    [Fact] public void ParallelMeasurementDoesNotCountStabilitySamplesAsMeasurement()
    {
        var m = Model(); m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0, Target("Measuring", 2), Target("Stabilizing", 0, CalibrationTargetState.Stabilizing) with { PeakId = "P2" }), Start);
        Assert.Equal(2, m.Samples); Assert.Equal(10, m.RequiredSamples); Assert.Equal(20, m.SampleProgress);
        Assert.Equal("Active", m.Steps[4].State); Assert.Equal("Active", m.Steps[5].State);
        Assert.Equal("SN1|CH1|P1", m.ActivePeakKey);
    }
    [Fact] public void WorkflowHelpUsesCurrentSettingsAndObservedCycleTime()
    {
        var m = new CalibrationDashboardViewModel();
        m.Configure("Profil", "Komora", new[] { -40d }, true, "Rules",
            toleranceC: 0.25, maxDriftCPerMinute: 0.1,
            requiredStableSamples: 12, requiredMeasurementSamples: 5,
            maxRangePm: 4, maxStdDevPm: 1.2, maxPeakDriftPmPerMinute: 0.8,
            stableDuration: TimeSpan.FromMinutes(3), stabilityTimeout: TimeSpan.FromMinutes(20), sensorTimeout: TimeSpan.FromMinutes(40));
        m.Begin(Start);
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0, Target("Stabilizing", 0)), Start);
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0, Target("Stabilizing", 0)), Start.AddSeconds(4));

        Assert.Contains("12 vzoriek", m.Steps[4].Detail);
        Assert.Contains($"range ≤ {4d.ToString("F3", CultureInfo.CurrentCulture)} pm", m.Steps[4].Detail);
        Assert.Contains("5 nových", m.Steps[5].Detail);
        Assert.Contains("20 s", m.Steps[5].Detail);
    }
    [Fact] public void WikaWorkflowHelpExplainsHowStableScoreIsCollectedAndPenalized()
    {
        var m = new CalibrationDashboardViewModel();
        m.Configure("Profil", "Komora", new[] { -40d }, true, "Rules",
            toleranceC: 0.25, maxDriftCPerMinute: 0.1,
            stableDuration: TimeSpan.FromMinutes(10), stabilityTimeout: TimeSpan.FromMinutes(30));

        string help = m.Steps[3].Detail;

        Assert.Contains("blokoch 5 vzoriek", help);
        Assert.Contains($"±{0.25d.ToString("F3", CultureInfo.CurrentCulture)} °C", help);
        Assert.Contains($"≤ {0.1d.ToString("F3", CultureInfo.CurrentCulture)} °C/min", help);
        Assert.Contains("pripočíta reálne uplynuté sekundy", help);
        Assert.Contains("odpočíta dvojnásobok", help);
        Assert.Contains("10 min 00 s", help);
        Assert.Contains("Timeout: 30 min 00 s", help);
        Assert.Contains("10 min 00 s", m.ReferenceTimeHelp);
        Assert.Contains("30 min 00 s", m.ReferenceTimeHelp);
        Assert.Contains("súčasne splnené obe podmienky", m.ReferenceTimeHelp);
        Assert.Contains("odchýlka alebo drift nevyhovuje", m.ReferenceTimeHelp);
    }
    [Fact] public void SummaryCardsShowGateOrderAndCurrentState()
    {
        var m = Model();
        Assert.Contains("MONITORING", m.ChamberCardState);
        Assert.Contains("PENDING", m.ReferenceCardState);

        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability), Start);
        Assert.Contains("WAITING", m.ReferenceCardState);
        Assert.Contains("PENDING", m.PeakCardState);
        Assert.Contains("PENDING", m.MeasurementCardState);

        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Stabilizing", 0, CalibrationTargetState.Stabilizing)), Start.AddSeconds(1));
        Assert.Contains("DONE", m.ReferenceCardState);
        Assert.Contains("RUNNING", m.PeakCardState);
        Assert.Contains("PENDING", m.MeasurementCardState);

        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Measuring", 2)), Start.AddSeconds(2));
        Assert.Contains("DONE", m.PeakCardState);
        Assert.Contains("RUNNING", m.MeasurementCardState);

        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Done", 5, CalibrationTargetState.Stable)), Start.AddSeconds(3));
        Assert.Contains("DONE", m.MeasurementCardState);
    }
    [Fact] public void NextFbgStepShowsSeparateObservedSampleDuration()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability), Start);
        Assert.Equal("Stabilita FBG", m.TimelineNextTitle);
        Assert.Contains("Odber každých 1 s", m.TimelineNextTiming);
        Assert.Contains("0 min 50 s", m.TimelineNextTiming);

        DateTimeOffset restarted = Start.AddMinutes(1);
        m.Begin(restarted);
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors), restarted);
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors), restarted.AddSeconds(3.4));
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability), restarted.AddSeconds(4));

        Assert.Contains("Odhad 50 vzoriek", m.TimelineNextTiming);
        Assert.Contains("2 min 50 s", m.TimelineNextTiming);
        Assert.Contains("skutočný cyklus", m.TimelineNextTiming);
    }
    [Fact] public void ConfiguredFbgAcquisitionIntervalIsVisibleAndPersistent()
    {
        var dashboard = new CalibrationDashboardViewModel();
        dashboard.Configure("Profil", "Komora", new[] { -40d }, true, "Rules",
            requiredStableSamples: 50, sampleAcquisitionIntervalSeconds: 30);
        dashboard.Begin(Start);
        dashboard.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability), Start);

        Assert.Contains("Odber každých 30 s", dashboard.TimelineNextTiming);
        Assert.Contains("25 min 00 s", dashboard.TimelineNextTiming);
        Assert.Contains("každých 30 s", dashboard.Steps[4].Detail);

        var settings = new CalibrationProfileSettings { SampleAcquisitionIntervalSeconds = 30 };
        string json = System.Text.Json.JsonSerializer.Serialize(settings);
        CalibrationProfileSettings restored = System.Text.Json.JsonSerializer.Deserialize<CalibrationProfileSettings>(json)!;
        Assert.Equal(30, restored.SampleAcquisitionIntervalSeconds);
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

        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Measuring", 1) with { CurrentWavelengthNm = 1550.005 },
            Target("Stabilizing", 0, CalibrationTargetState.Stabilizing) with { PeakId = "P2", CurrentWavelengthNm = 1551.006 }), Start.AddSeconds(2));
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Measuring", 2) with { CurrentWavelengthNm = 1550.007 },
            Target("Stabilizing", 0, CalibrationTargetState.Stabilizing) with { PeakId = "P2", CurrentWavelengthNm = 1551.008 }), Start.AddSeconds(3));

        Assert.Equal(2, m.FbgStabilityCharts[0].MeasurementChartPoints.Count);
        Assert.Empty(m.FbgStabilityCharts[1].MeasurementChartPoints);
        Assert.Equal(2, m.FbgStabilityCharts[0].ChartPoints.Count);

        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Stabilizing", 0, CalibrationTargetState.Stabilizing) with { CurrentWavelengthNm = 1550.009 },
            Target("Stabilizing", 0, CalibrationTargetState.Stabilizing) with { PeakId = "P2", CurrentWavelengthNm = 1551.010 }), Start.AddSeconds(4));
        Assert.Empty(m.FbgStabilityCharts[0].MeasurementChartPoints);

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
    [Fact] public void CalibrationEventsCapturePlateauAndTemperaturesAtEventTime()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 1, Target("Measuring", 2)) with
        {
            TargetTemperatureC = 0,
            ActualTemperatureC = 0.12,
            ReferenceTemperatureC = 0.034,
        }, Start.AddMinutes(1));

        DashboardEvent peakEvent = Assert.Single(m.Activity, item => item.Message.Contains("začína meranie"));
        Assert.Equal("PLATO 2 / 3", peakEvent.Plateau);
        Assert.Contains($"Cieľ {0d.ToString("F1", CultureInfo.CurrentCulture)} °C", peakEvent.Temperatures);
        Assert.Contains($"WIKA {0.034d.ToString("F3", CultureInfo.CurrentCulture)} °C", peakEvent.Temperatures);
        Assert.Contains($"Komora {0.12d.ToString("F2", CultureInfo.CurrentCulture)} °C", peakEvent.Temperatures);
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
    [Fact] public void DashboardExposesOnlyCurrentPlateauWindowForReferenceChart()
    {
        var m = Model();
        DateTimeOffset sampleAt = Start.AddHours(5);
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability, 2) with
        {
            PlateauElapsed = TimeSpan.FromMinutes(12),
        }, sampleAt);

        Assert.Equal(sampleAt.AddMinutes(-12), m.CurrentPlateauTraceStart);
        m.Apply(Snapshot(CalibrationRunState.PlateauCompleted, 2) with
        {
            PlateauElapsed = TimeSpan.FromMinutes(18),
        }, sampleAt.AddMinutes(6));
        Assert.Equal(sampleAt.AddMinutes(-12), m.CurrentPlateauTraceStart);
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
        Assert.Contains(0.2d.ToString("F3", CultureInfo.CurrentCulture), m.ReferenceToleranceLabel);
        Assert.Equal("Done", m.ReferenceDriftTone);
        Assert.Contains($"{0.08d.ToString("F3", CultureInfo.CurrentCulture)} / ≤ {0.1d.ToString("F3", CultureInfo.CurrentCulture)}", m.ReferenceDriftLabel);
        Assert.Equal("Waiting", m.ReferenceTimeTone);
        Assert.Contains("35 / 600 s", m.ReferenceTimeLabel);
        Assert.Contains("±0", m.ReferenceToleranceHelp);
        Assert.Contains("bloku 5 vzoriek", m.ReferenceDriftHelp);
        Assert.Contains("dvojnásobok", m.ReferenceTimeHelp);
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
