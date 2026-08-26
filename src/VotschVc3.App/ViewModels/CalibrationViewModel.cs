using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Communication.PolEko;
using VotschVc3.Core.Communication.Sika;
using VotschVc3.Core.Notifications;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.ViewModels;

public sealed class CalibrationViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ProfileStore _profileStore;
    private readonly ChamberConfigStore _chamberStore;
    private readonly CalibrationStore _calibrationStore;
    private readonly EmailNotifier _email = new();
    private PeakLoggerSettings _peakLoggerSettings = new();
    private IPeakLoggerClient? _peakLogger;
    private IChamberDevice? _chamber;
    private CalibrationProfileRunner? _runner;
    private CancellationTokenSource? _runCts;
    private CalibrationRunRecord? _activeRun;
    private CalibrationSetup _setup = new();
    private bool _stopRequested;

    public CalibrationViewModel()
    {
        AppPaths.Initialize();
        _profileStore = new ProfileStore(Path.Combine(AppPaths.ProfilesDir, "profiles.json"));
        _chamberStore = new ChamberConfigStore(Path.Combine(AppPaths.SettingsDir, "chambers.json"));
        _calibrationStore = new CalibrationStore(AppPaths.CalibrationDir);
        _email.Settings = new EmailSettingsStore(Path.Combine(AppPaths.SettingsDir, "email.json")).Load();

        Profiles = new ObservableCollection<TestProfile>(_profileStore.LoadAll());
        Chambers = new ObservableCollection<CalibrationChamberOption>(
            _chamberStore.LoadAll().Select(c => new CalibrationChamberOption(c)));
        Peaks = new ObservableCollection<CalibrationPeakRowViewModel>();
        CalibrationPoints = new ObservableCollection<CalibrationPointRowViewModel>();
        TargetProgress = new ObservableCollection<CalibrationTargetProgressViewModel>();
        History = new ObservableCollection<CalibrationRunRecord>(_calibrationStore.LoadHistory());

        ConnectPeakLoggerCommand = new AsyncRelayCommand(ConnectPeakLoggerAsync, () => !IsRunning, ReportError);
        RefreshSensorsCommand = new AsyncRelayCommand(DiscoverSensorsAsync, () => PeakLoggerConnected && !IsRunning, ReportError);
        SaveSetupCommand = new RelayCommand(SaveSetup, () => SelectedProfile is not null && !IsRunning);
        SelectSuggestedPeaksCommand = new RelayCommand(SelectSuggestedPeaks, () => Peaks.Count > 0 && !IsRunning);
        MarkAllPlateausCommand = new RelayCommand(MarkAllPlateaus, () => CalibrationPoints.Count > 0 && !IsRunning);
        StartCalibrationCommand = new AsyncRelayCommand(StartCalibrationAsync, CanStartCalibration, ReportError);
        PauseResumeCommand = new RelayCommand(PauseResume, () => IsRunning && _runner is not null);
        StopCalibrationCommand = new RelayCommand(StopCalibration, () => IsRunning);
        RefreshHistoryCommand = new RelayCommand(RefreshHistory);
        ExportSelectedRunCommand = new RelayCommand(ExportSelectedRun, () => SelectedHistoryRun is not null);

        if (Profiles.Count > 0) SelectedProfile = Profiles[0];
        if (Chambers.Count > 0) SelectedChamber = Chambers[0];
    }

    public ObservableCollection<TestProfile> Profiles { get; }
    public ObservableCollection<CalibrationChamberOption> Chambers { get; }
    public ObservableCollection<CalibrationPeakRowViewModel> Peaks { get; }
    public ObservableCollection<CalibrationPointRowViewModel> CalibrationPoints { get; }
    public ObservableCollection<CalibrationTargetProgressViewModel> TargetProgress { get; }
    public ObservableCollection<CalibrationRunRecord> History { get; }

    public AsyncRelayCommand ConnectPeakLoggerCommand { get; }
    public AsyncRelayCommand RefreshSensorsCommand { get; }
    public RelayCommand SaveSetupCommand { get; }
    public RelayCommand SelectSuggestedPeaksCommand { get; }
    public RelayCommand MarkAllPlateausCommand { get; }
    public AsyncRelayCommand StartCalibrationCommand { get; }
    public RelayCommand PauseResumeCommand { get; }
    public RelayCommand StopCalibrationCommand { get; }
    public RelayCommand RefreshHistoryCommand { get; }
    public RelayCommand ExportSelectedRunCommand { get; }

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
            if (SetProperty(ref _selectedChamber, value)) RefreshCommands();
        }
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

    private bool _useSimulator = true;
    public bool UseSimulator
    {
        get => _useSimulator;
        set => SetProperty(ref _useSimulator, value);
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
            if (SetProperty(ref _isRunning, value)) RefreshCommands();
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

    private string _stableLabel = "0 / 0";
    public string StableLabel { get => _stableLabel; private set => SetProperty(ref _stableLabel, value); }

    private string _warningText = string.Empty;
    public string WarningText { get => _warningText; private set => SetProperty(ref _warningText, value); }

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

        _setup = _calibrationStore.LoadSetup(SelectedProfile.Id) ?? new CalibrationSetup { ProfileId = SelectedProfile.Id };
        for (int i = 0; i < SelectedProfile.Segments.Count; i++)
        {
            ProfileSegment segment = SelectedProfile.Segments[i];
            if (!segment.IsRamp)
            {
                var point = new CalibrationPointRowViewModel(i, segment);
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
        if (_peakLogger is not null) await _peakLogger.DisposeAsync();
        _peakLoggerSettings = new PeakLoggerSettings
        {
            Host = PeakLoggerHost.Trim(),
            Port = PeakLoggerPort,
            UseSimulator = UseSimulator,
        };
        _peakLogger = UseSimulator
            ? new FakePeakLoggerClient(SimulatorScenario)
            : new PeakLoggerApiClient();

        PeakLoggerStatus = "Pripájam…";
        await _peakLogger.ConnectAsync(_peakLoggerSettings);
        PeakLoggerConnected = true;
        PeakLoggerStatus = UseSimulator ? $"Pripojený · simulátor ({SimulatorScenario})" : "Pripojený";
        await DiscoverSensorsAsync();
    }

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
                var row = new CalibrationPeakRowViewModel(sensor, peak, mapping);
                if (UseSimulator && string.IsNullOrWhiteSpace(row.SerialNumber))
                {
                    row.SerialNumber = sensor.SerialNumber;
                }
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(CalibrationPeakRowViewModel.Selected) or nameof(CalibrationPeakRowViewModel.SerialNumber))
                    {
                        StartCalibrationCommand.RaiseCanExecuteChanged();
                    }
                };
                Peaks.Add(row);
            }
        }

        PeakLoggerStatus = $"Pripojený · {sensors.Count} zdrojov/kanálov · {Peaks.Count} peakov";
        if (!UseSimulator && Peaks.Count > 0 && Peaks.All(p => string.IsNullOrWhiteSpace(p.SerialNumber)))
        {
            StatusMessage = "PeakLogger peaky načítané. Produkčné SN FBG senzora zadaj alebo naskenuj k vybranému peaku; deviceSN z API je SN interrogátora.";
        }
        RefreshCommands();
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

    private void SaveSetup()
    {
        if (SelectedProfile is null) return;
        _setup.ProfileId = SelectedProfile.Id;
        _setup.Mappings = Peaks.Select(p => p.ToMapping()).ToList();
        _calibrationStore.SaveSetup(_setup);

        SelectedProfile.ExecutionMode = ProfileExecutionMode.TemperatureCalibration;
        foreach (CalibrationPointRowViewModel point in CalibrationPoints)
        {
            SelectedProfile.Segments[point.SegmentIndex].IsCalibrationPoint = point.Selected;
        }
        _profileStore.Save(SelectedProfile);
        StatusMessage = $"Kalibračné zapojenie pre profil „{SelectedProfile.Name}“ bolo uložené.";
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
        SaveSetup();
        WarningText = string.Empty;
        TargetProgress.Clear();
        _runCts = new CancellationTokenSource();
        _stopRequested = false;
        IsRunning = true;

        try
        {
            _chamber = CreateChamberClient(SelectedChamber.Config);
            ChamberConnectionSettings connection = ToConnectionSettings(SelectedChamber.Config);
            StatusMessage = $"Pripájam komoru {SelectedChamber.Config.Name}…";
            await _chamber.ConnectAsync(connection, _runCts.Token);
            var initialReading = await _chamber.ReadAsync(_runCts.Token);
            double startTemperature = initialReading.Temperature
                ?? throw new InvalidOperationException("Komora neposkytla platnú nameranú teplotu pred začiatkom kalibrácie.");

            _activeRun = new CalibrationRunRecord
            {
                ProfileId = SelectedProfile.Id,
                ProfileName = SelectedProfile.Name,
                ChamberId = SelectedChamber.Config.Id,
                ChamberName = SelectedChamber.Config.Name,
                Operator = Environment.UserName,
                State = CalibrationRunState.Preflight,
            };

            await using CalibrationRunWriter writer = _calibrationStore.CreateRunWriter(_activeRun);
            var orchestrator = new CalibrationOrchestrator(_peakLogger);
            orchestrator.WarningRaised += warning =>
            {
                _ = Application.Current.Dispatcher.InvokeAsync(() => WarningText = warning.Message);
                _ = SendWarningEmailAsync(_activeRun, warning);
            };
            _runner = new CalibrationProfileRunner(_chamber, orchestrator, _calibrationStore);
            _runner.Progress += snapshot => _ = Application.Current.Dispatcher.InvokeAsync(() => ApplyProgress(snapshot));

            StatusMessage = "Kalibrácia spustená. Najskôr prebehne preflight a kontrola PeakLoggera.";
            await _runner.RunAsync(
                SelectedProfile,
                _setup,
                _activeRun,
                writer,
                startTemperature,
                null,
                null,
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
        TemperatureLabel = snapshot.ActualTemperatureC is { } actual
            ? $"{actual:F2} °C  →  {snapshot.TargetTemperatureC:F2} °C"
            : $"→ {snapshot.TargetTemperatureC:F2} °C";
        StableLabel = $"{snapshot.StableTargets} / {snapshot.TotalTargets}";
        StatusMessage = snapshot.Message;

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
            $"Run: {run.RunId}\nKomora: {run.ChamberName}\nProfil: {run.ProfileName}\nStav: {run.State}\nPlata: {run.Plateaus.Count}\nWarnings: {run.Warnings.Count}");
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
    }

    public async ValueTask DisposeAsync()
    {
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
    private string _serialNumber;
    private string _notes;
    private string _productDescription;
    private string _customer;
    private string _order;
    private double _timeoutMinutes;

    public CalibrationPeakRowViewModel(PeakLoggerSensor sensor, PeakLoggerPeak peak, CalibrationSensorMapping? saved)
    {
        PeakLoggerDeviceSerialNumber = sensor.SerialNumber;
        _serialNumber = saved?.SerialNumber ?? string.Empty;
        Channel = sensor.Channel;
        PeakId = peak.PeakId;
        PeakIndex = peak.PeakIndex;
        CurrentWavelengthNm = peak.WavelengthNm;
        Intensity = peak.Intensity;
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
    public string SerialNumber
    {
        get => _serialNumber;
        set => SetProperty(ref _serialNumber, value?.Trim() ?? string.Empty);
    }
    public string Channel { get; }
    public string PeakId { get; }
    public int PeakIndex { get; }
    public double CurrentWavelengthNm { get; }
    public double? Intensity { get; }
    public bool WasSavedSelected { get; }
    public int? Core1 { get; set; }
    public int? Core2 { get; set; }
    public bool Selected { get => _selected; set => SetProperty(ref _selected, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public string ProductDescription { get => _productDescription; set => SetProperty(ref _productDescription, value); }
    public string Customer { get => _customer; set => SetProperty(ref _customer, value); }
    public string Order { get => _order; set => SetProperty(ref _order, value); }
    public double TimeoutMinutes { get => _timeoutMinutes; set => SetProperty(ref _timeoutMinutes, Math.Max(0, value)); }

    public CalibrationSensorMapping ToMapping() => new()
    {
        Channel = Channel,
        Core1 = Core1,
        Core2 = Core2,
        SerialNumber = SerialNumber,
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
