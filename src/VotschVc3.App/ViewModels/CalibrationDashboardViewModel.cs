using System.Collections.ObjectModel;
using System.ComponentModel;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.ViewModels;

public sealed record DashboardTemperatureSample(DateTimeOffset Timestamp, double TemperatureC);
public sealed record DashboardStabilityScoreSample(DateTimeOffset Timestamp, int ScoreSeconds, int RequiredSeconds);

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
    private TimeSpan _stabilityTimeout = TimeSpan.FromHours(1);
    private TimeSpan _stabilityExtensionStep = TimeSpan.FromMinutes(15);
    private TimeSpan _maxAutomaticStabilityExtension = TimeSpan.FromHours(1);
    private TimeSpan _sensorTimeout = TimeSpan.FromMinutes(60);
    private bool _enableSetpointRamp = true;
    private double _setpointRampCPerMinute = 1;
    private double _finalConditioningTemperatureC = 25;
    private double[] _plannedTemperatures = Array.Empty<double>();
    private IReadOnlyDictionary<int, CalibrationPlateauStatistics> _historicalPlateaus =
        new Dictionary<int, CalibrationPlateauStatistics>();
    private double? _observedCycleSeconds;
    private readonly List<DashboardTemperatureSample> _chamberTemperatureTrace = new();
    private readonly List<DashboardStabilityScoreSample> _wikaStabilityScoreTrace = new();
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
    public string StateLabel => _paused ? "Pozastavené" : _state switch
    {
        CalibrationRunState.Completed => "Dokončené",
        CalibrationRunState.CompletedWithWarnings => "Dokončené s upozorneniami",
        CalibrationRunState.Failed => "Chyba",
        CalibrationRunState.Aborted => "Zastavené",
        CalibrationRunState.AwaitingOperator => "Čaká na rozhodnutie operátora",
        CalibrationRunState.WaitingForChamberStability => "Čaká na stabilitu",
        CalibrationRunState.FinalConditioning => "Temperovanie 25 °C",
        CalibrationRunState.StabilizingSensors when MeasuringCount > 0 => "Meria",
        _ when _running => "Prebieha",
        _ => "Pripravené"
    };
    public string Tone => _paused || _state is CalibrationRunState.AwaitingOperator or CalibrationRunState.CompletedWithWarnings or CalibrationRunState.Aborted ? "Waiting" :
        _state == CalibrationRunState.Failed ? "Error" : _state == CalibrationRunState.Completed ? "Done" :
        _state == CalibrationRunState.WaitingForChamberStability ? "Waiting" : _running ? "Active" : "Pending";
    public int CompletedPoints => Points.Count(p => p.State is "Done" or "Warning");
    public int SuccessfulPoints => Points.Count(p => p.State == "Done");
    public int UnconfirmedPoints => Points.Count(p => p.State == "Warning");
    public int RemainingPoints => Math.Max(0, Points.Count - CompletedPoints);
    public double OverallProgress => Points.Count == 0 ? 0 : 100d * CompletedPoints / Points.Count;
    public string ProgressLabel => $"{OverallProgress:F0} % · {CompletedPoints} / {Points.Count} bodov ukončených\nÚspešné {SuccessfulPoints} · nepotvrdené/neúspešné {UnconfirmedPoints} · zostáva {RemainingPoints}";
    public string Plateau => _state == CalibrationRunState.FinalConditioning
        ? $"Záverečné temperovanie {_finalConditioningTemperatureC:F1} °C"
        : _snapshot?.PlateauIndex < 0 ? "Príprava kalibračných bodov" : _snapshot is null ? $"Plán · {Points.Count} bodov" : $"Plato {_snapshot.PlateauIndex + 1} / {_snapshot.PlateauCount}";
    public int CurrentPlateauIndex => _snapshot?.PlateauIndex ?? -1;
    public string Target => _snapshot is null ? "—" : $"{_snapshot.TargetTemperatureC:F1} °C";
    public double? TargetTemperatureC => _snapshot?.TargetTemperatureC;
    public double? ActualTemperature => _snapshot?.ActualTemperatureC ?? _latestChamberTemperature;
    public IReadOnlyList<DashboardTemperatureSample> ChamberTemperatureTrace => _chamberTemperatureTrace.ToArray();
    public string ChamberTraceSamplesLabel => _chamberTemperatureTrace.Count == 0
        ? "Čaká sa na vzorky komory."
        : $"Vzorky komory: {_chamberTemperatureTrace.Count} · posledná: {_chamberTemperatureTrace[^1].Timestamp.ToLocalTime():HH:mm:ss}";
    public IReadOnlyList<DashboardStabilityScoreSample> WikaStabilityScoreTrace => _wikaStabilityScoreTrace.ToArray();
    public string Actual => ActualTemperature is { } t ? $"{t:F2} °C" : "—";
    public string Reference => _snapshot?.ReferenceTemperatureC is { } t ? $"{t:F3} °C" : "—";
    public string Delta => _snapshot?.ActualTemperatureC is { } t ? $"Δ {t - _snapshot.TargetTemperatureC:+0.00;-0.00;0.00} °C" : "Čaká na údaje";
    public string Trend { get; private set; } = "—";
    public string TrendTone { get; private set; } = "Steady";
    public bool HasReference { get; private set; }
    public bool CanForceTemperatureGate => _running && !WaitingForChamber && _snapshot?.Message.Contains("KOMORA ČAKÁ") != true &&
        _state == CalibrationRunState.WaitingForChamberStability &&
        (!HasReference || _snapshot?.ReferenceTemperatureC is not null);
    public string ReferenceStatus => !HasReference ? "Bez externej referencie" : _snapshot?.ReferenceTemperatureC is null ? "Čaká na vzorku WIKA" : "Posledná vzorka WIKA";
    public string ReferenceStatusHelp =>
        "Posledná vzorka WIKA je najnovšia úspešne načítaná teplota z referenčného teplomera WIKA CTH7000; veľká hodnota nad týmto textom je jej aktuálna hodnota. " +
        "Počas kalibrácie sa pravidelne obnovuje a používa sa na výpočet odchýlky od cieľa aj driftu. " +
        "Jedna vzorka sama osebe nepotvrdzuje stabilitu — aplikácia vyhodnocuje po sebe idúce bloky vzoriek a stabilný čas začne pribúdať iba vtedy, keď súčasne vyhovuje odchýlka aj drift. " +
        "Ak sa novú vzorku nepodarí načítať, zobrazí sa čakanie na WIKA a FBG stabilizácia sa nespustí.";
    public string ReferenceToleranceLabel => WaitingForChamber ? "Odchýlka · čaká na komoru" : _snapshot?.ReferenceTemperatureC is not { } reference
        ? "Odchýlka od cieľa · čaká na vzorku"
        : $"Odchýlka |Δ| {Math.Abs(reference - _snapshot.TargetTemperatureC):F3} / ≤ {StabilityToleranceC:F3} °C";
    public string ReferenceToleranceHelp =>
        "Pri zapnutej vstupnej kontrole začne vyhodnocovanie až po splnení všetkých podmienok komory. Predchádzajúce vzorky WIKA sa do stability nezapočítajú. " +
        $"WIKA musí byť pri cieľovej teplote {Target} v povolenej odchýlke ±{StabilityToleranceC:F3} °C. " +
        "Ak je odchýlka väčšia, stabilný čas sa nezbiera a FBG stabilizácia sa ešte nespustí. " +
        "Komora sa reguluje vlastným interným regulátorom; WIKA slúži iba ako autoritatívna referencia stability.";
    public string ReferenceToleranceTone => WaitingForChamber ? "Pending" : _snapshot?.ReferenceTemperatureC is { } reference &&
        Math.Abs(reference - _snapshot.TargetTemperatureC) <= StabilityToleranceC ? "Done" : "Waiting";
    public string ReferenceDriftLabel => WaitingForChamber ? "Drift · čaká na komoru" : _snapshot?.TemperatureDriftCPerMinute is not { } drift
        ? "Drift · čaká na blok 5 vzoriek"
        : $"Drift {Math.Abs(drift):F3} / ≤ {_stabilityMaxDriftCPerMinute:F3} °C/min";
    public string ReferenceDriftHelp =>
        $"Drift vyjadruje lineárnu rýchlosť zmeny WIKA teploty podľa skutočných časov vzoriek. " +
        $"Blok vyhovuje, iba ak prísnejší drift z celého okna alebo posledných 120 sekúnd neprekročí {_stabilityMaxDriftCPerMinute:F3} °C/min a posledná vzorka je v tolerancii cieľa.";
    public string ReferenceDriftTone => WaitingForChamber ? "Pending" : _snapshot?.TemperatureDriftCPerMinute is { } drift &&
        (_stabilityMaxDriftCPerMinute <= 0 || Math.Abs(drift) <= _stabilityMaxDriftCPerMinute) ? "Done" : "Waiting";
    public string ReferenceTimeLabel => WaitingForChamber ? "Stabilný čas · začne po ustálení komory" : $"Stabilný čas {TemperatureStableScoreSeconds} / {_snapshot?.RequiredTemperatureScoreSeconds ?? 0} s";
    public string ReferenceRangeLabel => ReferenceMetricLabel("Rozsah", _snapshot?.ReferenceMetrics?.Range, _snapshot?.ReferenceRangeLimit);
    public string ReferenceStdDevLabel => ReferenceMetricLabel("σ", _snapshot?.ReferenceMetrics?.StandardDeviation, _snapshot?.ReferenceStdDevLimit);
    private string ReferenceMetricLabel(string name, double? value, double? limit) =>
        WaitingForChamber ? $"{name} · čaká na komoru" : value is null ? $"{name} · čaká na vzorky" : limit <= 0 ? $"{name} {value:F4} °C · limit vypnutý" : $"{name} {value:F4} / ≤ {limit:F4} °C";
    public string ReferenceRangeTone => WaitingForChamber ? "Pending" : _snapshot?.ReferenceMetrics is { } metrics &&
        (_snapshot.ReferenceRangeLimit <= 0 || metrics.Range <= _snapshot.ReferenceRangeLimit) ? "Done" : "Waiting";
    public string ReferenceStdDevTone => WaitingForChamber ? "Pending" : _snapshot?.ReferenceMetrics is { } metrics &&
        (_snapshot.ReferenceStdDevLimit <= 0 || metrics.StandardDeviation <= _snapshot.ReferenceStdDevLimit) ? "Done" : "Waiting";
    public string ReferenceResetLabel => WaitingForChamber ? "Vyhodnocovanie začne po ustálení komory. WIKA sa zatiaľ iba meria a zaznamenáva." : _snapshot?.ReferenceResetReason is { } reason ? $"Posledný reset: {reason}" : "Čas stability zatiaľ nebol resetovaný.";
    public string ReferenceTimeHelp =>
        $"Všetky vzorky musia súvisle počas {Duration(_stableDuration)} spĺňať toleranciu ±{StabilityToleranceC:F3} °C, rozsah, σ a drift. " +
        "Prekročenie ktorejkoľvek podmienky vynuluje celý čas; posledná platná vzorka začne nové okno. Dôvod posledného resetu zostáva na karte. " +
        $"Základný limit čakania je {Duration(_stabilityTimeout)}, nejde o povinnú výdrž. Pri platných dátach sa predlžuje po {Duration(_stabilityExtensionStep)}, najviac o {Duration(_maxAutomaticStabilityExtension)}. " +
        "Operátorský dohľad vyžaduje rozhodnutie. Po vyčerpaní limitu sa bod odloží na jeden neskorší pokus; druhý neúspech zastaví automatický postup; nevyhovujúci bod sa nikdy automaticky neprijme.";
    public string ReferenceTimeTone => WaitingForChamber ? "Pending" : _snapshot?.TemperatureGateOpen == true ? "Done" : "Waiting";
    public double TemperatureProgress => WaitingForChamber ? 0 : _snapshot?.RequiredTemperatureScoreSeconds is > 0 ? Math.Clamp(100d * (_snapshot.TemperatureStableScoreSeconds ?? 0) / _snapshot.RequiredTemperatureScoreSeconds.Value, 0, 100) : 0;
    public int TemperatureStableScoreSeconds => WaitingForChamber ? 0 : _snapshot?.TemperatureStableScoreSeconds ?? 0;
    public DateTimeOffset? ReferenceStabilityStartedAt => WaitingForChamber ? null : _snapshot?.ReferenceEvaluationStartedAt;
    private TimeSpan TemperatureSettlingBaseLimit => _snapshot?.TemperatureSettlingBaseLimit ?? _stabilityTimeout;
    private TimeSpan AutomaticTemperatureExtensionUsed => _snapshot?.AutomaticTemperatureExtensionUsed ?? TimeSpan.Zero;
    private TimeSpan MaximumAutomaticTemperatureExtension => _snapshot?.MaximumAutomaticTemperatureExtension ?? _maxAutomaticStabilityExtension;
    private TimeSpan TemperatureSettlingElapsed => _snapshot?.TemperatureSettlingElapsed ?? TimeSpan.Zero;
    private TimeSpan CurrentTemperatureSettlingLimit => TemperatureSettlingBaseLimit + AutomaticTemperatureExtensionUsed;
    public string ReferenceSettlingLimitLabel =>
        $"Aktuálny limit plata: {Duration(CurrentTemperatureSettlingLimit)} · uplynulo {Duration(TemperatureSettlingElapsed)} · zostáva {Duration(RemainingTemperatureSettlingTime)}";
    public string ReferenceSettlingLimitBreakdown =>
        $"Základ {Duration(TemperatureSettlingBaseLimit)} · automaticky +{Duration(AutomaticTemperatureExtensionUsed)} / {Duration(MaximumAutomaticTemperatureExtension)}";
    public string ReferenceSettlingLimitHelp =>
        "Limit platí pre čakanie na ustálenie aktuálneho plata po skončení minimálneho času profilu. " +
        $"Začína na {Duration(TemperatureSettlingBaseLimit)}. Aplikácia môže pri platných dátach automaticky pridať po {Duration(_stabilityExtensionStep)}, " +
        $"najviac spolu {Duration(MaximumAutomaticTemperatureExtension)}. " +
        "Stabilné skóre je samostatná podmienka a predĺžením sa nevynuluje.";
    private TimeSpan RemainingTemperatureSettlingTime => CurrentTemperatureSettlingLimit > TemperatureSettlingElapsed
        ? CurrentTemperatureSettlingLimit - TemperatureSettlingElapsed
        : TimeSpan.Zero;
    public string TemperatureScore => _state is CalibrationRunState.Preflight or CalibrationRunState.Preparing or CalibrationRunState.MovingToPlateau ? "Po nastavení cieľa sa začne vyhodnocovať výhradne WIKA referencia." : _snapshot?.TemperatureStableScoreSeconds is { } score ? $"Skóre stability WIKA {score} / {_snapshot.RequiredTemperatureScoreSeconds} s" : "Čaká na skóre stability WIKA";
    public string TemperatureStatus => _snapshot is null || _state is CalibrationRunState.Preflight or CalibrationRunState.Preparing or CalibrationRunState.MovingToPlateau ? "Stabilita WIKA sa ešte nevyhodnocuje" : _snapshot?.TemperatureGateOpen == true ? "✓ SPLNENÉ · WIKA referencia potvrdená" : "ČAKÁ · Stabilita WIKA";
    public int TotalTargets => _snapshot?.TotalTargets ?? 0;
    public int StableCount => _snapshot?.Targets.Count(t => t.State == CalibrationTargetState.Stable || t.Phase == "Measuring") ?? 0;
    public int DoneCount => _snapshot?.Targets.Count(t => t.State is CalibrationTargetState.Stable or CalibrationTargetState.Overridden or CalibrationTargetState.CompletedWithStabilityWarning) ?? 0;
    public int IdentitySkippedCount => _snapshot?.Targets.Count(t => t.State == CalibrationTargetState.SkippedIdentityUncertain) ?? 0;
    public int MeasuringCount => _snapshot?.Targets.Count(t => t.Phase is "Measuring" or "MeasuringWithStabilityWarning") ?? 0;
    public int WarningMeasuringCount => _snapshot?.Targets.Count(t => t.Phase == "MeasuringWithStabilityWarning") ?? 0;
    public string PeakSummary => $"{StableCount} / {TotalTargets} prešlo stabilitou";
    public string SensorDeadlineHelp =>
        $"Dynamický základ je ({_requiredStableSamples} + {_requiredMeasurementSamples}) × interval odberu + 20 % rezerva, " +
        $"najmenej uložený limit {Duration(_sensorTimeout)}, najviac 90 min. " +
        $"Pri aktuálnom/nastavenom cykle vychádza {Duration(SensorTimeoutBudget.BaseLimit(_requiredStableSamples, _requiredMeasurementSamples, TimeSpan.FromSeconds(Math.Max(_sampleAcquisitionIntervalSeconds, _observedCycleSeconds ?? 0)), _sensorTimeout))}. " +
        "Reálny interval sa vyhodnocuje pre každý peak samostatne; jeho vlastný uložený limit môže byť odlišný. " +
        "Po vyčerpaní základu sa pridá najviac 10 min iba pri platných finálnych vzorkách z posledných 10 min alebo pri zlepšení najhoršieho pomeru range/σ/drift k limitu aspoň o 10 % medzi úplnými oknami v posledných 10 min. " +
        "Pevný strop je 90 min od prvého otvorenia FBG fázy na plate. Reset, zmena nastavení ani strata stability WIKA čas nevynuluje. " +
        "Bez pokroku alebo po strope: stabilita nepotvrdená / meranie nedokončené. Po limite nasleduje ohraničený odber finálnych vzoriek bez stabilnej wavelength. Výsledky sa vyhodnotia s problémovým označením. " +
        "Pri neistej identite (prekrytie, výpadok, nejednoznačné priradenie) sa bod dotknutého kanála automaticky vynechá bez finálneho odberu a bez dialógu operátora. " +
        "Po dokončení ostatných kanálov pokračuje ďalší bod. Identita sa po návrate peakov automaticky nepovažuje za obnovenú; dotknutý kanál zostáva vyradený do konca behu. Komora nedostáva STOP z dôvodu identity.";
    public string PeakDetail => $"{MeasuringCount} vo finálnom meraní{(WarningMeasuringCount > 0 ? $" · {WarningMeasuringCount} po timeout-e" : string.Empty)} · {DoneCount} úplne dokončených" +
        (IdentitySkippedCount > 0 ? $" · {IdentitySkippedCount} vynechaných pre neistú identitu" : string.Empty);
    public string PeakStabilityCriteria =>
        $"{_requiredStableSamples} vzoriek · range ≤ {_maxRangePm:F3} pm · σ ≤ {_maxStdDevPm:F3} pm · drift ≤ {_maxPeakDriftPmPerMinute:F3} pm/min";
    public string PeakStabilityCriteriaHelp =>
        "Každý peak musí v jednom spoločnom rolling okne splniť všetky štyri podmienky súčasne: požadovaný počet vzoriek, maximálny rozsah, smerodajnú odchýlku a absolútny drift. " +
        "Ak niektorá podmienka nevyhovie, okno sa zahodí a stabilizácia daného peaku začne od 0. Zobrazené hodnoty sú aktuálne nastavenia tejto kalibrácie.";
    public double StabilityProgress => TotalTargets == 0 ? 0 : 100d * StableCount / TotalTargets;
    private DateTimeOffset _timerNow;
    private DateTimeOffset? _lastFbgSampleAt;
    private double SampleCycleSeconds => Math.Max(1, Math.Max(_sampleAcquisitionIntervalSeconds, _observedCycleSeconds ?? 0));
    private bool SamplingActive => _running && !_paused && _state == CalibrationRunState.StabilizingSensors;
    private double SampleAge => _lastFbgSampleAt is { } at ? Math.Max(0, (_timerNow - at).TotalSeconds) : 0;
    public double SampleIntervalProgress => SamplingActive && _lastFbgSampleAt is not null ? Math.Clamp(100 * SampleAge / SampleCycleSeconds, 0, 100) : 0;
    public string NextSampleCountdown => _paused ? "Odber pozastavený" : !SamplingActive ? "Odber neprebieha – čaká na podmienky" :
        _lastFbgSampleAt is null ? "Čaká na prvú vzorku" : SampleAge >= SampleCycleSeconds ? "Čaká na ďalšiu vzorku…" :
        $"Ďalšia vzorka približne o {Duration(TimeSpan.FromSeconds(Math.Ceiling(SampleCycleSeconds - SampleAge)))}";
    public bool StabilitySamplingActive => SamplingActive && TotalTargets > StableCount + WarningMeasuringCount;
    public string StabilityCountdown => TotalTargets > 0 && StableCount >= TotalTargets ? "Stabilita FBG splnená" : NextSampleCountdown;
    public bool MeasurementSamplingActive => SamplingActive && MeasuringCount > 0;
    public double MeasurementIntervalProgress => MeasurementSamplingActive ? SampleIntervalProgress : 0;
    public string MeasurementCountdown => MeasurementSamplingActive ? NextSampleCountdown : "Čaká na stabilitu FBG";
    public string SampleCadence => $"Interval odberu {_sampleAcquisitionIntervalSeconds:0.#} s · cyklus ≈ {SampleCycleSeconds:0.#} s";
    public string StabilitySampleEstimate => SampleWindowEstimate(false);
    public string MeasurementSampleEstimate => SampleWindowEstimate(true);
    private string SampleWindowEstimate(bool measurement)
    {
        int configured = measurement ? _requiredMeasurementSamples : _requiredStableSamples;
        int remaining = _snapshot?.Targets.Select(t => measurement
            ? Math.Max(0, t.RequiredMeasurementSamples - t.MeasurementSamples)
            : t.Phase is "Measuring" or "MeasuringWithStabilityWarning" or "Done" ? 0 : Math.Max(0, t.RequiredStabilitySamples - t.StabilitySamples)).DefaultIfEmpty(configured).Max() ?? configured;
        string total = Duration(TimeSpan.FromSeconds(configured * SampleCycleSeconds));
        string rest = Duration(TimeSpan.FromSeconds(remaining * SampleCycleSeconds));
        return $"{configured} vzoriek / peak ≈ {total} · zostáva odber ≈ {rest}. " +
            (measurement ? "Peaky sa merajú súbežne; čakanie na stabilitu je navyše." : "Odhad naplnenia okna; nestabilita môže čas predĺžiť.");
    }
    public int Samples => _snapshot?.Targets.Sum(t => t.MeasurementSamples) ?? 0;
    public int RequiredSamples => _snapshot?.Targets.Sum(t => t.RequiredMeasurementSamples) ?? 0;
    public string SampleSummary => $"{Samples} / {RequiredSamples}";
    public double SampleProgress => RequiredSamples == 0 ? 0 : Math.Clamp(100d * Samples / RequiredSamples, 0, 100);
    private bool RunStoppedWithError => _state is CalibrationRunState.Failed or CalibrationRunState.AwaitingOperator or CalibrationRunState.Aborted;
    private bool PointFinished => AllTargetsFinished || _state is CalibrationRunState.PlateauCompleted or CalibrationRunState.MovingToNextPlateau or CalibrationRunState.Completed or CalibrationRunState.CompletedWithWarnings;
    private ChamberEntryStatus? ChamberEntry => _snapshot?.ChamberEntry;
    private bool WaitingForChamber => ChamberEntry is { Enabled: true, IsOpen: false };
    public string ChamberEntryDetail => ChamberEntry is null ? "Čaká na údaje vstupnej kontroly komory."
        : !ChamberEntry.Enabled ? "Vstupná kontrola vypnutá · komora sa iba monitoruje."
        : ChamberEntry.IsOpen ? $"Vstupná kontrola splnená. Hodnoty pri potvrdení {ChamberEntry.EvaluatedAt.ToLocalTime():HH:mm:ss}; ďalej rozhoduje WIKA."
        : "Vstupná kontrola zapnutá · čaká na ustálenie komory pred WIKA.";
    public string ChamberCardState => RunStoppedWithError ? "! ZASTAVENÉ" : ChamberEntry is null ? "○ ČAKÁ" : !ChamberEntry.Enabled ? "● MONITOROVANIE" : ChamberEntry.IsOpen ? "✓ SPLNENÉ" : "Ⅱ ČAKÁ";
    public string ChamberCardTone => RunStoppedWithError ? "Error" : ChamberEntry is null ? "Pending" : !ChamberEntry.Enabled ? "Active" : ChamberEntry.IsOpen ? "Done" : "Waiting";
    private string ChamberCriterion(double? value, double? limit, string name, string unit) =>
        ChamberEntry is null ? $"{name} · čaká na údaje" : !ChamberEntry.Enabled ? $"{name} · vypnuté"
        : $"{name} {(value is { } v ? v.ToString("F3") : "—")} / ≤ {limit:F3} {unit}";
    private string ChamberTone(double? value, double? limit) => ChamberEntry is not { Enabled: true } ? "Pending" : value is null ? "Waiting" : value <= limit ? "Done" : "Waiting";
    public string ChamberToleranceLabel => ChamberCriterion(ChamberEntry?.DeviationC, ChamberEntry?.ToleranceC, "Odchýlka |Δ|", "°C");
    public string ChamberRangeLabel => ChamberCriterion(ChamberEntry?.RangeC, ChamberEntry?.RangeLimitC, "Rozsah", "°C");
    public string ChamberDriftLabel => ChamberCriterion(ChamberEntry?.DriftCPerMinute, ChamberEntry?.DriftLimitCPerMinute, "Drift", "°C/min");
    public string ChamberTimeLabel => ChamberEntry is null ? "Časové okno · čaká na údaje" : !ChamberEntry.Enabled ? "Časové okno · vypnuté" : $"Časové okno {ChamberEntry.WindowSeconds:F0} / {ChamberEntry.RequiredSeconds:F0} s";
    public string ChamberToleranceTone => ChamberTone(ChamberEntry?.DeviationC, ChamberEntry?.ToleranceC);
    public string ChamberRangeTone => ChamberTone(ChamberEntry?.RangeC, ChamberEntry?.RangeLimitC);
    public string ChamberDriftTone => ChamberTone(ChamberEntry?.DriftCPerMinute, ChamberEntry?.DriftLimitCPerMinute);
    public string ChamberTimeTone => ChamberEntry is not { Enabled: true } ? "Pending" : ChamberEntry.WindowSeconds >= ChamberEntry.RequiredSeconds ? "Done" : "Waiting";
    public string ChamberToleranceHelp => "Absolútna odchýlka internej teploty komory od cieľa. Prekročenie tolerancie vymaže zbierané okno. Ide iba o podmienku merania, nie zásah do regulácie.";
    public string ChamberRangeHelp => "Rozdiel maxima a minima v aktuálnom časovom okne komory. Na výpočet treba aspoň dve vzorky. Všetky štyri podmienky musia vyhovieť súčasne.";
    public string ChamberDriftHelp => "Absolútna rýchlosť zmeny teploty vypočítaná lineárnou regresiou zo vzoriek v okne, v °C/min. Jedna vzorka nestačí.";
    public string ChamberTimeHelp => "Dĺžka zozbieraného okna v tolerancii. Výpadok vzoriek okno resetuje. Po splnení času, rozsahu a driftu sa vstupná kontrola pre toto plato potvrdí a začne nové okno WIKA. Zobrazené hodnoty sa potom uchovajú ako doklad potvrdenia.";
    public string ReferenceCardState => WaitingForChamber ? "○ ČAKÁ NA KOMORU" : RunStoppedWithError ? "! ZASTAVENÉ" : !HasReference ? "— NEDOSTUPNÉ" : _snapshot?.TemperatureGateOpen == true || _state is CalibrationRunState.StabilizingSensors or CalibrationRunState.PlateauCompleted or CalibrationRunState.MovingToNextPlateau or CalibrationRunState.Completed or CalibrationRunState.CompletedWithWarnings ? "✓ SPLNENÉ" : _state == CalibrationRunState.WaitingForChamberStability ? "Ⅱ ČAKÁ" : "○ ČAKÁ";
    public string ReferenceCardTone => RunStoppedWithError ? "Error" : WaitingForChamber || !HasReference ? "Pending" : ReferenceCardState.Contains("SPLNENÉ", StringComparison.Ordinal) ? "Done" : _state == CalibrationRunState.WaitingForChamberStability ? "Waiting" : "Pending";
    private bool UnconfirmedPoint => PointFinished && (TotalTargets == 0 || _snapshot!.Targets.Any(t => t.State != CalibrationTargetState.Stable));
    public string PeakCardState => RunStoppedWithError ? "! ZASTAVENÉ" : UnconfirmedPoint ? "! STABILITA NEPOTVRDENÁ" : TotalTargets > 0 && StableCount >= TotalTargets ? "✓ SPLNENÉ" : _state == CalibrationRunState.StabilizingSensors ? "● PREBIEHA" : PointFinished ? "✓ SPLNENÉ" : "○ ČAKÁ";
    public string PeakCardTone => RunStoppedWithError ? "Error" : UnconfirmedPoint ? "Waiting" : PeakCardState.Contains("SPLNENÉ", StringComparison.Ordinal) ? "Done" : PeakCardState.Contains("PREBIEHA", StringComparison.Ordinal) ? "Active" : "Pending";
    public string MeasurementCardState => RunStoppedWithError ? "! ZASTAVENÉ" : UnconfirmedPoint ? "! VÝSLEDOK NEPOTVRDENÝ" : PointFinished ? "✓ SPLNENÉ" : MeasuringCount > 0 || Samples > 0 ? "● PREBIEHA" : "○ ČAKÁ";
    public string MeasurementCardTone => RunStoppedWithError ? "Error" : UnconfirmedPoint ? "Waiting" : MeasurementCardState.Contains("SPLNENÉ", StringComparison.Ordinal) ? "Done" : MeasurementCardState.Contains("PREBIEHA", StringComparison.Ordinal) ? "Active" : "Pending";
    public string ActivePeakKey => _snapshot?.Targets.FirstOrDefault(t => t.Phase is "Measuring" or "MeasuringWithStabilityWarning") is { } m ? $"{m.SerialNumber}|{m.Channel}|{m.PeakId}" :
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
        CalibrationRunState.FinalConditioning => $"Temperovanie výrobkov pri {_finalConditioningTemperatureC:F1} °C",
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
        CalibrationRunState.FinalConditioning => _snapshot?.Message ?? $"Po meraní nasleduje záverečné overenie pri nastavenej teplote {_finalConditioningTemperatureC:F1} °C. Po potvrdení rovnakých stabilizačných brán ako na ostatných platách nasleduje nezávislý kontrolný odber FBG s koeficientmi, až potom vypnutie zariadenia.",
        CalibrationRunState.Completed => "Všetky kalibračné body sú dokončené. Výsledky a export nájdete v Histórii.",
        CalibrationRunState.CompletedWithWarnings => "Beh sa skončil s upozorneniami. Pred použitím výsledkov skontrolujte diagnostiku a históriu.",
        CalibrationRunState.Failed or CalibrationRunState.AwaitingOperator or CalibrationRunState.Aborted => Alert,
        _ => _running ? "Prebieha príprava a kontrola pripojených zariadení." : "Vyberte profil, skontrolujte zapojenie a spustite kalibráciu."
    };
    public string Started => _started?.ToLocalTime().ToString("HH:mm") ?? "—";
    public DateTimeOffset? StartedAt => _started;
    public string Elapsed { get; private set; } = "—";
    public string PhaseElapsed { get; private set; } = "—";
    public string PointElapsed => _snapshot is null ? "—" : Duration(_snapshot.PlateauElapsed);
    public DateTimeOffset? CurrentPlateauTraceStart { get; private set; }
    public DateTimeOffset? FbgStabilityStartedAt { get; private set; }
    public DateTimeOffset? FbgMeasurementStartedAt { get; private set; }
    public string Eta { get; private set; } = "Po prvom bode";
    public string Finish { get; private set; } = "—";
    public DateTimeOffset? EstimatedFinishAt { get; private set; }
    public string EtaBasis { get; private set; } = "Odhad sa spresní po dokončení prvého bodu alebo z historických behov tohto profilu.";
    public string LastUpdate => _lastSnapshotAt?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
    public DateTimeOffset? LastTemperatureSampleAt { get; private set; }
    private DateTimeOffset? _lastSnapshotAt;
    private bool AllTargetsFinished => _snapshot?.Targets.Count > 0 && _snapshot.Targets.All(t => t.Phase == "Done");
    public string Freshness { get; private set; } = "Čaká na dáta";
    public double PointProgress => Steps.Take(10).Count(s => s.State == "Done") * 100d / Math.Max(1, Steps.Take(10).Count(s => s.State != "Skipped"));
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
        double maxPeakDriftPmPerMinute = 1, TimeSpan? stableDuration = null, TimeSpan? stabilityTimeout = null,
        TimeSpan? stabilityExtensionStep = null, TimeSpan? maxAutomaticStabilityExtension = null, TimeSpan? sensorTimeout = null,
        bool enableSetpointRamp = true, double setpointRampCPerMinute = 1,
        double finalConditioningTemperatureC = 25, TimeSpan? finalConditioningDuration = null,
        IReadOnlyList<CalibrationPlateauStatistics>? historicalPlateaus = null)
    {
        if (_started is not null)
        {
            StabilityToleranceC = Math.Abs(toleranceC);
            _stabilityMaxDriftCPerMinute = Math.Max(0, maxDriftCPerMinute);
            _requiredStableSamples = Math.Max(2, requiredStableSamples);
            _requiredMeasurementSamples = Math.Max(2, requiredMeasurementSamples);
            _sampleAcquisitionIntervalSeconds = Math.Clamp(sampleAcquisitionIntervalSeconds, 1, 30);
            _maxRangePm = Math.Max(0, maxRangePm);
            _maxStdDevPm = Math.Max(0, maxStdDevPm);
            _maxPeakDriftPmPerMinute = Math.Max(0, maxPeakDriftPmPerMinute);
            _stableDuration = stableDuration ?? TimeSpan.Zero;
            Notify();
            return;
        }
        double[] plan = temperatures.ToArray();
        string signature = $"{profileCode}|{profile}|{chamber}|{hasReference}|{rules}|{referenceChamberId}|{Math.Abs(toleranceC)}|{maxDriftCPerMinute}|" +
            $"{requiredStableSamples}|{requiredMeasurementSamples}|{sampleAcquisitionIntervalSeconds}|{maxRangePm}|{maxStdDevPm}|{maxPeakDriftPmPerMinute}|{stableDuration}|{stabilityTimeout}|{stabilityExtensionStep}|{maxAutomaticStabilityExtension}|{sensorTimeout}|{enableSetpointRamp}|{setpointRampCPerMinute}|{finalConditioningTemperatureC}|{finalConditioningDuration}|" +
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
        _stabilityExtensionStep = stabilityExtensionStep ?? TimeSpan.FromMinutes(15);
        _maxAutomaticStabilityExtension = maxAutomaticStabilityExtension ?? TimeSpan.FromHours(1);
        _sensorTimeout = sensorTimeout ?? TimeSpan.Zero;
        _enableSetpointRamp = enableSetpointRamp;
        _setpointRampCPerMinute = Math.Clamp(Math.Abs(setpointRampCPerMinute), 0.1, 20.0);
        _finalConditioningTemperatureC = double.IsFinite(finalConditioningTemperatureC) ? finalConditioningTemperatureC : 25;
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
        _lastFbgSampleAt = null;
        _started = _phaseStarted = now; _ended = null; _snapshot = null; _lastSnapshotAt = null; _observedCycleSeconds = null;
        _latestChamberTemperature = null; LastTemperatureSampleAt = null; RunId = "Pripravuje sa…";
        CurrentPlateauTraceStart = null;
        FbgStabilityStartedAt = null;
        FbgMeasurementStartedAt = null;
        _running = true; _paused = false; _state = CalibrationRunState.Preflight; _lastWarning = "";
        Alert = "Bez hlásených upozornení"; Trend = "—"; TrendTone = "Steady"; _targetEvents.Clear(); Activity.Clear(); FbgStabilityCharts.Clear();
        foreach (var point in Points) { point.State = "Pending"; point.Detail = "Čaká"; point.Duration = null; point.Explanation = ""; point.Graphs.Clear(); }
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
            bool warning = plateau.Targets.Count == 0 || plateau.Targets.Any(target => target.Status != CalibrationTargetState.Stable);
            string completedAt = plateau.CompletedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
            point.SetCalibrationOutcome(plateau.Targets.Select(t => t.Status), plateau.Targets.Count);
            point.Explanation = string.Join("\n\n", plateau.Targets.Where(t => t.Status != CalibrationTargetState.Stable).Select(t =>
                $"SN {t.SerialNumber} · {t.Channel}/{t.PeakId}: {t.Problem ?? "Podrobný dôvod nie je uložený."}\nVzorky {t.SampleCount} · rozsah {t.RangePm:F3} pm · σ {t.StandardDeviationPm:F3} pm · drift {t.DriftPmPerMinute:F3} pm/min"));
            point.Graphs.Clear();
            foreach (var target in plateau.Targets)
                point.Graphs.Add(new PlateauDiagnosticGraph($"SN {target.SerialNumber} · {target.Channel}/{target.PeakId} – uložené finálne vzorky", "nm",
                    target.StableSamples.Select(sample => new DashboardTemperatureSample(sample.Timestamp, sample.WavelengthNm)).ToList()));
            point.Duration = duration;
            point.Detail = $"Stabilita {plateau.Targets.Count(t => t.Status == CalibrationTargetState.Stable)}/{plateau.Targets.Count} · {Duration(duration)} · {completedAt}";
            AddEvent(plateau.CompletedAt, warning ? "WARNING" : "SUCCESS",
                $"Obnovený bod {plateau.PlateauIndex + 1} bol dokončený {completedAt}; trvanie {Duration(duration)}.",
                plateau.PlateauIndex, Points.Count, plateau.TargetTemperatureC,
                plateau.ReferenceTemperatureC, plateau.ActualTemperatureC);
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
        if (previous is not null && previous.PlateauIndex == snapshot.PlateauIndex &&
            Math.Abs(previous.TargetTemperatureC - snapshot.TargetTemperatureC) < 0.001 &&
            snapshot.State is CalibrationRunState.PlateauCompleted or CalibrationRunState.MovingToNextPlateau)
            snapshot = snapshot with
            {
                ReferenceEvaluationStartedAt = snapshot.ReferenceEvaluationStartedAt ?? previous.ReferenceEvaluationStartedAt,
                ChamberEntry = snapshot.ChamberEntry ?? previous.ChamberEntry,
            };
        if (snapshot.State == CalibrationRunState.StabilizingSensors && snapshot.Targets.Any(t =>
            t.StabilitySamples > (previous?.Targets.FirstOrDefault(p => p.SerialNumber == t.SerialNumber && p.Channel == t.Channel && p.PeakId == t.PeakId)?.StabilitySamples ?? 0) ||
            t.MeasurementSamples > (previous?.Targets.FirstOrDefault(p => p.SerialNumber == t.SerialNumber && p.Channel == t.Channel && p.PeakId == t.PeakId)?.MeasurementSamples ?? 0)))
            _lastFbgSampleAt = now;
        string previousPhase = Phase;
        if (_lastSnapshotAt is { } previousUpdate && snapshot.State == CalibrationRunState.StabilizingSensors)
        {
            double seconds = (now - previousUpdate).TotalSeconds;
            if (seconds is >= 0.2 and <= 30)
                _observedCycleSeconds = _observedCycleSeconds is { } current ? current * 0.75 + seconds * 0.25 : seconds;
        }
        _lastSnapshotAt = now;
        bool plateauChanged = snapshot.PlateauIndex >= 0 &&
            (previous?.PlateauIndex != snapshot.PlateauIndex ||
             previous is not null && Math.Abs(previous.TargetTemperatureC - snapshot.TargetTemperatureC) > 0.001);
        if (snapshot.PlateauIndex < 0)
            CurrentPlateauTraceStart = null;
        else if (plateauChanged)
            CurrentPlateauTraceStart = now - (snapshot.PlateauElapsed < TimeSpan.Zero ? TimeSpan.Zero : snapshot.PlateauElapsed);
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
        if (plateauChanged)
        {
            _lastFbgSampleAt = null;
            FbgStabilityStartedAt = null;
            FbgMeasurementStartedAt = null;
            _chamberTemperatureTrace.Clear();
            _wikaStabilityScoreTrace.Clear();
            _targetEvents.Clear();
            FbgStabilityCharts.Clear();
            AddEvent(now, "INFO", $"Začal sa bod {snapshot.PlateauIndex + 1} / {snapshot.PlateauCount} na {Target}.");
        }
        if (snapshot.State == CalibrationRunState.StabilizingSensors)
            FbgStabilityStartedAt ??= now;
        if (snapshot.Targets.Any(t => t.MeasurementSamples > 0))
            FbgMeasurementStartedAt ??= now;
        int stableScoreSeconds = TemperatureStableScoreSeconds;
        int requiredStableScoreSeconds = snapshot.RequiredTemperatureScoreSeconds ?? 0;
        bool stableTimeStarted = snapshot.State == CalibrationRunState.WaitingForChamberStability && stableScoreSeconds > 0 &&
            (previous?.PlateauIndex != snapshot.PlateauIndex || (previous?.TemperatureStableScoreSeconds ?? 0) <= 0);
        if (stableTimeStarted && requiredStableScoreSeconds > 0)
        {
            DateTimeOffset stableWindowStart = now - TimeSpan.FromSeconds(stableScoreSeconds);
            // Keep the full plateau timeline, including unsuccessful stability windows.
            _wikaStabilityScoreTrace.Clear();
            AddWikaStabilityScoreSample(stableWindowStart, 0, requiredStableScoreSeconds);
        }
        if (snapshot.ActualTemperatureC is { } chamberTemperature)
            AddChamberTraceSample(now, chamberTemperature);
        if (snapshot.TemperatureStableScoreSeconds is { } score && snapshot.RequiredTemperatureScoreSeconds is { } required && required > 0)
            AddWikaStabilityScoreSample(now, WaitingForChamber ? 0 : score, required);
        if (snapshot.PlateauIndex >= 0 && snapshot.PlateauIndex < Points.Count)
        {
            var point = Points[snapshot.PlateauIndex];
            if (snapshot.State == CalibrationRunState.PlateauCompleted)
            {
                bool warning = snapshot.TotalTargets == 0 || snapshot.Targets.Count != snapshot.TotalTargets || snapshot.Targets.Any(t => t.State != CalibrationTargetState.Stable);
                if (point.Duration is null) AddEvent(now, warning ? "WARNING" : "SUCCESS", $"Bod {snapshot.PlateauIndex + 1} dokončený{(warning ? " s upozornením" : "")}.");
                point.SetCalibrationOutcome(snapshot.Targets.Select(t => t.State), snapshot.TotalTargets);
                point.Duration = snapshot.PlateauElapsed;
                point.Detail = $"Stabilita {snapshot.Targets.Count(t => t.State == CalibrationTargetState.Stable)}/{snapshot.TotalTargets} · {Duration(snapshot.PlateauElapsed)}";
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
                else if (t.Phase == "MeasuringWithStabilityWarning") AddEvent(now, "WARNING", $"Peak {key.Replace("|", " · ")} po timeout-e začína finálne meranie s upozornením na stabilizáciu.");
                _targetEvents[key] = state;
            }
        }
        if (snapshot.PlateauIndex >= 0 && snapshot.PlateauIndex < Points.Count)
        {
            var node = Points[snapshot.PlateauIndex];
            var targets = _snapshot?.Targets ?? snapshot.Targets;
            node.Explanation = string.Join("\n\n", targets.Where(t => t.State != CalibrationTargetState.Stable).Select(t =>
                $"SN {t.SerialNumber} · {t.Channel}/{t.PeakId}: {(!string.IsNullOrWhiteSpace(t.Detail) ? t.Detail : !string.IsNullOrWhiteSpace(t.BlockingReason) ? t.BlockingReason : "Stabilita zatiaľ nebola potvrdená; podrobný dôvod nie je dostupný.")}\nStabilizačné vzorky {t.StabilitySamples}/{t.RequiredStabilitySamples} · finálne {t.MeasurementSamples}/{t.RequiredMeasurementSamples}\nRozsah {t.RangePm:F3}/{t.RangeLimitPm:F3} pm · σ {t.StandardDeviationPm:F3}/{t.StdDevLimitPm:F3} pm · drift {t.DriftPmPerMinute:F3}/{t.DriftLimitPmPerMinute:F3} pm/min"));
            if (snapshot.ReferenceTemperatureC is { } reference && double.IsFinite(reference)) node.AddGraphSample("WIKA – priebeh plata", "°C", now, reference);
            foreach (var target in snapshot.Targets)
                if (target.CurrentWavelengthNm is { } wavelength && double.IsFinite(wavelength))
                    node.AddGraphSample($"SN {target.SerialNumber} · {target.Channel}/{target.PeakId} – priebeh FBG", "nm", now, wavelength);
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
        AddChamberTraceSample(now, temperature);
        Notify();
    }

    private void AddChamberTraceSample(DateTimeOffset timestamp, double temperature)
    {
        if (!double.IsFinite(temperature)) return;
        if (_chamberTemperatureTrace.Count > 0 && timestamp <= _chamberTemperatureTrace[^1].Timestamp) return;
        _chamberTemperatureTrace.Add(new DashboardTemperatureSample(timestamp, temperature));
    }

    private void AddWikaStabilityScoreSample(DateTimeOffset timestamp, int scoreSeconds, int requiredSeconds)
    {
        if (_wikaStabilityScoreTrace.Count > 0 && timestamp <= _wikaStabilityScoreTrace[^1].Timestamp) return;
        _wikaStabilityScoreTrace.Add(new DashboardStabilityScoreSample(timestamp, scoreSeconds, requiredSeconds));
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
        _timerNow = now;
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
        EstimatedFinishAt = null;
        EtaBasis = "Odhad sa spresní po dokončení prvého bodu alebo z historických behov tohto profilu.";
        if (_ended is not null)
        {
            Eta = "—";
            Finish = _ended.Value.ToLocalTime().ToString("HH:mm");
            EstimatedFinishAt = _ended;
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

        if (_state == CalibrationRunState.FinalConditioning)
        {
            Eta = "Závisí od stability";
            Finish = "—";
            EstimatedFinishAt = null;
            EtaBasis = $"Záverečné overenie pri {_finalConditioningTemperatureC:F1} °C nemá pevný čas temperovania. Čaká na stabilitu komory, WIKA a peakov, potom na kontrolný odber.";
            return;
        }

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
        int[] remainingIndices = Enumerable.Range(0, Points.Count)
            .Where(index => !Points[index].Duration.HasValue)
            .OrderBy(index => index == currentIndex ? 0 : index > currentIndex ? 1 : 2)
            .ThenBy(index => index)
            .ToArray();
        double seconds = 0;
        bool usedHistory = false;

        // A temperature timeout defers the plateau and appends it to the work queue.
        // Consequently the active plateau index can be 5 while plateaus 1–4 are still
        // unfinished. Estimate every unfinished point, not only indices after the active one.
        foreach (int index in remainingIndices)
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

        seconds += EstimateRemainingRampSeconds(remainingIndices, currentIndex, currentIsActive);
        seconds += _stableDuration.TotalSeconds + (_requiredStableSamples + _requiredMeasurementSamples) * _sampleAcquisitionIntervalSeconds;
        if (seconds <= 0)
        {
            Eta = "Dokončuje sa";
            EtaBasis = "Všetky merateľné zostávajúce kroky sú hotové; prebieha uloženie a uzavretie behu.";
            return;
        }

        Eta = "≈ " + Duration(TimeSpan.FromSeconds(seconds));
        Finish = "≈ " + now.AddSeconds(seconds).ToLocalTime().ToString("dd.MM. HH:mm");
        EstimatedFinishAt = now.AddSeconds(seconds);
        EtaBasis = usedHistory
            ? "Odhad používa historické mediány jednotlivých plat, aktuálny priebeh bodu a zostávajúci riadený nábeh setpointu."
            : "Odhad používa medián dokončených bodov tohto behu, aktuálny priebeh a zostávajúci riadený nábeh setpointu.";
    }

    private double EstimateRemainingRampSeconds(IReadOnlyList<int> remainingIndices, int currentIndex, bool currentIsActive)
    {
        if (!_enableSetpointRamp || _plannedTemperatures.Length == 0 || remainingIndices.Count == 0) return 0;
        double ratePerSecond = _setpointRampCPerMinute / 60d;
        double seconds = 0;
        int firstIndex = remainingIndices[0];
        double previousTemperature = _plannedTemperatures[firstIndex];

        if (ActualTemperature is { } actual &&
            (currentIndex < 0 || (currentIsActive && _state == CalibrationRunState.MovingToPlateau)))
            seconds += Math.Abs(previousTemperature - actual) / ratePerSecond;

        foreach (int index in remainingIndices.Skip(1))
        {
            double temperature = _plannedTemperatures[index];
            seconds += Math.Abs(temperature - previousTemperature) / ratePerSecond;
            previousTemperature = temperature;
        }
        seconds += Math.Abs(_finalConditioningTemperatureC - previousTemperature) / ratePerSecond;
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
        string[] names = { "Príprava", "Nastavenie cieľa", "Teplota komory", "WIKA referencia", "Stabilita FBG", "Meranie samples", "Vyhodnotenie", "Ďalšie plato", "Temperovanie 25 °C", "Dokončenie" };
        string[] tips =
        {
            "Skontroluje vybraný profil, zapojenie, SN, dostupnosť komory, WIKA a PeakLoggera. Pri dočasnom výpadku komory opakuje pripojenie a úvodné čítanie po 5 sekundách, najviac 30 minút. Pokračuje až po platnej teplote, počas obnovy nemení setpointy ani checkpoint. Stop obnovu zruší; po vyčerpaní limitu vyžaduje zásah operátora.",
            _enableSetpointRamp
                ? $"Aplikácia posúva setpoint plynulo najviac {_setpointRampCPerMinute:F2} °C/min. Komora sa naďalej reguluje vlastným interným snímačom; WIKA iba overí stabilitu po dosiahnutí cieľa. Profilové hold časy neurčujú dĺžku FBG kalibrácie."
                : "Plynulý nábeh je vypnutý a aplikácia nastaví cieľ plata priamo. Komora sa reguluje vlastným interným snímačom; WIKA iba overuje stabilitu.",
            "Pri zapnutej vstupnej podmienke musí komora najprv splniť vlastné okno, toleranciu, rozsah a drift. Až potom začne okno WIKA; čítanie a logovanie bežia počas oboch fáz.",
            $"Stabilné skóre WIKA sa zbiera po blokoch 5 vzoriek:\n" +
            $"1. Prvá vzorka nastaví porovnávaciu základňu.\n" +
            $"2. Posledná vzorka bloku musí byť pri cieli v tolerancii ±{StabilityToleranceC:F3} °C.\n" +
            $"3. Priemerná zmena vzoriek voči základni musí zodpovedať driftu ≤ {_stabilityMaxDriftCPerMinute:F3} °C/min.\n" +
            "4. Úspešný blok pripočíta reálne uplynuté sekundy ku skóre.\n" +
            "5. Neúspešný blok odpočíta dvojnásobok času bloku (najviac po nulu) a nastaví novú základňu.\n" +
            $"Brána sa otvorí po potvrdenom skóre {Duration(_stableDuration)}. Medzi uzavretými blokmi sa čas zobrazuje priebežne, ale potvrdí ho až celý blok. Timeout: {Duration(_stabilityTimeout)}.",
            $"Každý peak samostatne potrebuje {_requiredStableSamples} vzoriek na stabilizáciu: range ≤ {_maxRangePm:F3} pm, σ ≤ {_maxStdDevPm:F3} pm, drift ≤ {_maxPeakDriftPmPerMinute:F3} pm/min. Nevyhovujúce celé okno sa resetuje. {Estimate(_requiredStableSamples)}. {cycle}. " + SensorDeadlineHelp,
            $"Po stabilizácii každý peak zbiera {_requiredMeasurementSamples} nových finálnych vzoriek; {Estimate(_requiredMeasurementSamples)}. Pri strate stability sa nehotové meranie zahodí. Hotové peaky sa nemenia. {cycle}. " + SensorDeadlineHelp,            "Z finálnych meracích vzoriek každého peaku vypočíta priemer, medián, minimum, maximum, range, štandardnú odchýlku a drift; následne uloží bod, raw samples a diagnostiku.",
            "Po dokončení všetkých vybraných peakov uloží checkpoint a nastaví cieľ nasledujúceho vybraného plata. Ak žiadne nezostáva, prejde na záverečné temperovanie.",
            $"Po poslednom kalibračnom bode nastaví komoru na {_finalConditioningTemperatureC:F1} °C. Plato nemá pevné časové minimum. Pred odberom platia rovnaké brány ako pri kalibračných bodoch: vstupná stabilita komory podľa nastavení, WIKA {Duration(_stableDuration)} v tolerancii ±{StabilityToleranceC:F3} °C a stabilita každého peaku na {_requiredStableSamples} vzorkách (range ≤ {_maxRangePm:F3} pm, σ ≤ {_maxStdDevPm:F3} pm, drift ≤ {_maxPeakDriftPmPerMinute:F3} pm/min). Až potom nasleduje nezávislý odber {_requiredMeasurementSamples} kontrolných vzoriek každých {_sampleAcquisitionIntervalSeconds} s. Porovná sa meraná wavelength s wavelength vypočítanou z koeficientov pri teplote WIKA. Kontrola sa nezahrnie do fitovania; neistá stabilita, chýbajúce dáta a chyba sa označia. Až potom sa komora vypne.",
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
            CalibrationRunState.WaitingForChamberStability when WaitingForChamber => 2,
            CalibrationRunState.WaitingForChamberStability => 3,
            CalibrationRunState.StabilizingSensors when AllTargetsFinished => 6,
            CalibrationRunState.StabilizingSensors when TotalTargets > 0 && StableCount >= TotalTargets => 5,
            CalibrationRunState.StabilizingSensors => 4,
            CalibrationRunState.PlateauCompleted => 7,
            CalibrationRunState.MovingToNextPlateau => 7,
            CalibrationRunState.FinalConditioning => 8,
            CalibrationRunState.Completed or CalibrationRunState.CompletedWithWarnings => 10,
            _ => 0
        };
        for (int i = 0; i < Steps.Count; i++) Steps[i].State = i < phase ? "Done" : i == phase && _running ? "Active" : "Pending";
        if (phase is 2 or 3) Steps[phase].State = "Waiting";
        if (phase == 4 && MeasuringCount > 0) Steps[5].State = "Active";
        if (!HasReference) Steps[3].State = "Skipped";
        if (_paused) foreach (var step in Steps.Where(s => s.State is "Active" or "Waiting")) step.State = "Waiting";
        if (_state == CalibrationRunState.Failed) Steps[Math.Min(phase, Steps.Count - 1)].State = "Error";
        if (_state == CalibrationRunState.AwaitingOperator) Steps[Math.Min(phase, Steps.Count - 1)].State = "Waiting";
    }
    private void AddEvent(
        DateTimeOffset now,
        string level,
        string message,
        int? plateauIndex = null,
        int? plateauCount = null,
        double? targetTemperatureC = null,
        double? referenceTemperatureC = null,
        double? chamberTemperatureC = null)
    {
        CalibrationProgressSnapshot? snapshot = _snapshot;
        int effectiveIndex = plateauIndex ?? snapshot?.PlateauIndex ?? -1;
        int effectiveCount = plateauCount ?? snapshot?.PlateauCount ?? Points.Count;
        string plateau = _state == CalibrationRunState.FinalConditioning
            ? "TEMPEROVANIE 25 °C"
            : effectiveIndex >= 0
            ? $"PLATO {effectiveIndex + 1} / {Math.Max(effectiveIndex + 1, effectiveCount)}"
            : "PRÍPRAVA";
        double? target = targetTemperatureC ?? snapshot?.TargetTemperatureC;
        double? reference = referenceTemperatureC ?? snapshot?.ReferenceTemperatureC;
        double? chamber = chamberTemperatureC ?? snapshot?.ActualTemperatureC ?? _latestChamberTemperature;
        string temperatures = $"Cieľ {EventTemperature(target, 1)} · WIKA {EventTemperature(reference, 3)} · Komora {EventTemperature(chamber, 2)}";

        Activity.Insert(0, new DashboardEvent(
            now.ToLocalTime().ToString("HH:mm:ss"), level, plateau, temperatures, message));
        while (Activity.Count > 250) Activity.RemoveAt(Activity.Count - 1);
    }
    private static string EventTemperature(double? value, int decimals) =>
        value is { } finite && double.IsFinite(finite) ? $"{finite.ToString($"F{decimals}")} °C" : "—";
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
    public string Explanation { get; set; } = "";
    public string DiagnosticHelp => string.IsNullOrWhiteSpace(Explanation)
        ? (State == "Done" ? "Stabilita všetkých peakov bola potvrdená." : "Podrobný dôvod nie je dostupný. Plato môže ešte čakať na vyhodnotenie.")
        : "Nepotvrdené znamená, že nie všetky peaky majú potvrdenú stabilitu. Neznamená to automaticky, že chýbajú namerané vzorky.\n\n" + Explanation;
    public List<PlateauDiagnosticGraph> Graphs { get; } = new();
    public void AddGraphSample(string title, string unit, DateTimeOffset timestamp, double value)
    {
        var graph = Graphs.FirstOrDefault(g => g.Title == title);
        if (graph is null) { graph = new(title, unit, new()); Graphs.Add(graph); }
        if (graph.Samples.Count == 0 || graph.Samples[^1].Timestamp < timestamp)
            graph.Samples.Add(new(timestamp, value));
    }
    private string? _calibrationBadge;
    public void SetCalibrationOutcome(IEnumerable<CalibrationTargetState> states, int expected)
    {
        var results = states.ToArray();
        bool success = expected > 0 && results.Length == expected && results.All(s => s == CalibrationTargetState.Stable);
        bool failed = results.Any(s => s is CalibrationTargetState.Failed or CalibrationTargetState.TimedOut or CalibrationTargetState.PeakLost or CalibrationTargetState.Disconnected);
        _calibrationBadge = success ? "✓ ÚSPEŠNÉ" : failed ? "! NEÚSPEŠNÉ" : "! NEPOTVRDENÉ";
        State = success ? "Done" : "Warning";
    }
    public string Badge => _calibrationBadge is not null && State is "Done" or "Warning" ? _calibrationBadge : State switch { "Done" => "✓ SPLNENÉ", "Active" => "● PREBIEHA", "Waiting" => "Ⅱ ČAKÁ", "Error" => "! CHYBA", "Warning" => "! UPOZORNENIE", "Skipped" => "— NEDOSTUPNÉ", _ => "○ ČAKÁ" };
    public string ChipLabel => _calibrationBadge is not null && State is "Done" or "Warning" ? _calibrationBadge[2..] : State switch
    {
        "Done" => "DOKONČENÉ", "Active" => "PREBIEHA", "Waiting" => "ČAKÁ NA STABILITU",
        "Error" => "CHYBA", "Warning" => "UPOZORNENIE", "Skipped" => "PRESKOČENÉ", _ => "ČAKÁ"
    };
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
        _ when _progress?.State == CalibrationTargetState.SkippedIdentityUncertain => "VYNECHANÉ · IDENTITA",
        "Measuring" => "MERANIE",
        "MeasuringWithStabilityWarning" => "MERANIE · UPOZORNENIE",
        _ when _progress?.State == CalibrationTargetState.Stable => "HOTOVO",
        _ when _progress?.State == CalibrationTargetState.CompletedWithStabilityWarning => "UPOZORNENIE",
        "Done" => "NEPOTVRDENÉ",
        "Temperature" => "ČAKÁ NA TEPLOTU",
        _ when _progress?.State == CalibrationTargetState.WaitingForTemperature => "ČAKÁ NA TEPLOTU",
        "Stabilizing" => "STABILIZÁCIA",
        _ => "ČAKÁ",
    };
    public string StateBrush => _progress?.State == CalibrationTargetState.CompletedWithStabilityWarning || _progress?.Phase == "MeasuringWithStabilityWarning" ? "#E5AA54" : _progress?.State == CalibrationTargetState.Stable || _progress?.Phase == "Measuring"
        ? (_progress?.Phase == "Measuring" ? "#58A6FF" : "#45C99A")
        : "#DAA520";
    public string MeasurementSamples => _progress is null ? "Finálne vzorky —" : $"Finálne vzorky {_progress.MeasurementSamples} / {_progress.RequiredMeasurementSamples}";
    public double MeasurementProgress => _progress?.RequiredMeasurementSamples > 0
        ? Math.Clamp(100d * _progress.MeasurementSamples / _progress.RequiredMeasurementSamples, 0, 100)
        : 0;
    public string MeasurementState => _progress?.Phase switch
    {
        _ when _progress?.State == CalibrationTargetState.SkippedIdentityUncertain => "VYNECHANÉ · IDENTITA",
        "Measuring" => "MERANIE",
        "MeasuringWithStabilityWarning" => "MERANIE · UPOZORNENIE",
        "Done" when _progress.State == CalibrationTargetState.Stable => "HOTOVO",
        "Done" when _progress.State == CalibrationTargetState.CompletedWithStabilityWarning => "UPOZORNENIE",
        "Done" => "NEPOTVRDENÉ",
        _ => "ČAKÁ NA STABILITU FBG",
    };
    public string MeasurementStateBrush => _progress?.State == CalibrationTargetState.CompletedWithStabilityWarning || _progress?.Phase == "MeasuringWithStabilityWarning" ? "#E5AA54" : _progress?.Phase == "Measuring" ||
        _progress is { Phase: "Done", State: CalibrationTargetState.Stable }
        ? (_progress?.Phase == "Measuring" ? "#58A6FF" : "#45C99A")
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
public sealed record DashboardEvent(string Time, string Level, string Plateau, string Temperatures, string Message);

public sealed record PlateauDiagnosticGraph(string Title, string Unit, List<DashboardTemperatureSample> Samples);
