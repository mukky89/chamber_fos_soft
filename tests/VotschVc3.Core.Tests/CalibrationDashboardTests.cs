using System.Globalization;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;
using Xunit;
namespace VotschVc3.Core.Tests;
public sealed class CalibrationDashboardTests
{
    [Fact] public void SkippedPointWithInvalidTemperatureDoesNotPolluteLiveTemperatureOrTrace()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability, 0) with { ActualTemperatureC = 20.1 }, Start);
        var timestamp = m.LastTemperatureSampleAt;
        m.Apply(Snapshot(CalibrationRunState.PlateauCompleted, 0) with
        {
            ActualTemperatureC = double.NaN,
            ReferenceTemperatureC = double.PositiveInfinity,
        }, Start.AddSeconds(1));
        Assert.Equal(20.1, m.ActualTemperature);
        Assert.Equal(timestamp, m.LastTemperatureSampleAt);
        Assert.Single(m.ChamberTemperatureTrace);
        Assert.DoesNotContain("NaN", m.Delta);
        Assert.DoesNotContain("Infinity", m.Reference);
    }
    [Fact] public void TemperaturePhasesPreserveWikaReturnAndRestartFbgAtActualTime()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability, 0), Start);
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0), Start.AddMinutes(10));
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0), Start.AddMinutes(11));
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability, 0), Start.AddMinutes(12));
        Assert.Null(m.FbgStabilityStartedAt);
        Assert.Null(m.FbgMeasurementStartedAt);
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0), Start.AddMinutes(22));
        Assert.Equal(Start.AddMinutes(22), m.FbgStabilityStartedAt);
        Assert.Equal(new[] { "WIKA – stabilizácia", "FBG – stabilizácia", "WIKA – stabilizácia", "FBG – stabilizácia" },
            m.TemperaturePhases.Select(p => p.Label));
        Assert.Equal(new[] { Start, Start.AddMinutes(10), Start.AddMinutes(12), Start.AddMinutes(22) },
            m.TemperaturePhases.Select(p => p.Start));
        m.Apply(Snapshot(CalibrationRunState.MovingToPlateau, 1) with { TargetTemperatureC = 30 }, Start.AddMinutes(30));
        Assert.Single(m.TemperaturePhases);
        Assert.Equal(Start.AddMinutes(30), m.TemperaturePhases[0].Start);
    }
    [Fact] public void ReferenceCriteriaAreNeutralUntilChamberOpens()
    {
        var model = Model();
        var entry = new ChamberEntryStatus(true, false, 0.1, 0.01, 0.01, 17, 120, 0.5, 0.5, 0.1, Start);
        var snapshot = Snapshot(CalibrationRunState.WaitingForChamberStability, 0) with
        {
            ChamberEntry = entry, ReferenceTemperatureC = -40,
            TemperatureStableScoreSeconds = 100, RequiredTemperatureScoreSeconds = 600,
            ReferenceEvaluationStartedAt = Start.AddMinutes(-1),
        };
        model.Apply(snapshot, Start);
        Assert.All(new[] { model.ReferenceToleranceTone, model.ReferenceRangeTone, model.ReferenceStdDevTone,
            model.ReferenceDriftTone, model.ReferenceTimeTone, model.ReferenceCardTone }, tone => Assert.Equal("Pending", tone));
        Assert.Equal(0, model.TemperatureStableScoreSeconds);
        Assert.Equal(0, model.TemperatureProgress);
        Assert.Null(model.ReferenceStabilityStartedAt);
        Assert.Contains("po ustálení komory", model.ReferenceResetLabel);
        model.Apply(snapshot with { ChamberEntry = entry with { IsOpen = true },
            ReferenceEvaluationStartedAt = Start.AddSeconds(120), TemperatureStableScoreSeconds = 0 }, Start.AddSeconds(120));
        Assert.Equal("Done", model.ReferenceToleranceTone);
        Assert.Equal("Waiting", model.ReferenceTimeTone);
        Assert.Equal(Start.AddSeconds(120), model.ReferenceStabilityStartedAt);
    }

    [Fact] public void AllStableTargetsFinishStabilityStepAndHideItsCountdown()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0, Target("Measuring", 2)), Start);
        Assert.Equal("Done", m.Steps[4].State);
        Assert.Equal("Active", m.Steps[5].State);
        Assert.False(m.StabilitySamplingActive);
        Assert.Equal("Stabilita FBG splnená", m.StabilityCountdown);
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Stabilizing", 0, CalibrationTargetState.Stabilizing)), Start.AddSeconds(30));
        Assert.Equal("Active", m.Steps[4].State);
        Assert.True(m.StabilitySamplingActive);
    }
    [Fact] public void MeasurementCountdownOnlyRunsForMeasuringTargets()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Stabilizing", 0, CalibrationTargetState.Stabilizing)), Start);
        Assert.False(m.MeasurementSamplingActive);
        Assert.Equal(0, m.MeasurementIntervalProgress);
        Assert.Equal("Čaká na stabilitu FBG", m.MeasurementCountdown);
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Measuring", 1),
            Target("Stabilizing", 0, CalibrationTargetState.Stabilizing) with { PeakId = "P2" }), Start.AddSeconds(30));
        Assert.True(m.MeasurementSamplingActive);
        Assert.Equal(m.NextSampleCountdown, m.MeasurementCountdown);
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Stabilizing", 0, CalibrationTargetState.Stabilizing)), Start.AddSeconds(60));
        Assert.False(m.MeasurementSamplingActive);
        Assert.Equal(0, m.MeasurementIntervalProgress);
    }
    [Fact] public void ChamberCriteriaDistinguishWaitingConfirmedAndDisabled()
    {
        var m = Model();
        var entry = new ChamberEntryStatus(true, false, 0.1, 0.2, 0.01, 60, 120, 0.5, 0.5, 0.1, Start);
        var snapshot = Snapshot(CalibrationRunState.WaitingForChamberStability) with { ChamberEntry = entry };
        m.Apply(snapshot, Start);
        Assert.Equal("Done", m.ChamberToleranceTone);
        Assert.Equal("Waiting", m.ChamberTimeTone);
        Assert.Equal("Waiting", m.Steps[2].State);
        Assert.Equal("Pending", m.Steps[3].State);
        Assert.Contains("ČAKÁ NA KOMORU", m.ReferenceCardState);
        m.Apply(snapshot with { ChamberEntry = entry with { IsOpen = true, WindowSeconds = 120 } }, Start);
        Assert.Equal("Done", m.ChamberCardTone);
        Assert.Equal("Done", m.Steps[2].State);
        Assert.Equal("Waiting", m.Steps[3].State);
        Assert.Contains("Hodnoty pri potvrdení", m.ChamberEntryDetail);
        m.Apply(snapshot with { ChamberEntry = entry with { Enabled = false } }, Start);
        Assert.Equal("Pending", m.ChamberRangeTone);
        Assert.Contains("vypnuté", m.ChamberRangeLabel);
        m.Apply(snapshot with { ChamberEntry = null }, Start);
        Assert.Contains("čaká na údaje", m.ChamberRangeLabel);
    }
    [Fact] public void ConfigureKeepsExplicitReferenceChamberIdentity()
    {
        var dashboard = new CalibrationDashboardViewModel();
        Guid chamberId = Guid.NewGuid();
        dashboard.Configure("Profil", "Komora 2", new[] { -20d, 0d, 20d }, true, "pravidlá",
            referenceChamberId: chamberId);
        Assert.Equal(chamberId, dashboard.ReferenceChamberId);
    }

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
        Assert.Equal("Warning", m.Points[0].State); Assert.Contains("NEPOTVRDENÉ", m.Points[0].Badge); Assert.Equal("Pending", m.Points[1].State);
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

        Assert.Equal("≈ 2 h 56 min", m.Eta);
        Assert.Contains("historické mediány", m.EtaBasis);
        Assert.Equal(Start.AddHours(2).AddMinutes(55).AddSeconds(100), m.EstimatedFinishAt);
        Assert.Equal("Temperovanie 25 °C", m.Steps[8].Title);
    }
    [Fact] public void EtaIncludesDeferredUnfinishedPlateausBeforeCurrentIndex()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.PlateauCompleted, 0), Start.AddMinutes(10));
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability, 2) with
        {
            PlateauElapsed = TimeSpan.FromMinutes(2)
        }, Start.AddMinutes(12));

        // Point 1 (index 1) was deferred and remains unfinished even though point 2 is active.
        Assert.Equal("≈ 2 h 44 min", m.Eta);
        Assert.Equal(Start.AddHours(2).AddMinutes(55).AddSeconds(100), m.EstimatedFinishAt);
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
    [Fact] public void PeakCardShowsCurrentStabilityCriteria()
    {
        var m = new CalibrationDashboardViewModel();
        m.Configure("Profil", "Komora", new[] { -40d }, true, "Rules",
            requiredStableSamples: 32, maxRangePm: 4.25, maxStdDevPm: 1.2,
            maxPeakDriftPmPerMinute: 0.35);

        Assert.Contains("32 vzoriek", m.PeakStabilityCriteria);
        Assert.Contains($"range ≤ {4.25d.ToString("F3", CultureInfo.CurrentCulture)} pm", m.PeakStabilityCriteria);
        Assert.Contains($"σ ≤ {1.2d.ToString("F3", CultureInfo.CurrentCulture)} pm", m.PeakStabilityCriteria);
        Assert.Contains($"drift ≤ {0.35d.ToString("F3", CultureInfo.CurrentCulture)} pm/min", m.PeakStabilityCriteria);
        Assert.Contains("všetky štyri podmienky súčasne", m.PeakStabilityCriteriaHelp);

        m.Begin(Start);
        m.Configure("Profil", "Komora", new[] { -40d }, true, "Rules",
            requiredStableSamples: 50, maxRangePm: 5, maxStdDevPm: 1.5,
            maxPeakDriftPmPerMinute: 1);
        Assert.Contains("50 vzoriek", m.PeakStabilityCriteria);
        Assert.Contains($"range ≤ {5d.ToString("F3", CultureInfo.CurrentCulture)} pm", m.PeakStabilityCriteria);
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
        Assert.Contains("rozsah, σ a drift", m.ReferenceTimeHelp);
        Assert.Contains("najnovšia úspešne načítaná teplota", m.ReferenceStatusHelp);
        Assert.Contains("Jedna vzorka sama osebe nepotvrdzuje stabilitu", m.ReferenceStatusHelp);
        Assert.Contains("odchýlka aj drift", m.ReferenceStatusHelp);
        Assert.Contains("vynuluje celý čas", m.ReferenceTimeHelp);
        Assert.Contains("posledná platná vzorka začne nové okno", m.ReferenceTimeHelp);
        Assert.Contains("najviac o", m.ReferenceTimeHelp);
        Assert.Contains("15 min 00 s", m.ReferenceTimeHelp);
        Assert.Contains("1 h 00 min", m.ReferenceTimeHelp);
        Assert.Contains("nevyhovujúci bod sa nikdy automaticky neprijme", m.ReferenceTimeHelp);
        Assert.Contains("bod odloží", m.ReferenceTimeHelp);
        Assert.Contains("jeden neskorší pokus", m.ReferenceTimeHelp);
    }
    [Fact] public void WikaTimelineKeepsFirstFbgMilestonesAndResetsForNextPlateau()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability), Start);
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Stabilizing", 0, CalibrationTargetState.Stabilizing)), Start.AddMinutes(10));
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0, Target("Measuring", 2)), Start.AddMinutes(12));
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0, Target("Measuring", 3)), Start.AddMinutes(13));
        Assert.Equal(Start.AddMinutes(10), m.FbgStabilityStartedAt);
        Assert.Equal(Start.AddMinutes(12), m.FbgMeasurementStartedAt);
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability, 1), Start.AddMinutes(15));
        Assert.Null(m.FbgStabilityStartedAt);
        Assert.Null(m.FbgMeasurementStartedAt);
    }
    [Fact] public void SampleCountdownAdvancesResetsAndStopsWhenPaused()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0, Target("Measuring", 1)), Start);
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0, Target("Measuring", 2)), Start.AddSeconds(30));
        m.Tick(Start.AddSeconds(35));
        Assert.True(m.SampleIntervalProgress > 0);
        Assert.Contains("Ďalšia vzorka", m.NextSampleCountdown);
        m.Tick(Start.AddSeconds(95));
        Assert.Equal(100, m.SampleIntervalProgress);
        Assert.Contains("Čaká na ďalšiu", m.NextSampleCountdown);
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0, Target("Measuring", 3)), Start.AddSeconds(96));
        Assert.Equal(0, m.SampleIntervalProgress);
        m.Pause(true, Start.AddSeconds(97));
        Assert.Contains("pozastavený", m.NextSampleCountdown);
        Assert.Equal(0, m.SampleIntervalProgress);
    }
    [Fact] public void PlateauDiagnosticsRetainSpecificProblemAndSamplesAfterAdvancing()
    {
        var m = Model();
        var target = Target("Stabilizing", 0, CalibrationTargetState.Stabilizing) with { Detail = "Prekročený drift", CurrentWavelengthNm = 1550.2 };
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0, target), Start);
        m.Apply(Snapshot(CalibrationRunState.PlateauCompleted, 0, target), Start.AddSeconds(30));
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability, 1), Start.AddSeconds(40));
        Assert.Contains("Prekročený drift", m.Points[0].DiagnosticHelp);
        Assert.Contains(target.SerialNumber, m.Points[0].DiagnosticHelp);
        Assert.Contains(m.Points[0].Graphs, g => g.Unit == "nm" && g.Samples.Count == 2);
    }
    [Fact] public void PlateauDiagnosticsExcludePreGatePeakValuesFromFbgGraph()
    {
        var m = Model();
        var waiting = Target("Temperature", 0, CalibrationTargetState.WaitingForTemperature) with
        {
            CurrentWavelengthNm = 1511.229248
        };
        var stabilizing = Target("Stabilizing", 1, CalibrationTargetState.Stabilizing) with
        {
            CurrentWavelengthNm = 1511.502685
        };

        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability, 0, waiting), Start);
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0, stabilizing), Start.AddMinutes(50));

        PlateauDiagnosticGraph graph = Assert.Single(m.Points[0].Graphs, graph => graph.Unit == "nm");
        DashboardTemperatureSample sample = Assert.Single(graph.Samples);
        Assert.Equal(1511.502685, sample.TemperatureC, 6);
    }
    [Fact] public void SummaryCardsShowGateOrderAndCurrentState()
    {
        var m = Model();
        Assert.Contains("ČAKÁ", m.ChamberCardState);
        Assert.Contains("ČAKÁ", m.ReferenceCardState);

        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability), Start);
        Assert.Contains("ČAKÁ", m.ReferenceCardState);
        Assert.Contains("ČAKÁ", m.PeakCardState);
        Assert.Contains("ČAKÁ", m.MeasurementCardState);

        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Stabilizing", 0, CalibrationTargetState.Stabilizing)), Start.AddSeconds(1));
        Assert.Contains("SPLNENÉ", m.ReferenceCardState);
        Assert.Contains("PREBIEHA", m.PeakCardState);
        Assert.Contains("ČAKÁ", m.MeasurementCardState);

        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Measuring", 2)), Start.AddSeconds(2));
        Assert.Contains("SPLNENÉ", m.PeakCardState);
        Assert.Contains("PREBIEHA", m.MeasurementCardState);

        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Done", 5, CalibrationTargetState.Stable)), Start.AddSeconds(3));
        Assert.Contains("SPLNENÉ", m.MeasurementCardState);
    }
    [Fact] public void PeakSummaryDistinguishesPassedStabilityFromCompletedMeasurement()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Measuring", 2),
            Target("Stabilizing", 0, CalibrationTargetState.Stabilizing) with { PeakId = "P2" }), Start);

        Assert.Equal("1 / 2 prešlo stabilitou", m.PeakSummary);
        Assert.Equal("1 vo finálnom meraní · 0 úplne dokončených", m.PeakDetail);
    }
    [Fact] public void PeakLabelsFollowTemperatureStabilityMeasurementAndFailure()
    {
        var item = new FbgStabilityChartItem("SN1|CH1|P1");
        item.Update(Target("Temperature", 0, CalibrationTargetState.WaitingForTemperature), Start);
        Assert.Equal("ČAKÁ NA TEPLOTU", item.State);
        Assert.Equal("ČAKÁ NA STABILITU FBG", item.MeasurementState);
        item.Update(Target("Stabilizing", 0, CalibrationTargetState.Stabilizing), Start.AddSeconds(1));
        Assert.Equal("STABILIZÁCIA", item.State);
        Assert.Equal("ČAKÁ NA STABILITU FBG", item.MeasurementState);
        item.Update(Target("Measuring", 1), Start.AddSeconds(2));
        Assert.Equal("MERANIE", item.State);
        Assert.Equal("MERANIE", item.MeasurementState);
        item.Update(Target("Done", 0, CalibrationTargetState.TimedOut), Start.AddSeconds(3));
        Assert.Equal("NEPOTVRDENÉ", item.State);
        Assert.Equal("NEPOTVRDENÉ", item.MeasurementState);
    }

    [Fact] public void CompactPeakStatesUseDistinctColorsForMeasuringAndCompleted()
    {
        var item = new FbgStabilityChartItem("SN1|CH1|P1");
        item.Update(Target("Measuring", 2), Start);
        Assert.Equal("MERANIE", item.State);
        Assert.Equal("#58A6FF", item.StateBrush);
        Assert.Equal("#58A6FF", item.MeasurementStateBrush);

        item.Update(Target("Done", 5, CalibrationTargetState.Stable), Start.AddSeconds(1));
        Assert.Equal("HOTOVO", item.State);
        Assert.Equal("#45C99A", item.StateBrush);
        Assert.Equal("#45C99A", item.MeasurementStateBrush);
    }
    [Fact] public void WikaCardShowsCurrentSettlingLimitAndItsBreakdown()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability) with
        {
            TemperatureSettlingElapsed = TimeSpan.FromMinutes(18),
            TemperatureSettlingBaseLimit = TimeSpan.FromMinutes(30),
            AutomaticTemperatureExtensionUsed = TimeSpan.FromMinutes(15),
            MaximumAutomaticTemperatureExtension = TimeSpan.FromHours(1),
        }, Start.AddMinutes(18));

        Assert.Contains("Aktuálny limit plata: 45 min 00 s", m.ReferenceSettlingLimitLabel);
        Assert.Contains("uplynulo 18 min 00 s", m.ReferenceSettlingLimitLabel);
        Assert.Contains("zostáva 27 min 00 s", m.ReferenceSettlingLimitLabel);
        Assert.Contains("Základ 30 min 00 s", m.ReferenceSettlingLimitBreakdown);
        Assert.Contains("automaticky +15 min 00 s / 1 h 00 min", m.ReferenceSettlingLimitBreakdown);
        Assert.DoesNotContain("ručne", m.ReferenceSettlingLimitBreakdown);
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
    [Fact] public void FbgPeakListsAreSortedByNumericChannelAndPeakIndex()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0,
            Target("Stabilizing", 0) with { Channel = "3.4", PeakId = "P1", PeakIndex = 1 },
            Target("Stabilizing", 0) with { Channel = "1.10", PeakId = "P1", PeakIndex = 1 },
            Target("Stabilizing", 0) with { Channel = "1.2", PeakId = "P2", PeakIndex = 2 },
            Target("Stabilizing", 0) with { Channel = "1.2", PeakId = "P1", PeakIndex = 1 },
            Target("Stabilizing", 0) with { Channel = "4.4", PeakId = "P1", PeakIndex = 1 },
            Target("Stabilizing", 0) with { Channel = "1.1", PeakId = "P1", PeakIndex = 1 }), Start);

        Assert.Equal(
            new[] { "1.1/P1", "1.2/P1", "1.2/P2", "1.10/P1", "3.4/P1", "4.4/P1" },
            m.FbgStabilityCharts.Select(item => $"{item.Channel}/{item.PeakId}"));
    }
    [Fact] public void TemperatureLossRegressesGatesWithoutCompletingPoint()
    {
        var m = Model(); m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0, Target("Measuring", 2)), Start);
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability, 0, Target("Temperature", 0, CalibrationTargetState.WaitingForTemperature)), Start.AddSeconds(1));
        Assert.Equal(0, m.Samples); Assert.Equal("Waiting", m.Steps[3].State); Assert.Equal("Pending", m.Steps[5].State);
        Assert.Equal(0, m.CompletedPoints);
        Assert.Contains("ČAKÁ", m.TemperatureStatus); // 100% score alone is not gate approval.
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
    [Theory]
    [InlineData(CalibrationTargetState.Stable, "ÚSPEŠNÉ")]
    [InlineData(CalibrationTargetState.CompletedWithStabilityWarning, "NEPOTVRDENÉ")]
    [InlineData(CalibrationTargetState.TimedOut, "NEÚSPEŠNÉ")]
    [InlineData(CalibrationTargetState.Overridden, "NEPOTVRDENÉ")]
    public void RoadmapDistinguishesOutcomeInLiveAndRestoredResults(CalibrationTargetState status, string label)
    {
        var live = Model();
        live.Apply(Snapshot(CalibrationRunState.PlateauCompleted, 0, Target("Done", 5, status)), Start);
        Assert.Contains(label, live.Points[0].Badge);
        Assert.DoesNotContain("SPLNENÉ", live.Points[0].Badge);
        Assert.Contains("Stabilita", live.Points[0].Detail);
        Assert.Equal(1, live.CompletedPoints);
        if (status != CalibrationTargetState.Stable)
        {
            Assert.DoesNotContain("SPLNENÉ", live.PeakCardState);
            Assert.DoesNotContain("SPLNENÉ", live.MeasurementCardState);
        }
        var restored = Model();
        restored.RestoreCompletedPoints(new[] { new CalibrationPlateauResult
        {
            PlateauIndex = 0, StartedAt = Start, CompletedAt = Start.AddHours(2),
            Targets = new() { new CalibrationMeasurementResult { Status = status } },
        } });
        Assert.Equal(live.Points[0].Badge, restored.Points[0].Badge);
        Assert.Contains("Stabilita", restored.Points[0].Detail);
        Assert.Equal(1, restored.CompletedPoints);
    }
    [Fact] public void ProgressSeparatesSuccessfulUnconfirmedAndUnfinishedPoints()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.PlateauCompleted, 0, Target("Done", 5, CalibrationTargetState.CompletedWithStabilityWarning)), Start);
        m.Apply(Snapshot(CalibrationRunState.PlateauCompleted, 1, Target("Done", 5, CalibrationTargetState.Stable)), Start.AddMinutes(10));
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 2, Target("Stabilizing", 0, CalibrationTargetState.Stabilizing)), Start.AddMinutes(11));
        Assert.Equal(1, m.SuccessfulPoints);
        Assert.Equal(1, m.UnconfirmedPoints);
        Assert.Equal(1, m.RemainingPoints);
        Assert.Equal(2, m.CompletedPoints);
        Assert.Contains("Úspešné 1", m.ProgressLabel);
        Assert.Contains("zostáva 1", m.ProgressLabel);
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
            TemperatureStableScoreSeconds = 0,
        }, sampleAt);

        Assert.Equal(sampleAt.AddMinutes(-12), m.CurrentPlateauTraceStart);
        m.Apply(Snapshot(CalibrationRunState.PlateauCompleted, 2) with
        {
            PlateauElapsed = TimeSpan.FromMinutes(18),
        }, sampleAt.AddMinutes(6));
        Assert.Equal(sampleAt.AddMinutes(-12), m.CurrentPlateauTraceStart);
    }
    [Fact] public void ChamberTraceContinuesDuringFbgAndRejectsInvalidSamples()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability, 0) with
        {
            ActualTemperatureC = 20.1,
        }, Start);
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0) with
        {
            ActualTemperatureC = 20.2,
            ReferenceEvaluationStartedAt = Start.AddSeconds(1),
        }, Start.AddSeconds(2));
        m.ReportChamberTemperature(20.3, Start.AddSeconds(3));
        m.ReportChamberTemperature(double.NaN, Start.AddSeconds(4));
        m.Apply(Snapshot(CalibrationRunState.StabilizingSensors, 0) with
        {
            ActualTemperatureC = double.NaN,
        }, Start.AddSeconds(5));
        Assert.Equal(3, m.ChamberTemperatureTrace.Count);
        Assert.Equal(20.3, m.ChamberTemperatureTrace[^1].TemperatureC);
        Assert.Equal(Start.AddSeconds(2), m.FbgStabilityStartedAt);
        Assert.Contains("Vzorky komory: 3", m.ChamberTraceSamplesLabel);
    }
    [Fact] public void DashboardResetsAllLiveChartDataWhenTargetChangesToNewPlateau()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability, 0) with
        {
            TargetTemperatureC = 10,
            ActualTemperatureC = 10.1,
            TemperatureStableScoreSeconds = 500,
            RequiredTemperatureScoreSeconds = 600,
        }, Start);
        Assert.NotEmpty(m.ChamberTemperatureTrace);
        Assert.NotEmpty(m.WikaStabilityScoreTrace);

        DateTimeOffset nextPlateauAt = Start.AddMinutes(20);
        m.Apply(Snapshot(CalibrationRunState.MovingToPlateau, 0) with
        {
            TargetTemperatureC = 0,
            ActualTemperatureC = 9.9,
            PlateauElapsed = TimeSpan.Zero,
            TemperatureStableScoreSeconds = 0,
            RequiredTemperatureScoreSeconds = 0,
        }, nextPlateauAt);

        Assert.Equal(nextPlateauAt, m.CurrentPlateauTraceStart);
        Assert.Equal(0, m.TargetTemperatureC);
        Assert.Single(m.ChamberTemperatureTrace);
        Assert.Equal(nextPlateauAt, m.ChamberTemperatureTrace[0].Timestamp);
        Assert.Empty(m.WikaStabilityScoreTrace);
        Assert.Empty(m.FbgStabilityCharts);
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
        Assert.Contains("skutočných časov vzoriek", m.ReferenceDriftHelp);
        Assert.Contains("posledných 120 sekúnd", m.ReferenceDriftHelp);
        Assert.Contains("vynuluje celý čas", m.ReferenceTimeHelp);
        Assert.Equal(2, m.WikaStabilityScoreTrace.Count);
        Assert.Equal(0, m.WikaStabilityScoreTrace[0].ScoreSeconds);
        Assert.Equal(35, m.WikaStabilityScoreTrace[1].ScoreSeconds);
        Assert.Equal(600, m.WikaStabilityScoreTrace[1].RequiredSeconds);
    }
    [Fact] public void WikaChartKeepsFullPlateauWhenStableTimeStarts()
    {
        var m = Model();
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability) with
        {
            PlateauElapsed = TimeSpan.FromMinutes(10),
            TemperatureStableScoreSeconds = 0,
            RequiredTemperatureScoreSeconds = 600,
        }, Start);
        m.Apply(Snapshot(CalibrationRunState.WaitingForChamberStability) with
        {
            PlateauElapsed = TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(7)),
            TemperatureStableScoreSeconds = 5,
            RequiredTemperatureScoreSeconds = 600,
        }, Start.AddSeconds(7));

        Assert.Equal(Start.AddMinutes(-10), m.CurrentPlateauTraceStart);
        Assert.Equal(2, m.WikaStabilityScoreTrace.Count);
        Assert.Equal(0, m.WikaStabilityScoreTrace[0].ScoreSeconds);
        Assert.Equal(Start.AddSeconds(2), m.WikaStabilityScoreTrace[0].Timestamp);
        Assert.Equal(5, m.WikaStabilityScoreTrace[1].ScoreSeconds);
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
