using System.Collections.ObjectModel;
using System.ComponentModel;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.ViewModels;

/// <summary>UI-only projection of runner snapshots. Never controls calibration gates.</summary>
public sealed class CalibrationDashboardViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private CalibrationProgressSnapshot? _snapshot;
    private double? _latestChamberTemperature;
    private DateTimeOffset? _started, _ended, _phaseStarted;
    private bool _paused;
    private bool _running;
    private CalibrationRunState _state;
    private readonly Dictionary<string, string> _targetEvents = new();
    private string _lastWarning = "";
    private string _planSignature = "";
    private double _stabilityMaxDriftCPerMinute;
    private int _requiredStableSamples = 50;
    private int _requiredMeasurementSamples = 50;
    private int _sampleAcquisitionIntervalSeconds = 1;
    private double _maxRangePm = 5;
    private double _maxStdDevPm = 1.5;
    private double _maxPeakDriftPmPerMinute = 1;
    private TimeSpan _stableDuration = TimeSpan.FromMinutes(10);
    private TimeSpan _stabilityTimeout = TimeSpan.FromMinutes(30);
    private TimeSpan _sensorTimeout = TimeSpan.FromMinutes(60);
    private bool _enableSetpointRamp = true;
    private double _setpointRampCPerMinute = 1;
    private double[] _plannedTemperatures = Array.Empty<double>();
    private IReadOnlyDictionary<int, CalibrationPlateauStatistics> _historicalPlateaus =
        new Dictionary<int, CalibrationPlateauStatistics>();
    private double? _observedCycleSeconds;
    public ObservableCollection<DashboardNode> Steps { get; } = new();
    public ObservableCollection<DashboardNode> Points { get; } = new();
    public ObservableCollection<DashboardEvent> Activity { get; } = new();
    public ObservableCollection<FbgStabilityChartItem> FbgStabilityCharts { get; } = new();
    public string Profile { get; private set; } = "Vyberte kalibračný profil";
    public string ProfileDescription { get; private set; } = "Vyberte kalibračný profil";
    public string RunId { get; private set; } = "—";
    public string Chamber { get; private set; } = "Komora";
    public Guid? ReferenceChamberId { get; private set; }
    public string Rules { get; private set; } = "Pravidlá stability sú v nastaveniach.";
    public double StabilityToleranceC { get; private set; }
    public string Alert { get; private set; } = "Bez hlásených upozornení";
    public string AlertTone => _lastWarning.Length > 0 ? "Waiting" : "Done";
    public string StateLabel => _paused ? "PAUSED · Pauza" : _state switch
    {
        CalibrationRunState.Completed => "DONE · Ukončené",
        CalibrationRunState.CompletedWithWarnings => "DONE · S upozorneniami",
        CalibrationRunState.Failed => "ERROR · Chyba",
        CalibrationRunState.Aborted => "STOPPED · Zastavené",
        CalibrationRunState.AwaitingOperator => "BLOCKED · Zásah operátora",
        CalibrationRunState.WaitingForChamberStability => "WAITING · Čaká na stabilitu",
        CalibrationRunState.StabilizingSensors when MeasuringCount > 0 => "MEASURING · Meria",
        _ when _running => "RUNNING · Beží",
        _ => "READY · Pripravené"
    };
    public string Tone => _paused || _state is CalibrationRunState.AwaitingOperator or CalibrationRunState.CompletedWithWarnings or CalibrationRunState.Aborted ? "Waiting" :
        _state == CalibrationRunState.Failed ? "Error" : _state == CalibrationRunState.Completed ? "Done" :
        _state == CalibrationRunState.WaitingForChamberStability ? "Waiting" : _running ? "Active" : "Pending";
    public int CompletedPoints => Points.Count(p => p.State is "Done" or "Warning");
    public double OverallProgress => Points.Count == 0 ? 0 : 100d * CompletedPoints / Points.Count;
    public string ProgressLabel => $"{OverallProgress:F0} % · {CompletedPoints} / {Points.Count} bodov dokončených";
    public string Plateau => _snapshot?.PlateauIndex < 0 ? "Príprava kalibračných bodov" : _snapshot is null ? $"Plán · {Points.Count} bodov" : $"Plato {_snapshot.PlateauIndex + 1} / {_snapshot.PlateauCount}";
    public string Target => _snapshot is null ? "—" : $"{_snapshot.TargetTemperatureC:F1} °C";
    public double? TargetTemperatureC => _snapshot?.TargetTemperatureC;
    public double? ActualTemperature => _snapshot?.ActualTemperatureC ?? _latestChamberTemperature;
    public string Actual => ActualTemperature is { } t ? $"{t:F2} °C" : "—";
    public string Reference => _snapshot?.ReferenceTemperatureC is { } t ? $"{t:F3} °C" : "—";
    public string Delta => _snapshot?.ActualTemperatureC is { } t ? $"Δ {t - _snapshot.TargetTemperatureC:+0.00;-0.00;0.00} °C" : "Čaká na údaje";
    public string Trend { get; private set; } = "—";
    public string TrendTone { get; private set; } = "Steady";
    public bool HasReference { get; private set; }
    public bool CanForceTemperatureGate => _running &&
        _state == CalibrationRunState.WaitingForChamberStability &&
        (!HasReference || _snapshot?.ReferenceTemperatureC is not null);
    public string ReferenceStatus => !HasReference ? "Bez externej referencie" : _snapshot?.ReferenceTemperatureC is null ? "Čaká na vzorku WIKA" : "Posledná vzorka WIKA";
    public string ReferenceToleranceLabel => _snapshot?.ReferenceTemperatureC is not { } reference
        ? "Odchýlka od cieľa · čaká na vzorku"
        : $"Odchýlka |Δ| {Math.Abs(reference - _snapshot.TargetTemperatureC):F3} / ≤ {StabilityToleranceC:F3} °C";
    public string ReferenceToleranceTone => _snapshot?.ReferenceTemperatureC is { } reference &&
        Math.Abs(reference - _snapshot.TargetTemperatureC) <= StabilityToleranceC ? "Done" : "Waiting";
    public string ReferenceDriftLabel => _snapshot?.TemperatureDriftCPerMinute is not { } drift
        ? "Drift · čaká na blok 5 vzoriek"
        : $"Drift {Math.Abs(drift):F3} / ≤ {_stabilityMaxDriftCPerMinute:F3} °C/min";
    public string ReferenceDriftTone => _snapshot?.TemperatureDriftCPerMinute is { } drift &&
        (_stabilityMaxDriftCPerMinute <= 0 || Math.Abs(drift) <= _stabilityMaxDriftCPerMinute) ? "Done" : "Waiting";
    public string ReferenceTimeLabel => $"Stabilný čas {TemperatureStableScoreSeconds} / {_snapshot?.RequiredTemperatureScoreSeconds ?? 0} s";
    public string ReferenceTimeTone => _snapshot?.TemperatureGateOpen == true ? "Done" : "Waiting";
    public double TemperatureProgress => _snapshot?.RequiredTemperatureScoreSeconds is > 0 ? Math.Clamp(100d * (_snapshot.TemperatureStableScoreSeconds ?? 0) / _snapshot.RequiredTemperatureScoreSeconds.Value, 0, 100) : 0;
    public int TemperatureStableScoreSeconds => _snapshot?.TemperatureStableScoreSeconds ?? 0;
    public string TemperatureScore => _state is CalibrationRunState.Preflight or CalibrationRunState.Preparing or CalibrationRunState.MovingToPlateau ? "Po nastavení cieľa sa začne vyhodnocovať výhradne WIKA referencia." : _snapshot?.TemperatureStableScoreSeconds is { } score ? $"Skóre stability WIKA {score} / {_snapshot.RequiredTemperatureScoreSeconds} s" : "Čaká na skóre stability WIKA";
    public string TemperatureStatus => _snapshot is null || _state is CalibrationRunState.Preflight or CalibrationRunState.Preparing or CalibrationRunState.MovingToPlateau ? "Stabilita WIKA sa ešte nevyhodnocuje" : _snapshot?.TemperatureGateOpen == true ? "✓ STABLE · WIKA referencia potvrdená" : "WAITING · WIKA teplotná brána";
    public int TotalTargets => _snapshot?.TotalTargets ?? 0;
    public int StableCount => _snapshot?.Targets.Count(t => t.State == CalibrationTargetState.Stable || t.Phase == "Measuring") ?? 0;
    public int DoneCount => _snapshot?.Targets.Count(t => t.State == CalibrationTargetState.Stable) ?? 0;
    public int MeasuringCount => _snapshot?.Targets.Count(t => t.Phase == "Measuring") ?? 0;
    public string PeakSummary => $"{StableCount} / {TotalTargets} stabilných";
    public string PeakDetail => $"{DoneCount} hotových · {MeasuringCount} práve meria";
    public double StabilityProgress => TotalTargets == 0 ? 0 : 100d * StableCount / TotalTargets;
    public int Samples => _snapshot?.Targets.Sum(t => t.MeasurementSamples) ?? 0;
    public int RequiredSamples => _snapshot?.Targets.Sum(t => t.RequiredMeasurementSamples) ?? 0;
    public string SampleSummary => $"{Samples} / {RequiredSamples}";
    public double SampleProgress => RequiredSamples == 0 ? 0 : Math.Clamp(100d * Samples / RequiredSamples, 0, 100);
    private bool RunStoppedWithError => _state is CalibrationRunState.Failed or CalibrationRunState.AwaitingOperator or CalibrationRunState.Aborted;
    private bool PointFinished => AllTargetsFinished || _state is CalibrationRunState.PlateauCompleted or CalibrationRunState.MovingToNextPlateau or CalibrationRunState.Completed or CalibrationRunState.CompletedWithWarnings;
    public string ChamberCardState => _started is null ? "○ PENDING" : _running ? "● MONITORING" : RunStoppedWithError ? "! STOPPED" : "✓ DONE";
    public string ChamberCardTone => _started is null ? "Pending" : _running ? "Active" : RunStoppedWithError ? "Error" : "Done";
    public string ReferenceCardState => RunStoppedWithError ? "! STOPPED" : !HasReference ? "— N/A" : _snapshot?.TemperatureGateOpen == true || _state is CalibrationRunState.StabilizingSensors or CalibrationRunState.PlateauCompleted or CalibrationRunState.MovingToNextPlateau or CalibrationRunState.Completed or CalibrationRunState.CompletedWithWarnings ? "✓ DONE" : _state == CalibrationRunState.WaitingForChamberStability ? "Ⅱ WAITING" : "○ PENDING";
    public string ReferenceCardTone => RunStoppedWithError ? "Error" : !HasReference ? "Pending" : ReferenceCardState.Contains("DONE", StringComparison.Ordinal) ? "Done" : ReferenceCardState.Contains("WAITING", StringComparison.Ordinal) ? "Waiting" : "Pending";
    public string PeakCardState => RunStoppedWithError ? "! STOPPED" : TotalTargets > 0 && StableCount >= TotalTargets ? "✓ DONE" : _state == CalibrationRunState.StabilizingSensors ? "● RUNNING" : PointFinished ? "✓ DONE" : "○ PENDING";
    public string PeakCardTone => RunStoppedWithError ? "Error" : PeakCardState.Contains("DONE", StringComparison.Ordinal) ? "Done" : PeakCardState.Contains("RUNNING", StringComparison.Ordinal) ? "Active" : "Pending";
    public string MeasurementCardState => RunStoppedWithError ? "! STOPPED" : PointFinished ? "✓ DONE" : MeasuringCount > 0 || Samples > 0 ? "● RUNNING" : "○ PENDING";
    public string MeasurementCardTone => RunStoppedWithError ? "Error" : MeasurementCardState.Contains("DONE", StringComparison.Ordinal) ? "Done" : MeasurementCardState.Contains("RUNNING", StringComparison.Ordinal) ? "Active" : "Pending";
    public string ActivePeakKey => _snapshot?.Targets.FirstOrDefault(t => t.Phase == "Measuring") is { } m ? $"{m.SerialNumber}|{m.Channel}|{m.PeakId}" :
        _snapshot?.Targets.FirstOrDefault(t => t.State != CalibrationTargetState.Stable) is { } s ? $"{s.SerialNumber}|{s.Channel}|{s.PeakId}" : "";
    public string ActivePeak => ActivePeakKey.Length == 0 ? "—" : ActivePeakKey.Replace("|", " · ");
    public string Phase => _paused ? "Pozastavené" : _state switch
    {
        CalibrationRunState.WaitingForChamberStability => "Stabilita WIKA referencie",
        CalibrationRunState.StabilizingSensors when AllTargetsFinished => "Vyhodnotenie bodu",
        CalibrationRunState.StabilizingSensors => MeasuringCount > 0 ? "Meranie a stabilizácia FBG" : "Stabilizácia FBG",
        CalibrationRunState.MovingToPlateau => "Nastavenie cieľovej teploty",
        CalibrationRunState.PlateauCompleted => "Bod dokončený",
        CalibrationRunState.MovingToNextPlateau => "Presun na ďalšie plato",
        CalibrationRunState.Completed or CalibrationRunState.CompletedWithWarnings => "Kalibrácia ukončená",
        CalibrationRunState.Failed => "Kalibrácia zlyhala",
        CalibrationRunState.Aborted => "Kalibrácia zastavená",
        CalibrationRunState.AwaitingOperator => "Čaká na operátora",
        _ => "Príprava"
    };
    public string OperationDetail => _paused ? "Pokračovanie čaká na zrušenie pauzy." : !_running && _started is not null ? Now : _snapshot?.Message ?? _startupDetail;
    private string _startupDetail = "Čaká sa na prvý stav zariadení.";
    public void ReportStartup(string detail) { _startupDetail = detail; Notify(); }
    public string Now => _paused ? "Kalibrácia je pozastavená. Pokračujte tlačidlom Pauza." : _state switch
    {
        CalibrationRunState.WaitingForChamberStability => $"Čaká sa na stabilitu referencie WIKA pri cieli {Target}. Interná teplota komory je iba informatívna.",
        CalibrationRunState.StabilizingSensors when AllTargetsFinished => "Meranie peakov sa skončilo. Ukladá sa a vyhodnocuje kalibračný bod.",
        CalibrationRunState.StabilizingSensors => MeasuringCount > 0 ? $"Meria {MeasuringCount} peakov. Ostatné peaky pokračujú v stabilizácii. Namerané vzorky: {SampleSummary}." : $"Stabilizuje sa {TotalTargets} peakov. Aktuálne stabilné: {StableCount} / {TotalTargets}.",
        CalibrationRunState.MovingToPlateau => $"Komore sa nastavuje cieľ {Target}. Profilové rampy a časy sa ignorujú.",
        CalibrationRunState.PlateauCompleted => "Kalibračný bod je dokončený. Pripravuje sa ďalšie vybrané plato.",
        CalibrationRunState.Completed => "Všetky kalibračné body sú dokončené. Výsledky a export nájdete v Histórii.",
        CalibrationRunState.CompletedWithWarnings => "Beh sa skončil s upozorneniami. Pred použitím výsledkov skontrolujte diagnostiku a históriu.",
        CalibrationRunState.Failed or CalibrationRunState.AwaitingOperator or CalibrationRunState.Aborted => Alert,
        _ => _running ? "Prebieha príprava a kontrola pripojených zariadení." : "Vyberte profil, skontrolujte zapojenie a spustite kalibráciu."
    };
    public string Started => _started?.ToLocalTime().ToString("HH:mm") ?? "—";
    public string Elapsed { get; private set; } = "—";
    public string PhaseElapsed { get; private set; } = "—";
    public string PointElapsed => _snapshot is null ? "—" : Duration(_snapshot.PlateauElapsed);
    public DateTimeOffset? CurrentPlateauTraceStart { get; private set; }
    public string Eta { get; private set; } = "Po prvom bode";
    public string Finish { get; private set; } = "—";
    public string EtaBasis { get; private set; } = "Odhad sa spresní po dokončení prvého bodu alebo z historických behov tohto profilu.";
    public string LastUpdate => _lastSnapshotAt?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
    public DateTimeOffset? LastTemperatureSampleAt { get; private set; }
    private DateTimeOffset? _lastSnapshotAt;
    private bool AllTargetsFinished => _snapshot?.Targets.Count > 0 && _snapshot.Targets.All(t => t.Phase == "Done");
    public string Freshness { get; private set; } = "Čaká na dáta";
    public double PointProgress => Steps.Take(9).Count(s => s.State == "Done") * 100d / Math.Max(1, Steps.Take(9).Count(s => s.State != "Skipped"));
    private int TimelineCurrentIndex
    {
        get
        {
            int active = Steps.ToList().FindIndex(step => step.State is "Active" or "Waiting" or "Error");
            if (active >= 0) return active;
            int pending = Steps.ToList().FindIndex(step => step.State == "Pending");
            return pending >= 0 ? pending : Math.Max(0, Steps.Count - 1);
        }
    }
    private DashboardNode? TimelinePreviousNode => TimelineCurrentIndex > 0 ? Steps[TimelineCurrentIndex - 1] : null;
    private DashboardNode? TimelineCurrentNode => Steps.Count > 0 ? Steps[TimelineCurrentIndex] : null;
    private DashboardNode? TimelineNextNode => TimelineCurrentIndex + 1 < Steps.Count ? Steps[TimelineCurrentIndex + 1] : null;
    public string TimelinePreviousNumber => TimelinePreviousNode?.Number ?? "00";
    public string TimelinePreviousTitle => TimelinePreviousNode?.Title ?? "Štart kalibrácie";
    public string TimelinePreviousDetail => TimelinePreviousNode?.Detail ?? "Pripravené na spustenie.";
    public string TimelineCurrentNumber => TimelineCurrentNode?.Number ?? "—";
    public string TimelineCurrentTitle => TimelineCurrentNode?.Title ?? Phase;
    public string TimelineCurrentDetail => Now;
    public string TimelineNextNumber => TimelineNextNode?.Number ?? "✓";
    public string TimelineNextTitle => TimelineNextNode?.Title ?? "Kalibrácia dokončená";
    public string TimelineNextDetail => TimelineNextNode?.Detail ?? "Všetky naplánované kroky sú hotové.";
    public string TimelineNextTiming => TimelineNextNode?.Title switch
    {
        "Stabilita FBG" => SampleTiming(_requiredStableSamples),
        "Meranie samples" => SampleTiming(_requiredMeasurementSamples),
        _ => string.Empty,
    };

    private string SampleTiming(int samples) => _observedCycleSeconds is { } seconds
        ? $"Odber každých {_sampleAcquisitionIntervalSeconds} s · Odhad {samples} vzoriek: {Duration(TimeSpan.FromSeconds(Math.Round(samples * seconds)))} · skutočný cyklus ≈ {seconds:F1} s"
        : $"Odber každých {_sampleAcquisitionIntervalSeconds} s · Odhad {samples} vzoriek: {Duration(TimeSpan.FromSeconds(samples * _sampleAcquisitionIntervalSeconds))}";

    public void Configure(string profile, string chamber, IEnumerable<double> temperatures, bool hasReference, string rules, Guid? referenceChamberId = null, double toleranceC = 0, double maxDriftCPerMinute = 0, string? profileCode = null,
        int requiredStableSamples = 50, int requiredMeasurementSamples = 50, double maxRangePm = 5, double maxStdDevPm = 1.5,
        int sampleAcquisitionIntervalSeconds = 1,
        double maxPeakDriftPmPerMinute = 1, TimeSpan? stableDuration = null, TimeSpan? stabilityTimeout = null, TimeSpan? sensorTimeout = null,
        bool enableSetpointRamp = true, double setpointRampCPerMinute = 1,
        IReadOnlyList<CalibrationPlateauStatistics>? historicalPlateaus = null)
    {
        if (_started is not null) return;
        double[] plan = temperatures.ToArray();
        string signature = $"{profileCode}|{profile}|{chamber}|{hasReference}|{rules}|{referenceChamberId}|{Math.Abs(toleranceC)}|{maxDriftCPerMinute}|" +
            $"{requiredStableSamples}|{requiredMeasurementSamples}|{sampleAcquisitionIntervalSeconds}|{maxRangePm}|{maxStdDevPm}|{maxPeakDriftPmPerMinute}|{stableDuration}|{stabilityTimeout}|{sensorTimeout}|{enableSetpointRamp}|{setpointRampCPerMinute}|" +
            $"{string.Join(",", historicalPlateaus?.Select(item => $"{item.PlateauIndex}:{item.SampleCount}:{item.MedianDuration.Ticks}:{item.MaximumDuration.Ticks}") ?? Array.Empty<string>())}|{string.Join(",", plan)}";
        if (_planSignature == signature) return;
        _planSignature = signature;
        ProfileDescription = profile;
        string shortName = profile.Split(" · ", 2, StringSplitOptions.TrimEntries)[0];
        Profile = string.IsNullOrWhiteSpace(profileCode) ? shortName : $"{profileCode} · {shortName}";
        Chamber = chamber;
        HasReference = hasReference;
        Rules = rules;
        StabilityToleranceC = Math.Abs(toleranceC);
        _stabilityMaxDriftCPerMinute = Math.Max(0, maxDriftCPerMinute);
        _requiredStableSamples = Math.Max(2, requiredStableSamples);
        _requiredMeasurementSamples = Math.Max(2, requiredMeasurementSamples);
        _sampleAcquisitionIntervalSeconds = Math.Clamp(sampleAcquisitionIntervalSeconds, 1, 30);
        _maxRangePm = Math.Max(0, maxRangePm);
        _maxStdDevPm = Math.Max(0, maxStdDevPm);
        _maxPeakDriftPmPerMinute = Math.Max(0, maxPeakDriftPmPerMinute);
        _stableDuration = stableDuration ?? TimeSpan.Zero;
        _stabilityTimeout = stabilityTimeout ?? TimeSpan.Zero;
        _sensorTimeout = sensorTimeout ?? TimeSpan.Zero;
        _enableSetpointRamp = enableSetpointRamp;
        _setpointRampCPerMinute = Math.Clamp(Math.Abs(setpointRampCPerMinute), 0.1, 20.0);
        _plannedTemperatures = plan;
        _historicalPlateaus = (historicalPlateaus ?? Array.Empty<CalibrationPlateauStatistics>())
            .GroupBy(item => item.PlateauIndex)
            .ToDictionary(group => group.Key, group => group.First());
        ReferenceChamberId = referenceChamberId;
        Points.Clear();
        foreach (double t in plan) Points.Add(new DashboardNode($"{Points.Count + 1:00}", $"{t:F1} °C", "Čaká"));
        RefreshSteps();
        Notify();
    }
    public void Begin(DateTimeOffset now)
    {
        _startupDetail = "Čaká sa na prvý stav zariadení.";
        _started = _phaseStarted = now; _ended = null; _snapshot = null; _lastSnapshotAt = null; _observedCycleSeconds = null;
        _latestChamberTemperature = null; LastTemperatureSampleAt = null; RunId = "Pripravuje sa…";
        CurrentPlateauTraceStart = null;
        _running = true; _paused = false; _state = CalibrationRunState.Preflight; _lastWarning = "";
        Alert = "Bez hlásených upozornení"; Trend = "—"; TrendTone = "Steady"; _targetEvents.Clear(); Activity.Clear(); FbgStabilityCharts.Clear();
        foreach (var point in Points) { point.State = "Pending"; point.Detail = "Čaká"; point.Duration = null; }
        AddEvent(now, "INFO", "Kalibrácia spustená."); RefreshSteps(); Tick(now);
    }
    public void RestoreCompletedPoints(IEnumerable<CalibrationPlateauResult> completedPlateaus)
    {
        foreach (CalibrationPlateauResult plateau in completedPlateaus.OrderBy(item => item.PlateauIndex))
        {
            if (plateau.PlateauIndex < 0 || plateau.PlateauIndex >= Points.Count) continue;

            DashboardNode point = Points[plateau.PlateauIndex];
            TimeSpan duration = plateau.CompletedAt >= plateau.StartedAt
                ? plateau.CompletedAt - plateau.StartedAt
                : TimeSpan.Zero;
            bool warning = plateau.Targets.Any(target => target.Status != CalibrationTargetState.Stable);
            string completedAt = plateau.CompletedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
            point.State = warning ? "Warning" : "Done";
            point.Duration = duration;
            point.Detail = $"{(warning ? "!" : "✓")} {completedAt} · {Duration(duration)}";
            AddEvent(plateau.CompletedAt, warning ? "WARNING" : "SUCCESS",
                $"Obnovený bod {plateau.PlateauIndex + 1} bol dokončený {completedAt}; trvanie {Duration(duration)}.");
        }

        RefreshSteps();
        Notify();
    }
    public void SetRunId(string runId)
    {
        RunId = string.IsNullOrWhiteSpace(runId) ? "—" : runId;
        Notify();
    }
    public void ResetPlan() { _started = null; _planSignature = ""; }
    public void Apply(CalibrationProgressSnapshot snapshot, DateTimeOffset now)
    {
        var previous = _snapshot;
        string previousPhase = Phase;
        if (_lastSnapshotAt is { } previousUpdate && snapshot.State == CalibrationRunState.StabilizingSensors)
        {
            double seconds = (now - previousUpdate).TotalSeconds;
            if (seconds is >= 0.2 and <= 30)
                _observedCycleSeconds = _observedCycleSeconds is { } current ? current * 0.75 + seconds * 0.25 : seconds;
        }
        _lastSnapshotAt = now;
        CurrentPlateauTraceStart = snapshot.PlateauIndex >= 0
            ? now - (snapshot.PlateauElapsed < TimeSpan.Zero ? TimeSpan.Zero : snapshot.PlateauElapsed)
            : null;
        if (snapshot.ActualTemperatureC is { } actualTemperature)
        {
            _latestChamberTemperature = actualTemperature;
            LastTemperatureSampleAt = now;
        }
        if (snapshot.ActualTemperatureC is { } temperature && previous?.ActualTemperatureC is { } p)
        {
            double movement = temperature - p;
            Trend = Math.Abs(movement) < 0.01 ? "→ Drží" : movement > 0 ? "↗ Rastie" : "↘ Klesá";

            // Colour communicates whether the chamber is moving toward the current target,
            // not merely whether the numeric temperature is rising or falling.
            if (Math.Abs(snapshot.TargetTemperatureC - previous.TargetTemperatureC) > 0.001 || Math.Abs(movement) < 0.01)
            {
                TrendTone = "Steady";
            }
            else
            {
                double previousError = Math.Abs(p - snapshot.TargetTemperatureC);
                double currentError = Math.Abs(temperature - snapshot.TargetTemperatureC);
                TrendTone = currentError < previousError ? "Closer" : "Farther";
            }
        }
        if (previous?.State != snapshot.State) _phaseStarted = now;
        _state = snapshot.State;
        _snapshot = snapshot.State == CalibrationRunState.PlateauCompleted && snapshot.Targets.Count == 0 && previous?.PlateauIndex == snapshot.PlateauIndex
            ? snapshot with { Targets = previous.Targets, TemperatureStableScoreSeconds = previous.TemperatureStableScoreSeconds, RequiredTemperatureScoreSeconds = previous.RequiredTemperatureScoreSeconds, TemperatureGateOpen = previous.TemperatureGateOpen } : snapshot;
        if (previousPhase != Phase) _phaseStarted = now;
        if (snapshot.PlateauIndex >= 0 && previous?.PlateauIndex != snapshot.PlateauIndex)
        {
            _targetEvents.Clear();
            FbgStabilityCharts.Clear();
            AddEvent(now, "INFO", $"Začal sa bod {snapshot.PlateauIndex + 1} / {snapshot.PlateauCount} na {Target}.");
        }
        if (snapshot.PlateauIndex >= 0 && snapshot.PlateauIndex < Points.Count)
        {
            var point = Points[snapshot.PlateauIndex];
            if (snapshot.State == CalibrationRunState.PlateauCompleted)
            {
                bool warning = snapshot.StableTargets < snapshot.TotalTargets;
                if (point.Duration is null) AddEvent(now, warning ? "WARNING" : "SUCCESS", $"Bod {snapshot.PlateauIndex + 1} dokončený{(warning ? " s upozornením" : "")}.");
                point.State = warning ? "Warning" : "Done";
                point.Duration = snapshot.PlateauElapsed;
                point.Detail = $"{(warning ? "!" : "✓")} {Duration(snapshot.PlateauElapsed)}";
            }
            else { point.State = "Active"; point.Detail = Phase; }
        }
        foreach (var t in snapshot.Targets)
        {
            string key = $"{t.SerialNumber}|{t.Channel}|{t.PeakId}";
            FbgStabilityChartItem? chart = FbgStabilityCharts.FirstOrDefault(item => item.Identity == key);
            if (chart is null)
            {
                chart = new FbgStabilityChartItem(key);
                FbgStabilityCharts.Add(chart);
            }
            chart.Update(t, now);
            string state = $"{t.State}|{t.Phase}";
            if (_targetEvents.GetValueOrDefault(key) != state)
            {
                if (t.State == CalibrationTargetState.Stable) AddEvent(now, "SUCCESS", $"Peak {key.Replace("|", " · ")} je odmeraný.");
                else if (t.State is CalibrationTargetState.TimedOut or CalibrationTargetState.Failed or CalibrationTargetState.PeakLost or CalibrationTargetState.Disconnected or CalibrationTargetState.NoTemperatureResponse)
                {
                    AddEvent(now, "ERROR", $"Peak {key}: {t.Detail}");
                    Alert = $"Peak {key}: {t.Detail ?? t.State.ToString()}";
                    _lastWarning = Alert;
                }
                else if (t.Phase == "Measuring") AddEvent(now, "INFO", $"Peak {key.Replace("|", " · ")} je stabilný, začína meranie.");
                _targetEvents[key] = state;
            }
        }
        if (previous?.State != snapshot.State) AddEvent(now, "INFO", Now);
        RefreshSteps(); Tick(now);
    }
    public void ReportChamberTemperature(double temperature, DateTimeOffset now)
    {
        if (!double.IsFinite(temperature)) return;
        _latestChamberTemperature = temperature;
        LastTemperatureSampleAt = now;
        _lastSnapshotAt = now;
        Notify();
    }
    public void Pause(bool paused, DateTimeOffset now)
    {
        _paused = paused; AddEvent(now, "INFO", paused ? "Kalibrácia pozastavená." : "Kalibrácia pokračuje."); RefreshSteps(); Tick(now);
    }
    public void Warn(string message, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(message) || message == _lastWarning) return;
        _lastWarning = message; Alert = message; AddEvent(now, "WARNING", message); Notify();
    }
    public void ResolveWarning(string warningPrefix, string resolutionMessage, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(warningPrefix) ||
            !_lastWarning.StartsWith(warningPrefix, StringComparison.Ordinal))
        {
            return;
        }

        _lastWarning = string.Empty;
        Alert = "Bez hlásených upozornení";
        if (!string.IsNullOrWhiteSpace(resolutionMessage))
        {
            AddEvent(now, "SUCCESS", resolutionMessage);
        }
        Notify();
    }
    public void End(CalibrationRunState state, string message, DateTimeOffset now)
    {
        _state = state; _running = false; _paused = false; _ended = now;
        if (state != CalibrationRunState.Completed) { Alert = message; _lastWarning = message; }
        if (state is CalibrationRunState.Failed or CalibrationRunState.AwaitingOperator or CalibrationRunState.Aborted && _snapshot is not null && _snapshot.PlateauIndex >= 0 && _snapshot.PlateauIndex < Points.Count)
        {
            var point = Points[_snapshot.PlateauIndex];
            if (point.Duration is null) { point.State = state == CalibrationRunState.Failed ? "Error" : "Waiting"; point.Detail = Phase; }
        }
        AddEvent(now, state == CalibrationRunState.Completed ? "SUCCESS" : state == CalibrationRunState.Failed ? "ERROR" : "WARNING", message);
        RefreshSteps(); Tick(now);
    }
    public void Tick(DateTimeOffset now)
    {
        DateTimeOffset clock = _ended ?? now;
        Elapsed = _started is { } start ? Duration(clock - start) : "—";
        PhaseElapsed = _phaseStarted is { } phase ? Duration(clock - phase) : "—";
        Freshness = !_running ? (_started is null ? "Pripravené" : "Posledné dáta behu") : _lastSnapshotAt is null ? "Čaká na prvé dáta" :
            now - _lastSnapshotAt > TimeSpan.FromSeconds(15) ? "Bez nových dát > 15 s" : "Live dáta";
        UpdateEta(now);
        Notify();
    }

    private void UpdateEta(DateTimeOffset now)
    {
        Eta = "Po prvom bode";
        Finish = "—";
        EtaBasis = "Odhad sa spresní po dokončení prvého bodu alebo z historických behov tohto profilu.";
        if (_ended is not null)
        {
            Eta = "—";
            Finish = _ended.Value.ToLocalTime().ToString("HH:mm");
            EtaBasis = "Kalibrácia je ukončená; zobrazený je skutočný čas konca.";
            return;
        }
        if (_paused)
        {
            Eta = "Pozastavené";
            EtaBasis = "Počas pauzy sa predpokladaný koniec nepočíta.";
            return;
        }
        if (!_running || Points.Count == 0) return;

        double[] completedDurations = Points
            .Where(point => point.Duration.HasValue)
            .Select(point => point.Duration!.Value.TotalSeconds)
            .OrderBy(value => value)
            .ToArray();
        double? liveTypicalSeconds = completedDurations.Length == 0
            ? null
            : completedDurations[completedDurations.Length / 2];
        int currentIndex = _snapshot?.PlateauIndex ?? -1;
        bool currentIsActive = currentIndex >= 0 && _snapshot?.State != CalibrationRunState.PlateauCompleted;
        int firstRemainingIndex = currentIndex < 0 ? 0 : currentIsActive ? currentIndex : currentIndex + 1;
        double seconds = 0;
        bool usedHistory = false;

        for (int index = firstRemainingIndex; index < Points.Count; index++)
        {
            double? expected = null;
            double? upperBound = null;
            if (_historicalPlateaus.TryGetValue(index, out CalibrationPlateauStatistics? history))
            {
                expected = history.MedianDuration.TotalSeconds;
                upperBound = history.MaximumDuration.TotalSeconds;
                usedHistory = true;
            }
            else if (liveTypicalSeconds is { } liveTypical)
            {
                expected = liveTypical;
            }

            if (expected is null)
            {
                if (completedDurations.Length == 0 && _historicalPlateaus.Count == 0)
                {
                    Eta = "Po prvom bode";
                    EtaBasis = "Pre zostávajúce body zatiaľ nie je dokončený bod ani porovnateľná história.";
                }
                else
                {
                    Eta = "Neurčitý";
                    EtaBasis = "Pre niektorý zostávajúci bod nie je porovnateľná história ani spoľahlivý odhad.";
                }
                return;
            }

            if (currentIsActive && index == currentIndex)
            {
                double elapsed = Math.Max(0, _snapshot!.PlateauElapsed.TotalSeconds);
                if (elapsed >= expected.Value)
                {
                    if (upperBound is { } maximum && elapsed < maximum)
                        expected = maximum;
                    else
                    {
                        Eta = "Neurčitý";
                        EtaBasis = "Aktuálny bod už prekročil dostupný typický/historický čas; stabilitu WIKA ani FBG nemožno bezpečne predpovedať.";
                        return;
                    }
                }
                seconds += Math.Max(0, expected.Value - elapsed);
            }
            else
            {
                seconds += expected.Value;
            }
        }

        seconds += EstimateRemainingRampSeconds(currentIndex, currentIsActive);
        if (seconds <= 0)
        {
            Eta = "Dokončuje sa";
            EtaBasis = "Všetky merateľné zostávajúce kroky sú hotové; prebieha uloženie a uzavretie behu.";
            return;
        }

        Eta = "≈ " + Duration(TimeSpan.FromSeconds(seconds));
        Finish = "≈ " + now.AddSeconds(seconds).ToLocalTime().ToString("dd.MM. HH:mm");
        EtaBasis = usedHistory
            ? "Odhad používa historické mediány jednotlivých plat, aktuálny priebeh bodu a zostávajúci riadený nábeh setpointu."
            : "Odhad používa medián dokončených bodov tohto behu, aktuálny priebeh a zostávajúci riadený nábeh setpointu.";
    }

    private double EstimateRemainingRampSeconds(int currentIndex, bool currentIsActive)
    {
        if (!_enableSetpointRamp || _plannedTemperatures.Length == 0) return 0;
        double ratePerSecond = _setpointRampCPerMinute / 60d;
        double seconds = 0;
        int firstTransition = Math.Max(1, currentIndex + 1);

        if (currentIndex < 0 && ActualTemperature is { } actual)
            seconds += Math.Abs(_plannedTemperatures[0] - actual) / ratePerSecond;
        else if (currentIsActive && _state == CalibrationRunState.MovingToPlateau &&
                 currentIndex < _plannedTemperatures.Length && ActualTemperature is { } currentActual)
            seconds += Math.Abs(_plannedTemperatures[currentIndex] - currentActual) / ratePerSecond;

        for (int index = firstTransition; index < _plannedTemperatures.Length; index++)
            seconds += Math.Abs(_plannedTemperatures[index] - _plannedTemperatures[index - 1]) / ratePerSecond;
        return seconds;
    }
    private void RefreshSteps()
    {
        string cycle = _observedCycleSeconds is { } seconds
            ? $"Nastavený odber: každých {_sampleAcquisitionIntervalSeconds} s; skutočný cyklus dát trvá približne {seconds:F1} s"
            : $"Nastavený odber: každých {_sampleAcquisitionIntervalSeconds} s; skutočný cyklus sa zobrazí po spustení";
        string Estimate(int samples) => _observedCycleSeconds is { } seconds
            ? $"približne {Duration(TimeSpan.FromSeconds(samples * seconds))} pri aktuálnom cykle"
            : $"odhad {Duration(TimeSpan.FromSeconds(samples * _sampleAcquisitionIntervalSeconds))} pri nastavenom intervale";
        string[] names = { "Príprava", "Nastavenie cieľa", "Teplota komory", "WIKA referencia", "Stabilita FBG", "Meranie samples", "Vyhodnotenie", "Ďalšie plato", "Dokončenie" };
        string[] tips =
        {
            "Skontroluje vybraný profil, zapojenie, SN, dostupnosť komory, WIKA a PeakLoggera. Krok nemá pevný čas; pri chybe čaká na opravu alebo zásah operátora.",
            _enableSetpointRamp
                ? $"Aplikácia posúva setpoint plynulo najviac {_setpointRampCPerMinute:F2} °C/min. Komora sa naďalej reguluje vlastným interným snímačom; WIKA iba overí stabilitu po dosiahnutí cieľa. Profilové hold časy neurčujú dĺžku FBG kalibrácie."
                : "Plynulý nábeh je vypnutý a aplikácia nastaví cieľ plata priamo. Komora sa reguluje vlastným interným snímačom; WIKA iba overuje stabilitu.",
            "Interná sonda komory sa zobrazuje a loguje, ale neotvára ani neblokuje FBG bránu. Slúži na kontrolu správania regulátora a porovnanie s WIKA.",
            $"Stabilné skóre WIKA sa zbiera po blokoch 5 vzoriek:\n" +
            $"1. Prvá vzorka nastaví porovnávaciu základňu.\n" +
            $"2. Posledná vzorka bloku musí byť pri cieli v tolerancii ±{StabilityToleranceC:F3} °C.\n" +
            $"3. Priemerná zmena vzoriek voči základni musí zodpovedať driftu ≤ {_stabilityMaxDriftCPerMinute:F3} °C/min.\n" +
            "4. Úspešný blok pripočíta reálne uplynuté sekundy ku skóre.\n" +
            "5. Neúspešný blok odpočíta dvojnásobok času bloku (najviac po nulu) a nastaví novú základňu.\n" +
            $"Brána sa otvorí po potvrdenom skóre {Duration(_stableDuration)}. Medzi uzavretými blokmi sa čas zobrazuje priebežne, ale potvrdí ho až celý blok. Timeout: {Duration(_stabilityTimeout)}.",
            $"Každý vybraný peak má vlastný detektor. Potrebuje {_requiredStableSamples} vzoriek, range ≤ {_maxRangePm:F3} pm, σ ≤ {_maxStdDevPm:F3} pm a drift ≤ {_maxPeakDriftPmPerMinute:F3} pm/min. Peaky sa kontrolujú paralelne; {Estimate(_requiredStableSamples)}. Timeout peaku: {Duration(_sensorTimeout)}. {cycle}.",
            $"Po potvrdení stability sa stabilizačné vzorky nepoužijú ako výsledok. Každý peak zbiera {_requiredMeasurementSamples} nových finálnych vzoriek paralelne; {Estimate(_requiredMeasurementSamples)}. Ak peak prestane spĺňať limity, rozpracované meracie vzorky sa zahodia a vráti sa do stabilizácie. {cycle}.",
            "Z finálnych meracích vzoriek každého peaku vypočíta priemer, medián, minimum, maximum, range, štandardnú odchýlku a drift; následne uloží bod, raw samples a diagnostiku.",
            "Po dokončení všetkých vybraných peakov uloží checkpoint a nastaví cieľ nasledujúceho vybraného plata. Ak žiadne nezostáva, prejde na dokončenie.",
            "Uzavrie beh, uloží súhrn, históriu a exporty. Výsledný stav môže byť dokončené alebo dokončené s upozorneniami."
        };
        if (Steps.Count == 0)
        {
            for (int i = 0; i < names.Length; i++) Steps.Add(new DashboardNode($"{i + 1:00}", names[i], tips[i]));
        }
        else
        {
            for (int i = 0; i < Steps.Count && i < tips.Length; i++) Steps[i].Detail = tips[i];
        }
        var effectiveState = _state is CalibrationRunState.Failed or CalibrationRunState.AwaitingOperator or CalibrationRunState.Aborted ? _snapshot?.State ?? _state : _state;
        int phase = effectiveState switch
        {
            CalibrationRunState.MovingToPlateau => 1,
            CalibrationRunState.WaitingForChamberStability => 3,
            CalibrationRunState.StabilizingSensors when AllTargetsFinished => 6,
            CalibrationRunState.StabilizingSensors => 4,
            CalibrationRunState.PlateauCompleted => 7,
            CalibrationRunState.MovingToNextPlateau => 7,
            CalibrationRunState.Completed or CalibrationRunState.CompletedWithWarnings => 9,
            _ => 0
        };
        for (int i = 0; i < Steps.Count; i++) Steps[i].State = i < phase ? "Done" : i == phase && _running ? "Active" : "Pending";
        if (phase == 3) Steps[3].State = "Waiting";
        if (phase == 4 && MeasuringCount > 0) Steps[5].State = "Active";
        if (!HasReference) Steps[3].State = "Skipped";
        if (_paused) foreach (var step in Steps.Where(s => s.State is "Active" or "Waiting")) step.State = "Waiting";
        if (_state == CalibrationRunState.Failed) Steps[Math.Min(phase, Steps.Count - 1)].State = "Error";
        if (_state == CalibrationRunState.AwaitingOperator) Steps[Math.Min(phase, Steps.Count - 1)].State = "Waiting";
    }
    private void AddEvent(DateTimeOffset now, string level, string message)
    {
        Activity.Insert(0, new DashboardEvent(now.ToLocalTime().ToString("HH:mm:ss"), level, message));
        while (Activity.Count > 250) Activity.RemoveAt(Activity.Count - 1);
    }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public static string Duration(TimeSpan time) => time.TotalHours >= 1 ? $"{(int)time.TotalHours} h {time.Minutes:00} min" : $"{Math.Max(0, (int)time.TotalMinutes)} min {Math.Max(0, time.Seconds):00} s";
}
public sealed class DashboardNode : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public DashboardNode(string number, string title, string detail) { Number = number; Title = title; _detail = detail; }
    public string Number { get; }
    public string Title { get; }
    private string _state = "Pending", _detail;
    public string State { get => _state; set { _state = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); } }
    public string Detail { get => _detail; set { _detail = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail))); } }
    public string Badge => State switch { "Done" => "✓ DONE", "Active" => "● RUNNING", "Waiting" => "Ⅱ WAITING", "Error" => "! ERROR", "Warning" => "! DONE", "Skipped" => "— N/A", _ => "○ PENDING" };
    public TimeSpan? Duration { get; set; }
}

