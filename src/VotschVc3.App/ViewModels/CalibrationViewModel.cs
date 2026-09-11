using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using VotschVc3.App.Mvvm;
using VotschVc3.App.Notifications;
using VotschVc3.App.Thermometers;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Communication.PolEko;
using VotschVc3.Core.Communication.Sika;
using VotschVc3.Core.Diagnostics;
using VotschVc3.Core.Notifications;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.App.ViewModels;

public sealed partial class CalibrationViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly TimeSpan ProgressDiagnosticInterval = TimeSpan.FromSeconds(30);
    public CalibrationDashboardViewModel Dashboard { get; } = new();
    public ObservableCollection<string> CalibrationTerminalLines { get; } = new();

    public void RefreshDashboardPlan() => Dashboard.Configure(
        SelectedProfile?.Name ?? "Vyberte profil", SelectedChamber?.Config.Name ?? "Komora",
        CalibrationPoints.Where(p => p.Selected).Select(p => p.TemperatureC), SelectedF100 is not null,
        $"Teplota ±{ChamberToleranceC:F2} °C · stabilita {ChamberStableMinutes:F1} min · " +
        $"{RequiredStableSamples} vzoriek · odber každých {SampleAcquisitionIntervalSeconds} s · range ≤ {MaxRangePm:F3} pm · " +
        $"σ ≤ {MaxStdDevPm:F3} pm · drift ≤ {MaxDriftPmPerMinute:F3} pm/min. " +
        "Nulové FBG limity sú vypnuté. Čas hold profilu neurčuje trvanie kalibrácie. " +
        $"WIKA musí podmienky spĺňať súvisle; porušenie podmienok čas vynuluje. " +
        $"Rozsah WIKA ≤ {_setup.Settings.MaxChamberRangeC:F3} °C · σ ≤ {_setup.Settings.MaxChamberStdDevC:F3} °C.",
        referenceChamberId: _workspaceChamberId,
        toleranceC: ChamberToleranceC,
        maxDriftCPerMinute: _setup.Settings.MaxChamberDriftCPerMinute,
        profileCode: SelectedProfile?.Code,
        requiredStableSamples: _setup.Settings.RequiredStableSamples,
        requiredMeasurementSamples: _setup.Settings.RequiredMeasurementSamples,
        maxRangePm: _setup.Settings.MaxWavelengthRangePm,
        maxStdDevPm: _setup.Settings.MaxWavelengthStdDevPm,
        maxPeakDriftPmPerMinute: _setup.Settings.MaxWavelengthDriftPmPerMinute,
        sampleAcquisitionIntervalSeconds: _setup.Settings.SampleAcquisitionIntervalSeconds,
        stableDuration: _setup.Settings.ChamberStableDuration,
        stabilityTimeout: _setup.Settings.ChamberStabilityTimeout,
        finalConditioningTemperatureC: 25,
        finalConditioningDuration: _setup.Settings.FinalConditioningDuration,
        stabilityExtensionStep: _setup.Settings.ChamberStabilityExtensionStep,
        maxAutomaticStabilityExtension: _setup.Settings.MaxAutomaticChamberStabilityExtension,
        sensorTimeout: _setup.Settings.DefaultSensorStabilizationTimeout,
        enableSetpointRamp: _setup.Settings.EnableSetpointRamp,
        setpointRampCPerMinute: _setup.Settings.SetpointRampCPerMinute,
        historicalPlateaus: ProfileStatistics.Plateaus);
    private readonly ProfileStore _profileStore;
    private readonly ChamberConfigStore _chamberStore;
    private CalibrationStore _calibrationStore;
    private readonly CalibrationDefaultsStore _calibrationDefaultsStore;
    private readonly EmailNotifier _email = new();
    private readonly ThermometersViewModel _referenceThermometers;
    private readonly Guid _workspaceChamberId;
    private PeakLoggerSettings _peakLoggerSettings = new();
    private IPeakLoggerClient? _peakLogger;
    private IChamberDevice? _chamber;
    private CalibrationProfileRunner? _runner;
    private CancellationTokenSource? _runCts;
    private Task? _runSessionTask;
    private CancellationTokenSource? _peakMonitorCts;
    private CancellationTokenSource? _setupAutosaveCts;
    private CalibrationRunRecord? _activeRun;
    private CalibrationRunWriter? _activeWriter;
    private readonly SemaphoreSlim _wavelengthTraceGate = new(1, 1);
    private DateTimeOffset _nextWavelengthTraceAt = DateTimeOffset.MinValue;
    private CalibrationWlLog? _activeWlLog;
    private CalibrationSetup _setup = new();
    private bool _stopRequested;
    private bool _temperatureGateOverridePending;
    private double? _lastChamberTemperatureC;
    private double? _lastReferenceTemperatureC;
    private DateTimeOffset? _lastReferenceMismatchEmailAt;
    private bool _referenceMismatchWarningActive;
    private bool _propagatingChannelSerialNumber;
    private bool _applyingRecoveredMappings;
    private double _calibrationProgressPercent;
    private string? _reservedF100Key;
    private string? _reservedPeakLoggerKey;
    private ThermometerDeviceViewModel? _resumeObservedF100;
    private string _resumeReferencePort = string.Empty;
    private string _resumeReferenceSerialNumber = string.Empty;
    private bool _resumeRequiresReference;

    private static readonly Regex ProductionSerialNumberPattern = new(
        "^[A-Za-z0-9]{6}/[A-Za-z0-9]{4}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public CalibrationViewModel(Guid chamberId)
    {
        _workspaceChamberId = chamberId;
        AppPaths.Initialize();
        _profileStore = new ProfileStore(AppPaths.ProfilesDir);
        _chamberStore = new ChamberConfigStore(Path.Combine(AppPaths.SettingsDir, "chambers.json"));
        _calibrationStore = CalibrationStorage.CreateStore();
        _calibrationDefaultsStore = new CalibrationDefaultsStore(Path.Combine(AppPaths.SettingsDir, "fbg-calibration-defaults.json"));
        _setup.Settings = CalibrationCheckpointRecovery.CloneSettings(_calibrationDefaultsStore.Load());
        _email.Settings = new EmailSettingsStore(Path.Combine(AppPaths.SettingsDir, "email.json")).Load();
        _referenceThermometers = new ThermometersViewModel();

        Profiles = new ObservableCollection<TestProfile>();
        Chambers = new ObservableCollection<CalibrationChamberOption>(
            _chamberStore.LoadAll().Select(c => new CalibrationChamberOption(c)));
        Peaks = new ObservableCollection<CalibrationPeakRowViewModel>();
        PeaksView = CollectionViewSource.GetDefaultView(Peaks);
        PeaksView.Filter = MatchesPeakFilter;
        Peaks.CollectionChanged += (_, _) =>
        {
            NotifyPeakCounts();
            RefreshResumeReadiness();
        };
        CalibrationPoints = new ObservableCollection<CalibrationPointRowViewModel>();
        TargetProgress = new ObservableCollection<CalibrationTargetProgressViewModel>();
        History = new ObservableCollection<CalibrationRunRecord>(_calibrationStore.LoadHistory());

        ConnectPeakLoggerCommand = new AsyncRelayCommand(ConnectPeakLoggerAsync, () => !IsRunning && !IsLoadingDeviceData, ReportError);
        DiscoverPeakLoggerApisCommand = new AsyncRelayCommand(DiscoverPeakLoggerApisAsync, () => !IsRunning && !IsLoadingDeviceData && !UseSimulator, ReportError);
        RefreshSensorsCommand = new AsyncRelayCommand(DiscoverSensorsAsync, () => PeakLoggerConnected && !IsRunning && !IsLoadingDeviceData, ReportError);
        SaveSetupCommand = new RelayCommand(() => { try { SaveSetup(); } catch (Exception ex) { ReportError(ex); } }, () => SelectedProfile is not null && !IsRunning);
        SelectSuggestedPeaksCommand = new RelayCommand(SelectSuggestedPeaks, () => Peaks.Count > 0 && !IsRunning);
        ClearPeakSearchCommand = new RelayCommand(() => PeakSearchText = string.Empty);
        MarkAllPlateausCommand = new RelayCommand(MarkAllPlateaus, () => CalibrationPoints.Count > 0 && !IsRunning);
        ClearAllPlateausCommand = new RelayCommand(ClearAllPlateaus, () => CalibrationPoints.Count > 0 && !IsRunning);
        StartCalibrationCommand = new AsyncRelayCommand(StartCalibrationAsync, CanStartCalibration, ReportError);
        ResumeCalibrationCommand = new AsyncRelayCommand(ResumeCalibrationAsync, CanResumeCalibration, ReportError);
        ApplyCurrentDefaultsToResumeCommand = new RelayCommand(ApplyCurrentDefaultsToResume, () => HasResumableCalibration && !IsRunning);
        PauseResumeCommand = new RelayCommand(PauseResume, () => IsRunning && _runner is not null && !HasOperatorDecision);
        ForceNextStepCommand = new RelayCommand(ForceNextStep, () => IsRunning && _runner is not null && Dashboard.CanForceTemperatureGate && !_temperatureGateOverridePending && !HasOperatorDecision && !OperatorSupervisionEnabled);
        StopCalibrationCommand = new RelayCommand(StopCalibration, CanStopOrFinalizeCalibration);
        ResetCalibrationCommand = new RelayCommand(RequestFreshCalibration, () => !_freshStartPending && CanStopOrFinalizeCalibration());
        RefreshHistoryCommand = new RelayCommand(RefreshHistory);
        ExportSelectedRunCommand = new RelayCommand(ExportSelectedRun, () => SelectedHistoryRun is not null);
        ExportCalibrationCoefficientsCommand = new RelayCommand(ExportCalibrationCoefficients, () => SelectedHistoryRun?.CalibrationResults.Count > 0);
        RestoreSelectedRunCommand = new RelayCommand(RestoreSelectedHistoricalRun, CanRestoreSelectedHistoricalRun);

        RefreshF100PortsCommand = new AsyncRelayCommand(RefreshF100PortsAsync, () => !IsRunning && !IsLoadingDeviceData, ReportError);
        CheckF100Command = new AsyncRelayCommand(CheckF100Async, () => SelectedF100 is not null && !IsLoadingDeviceData, ReportError);
        ToggleF100ChartCommand = new RelayCommand(() => ShowF100Chart = !ShowF100Chart);
        ToggleUsbDiagnosticsCommand = new RelayCommand(ToggleUsbDiagnostics);
        AnalyzeUsbCommand = new RelayCommand(AnalyzeUsb);
        DiagnoseF100TalkOnlyCommand = new AsyncRelayCommand(
            DiagnoseF100TalkOnlyAsync,
            () => !IsRunning,
            ReportError);
        AddManualF100PortCommand = new RelayCommand(AddManualF100Port);
        ForceReconnectF100Command = new AsyncRelayCommand(ForceReconnectF100Async, () => SelectedF100 is not null, ReportError);

        SelectedChamber = Chambers.FirstOrDefault(chamber => chamber.Config.Id == chamberId)
            ?? Chambers.FirstOrDefault();
        RefreshProfiles();
        SelectedF100 = F100Devices.FirstOrDefault();
    }

    public ObservableCollection<TestProfile> Profiles { get; }
    public ObservableCollection<CalibrationChamberOption> Chambers { get; }
    public ObservableCollection<CalibrationPeakRowViewModel> Peaks { get; }
    public ICollectionView PeaksView { get; }

    private string _peakSearchText = string.Empty;
    private string _peakFilterMode = "All";
    public string PeakSearchText
    {
        get => _peakSearchText;
        set { if (SetProperty(ref _peakSearchText, value ?? string.Empty)) PeaksView.Refresh(); }
    }
    public bool PeakFilterAll { get => _peakFilterMode == "All"; set { if (value) SetPeakFilter("All"); } }
    public bool PeakFilterSelected { get => _peakFilterMode == "Selected"; set { if (value) SetPeakFilter("Selected"); } }
    public bool PeakFilterErrors { get => _peakFilterMode == "Errors"; set { if (value) SetPeakFilter("Errors"); } }
    public int PeakTotalCount => Peaks.Count;
    public int PeakSelectedCount => Peaks.Count(p => p.Selected);
    public int PeakErrorCount => Peaks.Count(p => p.NeedsSensorSerialNumber || p.HasSerialNumberWarning || p.RequiresReconnect);
    public string PeakSelectionSummary => $"{PeakSelectedCount} z {PeakTotalCount} vybraných · {Peaks.Where(p => p.Selected).Select(p => p.Channel).Distinct(StringComparer.OrdinalIgnoreCase).Count()} kanálov";

    private void NotifyPeakCounts()
    {
        OnPropertyChanged(nameof(PeakTotalCount));
        OnPropertyChanged(nameof(PeakSelectedCount));
        OnPropertyChanged(nameof(PeakErrorCount));
        OnPropertyChanged(nameof(PeakSelectionSummary));
    }

    private void SetPeakFilter(string mode)
    {
        if (_peakFilterMode == mode) return;
        _peakFilterMode = mode;
        OnPropertyChanged(nameof(PeakFilterAll));
        OnPropertyChanged(nameof(PeakFilterSelected));
        OnPropertyChanged(nameof(PeakFilterErrors));
        PeaksView.Refresh();
    }

    private bool MatchesPeakFilter(object item)
    {
        if (item is not CalibrationPeakRowViewModel row) return false;
        if (_peakFilterMode == "Selected" && !row.Selected) return false;
        if (_peakFilterMode == "Errors" && !row.NeedsSensorSerialNumber && !row.HasSerialNumberWarning && !row.RequiresReconnect) return false;
        string query = _peakSearchText.Trim();
        if (query.Length == 0) return true;
        return new[] { row.Channel, row.PeakId, row.PeakIndex.ToString(), row.SerialNumber, row.ChannelSerialNumber,
                row.ChainSerialNumber, row.Order, row.ProductDescription, row.Notes, row.FbgType }
            .Any(value => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
    }
    public ObservableCollection<CalibrationPointRowViewModel> CalibrationPoints { get; }
    public ObservableCollection<CalibrationTargetProgressViewModel> TargetProgress { get; }
    public ObservableCollection<CalibrationRunRecord> History { get; }

    private CalibrationProfileStatistics _profileStatistics = CalibrationProfileStatistics.Empty(Guid.Empty);
    public CalibrationProfileStatistics ProfileStatistics
    {
        get => _profileStatistics;
        private set => SetProperty(ref _profileStatistics, value);
    }
    public bool HasProfileHistory => ProfileStatistics.UsageCount > 0;
    public string ProfileUsageSummary => !HasProfileHistory
        ? "Zatiaľ bez historických behov"
        : $"Použitý {ProfileStatistics.UsageCount}× · dokončené {ProfileStatistics.CompletedCount}× · naposledy {ProfileStatistics.LastUsedAt?.ToLocalTime():dd.MM.yyyy HH:mm}";
    public string ProfileDurationSummary => ProfileStatistics.MedianDuration is not { } estimate
        ? "Odhad vznikne po prvom dokončenom behu"
        : $"Typický odhad {FormatHistoricalDuration(estimate)} · priemer {FormatHistoricalDuration(ProfileStatistics.AverageDuration!.Value)} · posledný {FormatHistoricalDuration(ProfileStatistics.LastCompletedDuration!.Value)}";
    public string ProfileDurationRange => ProfileStatistics.MinimumDuration is not { } minimum
        ? ""
        : $"Historický rozsah {FormatHistoricalDuration(minimum)} – {FormatHistoricalDuration(ProfileStatistics.MaximumDuration!.Value)}";
    public IReadOnlyList<ProfilePlateauAnalysisRow> ProfilePlateauAnalysis { get; private set; } = Array.Empty<ProfilePlateauAnalysisRow>();

    public ObservableCollection<ThermometerDeviceViewModel> F100Devices => _referenceThermometers.Devices;
    public IReadOnlyList<string> F100Channels => F100Protocol.ProbeChannels;

    public AsyncRelayCommand ConnectPeakLoggerCommand { get; }
    public AsyncRelayCommand DiscoverPeakLoggerApisCommand { get; }
    public AsyncRelayCommand RefreshSensorsCommand { get; }
    public RelayCommand SaveSetupCommand { get; }
    public RelayCommand SelectSuggestedPeaksCommand { get; }
    public RelayCommand ClearPeakSearchCommand { get; }
    public RelayCommand MarkAllPlateausCommand { get; }
    public RelayCommand ClearAllPlateausCommand { get; }
    public AsyncRelayCommand StartCalibrationCommand { get; }
    public AsyncRelayCommand ResumeCalibrationCommand { get; }
    public RelayCommand ApplyCurrentDefaultsToResumeCommand { get; }
    public RelayCommand PauseResumeCommand { get; }
    public RelayCommand ForceNextStepCommand { get; }
    public RelayCommand StopCalibrationCommand { get; }
    public RelayCommand ResetCalibrationCommand { get; }
    private bool _freshStartPending;
    public RelayCommand RefreshHistoryCommand { get; }
    public RelayCommand ExportSelectedRunCommand { get; }
    public RelayCommand ExportCalibrationCoefficientsCommand { get; }
    public RelayCommand RestoreSelectedRunCommand { get; }
    public AsyncRelayCommand RefreshF100PortsCommand { get; }
    public AsyncRelayCommand CheckF100Command { get; }
    public RelayCommand ToggleF100ChartCommand { get; }
    public RelayCommand ToggleUsbDiagnosticsCommand { get; }
    public RelayCommand AnalyzeUsbCommand { get; }
    public AsyncRelayCommand DiagnoseF100TalkOnlyCommand { get; }
    public RelayCommand AddManualF100PortCommand { get; }
    public AsyncRelayCommand ForceReconnectF100Command { get; }
    public ObservableCollection<string> UsbDiagnostics { get; } = new();

    private bool _showUsbDiagnostics;
    public bool ShowUsbDiagnostics { get => _showUsbDiagnostics; set => SetProperty(ref _showUsbDiagnostics, value); }

    private string _manualF100Port = "COM4";
    public string ManualF100Port { get => _manualF100Port; set => SetProperty(ref _manualF100Port, value); }

    private TestProfile? _selectedProfile;
    public TestProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                if (value is not null) AdoptTemperatureSafetyFromProfile(value);
                LoadProfileSetup();
                RefreshProfileStatistics();
            }
        }
    }

    private CalibrationChamberOption? _selectedChamber;
    public CalibrationChamberOption? SelectedChamber
    {
        get => _selectedChamber;
        set
        {
            if (SetProperty(ref _selectedChamber, value))
            {
                // Vötsch profiles (ramp + plateau) and SIKA profiles (setpoint + dwell)
                // must not get mixed up, so the profile list follows the chosen device.
                RefreshProfiles();
                RefreshCommands();
            }
        }
    }

    /// <summary>Preselects the device the operator opened this workspace from (one FBG
    /// calibration button per device card).</summary>
    public void SelectChamber(Guid chamberId)
    {
        if (Chambers.FirstOrDefault(c => c.Config.Id == chamberId) is { } match)
        {
            SelectedChamber = match;
        }
    }

    /// <summary>
    /// Reloads <see cref="Profiles"/> from the library, keeping only the profiles that
    /// belong to the selected device's family (plus the universal ones), and keeps the
    /// current selection when it survives the filter.
    /// </summary>
    private void RefreshProfiles()
    {
        ProfileDeviceKind deviceKind = SelectedChamber is { } chamber
            ? chamber.Config.Protocol.ToDeviceKind()
            : ProfileDeviceKind.Any;

        Guid? previous = SelectedProfile?.Id;
        Profiles.Clear();
        foreach (TestProfile profile in _profileStore.LoadActive().Where(p => p.DeviceKind.CanRunOn(deviceKind)))
        {
            Profiles.Add(profile);
        }

        Guid? recoveryProfile = SelectedChamber is { } selected
            ? _calibrationStore.LoadCheckpoint(selected.Config.Id)?.ProfileId : null;
        SelectedProfile = (recoveryProfile is { } recoveryId ? Profiles.FirstOrDefault(p => p.Id == recoveryId) : null)
            ?? (previous is { } id ? Profiles.FirstOrDefault(p => p.Id == id) : null)
            ?? Profiles.FirstOrDefault();
    }

    private CalibrationRunRecord? _selectedHistoryRun;
    public CalibrationRunRecord? SelectedHistoryRun
    {
        get => _selectedHistoryRun;
        set
        {
            if (SetProperty(ref _selectedHistoryRun, value))
            {
                ExportSelectedRunCommand.RaiseCanExecuteChanged();
                ExportCalibrationCoefficientsCommand.RaiseCanExecuteChanged();
                RestoreSelectedRunCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private ThermometerDeviceViewModel? _selectedF100;
    public ThermometerDeviceViewModel? SelectedF100
    {
        get => _selectedF100;
        set
        {
            if (SetProperty(ref _selectedF100, value))
            {
                ObserveResumeReference(value);
                if (value is not null)
                {
                    if (value.ChannelAutoDetected)
                    {
                        SelectedF100Channel = value.SelectedChannel;
                    }
                    else
                    {
                        value.SelectedChannel = SelectedF100Channel;
                    }
                }
                CheckF100Command.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(F100TemperatureLabel));
                OnPropertyChanged(nameof(F100ConnectionLabel));
                OnPropertyChanged(nameof(ReferenceThermometerTitle));
                RefreshResumeReadiness();
            }
        }
    }

    private string _selectedF100Channel = "A";
    public string SelectedF100Channel
    {
        get => _selectedF100Channel;
        set
        {
            string normalized = F100Protocol.NormalizeChannel(value);
            if (normalized == "A-B") normalized = "A";
            if (SetProperty(ref _selectedF100Channel, normalized) && SelectedF100 is not null)
            {
                SelectedF100.SelectedChannel = normalized;
                OnPropertyChanged(nameof(F100ConnectionLabel));
            }
        }
    }

    private bool _showF100Chart;
    public bool ShowF100Chart
    {
        get => _showF100Chart;
        set
        {
            if (SetProperty(ref _showF100Chart, value))
            {
                OnPropertyChanged(nameof(F100ChartButtonText));
            }
        }
    }

    public string F100ChartButtonText => ShowF100Chart ? "Skryť graf" : "Zobraziť graf";
    public string F100TemperatureLabel => SelectedF100?.Temperature is { } t ? $"{t:F3} {SelectedF100.Unit}" : "—";
    public string ReferenceThermometerTitle => SelectedF100?.DeviceName ?? "Referenčný teplomer";
    public string F100ConnectionLabel => SelectedF100 is null
        ? "Žiadny WIKA CTH7000"
        : $"{SelectedF100.PortName} · kanál {SelectedF100Channel} · {SelectedF100.ConnectionState}";

    private bool _useSimulator;
    public bool UseSimulator
    {
        get => _useSimulator;
        set
        {
            if (SetProperty(ref _useSimulator, value)) DiscoverPeakLoggerApisCommand.RaiseCanExecuteChanged();
        }
    }

    private FakePeakLoggerScenario _simulatorScenario = FakePeakLoggerScenario.Normal;
    public FakePeakLoggerScenario SimulatorScenario
    {
        get => _simulatorScenario;
        set => SetProperty(ref _simulatorScenario, value);
    }

    public Array SimulatorScenarios => Enum.GetValues(typeof(FakePeakLoggerScenario));

    private string _peakLoggerHost = "localhost";
    public string PeakLoggerHost { get => _peakLoggerHost; set => SetProperty(ref _peakLoggerHost, value); }

    private int _peakLoggerPort = PeakLoggerApiClient.DefaultPort;
    public int PeakLoggerPort { get => _peakLoggerPort; set => SetProperty(ref _peakLoggerPort, Math.Max(0, value)); }

    public ObservableCollection<PeakLoggerApiClient.DiscoveredInstance> PeakLoggerInstances { get; } = new();

    private PeakLoggerApiClient.DiscoveredInstance? _selectedPeakLoggerInstance;
    public PeakLoggerApiClient.DiscoveredInstance? SelectedPeakLoggerInstance
    {
        get => _selectedPeakLoggerInstance;
        set
        {
            if (SetProperty(ref _selectedPeakLoggerInstance, value) && value is not null)
            {
                PeakLoggerHost = value.Host;
                PeakLoggerPort = value.Port;
            }
        }
    }

    private string _peakLoggerDiscoverySummary = "API inštancie ešte neboli vyhľadané.";
    public string PeakLoggerDiscoverySummary
    {
        get => _peakLoggerDiscoverySummary;
        private set => SetProperty(ref _peakLoggerDiscoverySummary, value);
    }

    private bool _peakLoggerConnected;
    public bool PeakLoggerConnected
    {
        get => _peakLoggerConnected;
        private set
        {
            if (SetProperty(ref _peakLoggerConnected, value))
            {
                RefreshCommands();
                RefreshResumeReadiness();
            }
        }
    }

    private string _peakLoggerStatus = "Nepripojený";
    public string PeakLoggerStatus { get => _peakLoggerStatus; private set => SetProperty(ref _peakLoggerStatus, value); }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(StopCalibrationLabel));
                OnPropertyChanged(nameof(StopCalibrationDetail));
                RefreshCommands();
                PublishCalibrationStatus();
            }
        }
    }

    private string _statusMessage = "Pripravené.";
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    private CalibrationCheckpoint? _resumeCheckpoint;
    public bool HasResumableCalibration => _resumeCheckpoint is not null;
    public string StopCalibrationLabel => IsRunning ? "Stop a uložiť" : "Zrušiť pokračovanie";
    public string StopCalibrationDetail => IsRunning
        ? "Bezpečne zastaví komoru a uloží checkpoint. Dokončené plata zostanú zachované na pokračovanie."
        : "Definitívne ukončí zastavený beh a odstráni možnosť pokračovať. Dokončené plata a namerané súbory zostanú zachované.";
    public string ResumeCalibrationLabel => _resumeCheckpoint is null
        ? "Pokračovať v kalibrácii"
        : $"Pokračovať od plata č. {_resumeCheckpoint.CompletedPlateaus.Count + 1}";
    public string ResumeCalibrationDetail => _resumeCheckpoint is null
        ? string.Empty
        : $"Checkpoint uložený {_resumeCheckpoint.SavedAt.ToLocalTime():dd.MM.yyyy HH:mm:ss}. Obnoví beh s {_resumeCheckpoint.CompletedPlateaus.Count} dokončenými platami. Rozpracované plato sa stabilizuje a zmeria nanovo. {ResumeHardwareStatus}";
    public bool ResumeHardwareReady => _resumeCheckpoint is not null &&
        PeakLoggerConnected && Peaks.Count > 0 &&
        (!_resumeRequiresReference || ResumeReferenceIsReady());
    public string ResumeHardwareStatus
    {
        get
        {
            if (_resumeCheckpoint is null) return string.Empty;
            string peakLogger = PeakLoggerConnected && Peaks.Count > 0
                ? "PeakLogger načítaný ✓"
                : "najprv načítaj PeakLogger";
            string wika = !_resumeRequiresReference
                ? "WIKA nebola súčasťou pôvodného behu"
                : ResumeReferenceIsReady()
                    ? "WIKA načítaná ✓"
                    : $"najprv načítaj pôvodnú WIKA {(_resumeReferencePort.Length > 0 ? _resumeReferencePort : _resumeReferenceSerialNumber)}";
            return $"{peakLogger} · {wika}";
        }
    }

    private string _runState = "Idle";
    public string RunState { get => _runState; private set => SetProperty(ref _runState, value); }

    private string _plateauLabel = "—";
    public string PlateauLabel { get => _plateauLabel; private set => SetProperty(ref _plateauLabel, value); }

    private string _temperatureLabel = "—";
    public string TemperatureLabel { get => _temperatureLabel; private set => SetProperty(ref _temperatureLabel, value); }

    private string _referenceTemperatureLabel = "—";
    public string ReferenceTemperatureLabel { get => _referenceTemperatureLabel; private set => SetProperty(ref _referenceTemperatureLabel, value); }

    private string _stableLabel = "0 / 0";
    public string StableLabel { get => _stableLabel; private set => SetProperty(ref _stableLabel, value); }

    private string _warningText = string.Empty;
    public string WarningText { get => _warningText; private set => SetProperty(ref _warningText, value); }

    private string _serialNumberValidationMessage = string.Empty;
    public string SerialNumberValidationMessage
    {
        get => _serialNumberValidationMessage;
        private set => SetProperty(ref _serialNumberValidationMessage, value);
    }

    public int RequiredStableSamples
    {
        get => _setup.Settings.RequiredStableSamples;
        set { _setup.Settings.RequiredStableSamples = Math.Clamp(value, 2, 10000); OnRuntimeStabilitySettingChanged(); }
    }

    public bool EnableSetpointRamp
    {
        get => _setup.Settings.EnableSetpointRamp;
        set { _setup.Settings.EnableSetpointRamp = value; OnPropertyChanged(); RefreshDashboardPlan(); }
    }

    public double SetpointRampCPerMinute
    {
        get => _setup.Settings.SetpointRampCPerMinute;
        set
        {
            _setup.Settings.SetpointRampCPerMinute = double.IsFinite(value) ? Math.Clamp(Math.Abs(value), 0.1, 20.0) : 1.0;
            OnPropertyChanged();
            RefreshDashboardPlan();
        }
    }

    public int RequiredMeasurementSamples
    {
        get => _setup.Settings.RequiredMeasurementSamples;
        set { _setup.Settings.RequiredMeasurementSamples = Math.Clamp(value, 2, 10000); OnPropertyChanged(); }
    }

    public int SampleAcquisitionIntervalSeconds
    {
        get => _setup.Settings.SampleAcquisitionIntervalSeconds;
        set
        {
            _setup.Settings.SampleAcquisitionIntervalSeconds = Math.Clamp(value, 1, 30);
            OnPropertyChanged();
            RefreshDashboardPlan();
        }
    }

    public bool EnableWavelengthAveraging
    {
        get => _setup.Settings.EnableWavelengthAveraging;
        set { _setup.Settings.EnableWavelengthAveraging = value; OnPropertyChanged(); }
    }

    public int WavelengthAveragingSamples
    {
        get => _setup.Settings.WavelengthAveragingSamples;
        set { _setup.Settings.WavelengthAveragingSamples = Math.Clamp(value, 1, 1000); OnPropertyChanged(); }
    }

    public bool EnableWavelengthTraceLogging
    {
        get => _setup.Settings.EnableWavelengthTraceLogging;
        set { _setup.Settings.EnableWavelengthTraceLogging = value; OnPropertyChanged(); }
    }

    public int WavelengthTraceIntervalSeconds
    {
        get => _setup.Settings.WavelengthTraceIntervalSeconds;
        set { _setup.Settings.WavelengthTraceIntervalSeconds = Math.Clamp(value, 1, 86400); OnPropertyChanged(); }
    }

    public double MaxRangePm
    {
        get => _setup.Settings.MaxWavelengthRangePm;
        set { _setup.Settings.MaxWavelengthRangePm = Math.Max(0, value); OnRuntimeStabilitySettingChanged(); }
    }

    public double MaxStdDevPm
    {
        get => _setup.Settings.MaxWavelengthStdDevPm;
        set { _setup.Settings.MaxWavelengthStdDevPm = Math.Max(0, value); OnRuntimeStabilitySettingChanged(); }
    }

    public double MaxDriftPmPerMinute
    {
        get => _setup.Settings.MaxWavelengthDriftPmPerMinute;
        set { _setup.Settings.MaxWavelengthDriftPmPerMinute = Math.Max(0, value); OnRuntimeStabilitySettingChanged(); }
    }

    public double ChamberToleranceC
    {
        get => _setup.Settings.ChamberToleranceC;
        set { _setup.Settings.ChamberToleranceC = Math.Abs(value); OnRuntimeStabilitySettingChanged(); }
    }

    public bool ChamberEntryEnabled { get => _setup.Settings.ChamberEntryEnabled; set { _setup.Settings.ChamberEntryEnabled = value; OnRuntimeStabilitySettingChanged(); } }
    public double ChamberEntryToleranceC { get => _setup.Settings.ChamberEntryToleranceC; set { _setup.Settings.ChamberEntryToleranceC = Math.Clamp(value, 0.01, 20); OnRuntimeStabilitySettingChanged(); } }
    public double ChamberEntryStableSeconds { get => _setup.Settings.ChamberEntryStableSeconds; set { _setup.Settings.ChamberEntryStableSeconds = Math.Clamp(value, 1, 3600); OnRuntimeStabilitySettingChanged(); } }
    public double ChamberEntryRangeC { get => _setup.Settings.ChamberEntryRangeC; set { _setup.Settings.ChamberEntryRangeC = Math.Clamp(value, 0.01, 20); OnRuntimeStabilitySettingChanged(); } }
    public double ChamberEntryDriftCPerMinute { get => _setup.Settings.ChamberEntryDriftCPerMinute; set { _setup.Settings.ChamberEntryDriftCPerMinute = Math.Clamp(value, 0.001, 10); OnRuntimeStabilitySettingChanged(); } }

    public double ChamberStableMinutes
    {
        get => _setup.Settings.ChamberStableDuration.TotalMinutes;
        set { _setup.Settings.ChamberStableDuration = TimeSpan.FromMinutes(Math.Max(0, value)); OnRuntimeStabilitySettingChanged(); }
    }

    public double MaxChamberDriftCPerMinute
    {
        get => _setup.Settings.MaxChamberDriftCPerMinute;
        set { _setup.Settings.MaxChamberDriftCPerMinute = Math.Max(0, value); OnRuntimeStabilitySettingChanged(); }
    }

    public double MaxChamberRangeC
    {
        get => _setup.Settings.MaxChamberRangeC;
        set { _setup.Settings.MaxChamberRangeC = Math.Max(0, value); OnRuntimeStabilitySettingChanged(); }
    }

    public double MaxChamberStdDevC
    {
        get => _setup.Settings.MaxChamberStdDevC;
        set { _setup.Settings.MaxChamberStdDevC = Math.Max(0, value); OnRuntimeStabilitySettingChanged(); }
    }

    private void OnRuntimeStabilitySettingChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        OnPropertyChanged(propertyName);
        RefreshDashboardPlan();
    }

    public double SensorTimeoutMinutes
    {
        get => _setup.Settings.DefaultSensorStabilizationTimeout.TotalMinutes;
        set { _setup.Settings.DefaultSensorStabilizationTimeout = TimeSpan.FromMinutes(Math.Max(1, value)); OnPropertyChanged(); }
    }

    public double ValidationMinimumDeltaTemperatureC
    {
        get => _setup.Settings.ValidationMinimumDeltaTemperatureC;
        set { _setup.Settings.ValidationMinimumDeltaTemperatureC = Math.Max(0, value); OnPropertyChanged(); }
    }

    public double ValidationMinimumResponsePm
    {
        get => _setup.Settings.ValidationMinimumWavelengthResponsePm;
        set { _setup.Settings.ValidationMinimumWavelengthResponsePm = Math.Max(0, value); OnPropertyChanged(); }
    }

    public bool AllowValidationOverride
    {
        get => _setup.Settings.AllowValidationOverride;
        set { _setup.Settings.AllowValidationOverride = value; OnPropertyChanged(); }
    }

    public string ValidationOverrideReason
    {
        get => _setup.Settings.ValidationOverrideReason;
        set { _setup.Settings.ValidationOverrideReason = value ?? string.Empty; OnPropertyChanged(); }
    }

    public void RefreshAdminAcquisitionInterval()
    {
        if (IsRunning || HasResumableCalibration) return;
        _calibrationDefaultsStore.ApplyAcquisitionInterval(_setup.Settings, preserveRunSettings: false);
        RefreshSettingsBindings();
        OnPropertyChanged(nameof(OperatorSupervisionEnabled));
        OnPropertyChanged(nameof(OperatorSupervisionLabel));
        OnPropertyChanged(nameof(SampleAcquisitionIntervalSeconds));
        OnPropertyChanged(nameof(WavelengthTraceIntervalSeconds));
        RefreshDashboardPlan();
    }

    private void LoadProfileSetup()
    {
        SetupSaveStatus = "Automatické ukladanie";
        SetupSaveColor = "#AAB7CE";
        SetupSaveDetail = "Zmeny zapojenia sa ukladajú automaticky po 350 ms. Ikona uloží zapojenie okamžite.";
        Peaks.Clear();
        CalibrationPoints.Clear();
        if (SelectedProfile is null)
        {
            _setup = new CalibrationSetup { Settings = CalibrationCheckpointRecovery.CloneSettings(_calibrationDefaultsStore.Load()) };
            OnPropertyChanged(nameof(IgnoredChannelsLabel));
            RefreshSettingsBindings();
            RefreshResumeCheckpoint();
            RefreshCommands();
            return;
        }

        Guid chamberId = SelectedChamber?.Config.Id ?? _workspaceChamberId;
        _setup = _calibrationStore.LoadSetup(SelectedProfile.Id, chamberId)
            ?? new CalibrationSetup
            {
                ProfileId = SelectedProfile.Id,
                ChamberId = chamberId,
                Settings = CalibrationCheckpointRecovery.CloneSettings(_calibrationDefaultsStore.Load()),
            };
        for (int i = 0; i < SelectedProfile.Segments.Count; i++)
        {
            ProfileSegment segment = SelectedProfile.Segments[i];
            if (!segment.IsRamp)
            {
                var point = new CalibrationPointRowViewModel(i, segment);
                point.Selected = _setup.CalibrationSegmentIndices.Contains(i);
                point.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(CalibrationPointRowViewModel.Selected))
                    {
                        StartCalibrationCommand.RaiseCanExecuteChanged();
                        RefreshDashboardPlan();
                    }
                };
                CalibrationPoints.Add(point);
            }
        }
        OnPropertyChanged(nameof(IgnoredChannelsLabel));
        RefreshResumeCheckpoint();
        _calibrationDefaultsStore.ApplyAcquisitionInterval(_setup.Settings, preserveRunSettings: HasResumableCalibration || IsRunning);
        RefreshSettingsBindings();
        RefreshDashboardPlan();
        RefreshCommands();
    }

    private void RefreshResumeCheckpoint()
    {
        CalibrationCheckpoint? checkpoint = SelectedChamber is null
            ? null
            : _calibrationStore.LoadCheckpoint(SelectedChamber.Config.Id);
        _resumeCheckpoint = checkpoint is not null && SelectedProfile is not null &&
                            checkpoint.ProfileId == SelectedProfile.Id
            ? checkpoint
            : null;
        CalibrationRunRecord? resumeRun = _resumeCheckpoint is null
            ? null
            : _calibrationStore.LoadRun(_resumeCheckpoint.RunId);
        _resumeReferencePort = resumeRun?.ReferenceThermometerPort?.Trim() ?? string.Empty;
        _resumeReferenceSerialNumber = resumeRun?.ReferenceThermometerSerialNumber?.Trim() ?? string.Empty;
        _resumeRequiresReference = _resumeReferencePort.Length > 0 || _resumeReferenceSerialNumber.Length > 0;
        bool setupRecovered = false;
        if (_resumeCheckpoint is not null && CalibrationCheckpointRecovery.RestoreRunConfiguration(_setup, _resumeCheckpoint))
        {
            setupRecovered = true;
            foreach (CalibrationPointRowViewModel point in CalibrationPoints)
                point.Selected = _setup.CalibrationSegmentIndices.Contains(point.SegmentIndex);
            RefreshSettingsBindings();
        }
        if (_resumeCheckpoint is not null && CalibrationCheckpointRecovery.RestoreMappingsIfMissing(_setup, _resumeCheckpoint))
        {
            setupRecovered = true;
            ApplyRecoveredMappingsToVisiblePeaks(_setup.Mappings);
            StatusMessage = $"Zapojenie a SN pre {_resumeCheckpoint.Mappings.Count(mapping => mapping.Selected)} peakov boli obnovené z checkpointu.";
        }
        if (setupRecovered) _calibrationStore.SaveSetup(_setup);
        OnPropertyChanged(nameof(HasResumableCalibration));
        OnPropertyChanged(nameof(ResumeCalibrationLabel));
        OnPropertyChanged(nameof(ResumeCalibrationDetail));
        RefreshResumeReadiness();
        ApplyCurrentDefaultsToResumeCommand.RaiseCanExecuteChanged();
        ResumeCalibrationCommand.RaiseCanExecuteChanged();
        StopCalibrationCommand.RaiseCanExecuteChanged();
        ResetCalibrationCommand.RaiseCanExecuteChanged();
    }

    private void ApplyRecoveredMappingsToVisiblePeaks(IEnumerable<CalibrationSensorMapping> mappings)
    {
        if (Peaks.Count == 0) return;

        Dictionary<string, CalibrationSensorMapping> saved = mappings
            .GroupBy(mapping => mapping.SourceIdentity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        _applyingRecoveredMappings = true;
        try
        {
            foreach (CalibrationPeakRowViewModel row in Peaks)
            {
                string sourceIdentity = $"{row.PeakLoggerDeviceSerialNumber}|{row.Channel}|{row.PeakId}";
                if (saved.TryGetValue(sourceIdentity, out CalibrationSensorMapping? mapping))
                {
                    row.ApplySavedMapping(mapping);
                }
            }
        }
        finally
        {
            _applyingRecoveredMappings = false;
        }

        ValidateSerialNumbers();
        RefreshCommands();
    }

    private Task ConnectPeakLoggerAsync() => LoadDeviceDataAsync(ConnectPeakLoggerAsyncCore);

    private async Task ConnectPeakLoggerAsyncCore()
    {
        string? newReservationKey = null;
        if (!UseSimulator)
        {
            int port = PeakLoggerPort > 0 ? PeakLoggerPort : PeakLoggerApiClient.DefaultPort;
            newReservationKey = CalibrationResourceRegistry.PeakLoggerKey(PeakLoggerHost, port);
            if (!CalibrationResourceRegistry.TryAcquire(
                    newReservationKey, _workspaceChamberId, WorkspaceName, out string occupiedBy))
            {
                throw new InvalidOperationException(
                    $"PeakLogger API {PeakLoggerHost}:{port} je obsadené kalibráciou zariadenia „{occupiedBy}“. Vyber inú API inštanciu alebo ju najprv uvoľni v pôvodnom okne.");
            }
        }

        StopPeakMonitor();
        if (_peakLogger is not null) await _peakLogger.DisposeAsync();
        if (_reservedPeakLoggerKey is { } oldReservation &&
            !string.Equals(oldReservation, newReservationKey, StringComparison.OrdinalIgnoreCase))
        {
            CalibrationResourceRegistry.Release(oldReservation, _workspaceChamberId);
        }
        _reservedPeakLoggerKey = newReservationKey;
        _peakLoggerSettings = new PeakLoggerSettings
        {
            Host = PeakLoggerHost.Trim(),
            Port = PeakLoggerPort,
            UseSimulator = UseSimulator,
            PollingInterval = TimeSpan.FromMilliseconds(500),
        };
        _peakLogger = UseSimulator
            ? new FakePeakLoggerClient(SimulatorScenario)
            : new PeakLoggerApiClient();

        try
        {
            PeakLoggerStatus = "Pripájam…";
            await _peakLogger.ConnectAsync(_peakLoggerSettings);
            PeakLoggerConnected = true;
            PeakLoggerStatus = UseSimulator ? $"Pripojený · simulátor ({SimulatorScenario})" : "Pripojený";
            await DiscoverSensorsAsync();
            StartPeakMonitor();
        }
        catch
        {
            PeakLoggerConnected = false;
            if (newReservationKey is not null)
            {
                CalibrationResourceRegistry.Release(newReservationKey, _workspaceChamberId);
                _reservedPeakLoggerKey = null;
            }
            if (_peakLogger is not null)
            {
                await _peakLogger.DisposeAsync();
                _peakLogger = null;
            }
            throw;
        }
    }

    private Task DiscoverPeakLoggerApisAsync() => LoadDeviceDataAsync(DiscoverPeakLoggerApisAsyncCore);

    private async Task DiscoverPeakLoggerApisAsyncCore()
    {
        PeakLoggerDiscoverySummary = IsLocalPeakLoggerHost(PeakLoggerHost)
            ? "Hľadám PeakLogger API na všetkých aktívnych lokálnych TCP portoch…"
            : $"Hľadám PeakLogger API na {PeakLoggerHost}:{PeakLoggerPort}–{PeakLoggerPort + 63}…";
        PeakLoggerApiClient.DiscoveryReport report =
            await PeakLoggerApiClient.DiscoverInstancesAsync(PeakLoggerHost, PeakLoggerPort, 64);
        IReadOnlyList<PeakLoggerApiClient.DiscoveredInstance> found = report.Instances;

        PeakLoggerInstances.Clear();
        foreach (PeakLoggerApiClient.DiscoveredInstance instance in found) PeakLoggerInstances.Add(instance);
        SelectedPeakLoggerInstance = found.FirstOrDefault(x => x.Port == PeakLoggerPort) ?? found.FirstOrDefault();

        int interrogators = found.Sum(x => x.DeviceCount);
        int peaks = found.Sum(x => x.PeakCount);
        int localProcessCount = IsLocalPeakLoggerHost(PeakLoggerHost)
            ? GetLocalPeakLoggerProcessCount()
            : 0;
        PeakLoggerDiscoverySummary = found.Count == 0
            ? $"Nenašlo sa žiadne PeakLogger API · skontrolovaných portov: {report.ScannedPortCount}. Skontroluj proces a firewall."
            : localProcessCount > found.Count
                ? $"Nájdené API: {found.Count}, ale bežia procesy PeakLogger: {localProcessCount}. " +
                  $"Ďalšia inštancia nemá vlastný REST port (43122 môže držať iba jedna). " +
                  $"Dostupné interrogátory: {interrogators} · peaky: {peaks}."
                : $"Nájdené API: {found.Count} · interrogátory/inštancie: {interrogators} · peaky: {peaks} · skontrolované porty: {report.ScannedPortCount}";
        StatusMessage = PeakLoggerDiscoverySummary;
    }

    public async Task InitializeHardwareAsync()
    {
        UseSimulator = false;
        await RescanF100PortsAsync(showStatus: false);
        foreach (ThermometerDeviceViewModel thermometer in F100Devices)
        {
            try
            {
                thermometer.PollingEnabled = false;
                await thermometer.CheckAsync();
            }
            catch (Exception ex)
            {
                AppLog.Warn("USB teplomer", $"Automatický scan {thermometer.PortName}: {ex.Message}");
            }
        }
        if (SelectedF100?.ChannelAutoDetected == true)
        {
            SelectedF100Channel = SelectedF100.SelectedChannel;
            OnPropertyChanged(nameof(F100TemperatureLabel));
            OnPropertyChanged(nameof(F100ConnectionLabel));
            OnPropertyChanged(nameof(ReferenceThermometerTitle));
        }
        try
        {
            await DiscoverPeakLoggerApisAsync();
            if (SelectedPeakLoggerInstance is not null && !PeakLoggerConnected)
            {
                await ConnectPeakLoggerAsync();
            }
        }
        catch (Exception ex)
        {
            PeakLoggerStatus = "Nepripojený";
            AppLog.Warn("PeakLogger", $"Automatické pripojenie pri otvorení kalibrácie: {ex.Message}");
        }
    }

    private static int GetLocalPeakLoggerProcessCount()
    {
        try
        {
            Process[] processes = Process.GetProcessesByName("PeakLogger");
            try
            {
                return processes.Length;
            }
            finally
            {
                foreach (Process process in processes) process.Dispose();
            }
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsLocalPeakLoggerHost(string? host) =>
        string.IsNullOrWhiteSpace(host) ||
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
        host.Equals(".", StringComparison.OrdinalIgnoreCase) ||
        host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);

    public int RemoveMissingUnassignedPeaks()
    {
        if (IsRunning) return 0;
        var removable = Peaks.Where(p => CalibrationPeakTopologyPolicy.CanDiscardMissingPeak(
            p.IsDisconnected, p.Selected, p.SerialNumber, IsRunning)).ToArray();
        foreach (var row in removable) Peaks.Remove(row);
        if (removable.Length > 0)
        {
            PersistSetup(showStatus: false);
            NotifyPeakCounts();
            RefreshCommands();
        }
        return removable.Length;
    }

    private Task DiscoverSensorsAsync() => LoadDeviceDataAsync(DiscoverSensorsAsyncCore);

    private async Task DiscoverSensorsAsyncCore()
    {
        if (_peakLogger is null || SelectedProfile is null) return;
        IReadOnlyList<PeakLoggerSensor> sensors = await _peakLogger.DiscoverSensorsAsync();
        Dictionary<string, CalibrationSensorMapping> saved = _setup.Mappings
            .GroupBy(m => m.SourceIdentity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var previousRows = Peaks.ToDictionary(p => $"{p.PeakLoggerDeviceSerialNumber}|{p.Channel}|{p.PeakId}", StringComparer.OrdinalIgnoreCase);
        Peaks.Clear();
        foreach (PeakLoggerSensor sensor in sensors.Where(s => !IsPeakLoggerChannelIgnored(s.Channel)))
        {
            foreach (PeakLoggerPeak peak in sensor.Peaks)
            {
                string sourceIdentity = $"{sensor.SerialNumber}|{sensor.Channel}|{peak.PeakId}";
                saved.TryGetValue(sourceIdentity, out CalibrationSensorMapping? mapping);
                if (previousRows.Remove(sourceIdentity, out CalibrationPeakRowViewModel? existing))
                {
                    existing.MinimumIntensityDbm = new CalibrationDefaultsStore(Path.Combine(AppPaths.SettingsDir, "fbg-calibration-defaults.json")).Load().MinimumPeakIntensityDbm;
                    existing.UpdateLive(peak.WavelengthNm, peak.Intensity, DateTimeOffset.Now);
                    Peaks.Add(existing);
                }
                else Peaks.Add(CreatePeakRow(sensor, peak, mapping));
            }
        }

        foreach (CalibrationPeakRowViewModel missing in previousRows.Values.Where(p => !IsPeakLoggerChannelIgnored(p.Channel)))
        {
            missing.MarkDisconnected();
            Peaks.Add(missing);
        }
        int livePeakCount = sensors.Where(s => !IsPeakLoggerChannelIgnored(s.Channel)).Sum(sensor => sensor.Peaks.Count);
        PeakLoggerStatus = livePeakCount == 0
            ? $"API pripojené · {PeakLoggerHost}:{PeakLoggerPort} · bez peakov"
            : $"Pripojený · {sensors.Count} zdrojov/kanálov · {livePeakCount} peakov";
        if (_peakLogger is PeakLoggerApiClient api)
        {
            var connected = new PeakLoggerApiClient.DiscoveredInstance(PeakLoggerHost, PeakLoggerPort, api.PeaksPath, livePeakCount, sensors.Count);
            var previous = PeakLoggerInstances.FirstOrDefault(x => x.Host == PeakLoggerHost && x.Port == PeakLoggerPort);
            if (previous is not null) PeakLoggerInstances.Remove(previous);
            PeakLoggerInstances.Add(connected);
            SelectedPeakLoggerInstance = connected;
            PeakLoggerDiscoverySummary = livePeakCount == 0
                ? "Uložené API odpovedá bez peakov. Skontroluj pripojenie snímačov alebo vyber správnu API inštanciu."
                : "Uložený PeakLogger bol pripojený a načítaný.";
        }
        if (!UseSimulator && Peaks.Count > 0 && Peaks.All(p => string.IsNullOrWhiteSpace(p.SerialNumber)))
        {
            StatusMessage = "PeakLogger peaky načítané. Produkčné SN FBG senzora zadaj alebo naskenuj k vybranému peaku; deviceSN z API je SN interrogátora.";
        }
        ValidateSerialNumbers();
        RefreshCommands();
    }

    private CalibrationPeakRowViewModel CreatePeakRow(
        PeakLoggerSensor sensor,
        PeakLoggerPeak peak,
        CalibrationSensorMapping? saved)
    {
        var row = new CalibrationPeakRowViewModel(sensor, peak, saved);
        row.MinimumIntensityDbm = new CalibrationDefaultsStore(Path.Combine(AppPaths.SettingsDir, "fbg-calibration-defaults.json")).Load().MinimumPeakIntensityDbm;
        if (UseSimulator && string.IsNullOrWhiteSpace(row.SerialNumber))
        {
            row.ChannelSerialNumber = sensor.SerialNumber;
        }
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CalibrationPeakRowViewModel.ChannelSerialNumber) && !_propagatingChannelSerialNumber)
            {
                PropagateChannelSerialNumber(row);
            }

            if (e.PropertyName is nameof(CalibrationPeakRowViewModel.Selected)
                or nameof(CalibrationPeakRowViewModel.ChannelSerialNumber)
                or nameof(CalibrationPeakRowViewModel.ChainSerialNumber))
            {
                ValidateSerialNumbers();
                StartCalibrationCommand.RaiseCanExecuteChanged();
                NotifyPeakCounts();
                if (_peakFilterMode == "Selected" && e.PropertyName == nameof(CalibrationPeakRowViewModel.Selected))
                    PeaksView.Refresh();
            }

            if (e.PropertyName == nameof(CalibrationPeakRowViewModel.IsDisconnected)) NotifyPeakCounts();

            if (!_applyingRecoveredMappings &&
                e.PropertyName is not nameof(CalibrationPeakRowViewModel.CurrentWavelengthNm)
                and not nameof(CalibrationPeakRowViewModel.IsDisconnected)
                and not nameof(CalibrationPeakRowViewModel.RequiresReconnect)
                and not nameof(CalibrationPeakRowViewModel.ConnectionWarning)
                and not nameof(CalibrationPeakRowViewModel.HasWeakSignal)
                and not nameof(CalibrationPeakRowViewModel.LiveWavelengthLabel)
                and not nameof(CalibrationPeakRowViewModel.Intensity)
                and not nameof(CalibrationPeakRowViewModel.LastWavelengthUpdate)
                and not nameof(CalibrationPeakRowViewModel.SerialNumber)
                and not nameof(CalibrationPeakRowViewModel.NeedsSensorSerialNumber)
                and not nameof(CalibrationPeakRowViewModel.SerialNumberWarning)
                and not nameof(CalibrationPeakRowViewModel.HasSerialNumberWarning))
            {
                ScheduleSetupAutosave();
            }
        };
        return row;
    }

    private void PropagateChannelSerialNumber(CalibrationPeakRowViewModel source) { }

    private void ValidateSerialNumbers()
    {
        foreach (CalibrationPeakRowViewModel row in Peaks)
        {
            row.SetSerialNumberWarning(string.Empty);
        }

        foreach (CalibrationPeakRowViewModel row in Peaks.Where(row => !string.IsNullOrWhiteSpace(row.SerialNumber)))
        {
            if (!ProductionSerialNumberPattern.IsMatch(row.SerialNumber))
            {
                row.AddSerialNumberWarning("Neštandardný formát SN; očakáva sa XXXXXX/XXXX. Text je povolený, ale skontroluj ho.");
            }
        }

        foreach (IGrouping<string, CalibrationPeakRowViewModel> duplicate in Peaks
                     .Where(row => !string.IsNullOrWhiteSpace(row.SerialNumber))
                     .GroupBy(row => row.SerialNumber, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            bool containsChainOverride = duplicate.Any(row => !string.IsNullOrWhiteSpace(row.ChainSerialNumber));
            int channelCount = duplicate
                .Select(row => $"{row.PeakLoggerDeviceSerialNumber}|{row.Channel}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (!containsChainOverride && channelCount == 1) continue;

            foreach (CalibrationPeakRowViewModel row in duplicate)
            {
                row.AddSerialNumberWarning($"Duplicitné SN „{duplicate.Key}“ – skontroluj kanál alebo CHAIN zapojenie.");
            }
        }

        List<CalibrationPeakRowViewModel> warnings = Peaks.Where(row => row.HasSerialNumberWarning).ToList();
        SerialNumberValidationMessage = warnings.Count == 0
            ? string.Empty
            : $"⚠ Kontrola SN: {warnings.Count} riadkov vyžaduje kontrolu. Prejdi myšou na zvýraznené SN.";
    }

    private void ScheduleSetupAutosave()
    {
        if (SelectedProfile is null || IsRunning) return;
        _setupAutosaveCts?.Cancel();
        _setupAutosaveCts?.Dispose();
        SetupSaveStatus = "Ukladá sa…";
        SetupSaveColor = "#FFD27D";
        _setupAutosaveCts = new CancellationTokenSource();
        CancellationToken token = _setupAutosaveCts.Token;
        _ = AutosaveSetupAsync(token);
    }

    private async Task AutosaveSetupAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(350, token);
            await Application.Current.Dispatcher.InvokeAsync(() => { if (!token.IsCancellationRequested) PersistSetup(showStatus: false); });
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke restarts the short debounce window.
        }
        catch (Exception)
        {
            // The save status exposes the failure; observe background exceptions.
        }
    }

    private void StartPeakMonitor()
    {
        StopPeakMonitor();
        if (_peakLogger is null) return;
        _peakMonitorCts = new CancellationTokenSource();
        _ = MonitorPeakLoggerAsync(_peakMonitorCts.Token);
    }

    private void StopPeakMonitor()
    {
        _peakMonitorCts?.Cancel();
        _peakMonitorCts?.Dispose();
        _peakMonitorCts = null;
    }

    /// <summary>
    /// Keeps the wavelength column live before and during a real calibration and writes a
    /// whole-run trace for every selected FBG index. The simulator is not double-polled
    /// while a calibration runs, because its scenarios advance on each read; runner progress
    /// still updates the selected wavelengths in that case.
    /// </summary>
    private async Task MonitorPeakLoggerAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _peakLogger is not null)
        {
            try
            {
                if (!(UseSimulator && IsRunning))
                {
                    IReadOnlyList<PeakLoggerMeasurement> measurements = await _peakLogger.ReadMeasurementsAsync(token);
                    await Application.Current.Dispatcher.InvokeAsync(() => ApplyLivePeakMeasurements(measurements));

                    await AppendCompatibleWlLogAsync(measurements, token);
                    await AppendWavelengthTraceIfDueAsync(measurements, force: false, token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                PeakLoggerStatus = $"Live monitor: {ex.Message}";
            }

            try
            {
                await Task.Delay(_peakLoggerSettings.PollingInterval, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task AppendCompatibleWlLogAsync(IReadOnlyList<PeakLoggerMeasurement> measurements, CancellationToken token)
    {
        CalibrationWlLog? log = _activeWlLog;
        if (log is null || !IsRunning) return;
        var reference = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var device = SelectedF100;
            bool assigned = device is { IsConnected: true } &&
                string.Equals(device.PortName, _activeRun?.ReferenceThermometerPort, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(device.SerialNumber, _activeRun?.ReferenceThermometerSerialNumber, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(device.SelectedChannel, _activeRun?.ReferenceThermometerChannel, StringComparison.OrdinalIgnoreCase);
            return (Temperature: assigned ? device!.Temperature : null, At: assigned ? device!.LastUpdate : null);
        });
        await log.AppendAsync(measurements, reference.Temperature,
            reference.At, _setup.Settings.SampleAcquisitionIntervalSeconds, token);
        if (_activeRun is { } run) _calibrationStore.RequestReplication(run);
    }

    private async Task AppendWavelengthTraceIfDueAsync(
        IReadOnlyList<PeakLoggerMeasurement> measurements,
        bool force,
        CancellationToken token)
    {
        if (!_setup.Settings.EnableWavelengthTraceLogging || !IsRunning) return;

        await _wavelengthTraceGate.WaitAsync(token);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (!force && now < _nextWavelengthTraceAt) return;
            CalibrationRunWriter? writer = _activeWriter;
            CalibrationRunRecord? run = _activeRun;
            if (writer is null || run is null) return;

            List<CalibrationWavelengthTraceSample> trace = BuildTraceSamples(run, measurements);
            if (trace.Count == 0) return;
            await writer.AppendWavelengthTraceAsync(trace, token);
            _nextWavelengthTraceAt = now.AddSeconds(Math.Clamp(_setup.Settings.WavelengthTraceIntervalSeconds, 1, 86400));
        }
        finally
        {
            _wavelengthTraceGate.Release();
        }
    }

    private void ApplyLivePeakMeasurements(IReadOnlyList<PeakLoggerMeasurement> measurements)
    {
        Dictionary<string, PeakLoggerMeasurement> bySource = measurements
            .Where(m => !IsPeakLoggerChannelIgnored(m.Channel))
            .GroupBy(m => $"{m.SerialNumber}|{m.Channel}|{m.PeakId}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var knownSources = new HashSet<string>(
            Peaks.Select(row => $"{row.PeakLoggerDeviceSerialNumber}|{row.Channel}|{row.PeakId}"),
            StringComparer.OrdinalIgnoreCase);
        IReadOnlySet<string> sourcesToAdd = CalibrationPeakTopologyPolicy.SelectNewSources(
                knownSources,
                bySource.Keys,
                IsRunning)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0;
        foreach (KeyValuePair<string, PeakLoggerMeasurement> entry in bySource)
        {
            if (!sourcesToAdd.Contains(entry.Key)) continue;

            PeakLoggerMeasurement measurement = entry.Value;
            var sensor = new PeakLoggerSensor(measurement.SerialNumber, measurement.Channel, Array.Empty<PeakLoggerPeak>());
            var peak = new PeakLoggerPeak(
                measurement.PeakId,
                measurement.PeakIndex,
                measurement.WavelengthNm,
                measurement.Intensity);
            CalibrationPeakRowViewModel row = CreatePeakRow(sensor, peak, _setup.Mappings.FirstOrDefault(m =>
                string.Equals(m.SourceIdentity, entry.Key, StringComparison.OrdinalIgnoreCase)));
            row.UpdateLive(measurement.WavelengthNm, measurement.Intensity, measurement.Timestamp);
            Peaks.Add(row);
            knownSources.Add(entry.Key);
            added++;
        }

        foreach (CalibrationPeakRowViewModel row in Peaks)
        {
            string key = $"{row.PeakLoggerDeviceSerialNumber}|{row.Channel}|{row.PeakId}";
            if (bySource.TryGetValue(key, out PeakLoggerMeasurement? measurement))
            {
                row.UpdateLive(measurement.WavelengthNm, measurement.Intensity, measurement.Timestamp);
            }
            else row.MarkDisconnected();
        }

        if (added > 0)
        {
            int sources = Peaks
                .Select(row => $"{row.PeakLoggerDeviceSerialNumber}|{row.Channel}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            PeakLoggerStatus = $"Pripojený · {sources} zdrojov/kanálov · {Peaks.Count} peakov";
            StatusMessage = added == 1
                ? "Pribudol nový peak. Červený riadok čaká na zadanie FBG sensor SN."
                : $"Pribudli nové peaky ({added}). Červené riadky čakajú na zadanie FBG sensor SN.";
            RefreshCommands();
        }
    }

    private List<CalibrationWavelengthTraceSample> BuildTraceSamples(
        CalibrationRunRecord run,
        IReadOnlyList<PeakLoggerMeasurement> measurements)
    {
        Dictionary<string, CalibrationPeakRowViewModel> selected = Peaks
            .Where(p => p.Selected)
            .GroupBy(p => $"{p.PeakLoggerDeviceSerialNumber}|{p.Channel}|{p.PeakId}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var result = new List<CalibrationWavelengthTraceSample>();
        foreach (PeakLoggerMeasurement measurement in measurements)
        {
            string key = $"{measurement.SerialNumber}|{measurement.Channel}|{measurement.PeakId}";
            if (!selected.TryGetValue(key, out CalibrationPeakRowViewModel? row)) continue;
            result.Add(new CalibrationWavelengthTraceSample
            {
                RunId = run.RunId,
                Timestamp = measurement.Timestamp,
                SerialNumber = row.SerialNumber,
                PeakLoggerDeviceSerialNumber = row.PeakLoggerDeviceSerialNumber,
                Channel = row.Channel,
                PeakId = row.PeakId,
                PeakIndex = row.PeakIndex,
                WavelengthNm = measurement.WavelengthNm,
                Intensity = measurement.Intensity,
                ChamberTemperatureC = _lastChamberTemperatureC,
                ReferenceTemperatureC = _lastReferenceTemperatureC,
            });
        }
        return result;
    }

    private void SelectSuggestedPeaks()
    {
        foreach (IGrouping<string, CalibrationPeakRowViewModel> group in Peaks.GroupBy(p => $"{p.PeakLoggerDeviceSerialNumber}|{p.Channel}"))
        {
            foreach (CalibrationPeakRowViewModel row in group) row.Selected = false;
            CalibrationPeakRowViewModel? saved = group.FirstOrDefault(p => p.WasSavedSelected);
            (saved ?? group.First()).Selected = true;
        }
        StatusMessage = "Pre každý PeakLogger zdroj/kanál bol predvolený jeden peak. Operátor môže výber zmeniť a priradiť produkčné FBG SN.";
        RefreshCommands();
    }

    private void ClearAllPlateaus()
    {
        foreach (CalibrationPointRowViewModel point in CalibrationPoints) point.Selected = false;
        StatusMessage = "Výber všetkých kalibračných plat bol zrušený.";
        StartCalibrationCommand.RaiseCanExecuteChanged();
    }

    private void MarkAllPlateaus()
    {
        foreach (CalibrationPointRowViewModel point in CalibrationPoints) point.Selected = true;
        StatusMessage = "Všetky hold segmenty boli označené ako kalibračné plata.";
        StartCalibrationCommand.RaiseCanExecuteChanged();
    }

    private Task RefreshF100PortsAsync() => LoadDeviceDataAsync(RefreshF100PortsAsyncCore);

    private async Task RefreshF100PortsAsyncCore()
    {
        await RescanF100PortsAsync();
    }

    private async Task RescanF100PortsAsync(bool showStatus = true)
    {
        string? previousPort = SelectedF100?.PortName;
        string? previousUsbSerial = SelectedF100?.SerialNumber;
        await _referenceThermometers.RefreshAsync();
        // Calibration uses explicit one-shot reads. Background polling started by a newly
        // enumerated device would compete for the same serial response and can consume *IDN?.
        foreach (ThermometerDeviceViewModel device in F100Devices)
        {
            device.PollingEnabled = false;
        }
        SelectedF100 = (!string.IsNullOrWhiteSpace(previousUsbSerial)
                ? F100Devices.FirstOrDefault(device =>
                    string.Equals(device.SerialNumber, previousUsbSerial, StringComparison.OrdinalIgnoreCase))
                : null)
            ?? F100Devices.FirstOrDefault(device =>
                string.Equals(device.PortName, previousPort, StringComparison.OrdinalIgnoreCase))
            ?? F100Devices.FirstOrDefault();
        if (showStatus)
        {
            StatusMessage = F100Devices.Count == 0
                ? "USB teplomer: nový scan nenašiel žiadny COM port."
                : $"USB teplomer: nový scan našiel {F100Devices.Count} portov · vybraný {SelectedF100?.PortName}.";
        }
    }

    private void ToggleUsbDiagnostics()
    {
        ShowUsbDiagnostics = !ShowUsbDiagnostics;
        if (ShowUsbDiagnostics) AnalyzeUsb();
    }

    private void AnalyzeUsb()
    {
        UsbDiagnostics.Clear();
        foreach (string line in SerialPortEnumerator.DiagnoseUsb()) UsbDiagnostics.Add(line);
        StatusMessage = $"USB diagnostika dokončená · {UsbDiagnostics.Count} záznamov.";
    }

    private async Task DiagnoseF100TalkOnlyAsync()
    {
        StatusMessage = "Pasívna diagnostika F100: skenujem porty a čakám na talk-only dáta…";
        foreach (ThermometerDeviceViewModel connected in F100Devices.Where(device => device.IsConnected).ToList())
        {
            await connected.DisposeAsync();
        }
        await RescanF100PortsAsync(showStatus: false);
        AnalyzeUsb();

        List<SerialDeviceInfo> candidates = F100Devices
            .Where(device => ReferenceEquals(device, SelectedF100)
                || !string.IsNullOrWhiteSpace(device.SerialNumber)
                || (device.Info.Description?.Contains("USB Serial", StringComparison.OrdinalIgnoreCase) ?? false)
                || (device.Info.Description?.Contains("FTDI", StringComparison.OrdinalIgnoreCase) ?? false)
                || (device.Info.Description?.Contains("F100", StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(device => device.Info)
            .ToList();
        if (candidates.Count == 0 && SelectedF100 is not null)
        {
            candidates.Add(SelectedF100.Info);
        }

        UsbDiagnostics.Add("— Pasívny test F100 (bez odoslania príkazov) —");
        IReadOnlyList<string> results = await SerialPortEnumerator.DiagnoseTalkOnlyAsync(candidates);
        foreach (string line in results)
        {
            UsbDiagnostics.Add(line);
            AppLog.Info("F100 diagnostika", line);
        }

        bool hasData = results.Any(line => line.Contains("DATA OK", StringComparison.Ordinal));
        StatusMessage = hasData
            ? "Pasívna diagnostika F100: talk-only dáta boli nájdené."
            : "Pasívna diagnostika F100: port je dostupný, ale dáta neprišli. Skontroluj na F100 Options → Talk Only → On.";
    }

    private void AddManualF100Port()
    {
        try
        {
            SelectedF100 = _referenceThermometers.AddManualPort(ManualF100Port);
            StatusMessage = $"Port {SelectedF100.PortName} bol pridaný ručne. Stlač Načítať teplotu alebo Vynútiť pripojenie.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ručný port sa nepodarilo pridať: {ex.Message}";
        }
    }

    private async Task ForceReconnectF100Async()
    {
        await RescanF100PortsAsync(showStatus: false);
        if (SelectedF100 is null) return;
        bool acquired = EnsureF100Reservation();
        SelectedF100.SelectedChannel = SelectedF100Channel;
        StatusMessage = $"Vynucujem nové pripojenie {SelectedF100.PortName}…";
        double? value;
        try
        {
            value = await SelectedF100.ForceReconnectAsync();
        }
        catch
        {
            if (acquired) ReleaseF100Reservation();
            throw;
        }
        OnPropertyChanged(nameof(F100TemperatureLabel));
        OnPropertyChanged(nameof(F100ConnectionLabel));
        StatusMessage = value is { } t
            ? $"WIKA CTH7000 znovu pripojený · {SelectedF100.PortName} · {t:F3} °C"
            : $"Port {SelectedF100.PortName} sa otvoril, ale WIKA CTH7000 neposlal platnú teplotu.";
    }

    private Task CheckF100Async() => LoadDeviceDataAsync(CheckF100AsyncCore);

    private async Task CheckF100AsyncCore()
    {
        await RescanF100PortsAsync(showStatus: false);
        if (SelectedF100 is null) return;
        SelectedF100.ResumeAutomaticConnection();
        bool acquired = EnsureF100Reservation();
        SelectedF100.SelectedChannel = SelectedF100Channel;
        StatusMessage = $"Čítam referenčný teplomer · {SelectedF100.PortName} · kanál {SelectedF100Channel}…";
        double? value;
        try
        {
            value = await SelectedF100.CheckAsync();
            SelectedF100Channel = SelectedF100.SelectedChannel;
            OnPropertyChanged(nameof(ReferenceThermometerTitle));
        }
        catch
        {
            if (acquired) ReleaseF100Reservation();
            throw;
        }
        _lastReferenceTemperatureC = value;
        double? chamberTemperature = await ReadCurrentChamberTemperatureAsync();
        _lastChamberTemperatureC = chamberTemperature ?? _lastChamberTemperatureC;
        ReferenceTemperatureLabel = value is { } t ? $"{t:F3} °C" : "—";
        OnPropertyChanged(nameof(F100TemperatureLabel));
        OnPropertyChanged(nameof(F100ConnectionLabel));
        StatusMessage = value is { } temperature
            ? $"{SelectedF100.DeviceName} OK · {SelectedF100.PortName} · kanál {SelectedF100Channel} · {temperature:F3} °C"
            : $"{SelectedF100.PortName}: teplomer nevrátil platnú teplotu.";
        if (value is { } reference && chamberTemperature is { } chamber)
        {
            await ValidateReferenceTemperatureAsync(chamber, reference);
        }
    }

    private void StartReferenceTrace()
    {
        if (SelectedF100 is not { } device) return;
        EnsureF100Reservation();
        device.SelectedChannel = SelectedF100Channel;
        CalibrationReferenceTraceStore.Instance.BeginRun(_workspaceChamberId, DateTimeOffset.Now);
    }

    private Task StopReferenceTraceAsync()
    {
        CalibrationReferenceTraceStore.Instance.EndRun(_workspaceChamberId);
        return Task.CompletedTask;
    }

    private async Task<double?> ReadReferenceTemperatureAsync(CancellationToken token)
    {
        if (SelectedF100 is null) return null;
        EnsureF100Reservation();
        SelectedF100.SelectedChannel = SelectedF100Channel;
        double? value = await SelectedF100.ReadReferenceTemperatureAsync(token);
        _lastReferenceTemperatureC = value;
        if (value is { } traceTemperature && double.IsFinite(traceTemperature))
            CalibrationReferenceTraceStore.Instance.AppendRunSample(_workspaceChamberId,
                new(DateTimeOffset.Now, traceTemperature, SelectedF100.PortName, SelectedF100.SelectedChannel));
        // The calibration runner has already read the chamber at the beginning of this
        // sampling cycle. Reusing that fresh value avoids issuing a second chamber command
        // while the first cycle after checkpoint recovery is still being established.
        double? currentChamberTemperature = _lastChamberTemperatureC;
        if (value is { } reference && currentChamberTemperature is { } chamber)
        {
            await ValidateReferenceTemperatureAsync(chamber, reference, token);
        }
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ReferenceTemperatureLabel = value is { } t ? $"{t:F3} °C" : "—";
            OnPropertyChanged(nameof(F100TemperatureLabel));
            OnPropertyChanged(nameof(F100ConnectionLabel));
        });
        return value;
    }

    private async Task<double?> ReadCurrentChamberTemperatureAsync(CancellationToken cancellationToken = default)
    {
        if (_chamber is not null)
        {
            return (await _chamber.ReadAsync(cancellationToken)).Temperature;
        }

        if (SelectedChamber is null)
        {
            return null;
        }

        await using IChamberDevice chamber = CreateChamberClient(SelectedChamber.Config);
        await chamber.ConnectAsync(ToConnectionSettings(SelectedChamber.Config), cancellationToken);
        try
        {
            return (await chamber.ReadAsync(cancellationToken)).Temperature;
        }
        finally
        {
            try { await chamber.DisconnectAsync(); } catch { }
        }
    }

    private async Task ValidateReferenceTemperatureAsync(
        double chamberTemperature,
        double referenceTemperature,
        CancellationToken cancellationToken = default)
    {
        RefreshEmailSettings();
        EmailSettings settings = _email.Settings;
        if (!settings.ReferenceTemperatureMismatchAlertsEnabled)
        {
            return;
        }

        double limit = Math.Max(0.1, Math.Abs(settings.ReferenceTemperatureMismatchLimitC));
        double difference = Math.Abs(referenceTemperature - chamberTemperature);
        if (difference <= limit)
        {
            if (_referenceMismatchWarningActive)
            {
                string resolution = $"Teploty sú opäť v zhode: WIKA {referenceTemperature:F3} °C · " +
                                    $"komora {chamberTemperature:F3} °C · rozdiel {difference:F3} °C / ±{limit:F1} °C.";
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _referenceMismatchWarningActive = false;
                    if (WarningText.StartsWith("CHYBA TEPLOTY:", StringComparison.Ordinal))
                    {
                        WarningText = string.Empty;
                    }
                    if (StatusMessage.StartsWith("CHYBA TEPLOTY:", StringComparison.Ordinal))
                    {
                        StatusMessage = resolution;
                    }
                    Dashboard.ResolveWarning("CHYBA TEPLOTY:", resolution, DateTimeOffset.Now);
                });
                AppLog.Info("WIKA kontrola", resolution);
            }
            return;
        }

        string message = $"CHYBA TEPLOTY: WIKA CTH7000 {referenceTemperature:F3} °C sa nezhoduje s komorou " +
                         $"{chamberTemperature:F3} °C. Rozdiel {difference:F3} °C prekročil povolených ±{limit:F1} °C.";
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _referenceMismatchWarningActive = true;
            WarningText = message;
            StatusMessage = message;
        });
        AppLog.Warn("F100 kontrola", message);

        int cooldownMinutes = Math.Max(1, settings.ReferenceTemperatureMismatchEmailCooldownMinutes);
        DateTimeOffset now = DateTimeOffset.Now;
        if (_lastReferenceMismatchEmailAt is { } last && now - last < TimeSpan.FromMinutes(cooldownMinutes))
        {
            return;
        }

        EmailResult result = await _email.SendAsync(NotificationType.ReferenceTemperatureMismatch,
            $"CHYBA – rozdiel teploty F100 a komory – {SelectedChamber?.Config.Name ?? "komora"}",
            $"Komora: {SelectedChamber?.Config.Name}\nWIKA: {SelectedF100?.PortName} / kanál {SelectedF100Channel}\nČas: {now:yyyy-MM-dd HH:mm:ss}\n\n{message}",
            cancellationToken: cancellationToken);
        if (result.Sent)
        {
            _lastReferenceMismatchEmailAt = now;
        }
        else if (result.Error is { } error)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                WarningText = $"{message} E-mail sa nepodarilo odoslať: {error}");
        }
    }

    private string _setupSaveStatus = "Automatické ukladanie";
    public string SetupSaveStatus { get => _setupSaveStatus; private set => SetProperty(ref _setupSaveStatus, value); }
    private string _setupSaveDetail = "Zmeny zapojenia sa ukladajú automaticky po 350 ms. Ikona uloží zapojenie okamžite.";
    public string SetupSaveDetail { get => _setupSaveDetail; private set => SetProperty(ref _setupSaveDetail, value); }
    private string _setupSaveColor = "#AAB7CE";
    public string SetupSaveColor { get => _setupSaveColor; private set => SetProperty(ref _setupSaveColor, value); }

    public void ResetWiring()
    {
        if (IsRunning) throw new InvalidOperationException("Počas kalibrácie nemožno vymazať zapojenie.");
        if (_resumeCheckpoint is not null || (SelectedChamber is not null && _calibrationStore.LoadCheckpoint(SelectedChamber.Config.Id) is not null))
            throw new InvalidOperationException("Najprv ukonči rozpracovaný beh cez Ukončiť a začať odznova. Jeho zapojenie je potrebné pre pokračovanie.");
        Peaks.Clear();
        _setup.Mappings.Clear();
        PersistSetup(showStatus: false);
        ValidateSerialNumbers();
        NotifyPeakCounts();
        RefreshCommands();
        StatusMessage = "Zapojenie bolo vymazané. Obnov peaky alebo pripoj snímače a zadaj nové SN.";
    }

    private int _deviceLoadCount;
    public bool IsLoadingDeviceData => _deviceLoadCount > 0;

    private async Task LoadDeviceDataAsync(Func<Task> load)
    {
        _deviceLoadCount++;
        OnPropertyChanged(nameof(IsLoadingDeviceData));
        RefreshCommands();
        DiscoverPeakLoggerApisCommand.RaiseCanExecuteChanged();
        try { await load(); }
        finally
        {
            _deviceLoadCount--;
            OnPropertyChanged(nameof(IsLoadingDeviceData));
            RefreshCommands();
            DiscoverPeakLoggerApisCommand.RaiseCanExecuteChanged();
        }
    }

    private void SaveSetup() => PersistSetup(showStatus: true);

    private void PersistSetup(bool showStatus)
    {
        if (SelectedProfile is null) return;
        SetupSaveStatus = "Ukladá sa…";
        SetupSaveColor = "#FFD27D";
        try
        {
            PersistSetupCore(showStatus);
            SetupSaveStatus = $"Uložené o {DateTime.Now:HH:mm:ss}";
            SetupSaveColor = "#55D6A0";
            SetupSaveDetail = "Zapojenie bolo uložené. Ďalšie zmeny sa uložia automaticky; ikona vynúti okamžité uloženie.";
        }
        catch (Exception ex)
        {
            SetupSaveStatus = "Chyba uloženia";
            SetupSaveColor = "#FF7885";
            SetupSaveDetail = $"Zapojenie sa nepodarilo uložiť: {ex.Message}. Uloženie zopakujte ikonou.";
            throw;
        }
    }

    private void PersistSetupCore(bool showStatus)
    {
        if (SelectedProfile is null) return;
        _setup.ProfileId = SelectedProfile.Id;
        _setup.ChamberId = SelectedChamber?.Config.Id ?? _workspaceChamberId;
        _setup.Mappings = _setup.Mappings.Where(m => IsPeakLoggerChannelIgnored(m.Channel))
            .Concat(Peaks.Where(p => !IsPeakLoggerChannelIgnored(p.Channel)).Select(p => p.ToMapping()))
            .GroupBy(m => m.SourceIdentity, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToList();
        CalibrationCheckpoint? checkpoint = _resumeCheckpoint;
        if (checkpoint is null && SelectedChamber is not null)
        {
            CalibrationCheckpoint? storedCheckpoint = _calibrationStore.LoadCheckpoint(SelectedChamber.Config.Id);
            if (storedCheckpoint is not null && storedCheckpoint.ProfileId == SelectedProfile.Id &&
                storedCheckpoint.CompletedPlateaus.Count > 0)
            {
                checkpoint = storedCheckpoint;
            }
        }
        if (checkpoint is not null && CalibrationCheckpointRecovery.RestoreMappingsIfMissing(_setup, checkpoint))
        {
            _resumeCheckpoint = checkpoint;
            ApplyRecoveredMappingsToVisiblePeaks(_setup.Mappings);
        }
        _setup.CalibrationSegmentIndices = CalibrationPoints
            .Where(point => point.Selected)
            .Select(point => point.SegmentIndex)
            .ToList();
        _calibrationStore.SaveSetup(_setup);

        SelectedProfile.ExecutionMode = ProfileExecutionMode.TemperatureCalibration;
        _profileStore.Save(SelectedProfile);
        if (showStatus)
        {
            StatusMessage = $"Kalibračné zapojenie pre profil „{SelectedProfile.Name}“ bolo uložené.";
        }
        RefreshCommands();
    }

    private bool CanStartCalibration()
    {
        if (IsRunning || _freshStartPending || (!UseSimulator && SelectedF100?.IsManuallyDisconnected == true) || (!UseSimulator && !PeakLoggerConnected) || SelectedProfile is null || SelectedChamber is null ||
            !CalibrationPoints.Any(p => p.Selected))
        {
            return false;
        }

        List<CalibrationPeakRowViewModel> selected = Peaks.Where(p => p.Selected).ToList();
        return selected.Count > 0 && selected.All(p => !string.IsNullOrWhiteSpace(p.SerialNumber));
    }

    private bool CanResumeCalibration() =>
        HasResumableCalibration && ResumeHardwareReady && CanStartCalibration();

    private bool ResumeReferenceIsReady()
    {
        if (SelectedF100 is not { IsConnected: true, Temperature: { } temperature } ||
            !double.IsFinite(temperature))
        {
            return false;
        }

        if (_resumeReferenceSerialNumber.Length > 0 && !string.IsNullOrWhiteSpace(SelectedF100.SerialNumber))
        {
            return string.Equals(SelectedF100.SerialNumber, _resumeReferenceSerialNumber, StringComparison.OrdinalIgnoreCase);
        }

        return _resumeReferencePort.Length == 0 ||
               string.Equals(SelectedF100.PortName, _resumeReferencePort, StringComparison.OrdinalIgnoreCase);
    }

    private void ObserveResumeReference(ThermometerDeviceViewModel? device)
    {
        if (ReferenceEquals(_resumeObservedF100, device)) return;
        if (_resumeObservedF100 is not null)
            _resumeObservedF100.PropertyChanged -= OnResumeReferencePropertyChanged;
        _resumeObservedF100 = device;
        if (_resumeObservedF100 is not null)
            _resumeObservedF100.PropertyChanged += OnResumeReferencePropertyChanged;
    }

    private void OnResumeReferencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ThermometerDeviceViewModel.IsConnected)
            or nameof(ThermometerDeviceViewModel.Temperature)
            or nameof(ThermometerDeviceViewModel.SerialNumber))
        {
            RefreshResumeReadiness();
        }
    }

    private void RefreshResumeReadiness()
    {
        OnPropertyChanged(nameof(ResumeHardwareReady));
        OnPropertyChanged(nameof(ResumeHardwareStatus));
        OnPropertyChanged(nameof(ResumeCalibrationDetail));
        ResumeCalibrationCommand.RaiseCanExecuteChanged();
    }

    private void ApplyCurrentDefaultsToResume()
    {
        if (_resumeCheckpoint is null || IsRunning) return;

        MessageBoxResult answer = MessageBox.Show(
            "Aktuálne administrátorské predvoľby nahradia pôvodné rozhodovacie limity obnoveného behu.\n\n" +
            "Dokončené plata a zapojenie zostanú zachované. Rozpracované plato sa po pokračovaní stabilizuje a zmeria odznova. Zmena bude zapísaná do histórie behu.\n\n" +
            "Použiť aktuálne predvolené nastavenia?",
            "Predvoľby pre obnovenú kalibráciu",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        CalibrationProfileSettings defaults = _calibrationDefaultsStore.Load();
        CalibrationCheckpointRecovery.ApplyCurrentDefaults(_setup, _resumeCheckpoint, defaults);
        _calibrationStore.SaveCheckpoint(_resumeCheckpoint);
        _calibrationStore.SaveSetup(_setup);

        CalibrationRunRecord? run = _calibrationStore.LoadRun(_resumeCheckpoint.RunId);
        if (run is not null)
        {
            run.Warnings.Add(new CalibrationWarning
            {
                Code = "RECOVERY_DEFAULTS_APPLIED",
                Message = $"Operátor pred pokračovaním použil aktuálne predvolené nastavenia: " +
                          $"WIKA tolerancia ±{defaults.ChamberToleranceC:0.###} °C, stabilný čas {defaults.ChamberStableDuration.TotalMinutes:0.###} min, " +
                          $"WIKA rozsah {defaults.MaxChamberRangeC:0.###} °C, σ {defaults.MaxChamberStdDevC:0.###} °C, " +
                          $"drift {defaults.MaxChamberDriftCPerMinute:0.###} °C/min, FBG interval {defaults.SampleAcquisitionIntervalSeconds} s, " +
                          $"stabilita {defaults.RequiredStableSamples} vzoriek, finálne meranie {defaults.RequiredMeasurementSamples} vzoriek, " +
                          $"range {defaults.MaxWavelengthRangePm:0.###} pm, σ {defaults.MaxWavelengthStdDevPm:0.###} pm, " +
                          $"drift {defaults.MaxWavelengthDriftPmPerMinute:0.###} pm/min.",
                PlateauIndex = _resumeCheckpoint.CurrentPlateauIndex,
                Overridden = true,
                OverrideReason = "Aktuálne administrátorské predvoľby potvrdené operátorom pred obnovením.",
            });
            _calibrationStore.SaveRun(run);
        }

        RefreshSettingsBindings();
        RefreshDashboardPlan();
        StatusMessage = "Aktuálne predvolené nastavenia boli použité a uložené do obnovenej kalibrácie. Dokončené plata zostali zachované.";
        AppLog.Warn("FBG kalibrácia", $"Checkpoint {_resumeCheckpoint.RunId:N}: operátor použil aktuálne administrátorské predvoľby pred pokračovaním.");
    }

    private async Task StartCalibrationAsync()
        => await StartCalibrationAsync(resumeFromCheckpoint: false);

    private async Task ResumeCalibrationAsync()
        => await StartCalibrationAsync(resumeFromCheckpoint: true);

    private async Task StartCalibrationAsync(bool resumeFromCheckpoint)
    {
        Task session = RunCalibrationSessionAsync(resumeFromCheckpoint);
        _runSessionTask = session;
        try { await session; }
        finally { if (ReferenceEquals(_runSessionTask, session)) _runSessionTask = null; }
    }

    private async Task RunCalibrationSessionAsync(bool resumeFromCheckpoint)
    {
        if (!CanStartCalibration() || SelectedProfile is null || SelectedChamber is null) return;
        if (_peakLogger is null && UseSimulator)
        {
            await ConnectPeakLoggerAsync();
        }
        if (_peakLogger is null) return;
        _calibrationStore = CalibrationStorage.CreateStore();
        CalibrationCheckpoint? resume = resumeFromCheckpoint ? _resumeCheckpoint : null;
        if (resumeFromCheckpoint && resume is null)
            throw new InvalidOperationException("Uložený checkpoint pre vybraný profil a komoru už nie je dostupný.");
        if (_setup.Settings.OperatorSupervisionEnabled)
        {
            RefreshEmailSettings();
            if (!_email.CanSendType(NotificationType.CalibrationWarning))
                throw new InvalidOperationException("Operátorský dohľad vyžaduje e-mail: v Administrácii zapnite e-mailové upozornenia kalibrácie a nastavte adresáta.");
        }
        _calibrationDefaultsStore.ApplyAcquisitionInterval(_setup.Settings, preserveRunSettings: resumeFromCheckpoint);
        RefreshSettingsBindings();
        RefreshDashboardPlan();
        await RescanF100PortsAsync(showStatus: false);
        if (SelectedF100 is not null) EnsureF100Reservation();
        ThermometerDeviceViewModel? runReference = SelectedF100;
        SaveSetup();
        WarningText = string.Empty;
        TargetProgress.Clear();
        _runCts = new CancellationTokenSource();
        _stopRequested = false;
        _temperatureGateOverridePending = false;
        _calibrationProgressPercent = 0;
        Dashboard.ResetPlan();
        RefreshDashboardPlan();
        Dashboard.Begin(DateTimeOffset.Now);
        if (resume is not null)
        {
            Dashboard.RestoreCompletedPoints(resume.CompletedPlateaus);
        }
        RunState = CalibrationRunState.Preflight.ToString();
        IsRunning = true;
        _lastChamberTemperatureC = null;
        _lastReferenceTemperatureC = SelectedF100?.Temperature;

        try
        {
            StartReferenceTrace();
            _chamber = CreateChamberClient(SelectedChamber.Config);
            ChamberConnectionSettings connection = ToConnectionSettings(SelectedChamber.Config);
            StatusMessage = $"Pripájam komoru {SelectedChamber.Config.Name}…";
            Dashboard.ReportStartup(StatusMessage);
            await _chamber.ConnectAsync(connection, _runCts.Token);
            Dashboard.ReportStartup("Komora je pripojená. Čaká sa na prvú nameranú teplotu komory.");
            var initialReading = await _chamber.ReadAsync(_runCts.Token);
            double startTemperature = initialReading.Temperature
                ?? throw new InvalidOperationException("Komora neposkytla platnú nameranú teplotu pred začiatkom kalibrácie.");
            _lastChamberTemperatureC = startTemperature;
            await Application.Current.Dispatcher.InvokeAsync(() =>
                Dashboard.ReportChamberTemperature(startTemperature, initialReading.Timestamp));

            DateTimeOffset runStartedAt = DateTimeOffset.Now;
            _activeRun = resume is null ? new CalibrationRunRecord
            {
                HumanRunId = HumanReadableRunId.Allocate(AppPaths.CalibrationDir, runStartedAt),
                ProfileId = SelectedProfile.Id,
                ProfileCode = SelectedProfile.Code,
                ProfileName = SelectedProfile.Name,
                ChamberId = SelectedChamber.Config.Id,
                ChamberName = SelectedChamber.Config.Name,
                Operator = Environment.UserName,
                StartedAt = runStartedAt,
                State = CalibrationRunState.Preflight,
                ReferenceThermometerPort = SelectedF100?.PortName ?? string.Empty,
                ReferenceThermometerSerialNumber = SelectedF100?.SerialNumber ?? string.Empty,
                ReferenceThermometerChannel = SelectedF100 is null ? string.Empty : SelectedF100Channel,
            } : _calibrationStore.LoadRun(resume.RunId)
                ?? throw new InvalidOperationException("Súhrn prerušeného kalibračného behu sa nenašiel. Checkpoint nebol zmazaný.");
            _activeRun.CompletedAt = null;
            _activeRun.State = CalibrationRunState.Preflight;
            Dashboard.SetRunId(_activeRun.DisplayRunId);

            _nextWavelengthTraceAt = DateTimeOffset.MaxValue;
            await using CalibrationRunWriter writer = await Task.Run(() => _calibrationStore.CreateRunWriter(_activeRun, append: resume is not null));
            _activeWriter = writer;
            await using CalibrationWlLog wlLog = await Task.Run(() => new CalibrationWlLog(
                _calibrationStore.GetRunDirectory(_activeRun), _activeRun, _setup,
                message => writer.WriteDiagnostic("WARNING", "WLN_LOG", message)));
            _activeWlLog = wlLog;
            writer.WriteDiagnostic("INFO", "WLN_LOG_CREATED",
                $"files={string.Join(';', wlLog.Paths)}; intervalSeconds={_setup.Settings.SampleAcquisitionIntervalSeconds}; referenceMaxAgeSeconds=10");
            CalibrationTerminalLines.Clear();
            CalibrationTerminalLines.Add($"{DateTimeOffset.Now:HH:mm:ss.fff}  RUN  {_activeRun.DisplayRunId}  {writer.DiagnosticFilePath}");
            writer.DiagnosticWritten += line => _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CalibrationTerminalLines.Add(line);
                while (CalibrationTerminalLines.Count > 500) CalibrationTerminalLines.RemoveAt(0);
            });
            CalibrationProfileSettings diagnosticSettings = _setup.Settings;
            writer.WriteDiagnostic("INFO", "RUN_STARTED",
                $"version={GetType().Assembly.GetName().Version?.ToString(3)}; profileId={SelectedProfile.Id:N}; profile={SelectedProfile.Code}|{SelectedProfile.Name}; " +
                $"chamber={SelectedChamber.Config.Name}; wikaPort={SelectedF100?.PortName ?? "none"}; wikaSerial={SelectedF100?.SerialNumber ?? "none"}; wikaChannel={(SelectedF100 is null ? "none" : SelectedF100Channel)}; " +
                $"peakLogger={PeakLoggerHost}:{PeakLoggerPort}; simulator={UseSimulator}; selectedPeaks={Peaks.Count(p => p.Selected)}; plateaus={string.Join(',', _setup.CalibrationSegmentIndices)}");
            if (resume is not null)
                writer.WriteDiagnostic("INFO", "RUN_RESUMED_FROM_CHECKPOINT",
                    $"savedAt={resume.SavedAt:O}; completedPlateaus={resume.CompletedPlateaus.Count}; nextPlateau={resume.CompletedPlateaus.Count + 1}");
            writer.WriteDiagnostic("INFO", "STABILITY_CONFIGURATION",
                $"temperatureToleranceC={diagnosticSettings.ChamberToleranceC:G17}; temperatureStableSeconds={diagnosticSettings.ChamberStableDuration.TotalSeconds:G17}; " +
                $"temperatureMaxDriftCPerMinute={diagnosticSettings.MaxChamberDriftCPerMinute:G17}; temperatureMaxRangeC={diagnosticSettings.MaxChamberRangeC:G17}; " +
                $"temperatureMaxStdDevC={diagnosticSettings.MaxChamberStdDevC:G17}; temperatureTimeoutSeconds={diagnosticSettings.ChamberStabilityTimeout.TotalSeconds:G17}; " +
                $"temperatureExtensionStepSeconds={diagnosticSettings.ChamberStabilityExtensionStep.TotalSeconds:G17}; temperatureMaxExtensionSeconds={diagnosticSettings.MaxAutomaticChamberStabilityExtension.TotalSeconds:G17}; " +
                $"wavelengthStableSamples={diagnosticSettings.RequiredStableSamples}; measurementSamples={diagnosticSettings.RequiredMeasurementSamples}; sampleIntervalSeconds={diagnosticSettings.SampleAcquisitionIntervalSeconds}; " +
                $"rangeLimitPm={diagnosticSettings.MaxWavelengthRangePm:G17}; stdDevLimitPm={diagnosticSettings.MaxWavelengthStdDevPm:G17}; driftLimitPmPerMinute={diagnosticSettings.MaxWavelengthDriftPmPerMinute:G17}");
            writer.WriteDiagnostic("INFO", "DIAGNOSTIC_SAMPLING",
                $"progressIntervalSeconds={ProgressDiagnosticInterval.TotalSeconds:G17}; stateChangesLoggedImmediately=true");
            foreach (CalibrationPeakRowViewModel peak in Peaks.Where(p => p.Selected))
                writer.WriteDiagnostic("INFO", "SELECTED_PEAK",
                    $"sn={peak.SerialNumber}; device={peak.PeakLoggerDeviceSerialNumber}; channel={peak.Channel}; peak={peak.PeakId}; index={peak.PeakIndex}; wavelengthNm={peak.CurrentWavelengthNm:G17}; intensity={peak.Intensity:G17}");
            AppLog.Info("FBG kalibrácia", $"Run {_activeRun.DisplayRunId}: úplný diagnostický log {writer.DiagnosticFilePath}");
            if (_setup.Settings.EnableWavelengthTraceLogging)
            {
                Dashboard.ReportStartup("Čaká sa na prvé merania PeakLoggera pre záznam priebehu.");
                IReadOnlyList<PeakLoggerMeasurement> firstMeasurements = await _peakLogger.ReadMeasurementsAsync(_runCts.Token);
                await Application.Current.Dispatcher.InvokeAsync(() => ApplyLivePeakMeasurements(firstMeasurements));
                await AppendWavelengthTraceIfDueAsync(firstMeasurements, force: true, _runCts.Token);
            }
            var runPeakLogger = _peakLogger;
            var runPeakLoggerSettings = _peakLoggerSettings;
            var orchestrator = new CalibrationOrchestrator(runPeakLogger)
            {
                MeasurementBatchObserved = async (batch, reference, referenceAt, token) =>
                {
                    // Use the actual WIKA reading timestamp and verify its original assignment,
                    // just as the live monitor does; never substitute a chamber/simulator value.
                    await AppendCompatibleWlLogAsync(batch, token);
                },
                ReconnectPeakLoggerAsync = token => runPeakLogger.ConnectAsync(runPeakLoggerSettings, token),
                ReconnectChamberAsync = async token =>
                {
                    await _chamber.DisconnectAsync();
                    await _chamber.ConnectAsync(connection, token);
                }
            };
            orchestrator.OperatorAttentionRequired += OnOperatorAttentionRequired;
            orchestrator.WarningRaised += warning =>
            {
                writer.WriteDiagnostic("WARNING", warning.Code, warning.Message);
                AppLog.Warn("FBG kalibrácia", $"Run {_activeRun?.DisplayRunId}: {warning.Code} · {warning.Message}");
                _ = Application.Current.Dispatcher.InvokeAsync(() => WarningText = warning.Message);
                // Non-blocking warnings stay in history and the completion report.
                // Notify once only when the runner actually stops below.
            };
            _runner = new CalibrationProfileRunner(_chamber, orchestrator, _calibrationStore);
            DateTimeOffset nextProgressDiagnosticAt = DateTimeOffset.MinValue;
            string? lastProgressDiagnosticState = null;
            object progressDiagnosticSync = new();
            _runner.Progress += snapshot =>
            {
                string diagnosticState = ProgressDiagnosticState(snapshot);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                lock (progressDiagnosticSync)
                {
                    if (now >= nextProgressDiagnosticAt ||
                        !string.Equals(diagnosticState, lastProgressDiagnosticState, StringComparison.Ordinal))
                    {
                        WriteProgressDiagnostic(writer, snapshot);
                        nextProgressDiagnosticAt = now + ProgressDiagnosticInterval;
                        lastProgressDiagnosticState = diagnosticState;
                    }
                }
                _ = Application.Current.Dispatcher.InvokeAsync(() => ApplyProgress(snapshot));
            };

            StatusMessage = SelectedF100 is null
                ? "Kalibrácia spustená bez externého WIKA CTH7000. Najskôr prebehne preflight a kontrola PeakLoggera."
                : $"Kalibrácia spustená · referencia WIKA CTH7000 {SelectedF100.PortName}/{SelectedF100Channel}. Najskôr prebehne preflight.";
            try
            {
                await _runner.RunAsync(
                    SelectedProfile,
                    _setup,
                    _activeRun,
                    writer,
                    startTemperature,
                    null,
                    SelectedF100 is null ? null : ReadReferenceTemperatureAsync,
                    _runCts.Token,
                    resume);
            }
            catch (Exception ex)
            {
                // This catch is deliberately inside the writer lifetime. An await-using
                // declared in the outer try is disposed before the outer catch executes.
                // Logging here preserves the original device/runner exception.
                writer.WriteDiagnostic("ERROR", "RUN_FAILED", ex.ToString());
                throw;
            }

            writer.WriteDiagnostic("INFO", "RUN_FINISHED", $"state={_activeRun.State}; plateaus={_activeRun.Plateaus.Count}; warnings={_activeRun.Warnings.Count}");

            RunState = _activeRun.State.ToString();
            StatusMessage = _activeRun.State == CalibrationRunState.Completed
                ? "Kalibrácia úspešne dokončená."
                : "Kalibrácia dokončená s upozorneniami.";
            await SendCompletionEmailAsync(_activeRun);
            _calibrationStore.RequestReplication(_activeRun);
        }
        catch (CalibrationSupervisionStoppedException ex)
        {
            RunState = CalibrationRunState.Aborted.ToString();
            StatusMessage = "Operátorský dohľad ukončil beh; STOP komory bol potvrdený. " + ex.Message;
            await NotifyRunInterruptionAsync(new CalibrationWarning { Code = "SUPERVISION_STOPPED", Message = StatusMessage });
        }
        catch (CalibrationOperatorActionRequiredException ex)
        {
            // The runner exception was logged inside the writer lifetime.
            AppLog.Warn("FBG kalibrácia", $"Run {_activeRun?.DisplayRunId}: vyžaduje zásah operátora · {ex.Message}");
            WarningText = ex.Message;
            RunState = CalibrationRunState.AwaitingOperator.ToString();
            await NotifyRunInterruptionAsync(ex.Warning);
            StatusMessage = "Kalibrácia čaká na zásah operátora. Oprav výber/limity alebo povoľ zdôvodnený override a spusti kontrolu znovu.";
        }
        catch (OperationCanceledException)
        {
            _activeWriter?.WriteDiagnostic("WARNING", "RUN_CANCELLED", $"stopRequested={_stopRequested}");
            AppLog.Warn("FBG kalibrácia", $"Run {_activeRun?.DisplayRunId}: kalibrácia zrušená; stopRequested={_stopRequested}.");
            StatusMessage = "Kalibrácia bola zastavená operátorom.";
            RunState = CalibrationRunState.Aborted.ToString();
        }
        catch (Exception ex)
        {
            AppLog.Error("FBG kalibrácia", $"Run {_activeRun?.DisplayRunId}: {ex}");
            RunState = CalibrationRunState.Failed.ToString();
            StatusMessage = ex.Message;
            await NotifyRunInterruptionAsync(new CalibrationWarning { Code = "RUN_FAILED", Message = ex.Message });
            throw;
        }
        finally
        {
            await StopReferenceTraceAsync();
            Dashboard.End(Enum.TryParse<CalibrationRunState>(RunState, out var finalState) ? finalState : CalibrationRunState.Failed,
                StatusMessage, DateTimeOffset.Now);
            _activeWriter = null;
            _activeWlLog = null;
            _runner = null;
            if (_freshStartPending && _chamber is null)
            {
                _freshStartPending = false;
                StatusMessage = "STOP komory sa nedá potvrdiť bez pripojenia. Rozpracovaný beh zostal uložený.";
            }
            if (_chamber is not null)
            {
                if (_stopRequested)
                {
                    try { await _chamber.StopAsync(); }
                    catch (Exception stopError)
                    {
                        if (_freshStartPending)
                        {
                            _freshStartPending = false;
                            StatusMessage = "STOP komory sa nepodarilo potvrdiť. Rozpracovaný beh zostal uložený: " + stopError.Message;
                        }
                    }
                }
                try { await _chamber.DisconnectAsync(); } catch { }
                await _chamber.DisposeAsync();
                _chamber = null;
            }
            IsRunning = false;
            // A failed run must keep automatic reference reads available for checkpoint resume.
            // Only a completed or deliberately stopped run pauses reference reconnects.
            // The runner and reference trace have stopped. Reuse the explicit-disconnect
            // path: pause background reconnects, drain serial IO, return LOCAL and release COM.
            // Preserve the persistent chamber/reference assignment for the next run.
            if (runReference is not null &&
                (RunState == CalibrationRunState.Completed.ToString() ||
                 RunState == CalibrationRunState.CompletedWithWarnings.ToString() ||
                 RunState == CalibrationRunState.Aborted.ToString()))
            {
                try
                {
                    await runReference.DisconnectAsync();
                    ReferenceTemperatureLabel = "—";
                    AppLog.Info("FBG kalibrácia", $"WIKA {runReference.PortName}: po ukončení behu odpojená; COM port uvoľnený.");
                }
                catch (Exception referenceError)
                {
                    AppLog.Warn("FBG kalibrácia", $"WIKA {runReference.PortName}: uvoľnenie portu zlyhalo · {referenceError.Message}");
                    WarningText = $"WIKA sa po ukončení nepodarilo odpojiť: {referenceError.Message}";
                }
            }
            OnPropertyChanged(nameof(OperatorSupervisionEnabled));
            OnPropertyChanged(nameof(OperatorSupervisionLabel));
            _stopRequested = false;
            if (_freshStartPending) CompleteFreshCalibrationReset();
            RefreshResumeCheckpoint();
            _runCts?.Dispose();
            _runCts = null;
            RefreshHistory();
        }
    }

    private void ApplyProgress(CalibrationProgressSnapshot snapshot)
    {
        Dashboard.Apply(snapshot, DateTimeOffset.Now);
        RunState = snapshot.State.ToString();
        PlateauLabel = snapshot.PlateauCount <= 0 || snapshot.PlateauIndex < 0 ? "Priebeh profilu / príprava" : $"Plato {snapshot.PlateauIndex + 1} / {snapshot.PlateauCount}";
        _calibrationProgressPercent = Dashboard.OverallProgress;
        TemperatureLabel = snapshot.ActualTemperatureC is { } actual
            ? $"{actual:F2} °C  →  {snapshot.TargetTemperatureC:F2} °C"
            : $"→ {snapshot.TargetTemperatureC:F2} °C";
        _lastChamberTemperatureC = snapshot.ActualTemperatureC;
        _lastReferenceTemperatureC = snapshot.ReferenceTemperatureC ?? _lastReferenceTemperatureC;
        ReferenceTemperatureLabel = snapshot.ReferenceTemperatureC is { } reference ? $"{reference:F3} °C" : ReferenceTemperatureLabel;
        StableLabel = $"{snapshot.StableTargets} / {snapshot.TotalTargets}";
        StatusMessage = snapshot.Message;
        if (snapshot.State != CalibrationRunState.WaitingForChamberStability)
            _temperatureGateOverridePending = false;
        PublishCalibrationStatus();
        ForceNextStepCommand.RaiseCanExecuteChanged();

        Dictionary<string, CalibrationTargetProgressViewModel> existing = TargetProgress.ToDictionary(x => x.Identity, StringComparer.OrdinalIgnoreCase);
        foreach (CalibrationTargetProgress target in snapshot.Targets)
        {
            string id = $"{target.SerialNumber}|{target.Channel}|{target.PeakId}";
            if (!existing.TryGetValue(id, out CalibrationTargetProgressViewModel? row))
            {
                row = new CalibrationTargetProgressViewModel(target);
                TargetProgress.Add(row);
            }
            else
            {
                row.Update(target);
            }

            CalibrationPeakRowViewModel? sourceRow = Peaks.FirstOrDefault(p =>
                string.Equals(p.SerialNumber, target.SerialNumber, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Channel, target.Channel, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.PeakId, target.PeakId, StringComparison.OrdinalIgnoreCase));
            // A real PeakLogger is continuously sampled by MonitorPeakLoggerAsync.
            // Runner progress can contain the plateau's older snapshot and must not
            // overwrite that live value every five seconds (it creates false spikes).
            // The simulator is intentionally not double-polled, so it keeps this fallback.
            if (UseSimulator && sourceRow is not null && target.CurrentWavelengthNm is { } wavelength)
            {
                sourceRow.UpdateLive(wavelength, sourceRow.Intensity, DateTimeOffset.Now);
            }
        }
    }

    private void PauseResume()
    {
        if (_runner is null) return;
        if (_runner.IsPaused)
        {
            _runner.Resume();
            StatusMessage = "Kalibrácia pokračuje.";
        }
        else
        {
            _runner.Pause();
            StatusMessage = "Kalibrácia pozastavená; čas segmentu stojí.";
        }
        Dashboard.Pause(_runner.IsPaused, DateTimeOffset.Now);
        _activeWriter?.WriteDiagnostic("INFO", _runner.IsPaused ? "OPERATOR_PAUSE" : "OPERATOR_RESUME", StatusMessage);
        AppLog.Info("FBG kalibrácia", $"Run {_activeRun?.DisplayRunId}: {StatusMessage}");
    }

    private static void WriteProgressDiagnostic(CalibrationRunWriter writer, CalibrationProgressSnapshot snapshot)
    {
        writer.WriteDiagnostic("INFO", "PROGRESS",
            $"state={snapshot.State}; plateau={snapshot.PlateauIndex + 1}/{snapshot.PlateauCount}; elapsedSeconds={snapshot.PlateauElapsed.TotalSeconds:F1}; " +
            $"targetC={snapshot.TargetTemperatureC:G17}; chamberC={snapshot.ActualTemperatureC?.ToString("G17") ?? "null"}; wikaC={snapshot.ReferenceTemperatureC?.ToString("G17") ?? "null"}; " +
            $"temperatureGate={snapshot.TemperatureGateOpen?.ToString() ?? "null"}; temperatureScore={snapshot.TemperatureStableScoreSeconds?.ToString() ?? "null"}/{snapshot.RequiredTemperatureScoreSeconds?.ToString() ?? "null"}; " +
            $"stablePeaks={snapshot.StableTargets}/{snapshot.TotalTargets}; message={snapshot.Message}");
        foreach (CalibrationTargetProgress target in snapshot.Targets)
            writer.WriteDiagnostic("INFO", "PEAK_PROGRESS",
                $"sn={target.SerialNumber}; channel={target.Channel}; peak={target.PeakId}; index={target.PeakIndex}; phase={target.Phase}; state={target.State}; " +
                $"wavelengthNm={target.CurrentWavelengthNm?.ToString("G17") ?? "null"}; stableSamples={target.StabilitySamples}/{target.RequiredStabilitySamples}; measurementSamples={target.MeasurementSamples}/{target.RequiredMeasurementSamples}; " +
                $"rangePm={target.RangePm?.ToString("G17") ?? "null"}/{target.RangeLimitPm?.ToString("G17") ?? "null"}; stdDevPm={target.StandardDeviationPm?.ToString("G17") ?? "null"}/{target.StdDevLimitPm?.ToString("G17") ?? "null"}; " +
                $"driftPmPerMinute={target.DriftPmPerMinute?.ToString("G17") ?? "null"}/{target.DriftLimitPmPerMinute?.ToString("G17") ?? "null"}; blocking={target.BlockingReason}; detail={target.Detail}");
    }

    private static string ProgressDiagnosticState(CalibrationProgressSnapshot snapshot) =>
        string.Join('|', new[]
        {
            snapshot.PlateauIndex.ToString(),
            snapshot.State.ToString(),
            snapshot.TemperatureGateOpen?.ToString() ?? "null",
            snapshot.StableTargets.ToString(),
            string.Join(',', snapshot.Targets.Select(target => $"{target.Channel}:{target.PeakId}:{target.Phase}:{target.State}")),
        });

    private void ForceNextStep()
    {
        if (_runner is null || !Dashboard.CanForceTemperatureGate || _temperatureGateOverridePending) return;
        _temperatureGateOverridePending = true;
        _runner.RequestTemperatureGateOverride();
        StatusMessage = "Operátor vyžiadal vynútené pokračovanie na stabilizáciu FBG. Akcia bude zapísaná do výsledku kalibrácie.";
        Dashboard.Warn(StatusMessage, DateTimeOffset.Now);
        _activeWriter?.WriteDiagnostic("WARNING", "OPERATOR_FORCE_NEXT_STEP", StatusMessage);
        AppLog.Warn("FBG kalibrácia", $"Run {_activeRun?.DisplayRunId}: {StatusMessage}");
        ForceNextStepCommand.RaiseCanExecuteChanged();
    }

    private void RequestFreshCalibration()
    {
        if (!Views.ConfirmDialog.Ask(
            "Ukončiť tento beh a začať úplne od prvého bodu?\n\nKomora sa zastaví a možnosť pokračovať v tomto behu sa zruší. " +
            "Doterajšie merania zostanú v histórii. Zapojenie a nastavenia zostanú pripravené. " +
            "Nový beh s novým ID spustíte tlačidlom Spustiť kalibráciu.",
            "Ukončiť a začať odznova", confirmText: "Ukončiť a zrušiť pokračovanie", danger: true, cancelText: "Ponechať beh")) return;
        if (IsRunning)
        {
            _freshStartPending = true;
            TrySaveResumeCheckpoint("OPERATOR_REQUEST_FRESH_RUN");
            _stopRequested = true;
            _runCts?.Cancel();
            StatusMessage = "Zastavujem komoru. Po potvrdení STOP zruším rozpracovaný stav pre nový beh.";
            RefreshCommands();
        }
        else CompleteFreshCalibrationReset();
    }

    private void CompleteFreshCalibrationReset()
    {
        _freshStartPending = false;
        try
        {
            var checkpoint = _resumeCheckpoint;
            var run = _activeRun ?? (checkpoint is null ? null : _calibrationStore.LoadRun(checkpoint.RunId));
            if (run is null) throw new InvalidOperationException("Rozpracovaný beh sa nenašiel.");
            run.State = CalibrationRunState.Aborted;
            run.CompletedAt = DateTimeOffset.Now;
            run.Warnings.Add(new CalibrationWarning
            {
                Code = "OPERATOR_RESET_FOR_NEW_RUN",
                Message = "Operátor ukončil beh a zrušil pokračovanie, aby začal od prvého bodu s novým ID. Merania zostali v histórii.",
            });
            _calibrationStore.SaveRun(run);
            _calibrationStore.DeleteCheckpoint(run.ChamberId);
            _activeRun = null;
            _resumeCheckpoint = null;
            TargetProgress.Clear();
            RunState = CalibrationRunState.Idle.ToString();
            WarningText = string.Empty;
            StatusMessage = "Pripravené na nový beh od prvého bodu. Spustite kalibráciu; pôvodné merania sú v histórii.";
            Dashboard.End(CalibrationRunState.Aborted, StatusMessage, DateTimeOffset.Now);
            Dashboard.ResetPlan();
            Dashboard.SetRunId("—");
            RefreshDashboardPlan();
            RefreshResumeCheckpoint();
            RefreshHistory();
            PublishCalibrationStatus();
        }
        catch (Exception ex) { ReportError(ex); }
        RefreshCommands();
    }
    private void StopCalibration()
    {
        if (!IsRunning)
        {
            FinalizeStoppedCalibration();
            return;
        }

        string target = Dashboard.TargetTemperatureC is { } targetTemperature
            ? $" pri cieli {targetTemperature:F1} °C"
            : string.Empty;
        if (!Views.ConfirmDialog.Ask(
                $"Naozaj chcete ukončiť prebiehajúcu FBG kalibráciu{target}?\n\n" +
                "Komora sa bezpečne zastaví a uloží sa checkpoint. Dokončené plata zostanú zachované. " +
                "Rozpracované plato sa po obnovení znovu stabilizuje a zmeria z čerstvých vzoriek.",
                "Ukončiť kalibráciu?",
                confirmText: "Ukončiť a uložiť",
                danger: true,
                cancelText: "Pokračovať v kalibrácii"))
        {
            StatusMessage = "Ukončenie bolo zrušené. Kalibrácia pokračuje bez zmeny.";
            return;
        }

        bool checkpointSaved = TrySaveResumeCheckpoint("OPERATOR_STOP_FOR_RESTART");
        _stopRequested = true;
        _activeWriter?.WriteDiagnostic("WARNING", "OPERATOR_STOP", checkpointSaved
            ? "Operátor stlačil STOP; checkpoint je uložený pre pokračovanie po reštarte a komora sa bezpečne zastavuje."
            : "Operátor stlačil STOP; checkpoint sa nepodarilo uložiť a komora sa bezpečne zastavuje.");
        AppLog.Warn("FBG kalibrácia", $"Run {_activeRun?.DisplayRunId}: operátor stlačil STOP.");
        _runCts?.Cancel();
        StatusMessage = checkpointSaved
            ? "Checkpoint je uložený. Zastavujem kalibráciu a komoru; po aktualizácii použite Pokračovať v kalibrácii."
            : "Zastavujem kalibráciu a komoru. Checkpoint sa nepodarilo uložiť; skontrolujte diagnostiku.";
    }

    private bool CanStopOrFinalizeCalibration()
    {
        if (IsRunning || HasResumableCalibration) return true;
        return _activeRun is not null &&
               Enum.TryParse<CalibrationRunState>(RunState, out var state) &&
               state is CalibrationRunState.Failed or CalibrationRunState.AwaitingOperator;
    }

    private void FinalizeStoppedCalibration()
    {
        CalibrationCheckpoint? checkpoint = _resumeCheckpoint;
        CalibrationRunRecord? run = _activeRun ?? (checkpoint is null ? null : _calibrationStore.LoadRun(checkpoint.RunId));
        if (run is null)
        {
            StatusMessage = "Zastavený kalibračný beh sa nenašiel. Uložené súbory zostali bez zmeny.";
            RefreshResumeCheckpoint();
            return;
        }

        if (!Views.ConfirmDialog.Ask(
                "Naozaj chcete zrušiť možnosť pokračovať v tejto kalibrácii?\n\n" +
                "Doteraz dokončené plata a namerané súbory sa zachovajú vo výsledkoch. " +
                "Checkpoint sa odstráni. Pokračovanie bude možné obnoviť už iba manuálne z Histórie / výsledkov.",
                "Zrušiť možnosť pokračovania?",
                confirmText: "Zrušiť pokračovanie",
                danger: true,
                cancelText: "Ponechať na pokračovanie"))
        {
            StatusMessage = "Kalibrácia zostáva uložená na neskoršie pokračovanie.";
            return;
        }

        run.State = CalibrationRunState.Aborted;
        run.CompletedAt = DateTimeOffset.Now;
        if (!run.Warnings.Any(warning => warning.Code == "OPERATOR_FINALIZED_STOPPED_RUN"))
        {
            run.Warnings.Add(new CalibrationWarning
            {
                Code = "OPERATOR_FINALIZED_STOPPED_RUN",
                Message = "Operátor definitívne ukončil zastavenú kalibráciu. Dokončené plata a dovtedy uložené merania zostali zachované.",
            });
        }
        _calibrationStore.SaveRun(run);
        _calibrationStore.DeleteCheckpoint(run.ChamberId);
        _activeRun = run;
        _resumeCheckpoint = null;
        RunState = CalibrationRunState.Aborted.ToString();
        StatusMessage = "Kalibrácia bola definitívne ukončená. Dokončené plata a namerané súbory sú uložené v Histórii / výsledkoch.";
        Dashboard.End(CalibrationRunState.Aborted, StatusMessage, DateTimeOffset.Now);
        RefreshResumeCheckpoint();
        RefreshHistory();
        PublishCalibrationStatus();
        RefreshCommands();
    }

    private bool TrySaveResumeCheckpoint(string reason)
    {
        if (_activeRun is null || SelectedChamber is null) return false;

        try
        {
            // The run summary must exist before the checkpoint becomes visible. Resume deliberately
            // keeps only completed plateaus; an in-progress plateau is re-stabilized and measured
            // from fresh samples after hardware has been reconnected.
            _activeWriter?.SaveSummary();
            CalibrationCheckpoint? existingCheckpoint = _calibrationStore.LoadCheckpoint(_activeRun.ChamberId);
            _calibrationStore.SaveCheckpoint(new CalibrationCheckpoint
            {
                RunId = _activeRun.RunId,
                ProfileId = _activeRun.ProfileId,
                ChamberId = _activeRun.ChamberId,
                CurrentPlateauIndex = _activeRun.Plateaus.Count,
                CurrentTargetTemperatureC = Dashboard.TargetTemperatureC,
                State = Enum.TryParse<CalibrationRunState>(RunState, out var state) ? state : _activeRun.State,
                CompletedPlateaus = _activeRun.Plateaus.ToList(),
                DeferredPlateauIndices = existingCheckpoint?.DeferredPlateauIndices.ToList() ?? new List<int>(),
                Mappings = Peaks.Select(peak => peak.ToMapping()).ToList(),
                SettingsSnapshot = CalibrationCheckpointRecovery.CloneSettings(_setup.Settings),
                CalibrationSegmentIndices = _setup.CalibrationSegmentIndices.ToList(),
            });
            _activeWriter?.WriteDiagnostic("INFO", "RECOVERY_CHECKPOINT_SAVED",
                $"reason={reason}; completedPlateaus={_activeRun.Plateaus.Count}; nextPlateau={_activeRun.Plateaus.Count + 1}");
            return true;
        }
        catch (Exception ex)
        {
            // Recovery must never prevent the requested physical STOP or application shutdown.
            AppLog.Error("FBG kalibrácia", $"Checkpoint pre pokračovanie po reštarte sa nepodarilo uložiť: {ex}");
            return false;
        }
    }

    private void PublishCalibrationStatus() => CalibrationStatusViewModel.Instance.Update(
        _workspaceChamberId,
        SelectedChamber?.Config.Name ?? "Komora",
        IsRunning,
        SelectedProfile?.Name ?? "FBG kalibrácia",
        _activeRun?.DisplayRunId ?? "—",
        _activeRun?.ReplicaRunDirectory ?? string.Empty,
        RunState,
        PlateauLabel,
        _calibrationProgressPercent,
        OperatorSupervisionEnabled ? OperatorSupervisionLabel + " · " + Dashboard.StateLabel : Dashboard.StateLabel,
        HasOperatorDecision ? OperatorDecisionMessage + " · " + OperatorDecisionCountdown : Dashboard.Now,
        Dashboard.Target,
        Dashboard.Reference,
        Dashboard.PeakSummary,
        Dashboard.ProgressLabel,
        Dashboard.PhaseElapsed,
        HasOperatorDecision ? "Čaká na operátora" : Dashboard.Eta,
        HasOperatorDecision ? "—" : Dashboard.Finish,
        Dashboard.EtaBasis,
        Dashboard.StartedAt,
        Dashboard.EstimatedFinishAt);

    private string WorkspaceName => SelectedChamber?.Config.Name ?? "Neznáme zariadenie";

    /// <returns>True when this call acquired a new reservation.</returns>
    private bool EnsureF100Reservation()
    {
        if (SelectedF100 is null) return false;
        string key = CalibrationResourceRegistry.F100Key(SelectedF100.PortName);
        if (string.Equals(key, _reservedF100Key, StringComparison.OrdinalIgnoreCase)) return false;
        if (!CalibrationResourceRegistry.TryAcquire(key, _workspaceChamberId, WorkspaceName, out string occupiedBy))
        {
            throw new InvalidOperationException(
                $"Port {SelectedF100.PortName} / WIKA CTH7000 je obsadený kalibráciou zariadenia „{occupiedBy}“. Vyber iný CTH7000 alebo ho najprv uvoľni v pôvodnom okne.");
        }

        if (_reservedF100Key is { } oldKey)
        {
            CalibrationResourceRegistry.Release(oldKey, _workspaceChamberId);
        }
        _reservedF100Key = key;
        return true;
    }

    private void ReleaseF100Reservation()
    {
        if (_reservedF100Key is not { } key) return;
        CalibrationResourceRegistry.Release(key, _workspaceChamberId);
        _reservedF100Key = null;
    }

    public string StorageStatus => _calibrationStore.ReplicationStatus(_activeRun?.RunId);
    public string StorageDirectories
    {
        get
        {
            CalibrationStorageSettings settings = CalibrationStorage.Settings.Load();
            string local = _activeRun?.LocalRunDirectory ?? _calibrationStore.RunsDirectory;
            string network = _activeRun is not null
                ? _activeRun.ReplicaRunDirectory ?? "vypnutá pre tento beh"
                : settings.NetworkCopyEnabled ? settings.NetworkDirectory : "vypnutá pre nové behy";
            return $"Lokálne: {local}\nSieťová kópia: {network}";
        }
    }
    public string? CurrentRunDirectory
    {
        get
        {
            CalibrationRunRecord? run = _activeRun ?? History.FirstOrDefault();
            return run is null ? null : _calibrationStore.GetRunDirectory(run);
        }
    }

    private void RefreshHistory()
    {
        History.Clear();
        foreach (CalibrationRunRecord run in _calibrationStore.LoadHistory()) History.Add(run);
        RefreshProfileStatistics();
        ExportSelectedRunCommand.RaiseCanExecuteChanged();
        ExportCalibrationCoefficientsCommand.RaiseCanExecuteChanged();
        RestoreSelectedRunCommand.RaiseCanExecuteChanged();
    }

    private bool CanRestoreSelectedHistoricalRun()
    {
        CalibrationRunRecord? run = SelectedHistoryRun;
        if (IsRunning || HasResumableCalibration || run is null || SelectedProfile is null || SelectedChamber is null)
            return false;
        if (run.ProfileId != SelectedProfile.Id || run.ChamberId != SelectedChamber.Config.Id || run.Plateaus.Count == 0)
            return false;
        if (run.State is not (CalibrationRunState.Aborted or CalibrationRunState.Failed or CalibrationRunState.AwaitingOperator))
            return false;

        int plannedPlateaus = CalibrationPoints.Count(point => point.Selected);
        int completedPlateaus = run.Plateaus.Select(plateau => plateau.PlateauIndex).Distinct().Count();
        return plannedPlateaus > completedPlateaus;
    }

    private void RestoreSelectedHistoricalRun()
    {
        CalibrationRunRecord? run = SelectedHistoryRun;
        if (run is null || !CanRestoreSelectedHistoricalRun()) return;

        int completedPlateaus = run.Plateaus.Select(plateau => plateau.PlateauIndex).Distinct().Count();
        int plannedPlateaus = CalibrationPoints.Count(point => point.Selected);
        if (!Views.ConfirmDialog.Ask(
                $"Obnoviť kalibráciu „{run.DisplayRunId}“ z uložených výsledkov?\n\n" +
                $"Zachová sa {completedPlateaus} dokončených plat z {plannedPlateaus}. " +
                $"Pokračovanie začne platom č. {completedPlateaus + 1}; rozpracované plato sa stabilizuje a zmeria nanovo.",
                "Obnoviť historickú kalibráciu?",
                confirmText: "Obnoviť pokračovanie",
                cancelText: "Zrušiť"))
        {
            return;
        }

        CalibrationCheckpoint checkpoint = CalibrationCheckpointRecovery.CreateFromHistoricalRun(run, _setup);
        _calibrationStore.SaveCheckpoint(checkpoint);
        _activeRun = run;
        RunState = CalibrationRunState.Aborted.ToString();
        RefreshResumeCheckpoint();
        StatusMessage = $"Pokračovanie kalibrácie „{run.DisplayRunId}“ bolo obnovené. Použite {ResumeCalibrationLabel}.";
        RefreshCommands();
    }

    private void RefreshProfileStatistics()
    {
        Guid profileId = SelectedProfile?.Id ?? Guid.Empty;
        ProfileStatistics = CalibrationProfileStatisticsAnalyzer.Analyze(History, profileId);
        ProfilePlateauAnalysis = ProfileStatistics.Plateaus
            .Select(plateau => new ProfilePlateauAnalysisRow(
                $"PLATO {plateau.PlateauIndex + 1:00}",
                $"{plateau.TargetTemperatureC:F1} °C",
                $"typicky {FormatHistoricalDuration(plateau.MedianDuration)}",
                $"priemer {FormatHistoricalDuration(plateau.AverageDuration)} · {plateau.SampleCount} behov",
                $"{FormatHistoricalDuration(plateau.MinimumDuration)} – {FormatHistoricalDuration(plateau.MaximumDuration)}"))
            .ToArray();
        OnPropertyChanged(nameof(HasProfileHistory));
        OnPropertyChanged(nameof(ProfileUsageSummary));
        OnPropertyChanged(nameof(ProfileDurationSummary));
        OnPropertyChanged(nameof(ProfileDurationRange));
        OnPropertyChanged(nameof(ProfilePlateauAnalysis));
    }

    private static string FormatHistoricalDuration(TimeSpan duration)
    {
        duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return duration.TotalDays >= 1
            ? $"{(int)duration.TotalDays} d {duration.Hours} h {duration.Minutes:00} min"
            : duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours} h {duration.Minutes:00} min"
                : $"{Math.Max(0, duration.Minutes)} min {Math.Max(0, duration.Seconds):00} s";
    }

    private void ExportSelectedRun()
    {
        if (SelectedHistoryRun is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export kalibrácie",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"calibration-{SelectedHistoryRun.DisplayRunId}-{SelectedHistoryRun.DisplayProfileId}.csv",
        };
        if (dialog.ShowDialog() != true) return;
        CalibrationStore.ExportSummaryCsv(SelectedHistoryRun, dialog.FileName);
        StatusMessage = $"Export uložený: {dialog.FileName}";
    }

    private void ExportCalibrationCoefficients()
    {
        if (SelectedHistoryRun?.CalibrationResults.Count is not > 0) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export kalibračných koeficientov",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"coefficients-{SelectedHistoryRun.DisplayRunId}-{SelectedHistoryRun.DisplayProfileId}.csv",
        };
        if (dialog.ShowDialog() != true) return;
        TemperatureCalibrationAnalyzer.ExportCsv(SelectedHistoryRun.CalibrationResults, dialog.FileName);
        StatusMessage = $"Kalibračné koeficienty boli uložené: {dialog.FileName}";
    }

    private async Task NotifyRunInterruptionAsync(CalibrationWarning warning)
    {
        DesktopNotifier.Notify("FBG kalibrácia sa zastavila – vyžaduje zásah", warning.Message, DesktopNotificationKind.Alarm);
        try { await SendWarningEmailAsync(_activeRun, warning); }
        catch (Exception ex)
        {
            AppLog.Warn("FBG kalibrácia", $"E-mail o zastavení behu sa nepodarilo odoslať: {ex.Message}");
        }
    }
    private async Task SendWarningEmailAsync(CalibrationRunRecord? run, CalibrationWarning warning)
    {
        if (run is null) return;
        RefreshEmailSettings();
        string plateau = warning.PlateauIndex is { } plateauIndex ? (plateauIndex + 1).ToString() : "—";
        string peak = string.IsNullOrWhiteSpace(warning.PeakId) ? "—" : warning.PeakId;
        string sensor = string.IsNullOrWhiteSpace(warning.SerialNumber) ? "—" : warning.SerialNumber;

        bool operatorAction = true; // Called only after the runner stops or requires operator action.
        EmailResult result = await _email.SendAsync(NotificationType.CalibrationWarning,
            operatorAction
                ? warning.Code == "OPERATOR_DECISION_REQUIRED"
                    ? $"OPERÁTORSKÝ DOHĽAD – ČAKÁ NA ROZHODNUTIE – {run.DisplayProfileId}"
                    : $"KALIBRÁCIA ZASTAVENÁ – ZÁSAH OPERÁTORA – {run.DisplayProfileId}"
                : $"Kalibrácia FBG – WARNING – {run.DisplayProfileId}",
            $"Run ID: {run.DisplayRunId}\nProfil ID: {run.DisplayProfileId}\nKomora: {run.ChamberName}\nProfil: {run.ProfileName}\nPlato: {plateau}\nSnímač: {sensor}\nPeak: {peak}\nČas: {warning.Timestamp:yyyy-MM-dd HH:mm:ss}\n\n{warning.Message}");
        if (result.Error is { Length: > 0 } error)
        {
            AppLog.Warn("FBG kalibrácia", $"Run {run.DisplayRunId}: e-mail upozornenia sa nepodarilo odoslať · {error}");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                WarningText = $"{warning.Message} E-mail operátorovi sa nepodarilo odoslať: {error}");
            DesktopNotifier.Notify("E-mail operátorovi nebol odoslaný", error, DesktopNotificationKind.Alarm);
        }
        else if (operatorAction && result.Skipped)
        {
            const string emailDisabled = "E-mail operátorovi nebol odoslaný, pretože e-mailové upozornenia nie sú zapnuté alebo nemajú nastaveného adresáta.";
            AppLog.Warn("FBG kalibrácia", $"Run {run.DisplayRunId}: {emailDisabled}");
            await Application.Current.Dispatcher.InvokeAsync(() => WarningText = $"{warning.Message} {emailDisabled}");
            DesktopNotifier.Notify("E-mail operátorovi nie je nastavený", emailDisabled, DesktopNotificationKind.Alarm);
        }
    }

    private async Task SendCompletionEmailAsync(CalibrationRunRecord run)
    {
        try
        {
            RefreshEmailSettings();
            CalibrationCompletionMessage message = CalibrationCompletionEmail.Create(run, run.ReplicaRunDirectory);
            EmailResult result = await _email.SendAsync(NotificationType.CalibrationCompleted,
                message.Subject, message.Text, message.Html, message.Attachments);
            if (result.Error is { Length: > 0 })
                AppLog.Warn("FBG kalibrácia", $"Run {run.DisplayRunId}: dokončovací e-mail sa nepodarilo odoslať · {result.Error}");
        }
        catch (Exception ex)
        {
            // Report/attachment creation must never turn a completed calibration into a failed run.
            AppLog.Warn("FBG kalibrácia", $"Run {run.DisplayRunId}: vytvorenie dokončovacieho e-mailu zlyhalo · {ex.Message}");
        }
    }

    private void RefreshEmailSettings() =>
        _email.Settings = new EmailSettingsStore(Path.Combine(AppPaths.SettingsDir, "email.json")).Load();

    private IChamberDevice CreateChamberClient(ChamberConfig config)
    {
        IChamberDevice raw = config.Protocol switch
        {
            ChamberProtocol.PolEkoModbus => new PolEkoClient(),
            ChamberProtocol.SikaRestApi => new SikaTpClient(),
            _ => new ChamberClient(),
        };
        TemperatureSafetyPolicy policy = TemperatureSafetyRegistry.Get(config.Id, config.SafetyTempMin, config.SafetyTempMax);
        var guarded = new TemperatureSafetyChamberDevice(raw, policy);
        guarded.SafetyTripped += OnTemperatureSafetyTripped;
        return guarded;
    }

    private void AdoptTemperatureSafetyFromProfile(TestProfile profile)
    {
        if (SelectedChamber is not { } chamber || profile.Segments.Count == 0) return;
        const double marginC = 5;
        double profileMin = profile.Segments.Min(segment => segment.TargetTemperature);
        double profileMax = profile.Segments.Max(segment => segment.TargetTemperature);
        chamber.Config.SafetyTempMin = Math.Max(chamber.Config.TempMin, profileMin - marginC);
        chamber.Config.SafetyTempMax = Math.Min(chamber.Config.TempMax, profileMax + marginC);
        TemperatureSafetyRegistry.Get(chamber.Config.Id, chamber.Config.SafetyTempMin, chamber.Config.SafetyTempMax)
            .Configure(chamber.Config.SafetyTempMin, chamber.Config.SafetyTempMax);
        _chamberStore.SaveAll(Chambers.Select(item => item.Config));
    }

    private void OnTemperatureSafetyTripped(object? sender, TemperatureSafetyTrippedEventArgs e)
    {
        _stopRequested = true;
        _activeWriter?.WriteDiagnostic("ERROR", "TEMPERATURE_SAFETY_TRIPPED",
            $"actualC={e.ActualC:G17}; minimumC={e.MinimumC:G17}; maximumC={e.MaximumC:G17}; stopSucceeded={e.StopSucceeded}; stopError={e.StopError}");
        _runCts?.Cancel();
        string stopResult = e.StopSucceeded ? "Výkon komory bol vypnutý." : $"VYPNUTIE ZLYHALO: {e.StopError}";
        string message = $"TEPLOTNÁ POISTKA: {e.ActualC:0.###} °C mimo [{e.MinimumC:0.###}; {e.MaximumC:0.###}] °C. {stopResult}";
        AppLog.Error("FBG kalibrácia", message);
        DesktopNotifier.Notify("TEPLOTNÁ POISTKA · zásah operátora", message, DesktopNotificationKind.Alarm);
        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            WarningText = message;
            StatusMessage = "Kalibrácia bola zastavená teplotnou poistkou. Skontrolujte komoru a limity.";
        });
    }

    private static ChamberConnectionSettings ToConnectionSettings(ChamberConfig config) => new()
    {
        Host = config.Host,
        Port = config.Port,
        Address = config.Address,
        AnalogChannelCount = config.AnalogChannelCount,
        StartChannelIndex = config.StartChannelIndex,
        Terminator = config.Terminator.Contains("LF", StringComparison.OrdinalIgnoreCase) ? "\r\n" : "\r",
    };

    private void ReportError(Exception ex)
    {
        StatusMessage = $"Chyba: {ex.Message}";
        WarningText = ex.Message;
    }

    private void RefreshSettingsBindings()
    {
        OnPropertyChanged(nameof(EnableSetpointRamp));
        OnPropertyChanged(nameof(SetpointRampCPerMinute));
        OnPropertyChanged(nameof(EnableWavelengthAveraging));
        OnPropertyChanged(nameof(WavelengthAveragingSamples));
        OnPropertyChanged(nameof(EnableWavelengthTraceLogging));
        OnPropertyChanged(nameof(WavelengthTraceIntervalSeconds));
        OnPropertyChanged(nameof(OperatorSupervisionEnabled));
        OnPropertyChanged(nameof(OperatorSupervisionLabel));
        OnPropertyChanged(nameof(SampleAcquisitionIntervalSeconds));
        OnPropertyChanged(nameof(RequiredStableSamples));
        OnPropertyChanged(nameof(RequiredMeasurementSamples));
        OnPropertyChanged(nameof(MaxRangePm));
        OnPropertyChanged(nameof(MaxStdDevPm));
        OnPropertyChanged(nameof(MaxDriftPmPerMinute));
        OnPropertyChanged(nameof(ChamberToleranceC));
        OnPropertyChanged(nameof(ChamberEntryEnabled));
        OnPropertyChanged(nameof(ChamberEntryToleranceC));
        OnPropertyChanged(nameof(ChamberEntryStableSeconds));
        OnPropertyChanged(nameof(ChamberEntryRangeC));
        OnPropertyChanged(nameof(ChamberEntryDriftCPerMinute));
        OnPropertyChanged(nameof(ChamberStableMinutes));
        OnPropertyChanged(nameof(MaxChamberRangeC));
        OnPropertyChanged(nameof(MaxChamberStdDevC));
        OnPropertyChanged(nameof(SensorTimeoutMinutes));
        OnPropertyChanged(nameof(ValidationMinimumDeltaTemperatureC));
        OnPropertyChanged(nameof(ValidationMinimumResponsePm));
        OnPropertyChanged(nameof(AllowValidationOverride));
        OnPropertyChanged(nameof(ValidationOverrideReason));
    }

    /// <summary>
    /// Re-publishes the current persisted/checkpoint settings for dynamically created views.
    /// A settings view can be realized after checkpoint recovery and would otherwise miss
    /// the earlier PropertyChanged notifications even though the underlying values are valid.
    /// </summary>
    public void RefreshSettingsDisplay() => RefreshSettingsBindings();

    private void RefreshCommands()
    {
        ConnectPeakLoggerCommand.RaiseCanExecuteChanged();
        RefreshSensorsCommand.RaiseCanExecuteChanged();
        StartCalibrationCommand.RaiseCanExecuteChanged();
        ResumeCalibrationCommand.RaiseCanExecuteChanged();
        ApplyCurrentDefaultsToResumeCommand.RaiseCanExecuteChanged();
        StopCalibrationCommand.RaiseCanExecuteChanged();
        ResetCalibrationCommand.RaiseCanExecuteChanged();
        PauseResumeCommand.RaiseCanExecuteChanged();
        ForceNextStepCommand.RaiseCanExecuteChanged();
        SaveSetupCommand.RaiseCanExecuteChanged();
        SelectSuggestedPeaksCommand.RaiseCanExecuteChanged();
        MarkAllPlateausCommand.RaiseCanExecuteChanged();
        ClearAllPlateausCommand.RaiseCanExecuteChanged();
        RefreshF100PortsCommand.RaiseCanExecuteChanged();
        DiagnoseF100TalkOnlyCommand.RaiseCanExecuteChanged();
        CheckF100Command.RaiseCanExecuteChanged();
        RestoreSelectedRunCommand.RaiseCanExecuteChanged();
    }

    public async ValueTask DisposeAsync()
    {
        ObserveResumeReference(null);
        _setupAutosaveCts?.Cancel();
        _setupAutosaveCts?.Dispose();
        _setupAutosaveCts = null;
        if (SelectedProfile is not null && !IsRunning)
        {
            PersistSetup(showStatus: false);
        }
        StopPeakMonitor();
        if (IsRunning && _activeRun?.State is not (CalibrationRunState.Completed or CalibrationRunState.CompletedWithWarnings))
        {
            TrySaveResumeCheckpoint("APPLICATION_SHUTDOWN");
        }
        _activeWriter = null;
        _stopRequested = IsRunning;
        _runCts?.Cancel();
        if (_runSessionTask is { } session)
        {
            try { await session; }
            catch (Exception ex) { AppLog.Warn("FBG kalibrácia", $"Ukončenie behu pred zatvorením: {ex.Message}"); }
        }
        _activeWriter = null;
        await StopReferenceTraceAsync();
        if (_chamber is not null)
        {
            if (_stopRequested)
            {
                try { await _chamber.StopAsync(); } catch { }
            }
            await _chamber.DisposeAsync();
        }
        if (_peakLogger is not null) await _peakLogger.DisposeAsync();
        await _referenceThermometers.DisposeAsync();
        ReleaseF100Reservation();
        if (_reservedPeakLoggerKey is { } peakLoggerKey)
        {
            CalibrationResourceRegistry.Release(peakLoggerKey, _workspaceChamberId);
            _reservedPeakLoggerKey = null;
        }
        _runCts?.Dispose();
    }
}

