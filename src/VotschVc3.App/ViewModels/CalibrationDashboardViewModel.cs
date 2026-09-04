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
    public string Eta { get; private set; } = "Po prvom bode";
    public string Finish { get; private set; } = "—";
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

    public void Configure(string profile, string chamber, IEnumerable<double> temperatures, bool hasReference, string rules, Guid? referenceChamberId = null, double toleranceC = 0, double maxDriftCPerMinute = 0, string? profileCode = null)
    {
        if (_started is not null) return;
        double[] plan = temperatures.ToArray();
        string signature = $"{profileCode}|{profile}|{chamber}|{hasReference}|{rules}|{referenceChamberId}|{Math.Abs(toleranceC)}|{string.Join(",", plan)}";
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
        ReferenceChamberId = referenceChamberId;
        Points.Clear();
        foreach (double t in plan) Points.Add(new DashboardNode($"{Points.Count + 1:00}", $"{t:F1} °C", "Čaká"));
        RefreshSteps();
        Notify();
    }
    public void Begin(DateTimeOffset now)
    {
        _startupDetail = "Čaká sa na prvý stav zariadení.";
        _started = _phaseStarted = now; _ended = null; _snapshot = null; _lastSnapshotAt = null;
        _latestChamberTemperature = null; LastTemperatureSampleAt = null; RunId = "Pripravuje sa…";
        _running = true; _paused = false; _state = CalibrationRunState.Preflight; _lastWarning = "";
        Alert = "Bez hlásených upozornení"; Trend = "—"; _targetEvents.Clear(); Activity.Clear(); FbgStabilityCharts.Clear();
        foreach (var point in Points) { point.State = "Pending"; point.Detail = "Čaká"; point.Duration = null; }
        AddEvent(now, "INFO", "Kalibrácia spustená."); RefreshSteps(); Tick(now);
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
        _lastSnapshotAt = now;
        if (snapshot.ActualTemperatureC is { } actualTemperature)
        {
            _latestChamberTemperature = actualTemperature;
            LastTemperatureSampleAt = now;
        }
        if (snapshot.ActualTemperatureC is { } temperature && previous?.ActualTemperatureC is { } p)
            Trend = Math.Abs(temperature - p) < 0.01 ? "→ Drží" : temperature > p ? "↗ Rastie" : "↘ Klesá";
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
        var durations = Points.Where(p => p.Duration.HasValue).Select(p => p.Duration!.Value.TotalSeconds).ToArray();
        Eta = "Po prvom bode"; Finish = "—";
        if (_ended is not null) { Eta = "—"; Finish = _ended.Value.ToLocalTime().ToString("HH:mm"); }
        else if (_paused) Eta = "Pozastavené";
        else if (durations.Length > 0 && _running)
        {
            double seconds = durations.Average() * (Points.Count - CompletedPoints);
            if (_snapshot is { State: not CalibrationRunState.PlateauCompleted }) seconds -= _snapshot.PlateauElapsed.TotalSeconds;
            if (seconds > 0) { Eta = "≈ " + Duration(TimeSpan.FromSeconds(seconds)); Finish = "≈ " + now.AddSeconds(seconds).ToLocalTime().ToString("HH:mm"); }
            else Eta = "Odhad prekročený";
        }
        Notify();
    }
    private void RefreshSteps()
    {
        if (Steps.Count == 0)
        {
            string[] names = { "Príprava", "Nastavenie cieľa", "Teplota komory", "WIKA referencia", "Stabilita FBG", "Meranie samples", "Vyhodnotenie", "Ďalšie plato", "Dokončenie" };
            string[] tips = { "Kontrola zapojenia a zariadení.", "Priamo sa nastaví cieľ vybraného plata; rampy a časy profilu sa ignorujú.", "Interná sonda komory je iba informačná a neotvára kalibračnú bránu.", "Teplotnú bránu riadi výhradne WIKA referencia.", "Všetky vybrané peaky sa kontrolujú paralelne.", "Každý stabilný peak zbiera vlastné meracie vzorky.", "Uloženie a overenie výsledku bodu.", "Prechod priamo na ďalšie vybrané plato.", "Dokončenie behu; export je v Histórii." };
            for (int i = 0; i < names.Length; i++) Steps.Add(new DashboardNode($"{i + 1:00}", names[i], tips[i]));
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
    private readonly List<(DateTimeOffset Time, double Wavelength)> _samples = new();
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
    public IReadOnlyList<FbgStabilitySample> ChartPoints
    {
        get
        {
            if (_samples.Count == 0) return Array.Empty<FbgStabilitySample>();
            DateTimeOffset origin = _samples[0].Time;
            return _samples.Select(sample => new FbgStabilitySample(
                (sample.Time - origin).TotalMinutes,
                sample.Wavelength)).ToArray();
        }
    }

    public void Update(CalibrationTargetProgress progress, DateTimeOffset now)
    {
        _progress = progress;
        if (progress.Phase is "Stabilizing" or "Measuring" &&
            progress.CurrentWavelengthNm is { } wavelength && double.IsFinite(wavelength))
        {
            _samples.Add((now, wavelength));
            if (_samples.Count > 180) _samples.RemoveRange(0, _samples.Count - 180);
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private static string Metric(string name, double? value, double? limit, string unit) =>
        value is null || limit is null ? $"{name}: čaká na dáta" : $"{name}: {value:F3} / ≤ {limit:F3} {unit}";
}
public sealed record FbgStabilitySample(double Minutes, double WavelengthNm);
public sealed record DashboardEvent(string Time, string Level, string Message);