public sealed class FbgStabilityChartItem : INotifyPropertyChanged
{
    private readonly List<(DateTimeOffset Time, double Wavelength)> _stabilitySamples = new();
    private readonly List<(DateTimeOffset Time, double Wavelength)> _measurementSamples = new();
    private int _lastMeasurementCount;
    private CalibrationTargetProgress? _progress;

    public FbgStabilityChartItem(string identity)
    {
        Identity = identity;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Identity { get; }
    public string Title => $"SN {_progress?.SerialNumber ?? "—"} · {_progress?.Channel ?? "—"}/{_progress?.PeakId ?? "—"}";
    public string Wavelength => _progress?.CurrentWavelengthNm is { } value ? $"{value:F6} nm" : "—";
    public string Samples => _progress is null ? "Vzorky —" : $"Vzorky {_progress.StabilitySamples} / {_progress.RequiredStabilitySamples}";
    public double Progress => _progress?.RequiredStabilitySamples > 0
        ? Math.Clamp(100d * _progress.StabilitySamples / _progress.RequiredStabilitySamples, 0, 100)
        : 0;
    public string Range => Metric("Rozsah", _progress?.RangePm, _progress?.RangeLimitPm, "pm");
    public string StandardDeviation => Metric("σ", _progress?.StandardDeviationPm, _progress?.StdDevLimitPm, "pm");
    public string Drift => Metric("Drift", _progress?.DriftPmPerMinute is { } value ? Math.Abs(value) : null, _progress?.DriftLimitPmPerMinute, "pm/min");
    public string State => _progress?.Phase switch
    {
        "Measuring" => "MERANIE",
        _ when _progress?.State == CalibrationTargetState.Stable => "HOTOVO",
        _ => "STABILIZÁCIA",
    };
    public string StateBrush => _progress?.State == CalibrationTargetState.Stable || _progress?.Phase == "Measuring"
        ? "#3CB371"
        : "#DAA520";
    public string MeasurementSamples => _progress is null ? "Finálne vzorky —" : $"Finálne vzorky {_progress.MeasurementSamples} / {_progress.RequiredMeasurementSamples}";
    public double MeasurementProgress => _progress?.RequiredMeasurementSamples > 0
        ? Math.Clamp(100d * _progress.MeasurementSamples / _progress.RequiredMeasurementSamples, 0, 100)
        : 0;
    public string MeasurementState => _progress?.Phase switch
    {
        "Measuring" => "MERANIE",
        "Done" when _progress.State == CalibrationTargetState.Stable => "HOTOVO",
        _ => "ČAKÁ",
    };
    public string MeasurementStateBrush => _progress?.Phase == "Measuring" ||
        _progress is { Phase: "Done", State: CalibrationTargetState.Stable }
        ? "#3CB371"
        : "#DAA520";
    public IReadOnlyList<FbgStabilitySample> ChartPoints => Project(_stabilitySamples);
    public IReadOnlyList<FbgStabilitySample> MeasurementChartPoints => Project(_measurementSamples);

    public void Update(CalibrationTargetProgress progress, DateTimeOffset now)
    {
        _progress = progress;
        if (progress.Phase == "Stabilizing" &&
            progress.CurrentWavelengthNm is { } wavelength && double.IsFinite(wavelength))
        {
            AddBounded(_stabilitySamples, now, wavelength);
        }

        if (progress.MeasurementSamples < _lastMeasurementCount)
            _measurementSamples.Clear();
        if (progress.MeasurementSamples > _lastMeasurementCount &&
            progress.CurrentWavelengthNm is { } measured && double.IsFinite(measured))
        {
            AddBounded(_measurementSamples, now, measured);
        }
        _lastMeasurementCount = progress.MeasurementSamples;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private static void AddBounded(List<(DateTimeOffset Time, double Wavelength)> samples, DateTimeOffset now, double wavelength)
    {
        samples.Add((now, wavelength));
        if (samples.Count > 180) samples.RemoveRange(0, samples.Count - 180);
    }

    private static IReadOnlyList<FbgStabilitySample> Project(List<(DateTimeOffset Time, double Wavelength)> samples)
    {
        if (samples.Count == 0) return Array.Empty<FbgStabilitySample>();
        DateTimeOffset origin = samples[0].Time;
        return samples.Select(sample => new FbgStabilitySample(
            (sample.Time - origin).TotalMinutes,
            sample.Wavelength)).ToArray();
    }

    private static string Metric(string name, double? value, double? limit, string unit) =>
        value is null || limit is null ? $"{name}: čaká na dáta" : $"{name}: {value:F3} / ≤ {limit:F3} {unit}";
}
public sealed record FbgStabilitySample(double Minutes, double WavelengthNm);
public sealed record DashboardEvent(string Time, string Level, string Message);