public sealed record ProfilePlateauAnalysisRow(string Number, string Target, string Typical, string Average, string Range);

public sealed record CalibrationChamberOption(ChamberConfig Config)
{
    public string DisplayName => $"{Config.Name} · {Config.Host}";
}

public sealed class CalibrationPeakRowViewModel : ObservableObject
{
    public VotschVc3.App.Calibration.SylexFosDisplayMetadata ApiMetadata { get; } = new();
    private bool _selected;
    private string _channelSerialNumber;
    private string _chainSerialNumber;
    private string _serialNumberWarning = string.Empty;
    private string _notes;
    private string _productDescription;
    private string _customer;
    private string _order;
    private double _timeoutMinutes;
    private double _currentWavelengthNm;
    private double? _intensity;
    private DateTimeOffset? _lastWavelengthUpdate;

    public CalibrationPeakRowViewModel(PeakLoggerSensor sensor, PeakLoggerPeak peak, CalibrationSensorMapping? saved)
    {
        PeakLoggerDeviceSerialNumber = sensor.SerialNumber;
        _chainSerialNumber = saved?.ChainSerialNumber ?? string.Empty;
        ApiMetadata.SensorName = saved?.SensorName ?? string.Empty;
        ApiMetadata.SylexSerialNumber = VotschVc3.App.Calibration.SylexFosRowMetadataStore.ParseSerialNumber(saved?.SerialNumber);
        _channelSerialNumber = saved?.ChannelSerialNumber
            ?? (string.IsNullOrWhiteSpace(_chainSerialNumber) ? saved?.SerialNumber : string.Empty)
            ?? string.Empty;
        Channel = sensor.Channel;
        PeakId = peak.PeakId;
        PeakIndex = peak.PeakIndex;
        SensorType = string.IsNullOrWhiteSpace(peak.SensorType) ? "—" : peak.SensorType;
        FbgType = string.IsNullOrWhiteSpace(peak.FbgType) ? "—" : peak.FbgType;
        _currentWavelengthNm = peak.WavelengthNm;
        _intensity = peak.Intensity;
        _selected = saved?.Selected ?? false;
        WasSavedSelected = _selected;
        Core1 = saved?.Core1;
        Core2 = saved?.Core2;
        _notes = saved?.Notes ?? string.Empty;
        _productDescription = saved?.ProductDescription ?? string.Empty;
        _customer = saved?.Customer ?? string.Empty;
        _order = saved?.Order ?? string.Empty;
        _timeoutMinutes = saved?.StabilizationTimeoutOverride?.TotalMinutes ?? 0;
    }

