using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;
using VotschVc3.App.Mvvm;
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

public sealed class CalibrationViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ProfileStore _profileStore;
    private readonly ChamberConfigStore _chamberStore;
    private readonly CalibrationStore _calibrationStore;
    private readonly EmailNotifier _email = new();
    private readonly ThermometersViewModel _referenceThermometers;
    private readonly Guid _workspaceChamberId;
    private PeakLoggerSettings _peakLoggerSettings = new();
    private IPeakLoggerClient? _peakLogger;
    private IChamberDevice? _chamber;
    private CalibrationProfileRunner? _runner;
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _peakMonitorCts;
    private CancellationTokenSource? _setupAutosaveCts;
    private CalibrationRunRecord? _activeRun;
    private CalibrationRunWriter? _activeWriter;
    private CalibrationSetup _setup = new();
    private bool _stopRequested;
    private double? _lastChamberTemperatureC;
    private double? _lastReferenceTemperatureC;
    private DateTimeOffset? _lastReferenceMismatchEmailAt;
    private bool _propagatingChannelSerialNumber;
    private double _calibrationProgressPercent;
    private string? _reservedF100Key;
    private string? _reservedPeakLoggerKey;

    private static readonly Regex ProductionSerialNumberPattern = new(
        "^[A-Za-z0-9]{6}/[A-Za-z0-9]{4}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public CalibrationViewModel(Guid chamberId)
    {
        _workspaceChamberId = chamberId;
        AppPaths.Initialize();
        _profileStore = new ProfileStore(AppPaths.ProfilesDir);
        _chamberStore = new ChamberConfigStore(Path.Combine(AppPaths.SettingsDir, "chambers.json"));
        _calibrationStore = new CalibrationStore(AppPaths.CalibrationDir);
        _email.Settings = new EmailSettingsStore(Path.Combine(AppPaths.SettingsDir, "email.json")).Load();
        _referenceThermometers = new ThermometersViewModel();

        Profiles = new ObservableCollection<TestProfile>();
        Chambers = new ObservableCollection<CalibrationChamberOption>(
            _chamberStore.LoadAll().Select(c => new CalibrationChamberOption(c)));
        Peaks = new ObservableCollection<CalibrationPeakRowViewModel>();
        CalibrationPoints = new ObservableCollection<CalibrationPointRowViewModel>();
        TargetProgress = new ObservableCollection<CalibrationTargetProgressViewModel>();
        History = new ObservableCollection<CalibrationRunRecord>(_calibrationStore.LoadHistory());

        ConnectPeakLoggerCommand = new AsyncRelayCommand(ConnectPeakLoggerAsync, () => !IsRunning, ReportError);
        DiscoverPeakLoggerApisCommand = new AsyncRelayCommand(DiscoverPeakLoggerApisAsync, () => !IsRunning && !UseSimulator, ReportError);
        RefreshSensorsCommand = new AsyncRelayCommand(DiscoverSensorsAsync, () => PeakLoggerConnected && !IsRunning, ReportError);
        SaveSetupCommand = new RelayCommand(SaveSetup, () => SelectedProfile is not null && !IsRunning);
        SelectSuggestedPeaksCommand = new RelayCommand(SelectSuggestedPeaks, () => Peaks.Count > 0 && !IsRunning);
        MarkAllPlateausCommand = new RelayCommand(MarkAllPlateaus, () => CalibrationPoints.Count > 0 && !IsRunning);
        StartCalibrationCommand = new AsyncRelayCommand(StartCalibrationAsync, CanStartCalibration, ReportError);
        PauseResumeCommand = new RelayCommand(PauseResume, () => IsRunning && _runner is not null);
        StopCalibrationCommand = new RelayCommand(StopCalibration, () => IsRunning);
        RefreshHistoryCommand = new RelayCommand(RefreshHistory);
        ExportSelectedRunCommand = new RelayCommand(ExportSelectedRun, () => SelectedHistoryRun is not null);

        RefreshF100PortsCommand = new AsyncRelayCommand(RefreshF100PortsAsync, () => !IsRunning, ReportError);
        CheckF100Command = new AsyncRelayCommand(CheckF100Async, () => SelectedF100 is not null, ReportError);
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
    public ObservableCollection<CalibrationPointRowViewModel> CalibrationPoints { get; }
    public ObservableCollection<CalibrationTargetProgressViewModel> TargetProgress { get; }
    public ObservableCollection<CalibrationRunRecord> History { get; }

    public ObservableCollection<ThermometerDeviceViewModel> F100Devices => _referenceThermometers.Devices;
    public IReadOnlyList<string> F100Channels => F100Protocol.ProbeChannels;

    public AsyncRelayCommand ConnectPeakLoggerCommand { get; }
    public AsyncRelayCommand DiscoverPeakLoggerApisCommand { get; }
    public AsyncRelayCommand RefreshSensorsCommand { get; }
    public RelayCommand SaveSetupCommand { get; }
    public RelayCommand SelectSuggestedPeaksCommand { get; }
    public RelayCommand MarkAllPlateausCommand { get; }
    public AsyncRelayCommand StartCalibrationCommand { get; }
    public RelayCommand PauseResumeCommand { get; }
    public RelayCommand StopCalibrationCommand { get; }
    public RelayCommand RefreshHistoryCommand { get; }
    public RelayCommand ExportSelectedRunCommand { get; }
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
            if (SetProperty(ref _selectedProfile, value)) LoadProfileSetup();
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
        foreach (TestProfile profile in _profileStore.LoadAll().Where(p => p.DeviceKind.CanRunOn(deviceKind)))
        {
            Profiles.Add(profile);
        }

        SelectedProfile = (previous is { } id ? Profiles.FirstOrDefault(p => p.Id == id) : null)
            ?? Profiles.FirstOrDefault();
    }

    private CalibrationRunRecord? _selectedHistoryRun;
    public CalibrationRunRecord? SelectedHistoryRun
    {
        get => _selectedHistoryRun;
        set
        {
            if (SetProperty(ref _selectedHistoryRun, value)) ExportSelectedRunCommand.RaiseCanExecuteChanged();
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
        ? "Žiadny ASL F100"
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
            if (SetProperty(ref _peakLoggerConnected, value)) RefreshCommands();
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
                RefreshCommands();
                PublishCalibrationStatus();
            }
        }
    }

    private string _statusMessage = "Pripravené.";
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

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
        set { _setup.Settings.RequiredStableSamples = Math.Clamp(value, 2, 10000); OnPropertyChanged(); }
    }

    public double MaxRangePm
    {
        get => _setup.Settings.MaxWavelengthRangePm;
        set { _setup.Settings.MaxWavelengthRangePm = Math.Max(0, value); OnPropertyChanged(); }
    }

    public double MaxStdDevPm
    {
        get => _setup.Settings.MaxWavelengthStdDevPm;
        set { _setup.Settings.MaxWavelengthStdDevPm = Math.Max(0, value); OnPropertyChanged(); }
    }

    public double MaxDriftPmPerMinute
    {
        get => _setup.Settings.MaxWavelengthDriftPmPerMinute;
        set { _setup.Settings.MaxWavelengthDriftPmPerMinute = Math.Max(0, value); OnPropertyChanged(); }
    }

    public double ChamberToleranceC
    {
        get => _setup.Settings.ChamberToleranceC;
        set { _setup.Settings.ChamberToleranceC = Math.Abs(value); OnPropertyChanged(); }
    }

    public double ChamberStableMinutes
    {
        get => _setup.Settings.ChamberStableDuration.TotalMinutes;
        set { _setup.Settings.ChamberStableDuration = TimeSpan.FromMinutes(Math.Max(0, value)); OnPropertyChanged(); }
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

    private void LoadProfileSetup()
    {
        Peaks.Clear();
        CalibrationPoints.Clear();
        if (SelectedProfile is null)
        {
            _setup = new CalibrationSetup();
            RefreshSettingsBindings();
            RefreshCommands();
            return;
        }

        Guid chamberId = SelectedChamber?.Config.Id ?? _workspaceChamberId;
        _setup = _calibrationStore.LoadSetup(SelectedProfile.Id, chamberId)
            ?? new CalibrationSetup { ProfileId = SelectedProfile.Id, ChamberId = chamberId };
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
                    }
                };
                CalibrationPoints.Add(point);
            }
        }
        RefreshSettingsBindings();
        RefreshCommands();
    }

    private async Task ConnectPeakLoggerAsync()
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

    private async Task DiscoverPeakLoggerApisAsync()
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

    private async Task DiscoverSensorsAsync()
    {
        if (_peakLogger is null || SelectedProfile is null) return;
        IReadOnlyList<PeakLoggerSensor> sensors = await _peakLogger.DiscoverSensorsAsync();
        Dictionary<string, CalibrationSensorMapping> saved = _setup.Mappings
            .GroupBy(m => m.SourceIdentity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        Peaks.Clear();
        foreach (PeakLoggerSensor sensor in sensors)
        {
            foreach (PeakLoggerPeak peak in sensor.Peaks)
            {
                string sourceIdentity = $"{sensor.SerialNumber}|{sensor.Channel}|{peak.PeakId}";
                saved.TryGetValue(sourceIdentity, out CalibrationSensorMapping? mapping);
                Peaks.Add(CreatePeakRow(sensor, peak, mapping));
            }
        }

        PeakLoggerStatus = $"Pripojený · {sensors.Count} zdrojov/kanálov · {Peaks.Count} peakov";
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
            }

            if (e.PropertyName is not nameof(CalibrationPeakRowViewModel.CurrentWavelengthNm)
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

    private void PropagateChannelSerialNumber(CalibrationPeakRowViewModel source)
    {
        _propagatingChannelSerialNumber = true;
        try
        {
            foreach (CalibrationPeakRowViewModel row in Peaks.Where(row =>
                         !ReferenceEquals(row, source) &&
                         string.Equals(row.PeakLoggerDeviceSerialNumber, source.PeakLoggerDeviceSerialNumber, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(row.Channel, source.Channel, StringComparison.OrdinalIgnoreCase)))
            {
                row.ChannelSerialNumber = source.ChannelSerialNumber;
            }
        }
        finally
        {
            _propagatingChannelSerialNumber = false;
        }
    }

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
        _setupAutosaveCts = new CancellationTokenSource();
        CancellationToken token = _setupAutosaveCts.Token;
        _ = AutosaveSetupAsync(token);
    }

    private async Task AutosaveSetupAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(350, token);
            await Application.Current.Dispatcher.InvokeAsync(() => PersistSetup(showStatus: false));
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke restarts the short debounce window.
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

                    CalibrationRunWriter? writer = _activeWriter;
                    CalibrationRunRecord? run = _activeRun;
                    if (writer is not null && run is not null && IsRunning)
                    {
                        List<CalibrationWavelengthTraceSample> trace = BuildTraceSamples(run, measurements);
                        if (trace.Count > 0)
                        {
                            await writer.AppendWavelengthTraceAsync(trace, token);
                        }
                    }
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

    private void ApplyLivePeakMeasurements(IReadOnlyList<PeakLoggerMeasurement> measurements)
    {
        Dictionary<string, PeakLoggerMeasurement> bySource = measurements
            .GroupBy(m => $"{m.SerialNumber}|{m.Channel}|{m.PeakId}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var knownSources = new HashSet<string>(
            Peaks.Select(row => $"{row.PeakLoggerDeviceSerialNumber}|{row.Channel}|{row.PeakId}"),
            StringComparer.OrdinalIgnoreCase);
        int added = 0;
        foreach (KeyValuePair<string, PeakLoggerMeasurement> entry in bySource)
        {
            if (knownSources.Contains(entry.Key)) continue;

            PeakLoggerMeasurement measurement = entry.Value;
            var sensor = new PeakLoggerSensor(measurement.SerialNumber, measurement.Channel, Array.Empty<PeakLoggerPeak>());
            var peak = new PeakLoggerPeak(
                measurement.PeakId,
                measurement.PeakIndex,
                measurement.WavelengthNm,
                measurement.Intensity);
            CalibrationPeakRowViewModel row = CreatePeakRow(sensor, peak, null);
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

    private void MarkAllPlateaus()
    {
        foreach (CalibrationPointRowViewModel point in CalibrationPoints) point.Selected = true;
        StatusMessage = "Všetky hold segmenty boli označené ako kalibračné plata.";
        StartCalibrationCommand.RaiseCanExecuteChanged();
    }

    private async Task RefreshF100PortsAsync()
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
            ? $"ASL F100 znovu pripojený · {SelectedF100.PortName} · {t:F3} °C"
            : $"Port {SelectedF100.PortName} sa otvoril, ale F100 neposlal platnú teplotu.";
    }

    private async Task CheckF100Async()
    {
        await RescanF100PortsAsync(showStatus: false);
        if (SelectedF100 is null) return;
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

    private async Task<double?> ReadReferenceTemperatureAsync(CancellationToken token)
    {
        if (SelectedF100 is null) return null;
        EnsureF100Reservation();
        SelectedF100.SelectedChannel = SelectedF100Channel;
        double? value = await SelectedF100.ReadReferenceTemperatureAsync(token);
        _lastReferenceTemperatureC = value;
        double? currentChamberTemperature = await ReadCurrentChamberTemperatureAsync(token);
        _lastChamberTemperatureC = currentChamberTemperature ?? _lastChamberTemperatureC;
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
        EmailSettings settings = _email.Settings;
        if (!settings.ReferenceTemperatureMismatchAlertsEnabled)
        {
            return;
        }

        double limit = Math.Max(0.1, Math.Abs(settings.ReferenceTemperatureMismatchLimitC));
        double difference = Math.Abs(referenceTemperature - chamberTemperature);
        if (difference <= limit)
        {
            return;
        }

        string message = $"CHYBA TEPLOTY: F100 {referenceTemperature:F3} °C sa nezhoduje s komorou " +
                         $"{chamberTemperature:F3} °C. Rozdiel {difference:F3} °C prekročil povolených ±{limit:F1} °C.";
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
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

        EmailResult result = await _email.SendAsync(
            $"CHYBA – rozdiel teploty F100 a komory – {SelectedChamber?.Config.Name ?? "komora"}",
            $"{message}\n\nKomora: {SelectedChamber?.Config.Name}\nF100: {SelectedF100?.PortName} / kanál {SelectedF100Channel}\nČas: {now:yyyy-MM-dd HH:mm:ss}",
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

    private void SaveSetup() => PersistSetup(showStatus: true);

    private void PersistSetup(bool showStatus)
    {
        if (SelectedProfile is null) return;
        _setup.ProfileId = SelectedProfile.Id;
        _setup.ChamberId = SelectedChamber?.Config.Id ?? _workspaceChamberId;
        _setup.Mappings = Peaks.Select(p => p.ToMapping()).ToList();
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
        if (IsRunning || !PeakLoggerConnected || SelectedProfile is null || SelectedChamber is null ||
            !CalibrationPoints.Any(p => p.Selected))
        {
            return false;
        }

        List<CalibrationPeakRowViewModel> selected = Peaks.Where(p => p.Selected).ToList();
        return selected.Count > 0 && selected.All(p => !string.IsNullOrWhiteSpace(p.SerialNumber));
    }

    private async Task StartCalibrationAsync()
    {
        if (!CanStartCalibration() || SelectedProfile is null || SelectedChamber is null || _peakLogger is null) return;
        await RescanF100PortsAsync(showStatus: false);
        if (SelectedF100 is not null) EnsureF100Reservation();
        SaveSetup();
        WarningText = string.Empty;
        TargetProgress.Clear();
        _runCts = new CancellationTokenSource();
        _stopRequested = false;
        _calibrationProgressPercent = 0;
        IsRunning = true;
        _lastChamberTemperatureC = null;
        _lastReferenceTemperatureC = SelectedF100?.Temperature;

        try
        {
            _chamber = CreateChamberClient(SelectedChamber.Config);
            ChamberConnectionSettings connection = ToConnectionSettings(SelectedChamber.Config);
            StatusMessage = $"Pripájam komoru {SelectedChamber.Config.Name}…";
            await _chamber.ConnectAsync(connection, _runCts.Token);
            var initialReading = await _chamber.ReadAsync(_runCts.Token);
            double startTemperature = initialReading.Temperature
                ?? throw new InvalidOperationException("Komora neposkytla platnú nameranú teplotu pred začiatkom kalibrácie.");
            _lastChamberTemperatureC = startTemperature;

            _activeRun = new CalibrationRunRecord
            {
                ProfileId = SelectedProfile.Id,
                ProfileName = SelectedProfile.Name,
                ChamberId = SelectedChamber.Config.Id,
                ChamberName = SelectedChamber.Config.Name,
                Operator = Environment.UserName,
                State = CalibrationRunState.Preflight,
                ReferenceThermometerPort = SelectedF100?.PortName ?? string.Empty,
                ReferenceThermometerSerialNumber = SelectedF100?.SerialNumber ?? string.Empty,
                ReferenceThermometerChannel = SelectedF100 is null ? string.Empty : SelectedF100Channel,
            };

            await using CalibrationRunWriter writer = _calibrationStore.CreateRunWriter(_activeRun);
            _activeWriter = writer;
            var orchestrator = new CalibrationOrchestrator(_peakLogger);
            orchestrator.WarningRaised += warning =>
            {
                _ = Application.Current.Dispatcher.InvokeAsync(() => WarningText = warning.Message);
                _ = SendWarningEmailAsync(_activeRun, warning);
            };
            _runner = new CalibrationProfileRunner(_chamber, orchestrator, _calibrationStore);
            _runner.Progress += snapshot => _ = Application.Current.Dispatcher.InvokeAsync(() => ApplyProgress(snapshot));

            StatusMessage = SelectedF100 is null
                ? "Kalibrácia spustená bez externého F100. Najskôr prebehne preflight a kontrola PeakLoggera."
                : $"Kalibrácia spustená · referencia F100 {SelectedF100.PortName}/{SelectedF100Channel}. Najskôr prebehne preflight.";
            await _runner.RunAsync(
                SelectedProfile,
                _setup,
                _activeRun,
                writer,
                startTemperature,
                null,
                SelectedF100 is null ? null : ReadReferenceTemperatureAsync,
                _runCts.Token);

            RunState = _activeRun.State.ToString();
            StatusMessage = _activeRun.State == CalibrationRunState.Completed
                ? "Kalibrácia úspešne dokončená."
                : "Kalibrácia dokončená s upozorneniami.";
            await SendCompletionEmailAsync(_activeRun);
        }
        catch (CalibrationOperatorActionRequiredException ex)
        {
            WarningText = ex.Message;
            RunState = CalibrationRunState.AwaitingOperator.ToString();
            StatusMessage = "Kalibrácia čaká na zásah operátora. Oprav výber/limity alebo povoľ zdôvodnený override a spusti kontrolu znovu.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Kalibrácia bola zastavená operátorom.";
            RunState = CalibrationRunState.Aborted.ToString();
        }
        finally
        {
            _activeWriter = null;
            IsRunning = false;
            _runner = null;
            if (_chamber is not null)
            {
                if (_stopRequested)
                {
                    try { await _chamber.StopAsync(); } catch { }
                }
                try { await _chamber.DisconnectAsync(); } catch { }
                await _chamber.DisposeAsync();
                _chamber = null;
            }
            _stopRequested = false;
            _runCts?.Dispose();
            _runCts = null;
            RefreshHistory();
        }
    }

    private void ApplyProgress(CalibrationProgressSnapshot snapshot)
    {
        RunState = snapshot.State.ToString();
        PlateauLabel = snapshot.PlateauCount <= 0 ? "—" : $"Plato {snapshot.PlateauIndex + 1} / {snapshot.PlateauCount}";
        _calibrationProgressPercent = snapshot.PlateauCount <= 0
            ? 0
            : 100d * Math.Clamp(snapshot.PlateauIndex + 1, 0, snapshot.PlateauCount) / snapshot.PlateauCount;
        TemperatureLabel = snapshot.ActualTemperatureC is { } actual
            ? $"{actual:F2} °C  →  {snapshot.TargetTemperatureC:F2} °C"
            : $"→ {snapshot.TargetTemperatureC:F2} °C";
        _lastChamberTemperatureC = snapshot.ActualTemperatureC;
        _lastReferenceTemperatureC = snapshot.ReferenceTemperatureC ?? _lastReferenceTemperatureC;
        ReferenceTemperatureLabel = snapshot.ReferenceTemperatureC is { } reference ? $"{reference:F3} °C" : ReferenceTemperatureLabel;
        StableLabel = $"{snapshot.StableTargets} / {snapshot.TotalTargets}";
        StatusMessage = snapshot.Message;
        PublishCalibrationStatus();

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
            if (sourceRow is not null && target.CurrentWavelengthNm is { } wavelength)
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
    }

    private void StopCalibration()
    {
        _stopRequested = true;
        _runCts?.Cancel();
        StatusMessage = "Zastavujem kalibráciu a posielam bezpečný STOP komore…";
    }

    private void PublishCalibrationStatus() => CalibrationStatusViewModel.Instance.Update(
        _workspaceChamberId,
        SelectedChamber?.Config.Name ?? "Komora",
        IsRunning,
        SelectedProfile?.Name ?? "FBG kalibrácia",
        RunState,
        PlateauLabel,
        _calibrationProgressPercent);

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
                $"Port {SelectedF100.PortName} / ASL F100 je obsadený kalibráciou zariadenia „{occupiedBy}“. Vyber iný F100 alebo ho najprv uvoľni v pôvodnom okne.");
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

    private void RefreshHistory()
    {
        History.Clear();
        foreach (CalibrationRunRecord run in _calibrationStore.LoadHistory()) History.Add(run);
        ExportSelectedRunCommand.RaiseCanExecuteChanged();
    }

    private void ExportSelectedRun()
    {
        if (SelectedHistoryRun is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export kalibrácie",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"calibration-{SelectedHistoryRun.StartedAt:yyyyMMdd-HHmm}-{SelectedHistoryRun.RunId:N}.csv",
        };
        if (dialog.ShowDialog() != true) return;
        CalibrationStore.ExportSummaryCsv(SelectedHistoryRun, dialog.FileName);
        StatusMessage = $"Export uložený: {dialog.FileName}";
    }

    private async Task SendWarningEmailAsync(CalibrationRunRecord? run, CalibrationWarning warning)
    {
        if (run is null) return;
        await _email.SendAsync(
            $"Kalibrácia FBG – warning – {run.ProfileName}",
            $"Run: {run.RunId}\nKomora: {run.ChamberName}\nProfil: {run.ProfileName}\n\n{warning.Message}");
    }

    private async Task SendCompletionEmailAsync(CalibrationRunRecord run)
    {
        await _email.SendAsync(
            $"Kalibrácia FBG – {run.State} – {run.ProfileName}",
            $"Run: {run.RunId}\nKomora: {run.ChamberName}\nProfil: {run.ProfileName}\nStav: {run.State}\nF100: {run.ReferenceThermometerPort} / {run.ReferenceThermometerChannel}\nPlata: {run.Plateaus.Count}\nWarnings: {run.Warnings.Count}");
    }

    private static IChamberDevice CreateChamberClient(ChamberConfig config) => config.Protocol switch
    {
        ChamberProtocol.PolEkoModbus => new PolEkoClient(),
        ChamberProtocol.SikaRestApi => new SikaTpClient(),
        _ => new ChamberClient(),
    };

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
        OnPropertyChanged(nameof(RequiredStableSamples));
        OnPropertyChanged(nameof(MaxRangePm));
        OnPropertyChanged(nameof(MaxStdDevPm));
        OnPropertyChanged(nameof(MaxDriftPmPerMinute));
        OnPropertyChanged(nameof(ChamberToleranceC));
        OnPropertyChanged(nameof(ChamberStableMinutes));
        OnPropertyChanged(nameof(SensorTimeoutMinutes));
        OnPropertyChanged(nameof(ValidationMinimumDeltaTemperatureC));
        OnPropertyChanged(nameof(ValidationMinimumResponsePm));
        OnPropertyChanged(nameof(AllowValidationOverride));
        OnPropertyChanged(nameof(ValidationOverrideReason));
    }

    private void RefreshCommands()
    {
        ConnectPeakLoggerCommand.RaiseCanExecuteChanged();
        RefreshSensorsCommand.RaiseCanExecuteChanged();
        StartCalibrationCommand.RaiseCanExecuteChanged();
        StopCalibrationCommand.RaiseCanExecuteChanged();
        PauseResumeCommand.RaiseCanExecuteChanged();
        SaveSetupCommand.RaiseCanExecuteChanged();
        SelectSuggestedPeaksCommand.RaiseCanExecuteChanged();
        MarkAllPlateausCommand.RaiseCanExecuteChanged();
        RefreshF100PortsCommand.RaiseCanExecuteChanged();
        DiagnoseF100TalkOnlyCommand.RaiseCanExecuteChanged();
        CheckF100Command.RaiseCanExecuteChanged();
    }

    public async ValueTask DisposeAsync()
    {
        _setupAutosaveCts?.Cancel();
        _setupAutosaveCts?.Dispose();
        _setupAutosaveCts = null;
        if (SelectedProfile is not null && !IsRunning)
        {
            PersistSetup(showStatus: false);
        }
        StopPeakMonitor();
        _activeWriter = null;
        _stopRequested = IsRunning;
        _runCts?.Cancel();
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

public sealed record CalibrationChamberOption(ChamberConfig Config)
{
    public string DisplayName => $"{Config.Name} · {Config.Host}";
}

public sealed class CalibrationPeakRowViewModel : ObservableObject
{
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
            }
        }
    }
    public string SerialNumber => string.IsNullOrWhiteSpace(ChainSerialNumber)
        ? ChannelSerialNumber
        : ChainSerialNumber;
    public bool NeedsSensorSerialNumber => string.IsNullOrWhiteSpace(SerialNumber);
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
    public double? Intensity { get => _intensity; private set => SetProperty(ref _intensity, value); }
    public DateTimeOffset? LastWavelengthUpdate { get => _lastWavelengthUpdate; private set => SetProperty(ref _lastWavelengthUpdate, value); }
    public bool WasSavedSelected { get; }
    public int? Core1 { get; set; }
    public int? Core2 { get; set; }
    public bool Selected { get => _selected; set => SetProperty(ref _selected, value); }
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
    }

    public void SetSerialNumberWarning(string warning) => SerialNumberWarning = warning;

    public void AddSerialNumberWarning(string warning) => SerialNumberWarning =
        string.IsNullOrWhiteSpace(SerialNumberWarning) ? warning : $"{SerialNumberWarning} {warning}";

    private static string NormalizeBarcode(string? value) =>
        (value ?? string.Empty).Trim().Replace("\r", string.Empty).Replace("\n", string.Empty);

    public CalibrationSensorMapping ToMapping() => new()
    {
        Channel = Channel,
        Core1 = Core1,
        Core2 = Core2,
        SerialNumber = SerialNumber,
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