    public string PeakLoggerDeviceSerialNumber { get; }
    public string ChannelSerialNumber
    {
        get => _channelSerialNumber;
        set
        {
            if (SetProperty(ref _channelSerialNumber, NormalizeBarcode(value)))
            {
                OnPropertyChanged(nameof(SerialNumber));
                OnPropertyChanged(nameof(NeedsSensorSerialNumber));
                OnPropertyChanged(nameof(RequiresReconnect));
                OnPropertyChanged(nameof(ConnectionWarning));
            }
        }
    }

    public string ChainSerialNumber
    {
        get => _chainSerialNumber;
        set
        {
            if (SetProperty(ref _chainSerialNumber, NormalizeBarcode(value)))
            {
                OnPropertyChanged(nameof(SerialNumber));
                OnPropertyChanged(nameof(NeedsSensorSerialNumber));
                OnPropertyChanged(nameof(RequiresReconnect));
                OnPropertyChanged(nameof(ConnectionWarning));
            }
        }
    }
    public string SerialNumber => string.IsNullOrWhiteSpace(ChainSerialNumber)
        ? ChannelSerialNumber
        : ChainSerialNumber;
    public bool NeedsSensorSerialNumber => (!IsDisconnected || Selected) && string.IsNullOrWhiteSpace(SerialNumber);
    public bool RequiresReconnect => CalibrationPeakTopologyPolicy.RequiresReconnect(IsDisconnected, Selected, SerialNumber);
    public string SerialNumberWarning
    {
        get => _serialNumberWarning;
        private set
        {
            if (SetProperty(ref _serialNumberWarning, value))
            {
                OnPropertyChanged(nameof(HasSerialNumberWarning));
            }
        }
    }
    public bool HasSerialNumberWarning => !string.IsNullOrWhiteSpace(SerialNumberWarning);
    public string Channel { get; }
    public string PeakId { get; }
    public int PeakIndex { get; }
    public string SensorType { get; }
    public string FbgType { get; }
    public double CurrentWavelengthNm { get => _currentWavelengthNm; private set => SetProperty(ref _currentWavelengthNm, value); }
    private bool _isDisconnected;
    public bool IsDisconnected { get => _isDisconnected; private set => SetProperty(ref _isDisconnected, value); }
    public double MinimumIntensityDbm { get; set; } = -40;
    public bool HasWeakSignal => !IsDisconnected && Intensity is double value && value < MinimumIntensityDbm;
    public string ConnectionWarning => IsDisconnected
        ? RequiresReconnect ? $"Pripoj snímač {SerialNumber} späť do kanála {Channel}." : "Nepriradený peak už nie je prítomný. Možno ho odstrániť."
        : HasWeakSignal ? $"Slabý signál: {Intensity:F1} dBm; minimum {MinimumIntensityDbm:F1} dBm. Vyčisti konektor. Kalibrácia nie je blokovaná." : string.Empty;
    public string LiveWavelengthLabel => IsDisconnected ? "—" : CurrentWavelengthNm.ToString("F3");
    public void MarkDisconnected()
    {
        IsDisconnected = true;
        Intensity = null;
        OnPropertyChanged(nameof(ConnectionWarning));
        OnPropertyChanged(nameof(NeedsSensorSerialNumber));
        OnPropertyChanged(nameof(RequiresReconnect));
        OnPropertyChanged(nameof(HasWeakSignal));
        OnPropertyChanged(nameof(LiveWavelengthLabel));
    }
    public double? Intensity { get => _intensity; private set => SetProperty(ref _intensity, value); }
    public DateTimeOffset? LastWavelengthUpdate { get => _lastWavelengthUpdate; private set => SetProperty(ref _lastWavelengthUpdate, value); }
    public bool WasSavedSelected { get; }
    public int? Core1 { get; set; }
    public int? Core2 { get; set; }
    public bool Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            OnPropertyChanged(nameof(NeedsSensorSerialNumber));
            OnPropertyChanged(nameof(RequiresReconnect));
            OnPropertyChanged(nameof(ConnectionWarning));
        }
    }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public string ProductDescription { get => _productDescription; set => SetProperty(ref _productDescription, value); }
    public string Customer { get => _customer; set => SetProperty(ref _customer, value); }
    public string Order { get => _order; set => SetProperty(ref _order, value); }
    public double TimeoutMinutes { get => _timeoutMinutes; set => SetProperty(ref _timeoutMinutes, Math.Max(0, value)); }

    public void UpdateLive(double wavelengthNm, double? intensity, DateTimeOffset timestamp)
    {
        CurrentWavelengthNm = wavelengthNm;
        Intensity = intensity;
        LastWavelengthUpdate = timestamp;
        IsDisconnected = false;
        OnPropertyChanged(nameof(ConnectionWarning));
        OnPropertyChanged(nameof(NeedsSensorSerialNumber));
        OnPropertyChanged(nameof(RequiresReconnect));
        OnPropertyChanged(nameof(HasWeakSignal));
        OnPropertyChanged(nameof(LiveWavelengthLabel));
    }

    public void SetSerialNumberWarning(string warning) => SerialNumberWarning = warning;

    public void ApplySavedMapping(CalibrationSensorMapping mapping)
    {
        ChannelSerialNumber = mapping.ChannelSerialNumber
            ?? (string.IsNullOrWhiteSpace(mapping.ChainSerialNumber) ? mapping.SerialNumber : string.Empty)
            ?? string.Empty;
        ChainSerialNumber = mapping.ChainSerialNumber ?? string.Empty;
        Core1 = mapping.Core1;
        Core2 = mapping.Core2;
        Selected = mapping.Selected;
        Notes = mapping.Notes ?? string.Empty;
        ProductDescription = mapping.ProductDescription ?? string.Empty;
        Customer = mapping.Customer ?? string.Empty;
        Order = mapping.Order ?? string.Empty;
        TimeoutMinutes = mapping.StabilizationTimeoutOverride?.TotalMinutes ?? 0;
    }

    public void AddSerialNumberWarning(string warning) => SerialNumberWarning =
        string.IsNullOrWhiteSpace(SerialNumberWarning) ? warning : $"{SerialNumberWarning} {warning}";

    private static string NormalizeBarcode(string? value) =>
        FbgSerialParser.Parse(value);

    public CalibrationSensorMapping ToMapping() => new()
    {
        Channel = Channel,
        Core1 = Core1,
        Core2 = Core2,
        SerialNumber = SerialNumber,
        SensorName = ApiMetadata.SensorName,
        ChannelSerialNumber = ChannelSerialNumber,
        ChainSerialNumber = ChainSerialNumber,
        PeakLoggerDeviceSerialNumber = PeakLoggerDeviceSerialNumber,
        PeakId = PeakId,
        PeakIndex = PeakIndex,
        NominalWavelengthNm = CurrentWavelengthNm,
        CurrentWavelengthNm = CurrentWavelengthNm,
        Selected = Selected,
        Notes = Notes,
        ProductDescription = ProductDescription,
        Customer = Customer,
        Order = Order,
        StabilizationTimeoutOverride = TimeoutMinutes > 0 ? TimeSpan.FromMinutes(TimeoutMinutes) : null,
    };
}

public sealed class CalibrationPointRowViewModel : ObservableObject
{
    private readonly ProfileSegment _segment;
    private bool _selected;

    public CalibrationPointRowViewModel(int segmentIndex, ProfileSegment segment)
    {
        SegmentIndex = segmentIndex;
        _segment = segment;
        _selected = segment.IsCalibrationPoint;
    }

    public int SegmentIndex { get; }
    public string Name => _segment.Name;
    public double TemperatureC => _segment.TargetTemperature;
    public TimeSpan Duration => _segment.Duration;
    public bool Selected { get => _selected; set => SetProperty(ref _selected, value); }
}

public sealed class CalibrationTargetProgressViewModel : ObservableObject
{
    private double? _wavelength;
    private int _samples;
    private double? _stdDev;
    private double? _drift;
    private TimeSpan _elapsed;
    private CalibrationTargetState _state;
    private string? _detail;

    public CalibrationTargetProgressViewModel(CalibrationTargetProgress progress)
    {
        SerialNumber = progress.SerialNumber;
        Channel = progress.Channel;
        PeakId = progress.PeakId;
        PeakIndex = progress.PeakIndex;
        RequiredSamples = progress.RequiredSamples;
        Timeout = progress.Timeout;
        Update(progress);
    }

    public string SerialNumber { get; }
    public string Channel { get; }
    public string PeakId { get; }
    public int PeakIndex { get; }
    public int RequiredSamples { get; }
    public TimeSpan Timeout { get; }
    public string Identity => $"{SerialNumber}|{Channel}|{PeakId}";
    public double? CurrentWavelengthNm { get => _wavelength; private set => SetProperty(ref _wavelength, value); }
    public int StableSamples { get => _samples; private set => SetProperty(ref _samples, value); }
    public string SamplesLabel => $"{StableSamples}/{RequiredSamples}";
    public double? StandardDeviationPm { get => _stdDev; private set => SetProperty(ref _stdDev, value); }
    public double? DriftPmPerMinute { get => _drift; private set => SetProperty(ref _drift, value); }
    public TimeSpan Elapsed { get => _elapsed; private set => SetProperty(ref _elapsed, value); }
    public CalibrationTargetState State { get => _state; private set => SetProperty(ref _state, value); }
    public string? Detail { get => _detail; private set => SetProperty(ref _detail, value); }

    public void Update(CalibrationTargetProgress p)
    {
        CurrentWavelengthNm = p.CurrentWavelengthNm;
        StableSamples = p.StableSamples;
        StandardDeviationPm = p.StandardDeviationPm;
        DriftPmPerMinute = p.DriftPmPerMinute;
        Elapsed = p.Elapsed;
        State = p.State;
        Detail = p.Detail;
        OnPropertyChanged(nameof(SamplesLabel));
    }
}
