using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using VotschVc3.App.Charting;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Notifications;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;
using VotschVc3.Core.Recording;
using VotschVc3.Core.Security;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// View model for a single chamber. Each instance owns its own connection,
/// polling loop and profile runner, so two chambers can be operated
/// simultaneously and independently.
/// </summary>
public sealed class ChamberViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxTerminalLines = 1000;

    private readonly ChamberClient _client = new();
    private readonly ProfileStore _store;
    private readonly EmailNotifier _email;
    private readonly ThermometersViewModel _thermometers;
    private readonly AuditLog _audit;
    private CancellationTokenSource? _pollingCts;
    private CancellationTokenSource? _profileCts;
    private CsvRecorder? _recorder;
    private DateTime? _profileActualStart;

    public ChamberViewModel(ChamberConfig config, ProfileStore store, EmailNotifier email, ThermometersViewModel thermometers, AuditLog audit)
    {
        ArgumentNullException.ThrowIfNull(config);
        Id = config.Id;
        Name = config.Name;
        Kind = config.Kind;
        _host = config.Host;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _thermometers = thermometers ?? throw new ArgumentNullException(nameof(thermometers));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _client.FrameExchanged += OnFrameExchanged;

        Segments = new ObservableCollection<SegmentViewModel>();
        Segments.CollectionChanged += OnSegmentsChanged;

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected, ReportError);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected, ReportError);
        ReadOnceCommand = new AsyncRelayCommand(ReadOnceAsync, () => IsConnected, ReportError);
        ApplySetpointCommand = new AsyncRelayCommand(ApplySetpointAsync, () => IsConnected && IsControlAllowed, ReportError);
        StopChamberCommand = new AsyncRelayCommand(StopChamberAsync, () => IsConnected && IsControlAllowed, ReportError);

        StartProfileCommand = new AsyncRelayCommand(StartProfileAsync, () => IsConnected && IsControlAllowed && !IsProfileRunning && Segments.Count > 0, ReportError);
        StopProfileCommand = new RelayCommand(StopProfile, () => IsProfileRunning);
        AddSegmentCommand = new RelayCommand(AddSegment);
        RemoveSegmentCommand = new RelayCommand(RemoveSegment, () => SelectedSegment is not null);
        MoveSegmentUpCommand = new RelayCommand(() => MoveSegment(-1), () => SelectedSegment is not null);
        MoveSegmentDownCommand = new RelayCommand(() => MoveSegment(+1), () => SelectedSegment is not null);

        SaveToHistoryCommand = new RelayCommand(SaveToHistory, () => Segments.Count > 0);
        LoadFromHistoryCommand = new RelayCommand(LoadFromHistory, () => SelectedHistoryProfile is not null);
        DeleteFromHistoryCommand = new RelayCommand(DeleteFromHistory, () => SelectedHistoryProfile is not null);
        ImportProfileCommand = new RelayCommand(ImportProfile);
        ExportProfileCommand = new RelayCommand(ExportProfile, () => Segments.Count > 0);

        StartRecordingCommand = new RelayCommand(StartRecording, () => !IsRecording);
        StopRecordingCommand = new RelayCommand(StopRecording, () => IsRecording);
        BrowseRecordingPathCommand = new RelayCommand(BrowseRecordingPath);

        SendTerminalCommand = new AsyncRelayCommand(SendTerminalAsync, () => IsConnected && !string.IsNullOrWhiteSpace(TerminalInput), ReportError);
        ClearTerminalCommand = new RelayCommand(() => TerminalLines.Clear());

        ApplyConfig(config);
        SeedDefaultProfile();
        RefreshHistory();
        RecalculateTiming();
    }

    /// <summary>Stable identity (used as a key in the shell and for persistence).</summary>
    public Guid Id { get; }

    /// <summary>Human readable chamber name shown on the home page and header.</summary>
    public string Name { get; }

    /// <summary>Chamber capabilities.</summary>
    public ChamberKind Kind { get; }

    /// <summary><c>true</c> when the chamber supports humidity control.</summary>
    public bool SupportsHumidity => Kind == ChamberKind.TemperatureHumidity;

    /// <summary>Short capability label for the UI.</summary>
    public string KindLabel => SupportsHumidity ? "Teplota + vlhkosť" : "Teplota";

    private bool _isControlAllowed = true;
    /// <summary><c>false</c> for view-only users (Operator role); gates control commands.</summary>
    public bool IsControlAllowed
    {
        get => _isControlAllowed;
        private set { if (SetProperty(ref _isControlAllowed, value)) RefreshCommands(); }
    }

    /// <summary>Sets whether the current user may operate this chamber.</summary>
    public void SetControlAllowed(bool allowed) => IsControlAllowed = allowed;

    #region Connection

    private string _host;
    public string Host { get => _host; set { if (SetProperty(ref _host, value)) OnPropertyChanged(nameof(Endpoint)); } }

    private int _port = 1080;
    public int Port { get => _port; set { if (SetProperty(ref _port, value)) OnPropertyChanged(nameof(Endpoint)); } }

    public string Endpoint => $"{Host}:{Port}";

    private int _address = 1;
    public int Address { get => _address; set => SetProperty(ref _address, value); }

    private int _analogChannelCount = Ascii2Protocol.DefaultAnalogChannelCount;
    public int AnalogChannelCount { get => _analogChannelCount; set => SetProperty(ref _analogChannelCount, Math.Max(1, value)); }

    private int _startChannelIndex;
    public int StartChannelIndex { get => _startChannelIndex; set => SetProperty(ref _startChannelIndex, Math.Clamp(value, 0, DigitalChannels.Count - 1)); }

    public IReadOnlyList<string> TerminatorOptions { get; } = new[] { "CR (\\r)", "CR LF (\\r\\n)", "LF (\\n)" };

    private string _selectedTerminator = "CR (\\r)";
    public string SelectedTerminator { get => _selectedTerminator; set => SetProperty(ref _selectedTerminator, value); }

    private string TerminatorValue => SelectedTerminator switch
    {
        "CR LF (\\r\\n)" => "\r\n",
        "LF (\\n)" => "\n",
        _ => "\r",
    };

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(ConnectionState));
                RefreshCommands();
            }
        }
    }

    public string ConnectionState => IsConnected ? $"Pripojené · {Endpoint}" : "Odpojené";

    private string _statusMessage = "Pripravené.";
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }

    private ChamberConnectionSettings BuildSettings() => new()
    {
        Host = Host,
        Port = Port,
        Address = Address,
        Terminator = TerminatorValue,
        AnalogChannelCount = AnalogChannelCount,
        StartChannelIndex = StartChannelIndex,
    };

    private async Task ConnectAsync()
    {
        StopReconnect();
        _connectionLostHandled = false;
        _pollFailureCount = 0;
        ClearAlarm("link");

        StatusMessage = $"Pripájam sa na {Endpoint}…";
        await _client.ConnectAsync(BuildSettings());
        IsConnected = true;
        StatusMessage = "Pripojené.";
        _audit.Log(Name, "Pripojenie", Endpoint);

        if (PollingEnabled)
        {
            StartPolling();
        }
    }

    private async Task DisconnectAsync()
    {
        StopReconnect();
        StopPolling();
        StopProfile();
        await _client.DisconnectAsync();
        IsConnected = false;
        _connectionLostHandled = false;
        ClearAllAlarms();
        StatusMessage = "Odpojené.";
        _audit.Log(Name, "Odpojenie", Endpoint);
    }

    #endregion

    #region Live monitoring

    private bool _pollingEnabled = true;
    public bool PollingEnabled
    {
        get => _pollingEnabled;
        set
        {
            if (SetProperty(ref _pollingEnabled, value) && IsConnected)
            {
                if (value) StartPolling(); else StopPolling();
            }
        }
    }

    private double _pollIntervalSeconds = 2;
    public double PollIntervalSeconds { get => _pollIntervalSeconds; set => SetProperty(ref _pollIntervalSeconds, Math.Max(0.5, value)); }

    private double? _measuredTemperature;
    public double? MeasuredTemperature { get => _measuredTemperature; private set => SetProperty(ref _measuredTemperature, value); }

    private double? _measuredTemperatureSetpoint;
    public double? MeasuredTemperatureSetpoint { get => _measuredTemperatureSetpoint; private set => SetProperty(ref _measuredTemperatureSetpoint, value); }

    private double? _measuredHumidity;
    public double? MeasuredHumidity { get => _measuredHumidity; private set => SetProperty(ref _measuredHumidity, value); }

    private double? _measuredHumiditySetpoint;
    public double? MeasuredHumiditySetpoint { get => _measuredHumiditySetpoint; private set => SetProperty(ref _measuredHumiditySetpoint, value); }

    private string _lastRaw = string.Empty;
    public string LastRaw { get => _lastRaw; private set => SetProperty(ref _lastRaw, value); }

    private DateTimeOffset? _lastUpdate;
    public DateTimeOffset? LastUpdate { get => _lastUpdate; private set => SetProperty(ref _lastUpdate, value); }

    public AsyncRelayCommand ReadOnceCommand { get; }

    private async Task ReadOnceAsync() => ApplyReading(await _client.ReadAsync());

    private void StartPolling()
    {
        StopPolling();
        _pollingCts = new CancellationTokenSource();
        _ = PollLoopAsync(_pollingCts.Token);
    }

    private void StopPolling()
    {
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        _pollingCts = null;
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                ChamberReading reading = await _client.ReadAsync(token);
                _pollFailureCount = 0;
                ApplyReading(reading);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (await OnPollFailureAsync(ex))
                {
                    break;
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ApplyReading(ChamberReading reading)
    {
        MeasuredTemperature = reading.Temperature;
        MeasuredTemperatureSetpoint = reading.TemperatureSetpoint;
        if (SupportsHumidity)
        {
            MeasuredHumidity = reading.Humidity;
            MeasuredHumiditySetpoint = reading.HumiditySetpoint;
        }

        LastRaw = reading.Raw;
        LastUpdate = reading.Timestamp;
        OnPropertyChanged(nameof(ReferenceDeviation));
        RecordLive(reading);
        CheckAlarms(reading);

        if (IsRecording)
        {
            try
            {
                _recorder?.Record(reading, ReferenceTemperature);
                RecordedRows = _recorder?.RowCount ?? 0;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba záznamu: {ex.Message}";
                StopRecording();
            }
        }
    }

    #endregion

    #region Reference thermometer

    /// <summary>Thermometers available as an external reference (live collection).</summary>
    public ObservableCollection<ThermometerDeviceViewModel> AvailableThermometers => _thermometers.Devices;

    private ThermometerDeviceViewModel? _selectedReferenceThermometer;
    /// <summary>External ASL F100 used as a reference next to the chamber's own probe.</summary>
    public ThermometerDeviceViewModel? SelectedReferenceThermometer
    {
        get => _selectedReferenceThermometer;
        set
        {
            ThermometerDeviceViewModel? old = _selectedReferenceThermometer;
            if (SetProperty(ref _selectedReferenceThermometer, value))
            {
                if (old is not null) old.PropertyChanged -= OnReferenceChanged;
                if (value is not null) value.PropertyChanged += OnReferenceChanged;
                RaiseReference();
            }
        }
    }

    /// <summary>The reference thermometer's current temperature, if any.</summary>
    public double? ReferenceTemperature => SelectedReferenceThermometer?.Temperature;

    /// <summary><c>true</c> when a reference thermometer is selected.</summary>
    public bool HasReference => SelectedReferenceThermometer is not null;

    /// <summary>Chamber measured temperature minus reference temperature.</summary>
    public double? ReferenceDeviation =>
        MeasuredTemperature is { } m && ReferenceTemperature is { } r ? m - r : null;

    private void OnReferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ThermometerDeviceViewModel.Temperature))
        {
            RaiseReference();
        }
    }

    private void RaiseReference()
    {
        OnPropertyChanged(nameof(ReferenceTemperature));
        OnPropertyChanged(nameof(ReferenceDeviation));
        OnPropertyChanged(nameof(HasReference));
    }

    #endregion

    #region Manual set point

    private double _manualTemperature = 25;
    public double ManualTemperature { get => _manualTemperature; set => SetProperty(ref _manualTemperature, value); }

    private bool _controlHumidity;
    public bool ControlHumidity { get => _controlHumidity; set => SetProperty(ref _controlHumidity, value); }

    private double _manualHumidity = 50;
    public double ManualHumidity { get => _manualHumidity; set => SetProperty(ref _manualHumidity, value); }

    private string _digitalChannelsText = new('0', DigitalChannels.Count);
    public string DigitalChannelsText { get => _digitalChannelsText; set => SetProperty(ref _digitalChannelsText, value); }

    public AsyncRelayCommand ApplySetpointCommand { get; }
    public AsyncRelayCommand StopChamberCommand { get; }

    private async Task ApplySetpointAsync()
    {
        DigitalChannels digital = ParseDigitalText();
        digital.StartChannelIndex = StartChannelIndex;
        digital.Start = true;

        bool humidity = SupportsHumidity && ControlHumidity;
        var setpoints = new List<double> { ManualTemperature, humidity ? ManualHumidity : 0d };
        await _client.WriteSetpointsAsync(setpoints, digital);
        StatusMessage = $"Setpoint zapísaný: {ManualTemperature:0.0} °C" +
            (humidity ? $", {ManualHumidity:0.0} %" : string.Empty);
        _audit.Log(Name, "Setpoint", $"{ManualTemperature:0.0} °C" + (humidity ? $", {ManualHumidity:0.0} %" : string.Empty));
    }

    private async Task StopChamberAsync()
    {
        DigitalChannels digital = ParseDigitalText();
        digital.StartChannelIndex = StartChannelIndex;
        digital.Start = false;

        var setpoints = new List<double> { ManualTemperature, 0d };
        await _client.WriteSetpointsAsync(setpoints, digital);
        StatusMessage = "Komora zastavená (štart kanál vynulovaný).";
        _audit.Log(Name, "Stop komory", string.Empty);
    }

    private DigitalChannels ParseDigitalText() => DigitalChannels.Parse(DigitalChannelsText, StartChannelIndex);

    #endregion

    #region Profile editor

    public ObservableCollection<SegmentViewModel> Segments { get; }

    private SegmentViewModel? _selectedSegment;
    public SegmentViewModel? SelectedSegment
    {
        get => _selectedSegment;
        set
        {
            if (SetProperty(ref _selectedSegment, value))
            {
                RemoveSegmentCommand.RaiseCanExecuteChanged();
                MoveSegmentUpCommand.RaiseCanExecuteChanged();
                MoveSegmentDownCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _profileName = "Profil 1";
    public string ProfileName { get => _profileName; set => SetProperty(ref _profileName, value); }

    private int _cycles = 1;
    public int Cycles
    {
        get => _cycles;
        set { if (SetProperty(ref _cycles, Math.Max(1, value))) RecalculateTiming(); }
    }

    private double _profileUpdateIntervalSeconds = 5;
    public double ProfileUpdateIntervalSeconds { get => _profileUpdateIntervalSeconds; set => SetProperty(ref _profileUpdateIntervalSeconds, Math.Max(1, value)); }

    private bool _useDelayedStart;
    /// <summary>When set, the profile waits until <see cref="ScheduledStart"/> before running.</summary>
    public bool UseDelayedStart
    {
        get => _useDelayedStart;
        set { if (SetProperty(ref _useDelayedStart, value)) RecalculateTiming(); }
    }

    private DateTime _scheduledStartDate = DateTime.Today;
    public DateTime ScheduledStartDate
    {
        get => _scheduledStartDate;
        set { if (SetProperty(ref _scheduledStartDate, value)) RecalculateTiming(); }
    }

    private string _scheduledStartTime = DateTime.Now.AddMinutes(5).ToString("HH:mm");
    public string ScheduledStartTime
    {
        get => _scheduledStartTime;
        set { if (SetProperty(ref _scheduledStartTime, value)) RecalculateTiming(); }
    }

    /// <summary>The resolved scheduled start moment (date + parsed time).</summary>
    public DateTime ScheduledStart
    {
        get
        {
            TimeSpan time = TimeSpan.TryParse(ScheduledStartTime, out TimeSpan t) ? t : TimeSpan.Zero;
            return ScheduledStartDate.Date + time;
        }
    }

    private bool _isProfileRunning;
    public bool IsProfileRunning
    {
        get => _isProfileRunning;
        private set { if (SetProperty(ref _isProfileRunning, value)) RefreshCommands(); }
    }

    private string _profileStatus = "Nečinné.";
    public string ProfileStatus { get => _profileStatus; private set => SetProperty(ref _profileStatus, value); }

    private double _profileProgress;
    public double ProfileProgress { get => _profileProgress; private set => SetProperty(ref _profileProgress, value); }

    private string _profileDurationText = "—";
    /// <summary>Total duration of the profile including cycles.</summary>
    public string ProfileDurationText { get => _profileDurationText; private set => SetProperty(ref _profileDurationText, value); }

    private string _profileScheduleText = string.Empty;
    /// <summary>Computed start / end schedule of the profile.</summary>
    public string ProfileScheduleText { get => _profileScheduleText; private set => SetProperty(ref _profileScheduleText, value); }

    public AsyncRelayCommand StartProfileCommand { get; }
    public RelayCommand StopProfileCommand { get; }
    public RelayCommand AddSegmentCommand { get; }
    public RelayCommand RemoveSegmentCommand { get; }
    public RelayCommand MoveSegmentUpCommand { get; }
    public RelayCommand MoveSegmentDownCommand { get; }

    private void SeedDefaultProfile()
    {
        if (SupportsHumidity)
        {
            Segments.Add(new SegmentViewModel(new ProfileSegment { Name = "Ohrev", TargetTemperature = 60, TargetHumidity = 80, Duration = TimeSpan.FromMinutes(30), IsRamp = true }));
            Segments.Add(new SegmentViewModel(new ProfileSegment { Name = "Plato", TargetTemperature = 60, TargetHumidity = 80, Duration = TimeSpan.FromMinutes(60), IsRamp = false }));
            Segments.Add(new SegmentViewModel(new ProfileSegment { Name = "Chladenie", TargetTemperature = 25, TargetHumidity = 50, Duration = TimeSpan.FromMinutes(30), IsRamp = true }));
        }
        else
        {
            Segments.Add(new SegmentViewModel(new ProfileSegment { Name = "Ohrev", TargetTemperature = 85, Duration = TimeSpan.FromMinutes(30), IsRamp = true }));
            Segments.Add(new SegmentViewModel(new ProfileSegment { Name = "Plato", TargetTemperature = 85, Duration = TimeSpan.FromMinutes(60), IsRamp = false }));
            Segments.Add(new SegmentViewModel(new ProfileSegment { Name = "Chladenie", TargetTemperature = -40, Duration = TimeSpan.FromMinutes(30), IsRamp = true }));
        }
    }

    private TestProfile BuildProfile() => new()
    {
        Name = ProfileName,
        Kind = Kind,
        Cycles = Cycles,
        CreatedAt = DateTimeOffset.Now,
        Segments = Segments.Select(s => s.ToModel()).ToList(),
    };

    private async Task StartProfileAsync()
    {
        TestProfile profile = BuildProfile();

        double startTemp = MeasuredTemperature ?? profile.Segments[0].TargetTemperature;
        double? startHum = SupportsHumidity ? MeasuredHumidity : null;

        var runner = new ProfileRunner(_client, TimeSpan.FromSeconds(ProfileUpdateIntervalSeconds));
        double totalSeconds = Math.Max(1, profile.TotalDuration.TotalSeconds);
        double singlePassSeconds = Math.Max(1, profile.SinglePassDuration.TotalSeconds);

        runner.Progress += (_, e) => RunOnUi(() =>
        {
            double completedBeforeSegment = ElapsedBeforeSegment(profile, e.SegmentIndex);
            double doneThisPass = completedBeforeSegment + e.Segment.Duration.TotalSeconds * e.Fraction;
            double overallSeconds = e.Cycle * singlePassSeconds + doneThisPass;
            ProfileProgress = Math.Clamp(overallSeconds / totalSeconds * 100d, 0, 100);
            ProfileStatus =
                (e.IsSoaking ? "⏳ Soak — čakám na toleranciu · " : string.Empty) +
                $"Cyklus {e.Cycle + 1}/{profile.Cycles} · segment {e.SegmentIndex + 1}/{profile.Segments.Count} " +
                $"\"{e.Segment.Name}\" · {e.TemperatureSetpoint:0.0} °C" +
                (e.HumiditySetpoint is { } h ? $", {h:0.0} %" : string.Empty);
        });

        _profileCts = new CancellationTokenSource();
        CancellationToken token = _profileCts.Token;
        IsProfileRunning = true;
        ProfileProgress = 0;

        try
        {
            if (UseDelayedStart && ScheduledStart > DateTime.Now)
            {
                await WaitForScheduledStartAsync(token);
            }

            _profileActualStart = DateTime.Now;
            ProfileStatus = "Profil spustený.";
            StatusMessage = $"Beží profil \"{ProfileName}\".";
            _audit.Log(Name, "Štart profilu", $"{ProfileName}, {Cycles} cyklov");
            RecalculateTiming();

            await runner.RunAsync(profile, startTemp, startHum, token);

            ProfileProgress = 100;
            ProfileStatus = "Profil dokončený.";
            StatusMessage = "Profil dokončený.";
            _audit.Log(Name, "Profil dokončený", ProfileName);
            await NotifyCompletionAsync();
        }
        catch (OperationCanceledException)
        {
            ProfileStatus = "Profil zrušený.";
            StatusMessage = "Profil zrušený.";
            _audit.Log(Name, "Profil zrušený", ProfileName);
        }
        finally
        {
            IsProfileRunning = false;
            _profileActualStart = null;
            _profileCts?.Dispose();
            _profileCts = null;
            RecalculateTiming();
        }
    }

    private async Task WaitForScheduledStartAsync(CancellationToken token)
    {
        while (DateTime.Now < ScheduledStart)
        {
            token.ThrowIfCancellationRequested();
            TimeSpan remaining = ScheduledStart - DateTime.Now;
            ProfileStatus = $"Naplánované na {ScheduledStart:dd.MM HH:mm} · štart o {FormatCountdown(remaining)}";
            StatusMessage = ProfileStatus;
            TimeSpan tick = remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
            if (tick > TimeSpan.Zero)
            {
                await Task.Delay(tick, token);
            }
        }
    }

    private async Task NotifyCompletionAsync()
    {
        if (!_email.CanSend)
        {
            return;
        }

        string subject = $"Profil dokončený: {ProfileName} ({Name})";
        string body =
            $"Testovací profil \"{ProfileName}\" na komore \"{Name}\" bol dokončený " +
            $"{DateTime.Now:dd.MM.yyyy HH:mm:ss}.\r\nCelkové trvanie: {ProfileDurationText}.";

        EmailResult result = await _email.SendAsync(subject, body);
        if (result.Sent)
        {
            StatusMessage = "Profil dokončený · e-mail odoslaný.";
        }
        else if (result.Error is not null)
        {
            StatusMessage = $"Profil dokončený · e-mail zlyhal: {result.Error}";
        }
    }

    private static string FormatCountdown(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
        if (t.TotalMinutes >= 1) return $"{t.Minutes} min {t.Seconds} s";
        return $"{t.Seconds} s";
    }

    private static double ElapsedBeforeSegment(TestProfile profile, int segmentIndex)
    {
        double seconds = 0;
        for (int i = 0; i < segmentIndex && i < profile.Segments.Count; i++)
        {
            seconds += profile.Segments[i].Duration.TotalSeconds;
        }

        return seconds;
    }

    private void StopProfile() => _profileCts?.Cancel();

    private void AddSegment()
    {
        var segment = new SegmentViewModel(new ProfileSegment
        {
            Name = $"Segment {Segments.Count + 1}",
            TargetTemperature = MeasuredTemperature ?? 25,
            TargetHumidity = SupportsHumidity ? (MeasuredHumidity ?? 50) : null,
            Duration = TimeSpan.FromMinutes(10),
            IsRamp = true,
        });
        Segments.Add(segment);
        SelectedSegment = segment;
    }

    private void RemoveSegment()
    {
        if (SelectedSegment is { } segment)
        {
            int index = Segments.IndexOf(segment);
            Segments.Remove(segment);
            if (Segments.Count > 0)
            {
                SelectedSegment = Segments[Math.Clamp(index, 0, Segments.Count - 1)];
            }
        }

        StartProfileCommand.RaiseCanExecuteChanged();
        SaveToHistoryCommand.RaiseCanExecuteChanged();
    }

    private void MoveSegment(int delta)
    {
        if (SelectedSegment is not { } segment)
        {
            return;
        }

        int index = Segments.IndexOf(segment);
        int target = index + delta;
        if (target < 0 || target >= Segments.Count)
        {
            return;
        }

        Segments.Move(index, target);
        SelectedSegment = segment;
    }

    private void OnSegmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (SegmentViewModel s in e.OldItems)
            {
                s.PropertyChanged -= OnSegmentEdited;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (SegmentViewModel s in e.NewItems)
            {
                s.PropertyChanged += OnSegmentEdited;
            }
        }

        StartProfileCommand.RaiseCanExecuteChanged();
        SaveToHistoryCommand.RaiseCanExecuteChanged();
        ExportProfileCommand.RaiseCanExecuteChanged();
        RecalculateTiming();
    }

    private void OnSegmentEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SegmentViewModel.DurationMinutes))
        {
            RecalculateTiming();
        }
    }

    private void RecalculateTiming()
    {
        double minutes = Segments.Sum(s => s.DurationMinutes) * Math.Max(1, Cycles);
        var total = TimeSpan.FromMinutes(minutes);
        ProfileDurationText = FormatDuration(total);

        if (IsProfileRunning && _profileActualStart is { } start)
        {
            DateTime end = start + total;
            ProfileScheduleText = $"Spustené {start:HH:mm:ss} · koniec ~ {end:HH:mm:ss} ({end:d})";
        }
        else if (UseDelayedStart)
        {
            DateTime begin = ScheduledStart;
            DateTime end = begin + total;
            ProfileScheduleText = $"Štart {begin:dd.MM HH:mm} · koniec ~ {end:HH:mm} ({end:d})";
        }
        else
        {
            DateTime end = DateTime.Now + total;
            ProfileScheduleText = $"Ak spustíš teraz, koniec ~ {end:HH:mm} ({end:d})";
        }

        BuildPreviewCharts();
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalMinutes < 1) return "< 1 min";
        int days = (int)t.TotalDays;
        string result = days > 0 ? $"{days} d " : string.Empty;
        return result + $"{t.Hours} h {t.Minutes} min";
    }

    #endregion

    #region Profile history

    public ObservableCollection<TestProfile> History { get; } = new();

    private TestProfile? _selectedHistoryProfile;
    public TestProfile? SelectedHistoryProfile
    {
        get => _selectedHistoryProfile;
        set
        {
            if (SetProperty(ref _selectedHistoryProfile, value))
            {
                LoadFromHistoryCommand.RaiseCanExecuteChanged();
                DeleteFromHistoryCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand SaveToHistoryCommand { get; }
    public RelayCommand LoadFromHistoryCommand { get; }
    public RelayCommand DeleteFromHistoryCommand { get; }
    public RelayCommand ImportProfileCommand { get; }
    public RelayCommand ExportProfileCommand { get; }

    private void RefreshHistory()
    {
        History.Clear();
        foreach (TestProfile profile in _store.LoadAll().Where(p => p.Kind == Kind))
        {
            History.Add(profile);
        }
    }

    private void SaveToHistory()
    {
        TestProfile profile = BuildProfile();
        profile.Id = Guid.NewGuid();
        _store.Save(profile);
        RefreshHistory();
        SelectedHistoryProfile = History.FirstOrDefault(p => p.Id == profile.Id);
        StatusMessage = $"Profil \"{profile.Name}\" uložený do histórie.";
    }

    private void LoadFromHistory()
    {
        if (SelectedHistoryProfile is { } profile)
        {
            ApplyProfile(profile);
            StatusMessage = $"Profil \"{profile.Name}\" načítaný z histórie.";
        }
    }

    private void ImportProfile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importovať Vötsch / SIMPATI profil",
            Filter = "Profily (*.csv;*.txt;*.dat;*.prg;*.json)|*.csv;*.txt;*.dat;*.prg;*.json|Všetky súbory (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ProfileImportResult result = ProfileImporter.ImportFile(dialog.FileName, Kind);
            ApplyProfile(result.Profile);

            string warnings = result.Warnings.Count > 0
                ? $" · {result.Warnings.Count} upozornení: {string.Join(" ", result.Warnings.Take(2))}"
                : string.Empty;
            StatusMessage = $"Importované ({result.FormatDescription}), {result.Profile.Segments.Count} segmentov{warnings}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import zlyhal: {ex.Message}";
        }
    }

    private void ExportProfile()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportovať profil",
            Filter = "CSV (pre Vötsch/Excel) (*.csv)|*.csv|JSON (*.json)|*.json",
            DefaultExt = ".csv",
            FileName = $"{Sanitize(ProfileName)}.csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ProfileExporter.ExportFile(BuildProfile(), dialog.FileName);
            StatusMessage = $"Profil exportovaný do {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export zlyhal: {ex.Message}";
        }
    }

    private static string Sanitize(string name)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "profil" : name;
    }

    private void ApplyProfile(TestProfile profile)
    {
        ProfileName = profile.Name;
        Cycles = profile.Cycles;

        Segments.Clear();
        foreach (ProfileSegment segment in profile.Segments)
        {
            Segments.Add(new SegmentViewModel(segment));
        }

        SelectedSegment = Segments.FirstOrDefault();
        RecalculateTiming();
    }

    private void DeleteFromHistory()
    {
        if (SelectedHistoryProfile is { } profile)
        {
            _store.Delete(profile.Id);
            RefreshHistory();
            StatusMessage = $"Profil \"{profile.Name}\" odstránený z histórie.";
        }
    }

    #endregion

    #region Recording

    private string _recordingPath =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            $"vc3_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    public string RecordingPath { get => _recordingPath; set => SetProperty(ref _recordingPath, value); }

    private bool _isRecording;
    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (SetProperty(ref _isRecording, value))
            {
                StartRecordingCommand.RaiseCanExecuteChanged();
                StopRecordingCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private long _recordedRows;
    public long RecordedRows { get => _recordedRows; private set => SetProperty(ref _recordedRows, value); }

    public RelayCommand StartRecordingCommand { get; }
    public RelayCommand StopRecordingCommand { get; }
    public RelayCommand BrowseRecordingPathCommand { get; }

    private void StartRecording()
    {
        try
        {
            _recorder = new CsvRecorder(RecordingPath);
            RecordedRows = _recorder.RowCount;
            IsRecording = true;
            StatusMessage = $"Zaznamenávam do {_recorder.FilePath}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Nedá sa spustiť záznam: {ex.Message}";
        }
    }

    private void StopRecording()
    {
        _recorder?.Dispose();
        _recorder = null;
        IsRecording = false;
        StatusMessage = "Záznam zastavený.";
    }

    private void BrowseRecordingPath()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Súbor záznamu",
            Filter = "CSV súbory (*.csv)|*.csv|Všetky súbory (*.*)|*.*",
            DefaultExt = ".csv",
            FileName = System.IO.Path.GetFileName(RecordingPath),
            InitialDirectory = System.IO.Path.GetDirectoryName(RecordingPath) ?? string.Empty,
        };

        if (dialog.ShowDialog() == true)
        {
            RecordingPath = dialog.FileName;
        }
    }

    #endregion

    #region Terminal

    public ObservableCollection<string> TerminalLines { get; } = new();

    private string _terminalInput = "$01I";
    public string TerminalInput
    {
        get => _terminalInput;
        set { if (SetProperty(ref _terminalInput, value)) SendTerminalCommand.RaiseCanExecuteChanged(); }
    }

    public AsyncRelayCommand SendTerminalCommand { get; }
    public RelayCommand ClearTerminalCommand { get; }

    private async Task SendTerminalAsync() => await _client.SendRawAsync(TerminalInput);

    private void OnFrameExchanged(object? sender, FrameExchangedEventArgs e) => RunOnUi(() =>
    {
        AppendTerminal($"{e.Timestamp:HH:mm:ss.fff}  TX  {Visualise(e.Request)}");
        AppendTerminal($"{e.Timestamp:HH:mm:ss.fff}  RX  {Visualise(e.Response)}");
    });

    private void AppendTerminal(string line)
    {
        TerminalLines.Add(line);
        while (TerminalLines.Count > MaxTerminalLines)
        {
            TerminalLines.RemoveAt(0);
        }
    }

    private static string Visualise(string frame) => frame.Replace("\r", "<CR>").Replace("\n", "<LF>");

    #endregion

    #region Safety / watchdog

    private const int MaxPollFailures = 3;
    private readonly Dictionary<string, string> _alarms = new();
    private int _pollFailureCount;
    private bool _connectionLostHandled;
    private CancellationTokenSource? _reconnectCts;

    private bool _alarmsEnabled;
    public bool AlarmsEnabled
    {
        get => _alarmsEnabled;
        set
        {
            if (SetProperty(ref _alarmsEnabled, value) && !value)
            {
                ClearAlarm("temp");
                ClearAlarm("hum");
            }
        }
    }

    private double _tempMin = -45;
    public double TempMin { get => _tempMin; set => SetProperty(ref _tempMin, value); }

    private double _tempMax = 190;
    public double TempMax { get => _tempMax; set => SetProperty(ref _tempMax, value); }

    private double _humMin;
    public double HumMin { get => _humMin; set => SetProperty(ref _humMin, value); }

    private double _humMax = 100;
    public double HumMax { get => _humMax; set => SetProperty(ref _humMax, value); }

    private bool _autoStopOnAlarm = true;
    /// <summary>Stop a running profile when an alarm or connection loss occurs.</summary>
    public bool AutoStopOnAlarm { get => _autoStopOnAlarm; set => SetProperty(ref _autoStopOnAlarm, value); }

    private bool _autoReconnect = true;
    /// <summary>Automatically try to reconnect after a connection loss.</summary>
    public bool AutoReconnect { get => _autoReconnect; set => SetProperty(ref _autoReconnect, value); }

    private bool _isAlarm;
    public bool IsAlarm { get => _isAlarm; private set => SetProperty(ref _isAlarm, value); }

    private string _alarmMessage = "Bez alarmu.";
    public string AlarmMessage { get => _alarmMessage; private set => SetProperty(ref _alarmMessage, value); }

    private void CheckAlarms(ChamberReading reading)
    {
        if (!AlarmsEnabled)
        {
            return;
        }

        if (reading.Temperature is { } t)
        {
            if (t < TempMin || t > TempMax)
            {
                RaiseAlarm("temp", $"Teplota {t:0.0} °C mimo limitu [{TempMin:0.#}; {TempMax:0.#}]");
            }
            else
            {
                ClearAlarm("temp");
            }
        }

        if (SupportsHumidity && reading.Humidity is { } h)
        {
            if (h < HumMin || h > HumMax)
            {
                RaiseAlarm("hum", $"Vlhkosť {h:0.0} % mimo limitu [{HumMin:0.#}; {HumMax:0.#}]");
            }
            else
            {
                ClearAlarm("hum");
            }
        }
    }

    private void RaiseAlarm(string key, string message)
    {
        bool isNew = !_alarms.ContainsKey(key);
        _alarms[key] = message;
        UpdateAlarmState();

        if (isNew)
        {
            StatusMessage = $"⚠ ALARM: {message}";
            _audit.Log(Name, "ALARM", message);
            _ = SendAlarmEmailAsync(message);
            if (AutoStopOnAlarm && IsProfileRunning)
            {
                StopProfile();
            }
        }
    }

    private void ClearAlarm(string key)
    {
        if (_alarms.Remove(key))
        {
            UpdateAlarmState();
        }
    }

    private void ClearAllAlarms()
    {
        if (_alarms.Count > 0)
        {
            _alarms.Clear();
            UpdateAlarmState();
        }
    }

    private void UpdateAlarmState()
    {
        IsAlarm = _alarms.Count > 0;
        AlarmMessage = _alarms.Count > 0 ? string.Join(" · ", _alarms.Values) : "Bez alarmu.";
    }

    private async Task<bool> OnPollFailureAsync(Exception ex)
    {
        _pollFailureCount++;
        StatusMessage = $"Chyba pollingu ({_pollFailureCount}/{MaxPollFailures}): {ex.Message}";
        if (_pollFailureCount >= MaxPollFailures)
        {
            await HandleConnectionLostAsync(ex.Message);
            return true;
        }

        return false;
    }

    private async Task HandleConnectionLostAsync(string reason)
    {
        if (_connectionLostHandled)
        {
            return;
        }

        _connectionLostHandled = true;
        StopPolling();
        await _client.DisconnectAsync();
        IsConnected = false;

        RaiseAlarm("link", $"Strata spojenia: {reason}");

        if (AutoReconnect)
        {
            StartReconnect();
        }
    }

    private void StartReconnect()
    {
        StopReconnect();
        _reconnectCts = new CancellationTokenSource();
        _ = ReconnectLoopAsync(_reconnectCts.Token);
    }

    private void StopReconnect()
    {
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;
    }

    private async Task ReconnectLoopAsync(CancellationToken token)
    {
        int delaySeconds = 2;
        while (!token.IsCancellationRequested)
        {
            StatusMessage = $"Pokus o opätovné pripojenie o {delaySeconds} s…";
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                await _client.ConnectAsync(BuildSettings(), token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Opätovné pripojenie zlyhalo: {ex.Message}";
                delaySeconds = Math.Min(delaySeconds * 2, 30);
                continue;
            }

            _connectionLostHandled = false;
            _pollFailureCount = 0;
            IsConnected = true;
            ClearAlarm("link");
            StatusMessage = "Opätovne pripojené.";
            if (PollingEnabled)
            {
                StartPolling();
            }

            return;
        }
    }

    private Task SendAlarmEmailAsync(string message)
    {
        if (!_email.CanSend)
        {
            return Task.CompletedTask;
        }

        return _email.SendAsync(
            $"⚠ ALARM – {Name}",
            $"Komora \"{Name}\": {message}\r\nČas: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
    }

    #endregion

    #region Charting

    private const int LiveWindow = 600;
    private readonly List<LiveSample> _live = new();

    private static readonly Brush TempBrush = Freeze(0xFF, 0x8A, 0x5C);
    private static readonly Brush TempSpBrush = Freeze(0xFF, 0xC2, 0xA8);
    private static readonly Brush HumBrush = Freeze(0x4F, 0xB6, 0xFF);
    private static readonly Brush HumSpBrush = Freeze(0xA9, 0xDC, 0xFF);

    private IReadOnlyList<ChartSeries> _liveTempSeries = Array.Empty<ChartSeries>();
    public IReadOnlyList<ChartSeries> LiveTempSeries { get => _liveTempSeries; private set => SetProperty(ref _liveTempSeries, value); }

    private IReadOnlyList<ChartSeries> _liveHumSeries = Array.Empty<ChartSeries>();
    public IReadOnlyList<ChartSeries> LiveHumSeries { get => _liveHumSeries; private set => SetProperty(ref _liveHumSeries, value); }

    private IReadOnlyList<ChartSeries> _previewTempSeries = Array.Empty<ChartSeries>();
    public IReadOnlyList<ChartSeries> PreviewTempSeries { get => _previewTempSeries; private set => SetProperty(ref _previewTempSeries, value); }

    private IReadOnlyList<ChartSeries> _previewHumSeries = Array.Empty<ChartSeries>();
    public IReadOnlyList<ChartSeries> PreviewHumSeries { get => _previewHumSeries; private set => SetProperty(ref _previewHumSeries, value); }

    private void RecordLive(ChamberReading reading)
    {
        _live.Add(new LiveSample(reading.Timestamp, reading.Temperature, reading.TemperatureSetpoint,
            SupportsHumidity ? reading.Humidity : null, SupportsHumidity ? reading.HumiditySetpoint : null));
        if (_live.Count > LiveWindow)
        {
            _live.RemoveRange(0, _live.Count - LiveWindow);
        }

        BuildLiveCharts();
    }

    private void BuildLiveCharts()
    {
        if (_live.Count == 0)
        {
            LiveTempSeries = Array.Empty<ChartSeries>();
            LiveHumSeries = Array.Empty<ChartSeries>();
            return;
        }

        DateTimeOffset t0 = _live[0].Time;
        double X(LiveSample s) => (s.Time - t0).TotalMinutes;

        var temp = new List<ChartSeries>();
        AddIf(temp, Line("Teplota", TempBrush, false, _live.Select(s => (X(s), s.Temp))));
        AddIf(temp, Line("Setpoint", TempSpBrush, true, _live.Select(s => (X(s), s.TempSp))));
        LiveTempSeries = temp;

        if (SupportsHumidity)
        {
            var hum = new List<ChartSeries>();
            AddIf(hum, Line("Vlhkosť", HumBrush, false, _live.Select(s => (X(s), s.Hum))));
            AddIf(hum, Line("Setpoint", HumSpBrush, true, _live.Select(s => (X(s), s.HumSp))));
            LiveHumSeries = hum;
        }
    }

    private void BuildPreviewCharts()
    {
        if (Segments.Count == 0)
        {
            PreviewTempSeries = Array.Empty<ChartSeries>();
            PreviewHumSeries = Array.Empty<ChartSeries>();
            return;
        }

        var tempPts = new List<Point>();
        var humPts = new List<Point>();
        double prevT = MeasuredTemperature ?? Segments[0].TargetTemperature;
        double prevH = MeasuredHumidity ?? (Segments[0].TargetHumidity ?? 50);
        double t = 0;

        tempPts.Add(new Point(0, prevT));
        if (SupportsHumidity) humPts.Add(new Point(0, prevH));

        int cycles = Math.Max(1, Cycles);
        for (int c = 0; c < cycles; c++)
        {
            foreach (SegmentViewModel s in Segments)
            {
                double dur = Math.Max(0, s.DurationMinutes);
                double targetT = s.TargetTemperature;
                double targetH = s.TargetHumidity ?? prevH;

                if (s.IsRamp)
                {
                    t += dur;
                    tempPts.Add(new Point(t, targetT));
                    if (SupportsHumidity) humPts.Add(new Point(t, targetH));
                }
                else
                {
                    tempPts.Add(new Point(t, targetT));
                    if (SupportsHumidity) humPts.Add(new Point(t, targetH));
                    t += dur;
                    tempPts.Add(new Point(t, targetT));
                    if (SupportsHumidity) humPts.Add(new Point(t, targetH));
                }

                prevT = targetT;
                prevH = targetH;
            }
        }

        PreviewTempSeries = new[] { new ChartSeries("Profil teplota", TempBrush, tempPts) };
        PreviewHumSeries = SupportsHumidity
            ? new[] { new ChartSeries("Profil vlhkosť", HumBrush, humPts) }
            : Array.Empty<ChartSeries>();
    }

    private static ChartSeries? Line(string name, Brush brush, bool dashed, IEnumerable<(double x, double? y)> points)
    {
        var pts = points.Where(p => p.y.HasValue).Select(p => new Point(p.x, p.y!.Value)).ToList();
        return pts.Count > 0 ? new ChartSeries(name, brush, pts, dashed) : null;
    }

    private static void AddIf(List<ChartSeries> list, ChartSeries? series)
    {
        if (series is not null)
        {
            list.Add(series);
        }
    }

    private static Brush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private readonly record struct LiveSample(DateTimeOffset Time, double? Temp, double? TempSp, double? Hum, double? HumSp);

    #endregion

    #region Persistence

    /// <summary>Applies a saved configuration (connection, mapping, alarm limits).</summary>
    public void ApplyConfig(ChamberConfig c)
    {
        ArgumentNullException.ThrowIfNull(c);
        Host = c.Host;
        Port = c.Port;
        Address = c.Address;
        AnalogChannelCount = c.AnalogChannelCount;
        StartChannelIndex = c.StartChannelIndex;
        SelectedTerminator = c.Terminator;
        PollIntervalSeconds = c.PollIntervalSeconds;
        AlarmsEnabled = c.AlarmsEnabled;
        TempMin = c.TempMin;
        TempMax = c.TempMax;
        HumMin = c.HumMin;
        HumMax = c.HumMax;
        AutoStopOnAlarm = c.AutoStopOnAlarm;
        AutoReconnect = c.AutoReconnect;
    }

    /// <summary>Captures the current configuration for persistence.</summary>
    public ChamberConfig ToConfig() => new()
    {
        Id = Id,
        Name = Name,
        Kind = Kind,
        Host = Host,
        Port = Port,
        Address = Address,
        AnalogChannelCount = AnalogChannelCount,
        StartChannelIndex = StartChannelIndex,
        Terminator = SelectedTerminator,
        PollIntervalSeconds = PollIntervalSeconds,
        AlarmsEnabled = AlarmsEnabled,
        TempMin = TempMin,
        TempMax = TempMax,
        HumMin = HumMin,
        HumMax = HumMax,
        AutoStopOnAlarm = AutoStopOnAlarm,
        AutoReconnect = AutoReconnect,
    };

    #endregion

    #region Infrastructure

    private void RefreshCommands()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        ReadOnceCommand.RaiseCanExecuteChanged();
        ApplySetpointCommand.RaiseCanExecuteChanged();
        StopChamberCommand.RaiseCanExecuteChanged();
        StartProfileCommand.RaiseCanExecuteChanged();
        StopProfileCommand.RaiseCanExecuteChanged();
        SendTerminalCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(IsConnected));
    }

    private void ReportError(Exception ex) => StatusMessage = $"Chyba: {ex.Message}";

    private static void RunOnUi(Action action)
    {
        Application? app = Application.Current;
        if (app?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    public async ValueTask DisposeAsync()
    {
        StopReconnect();
        StopPolling();
        StopProfile();
        StopRecording();
        if (_selectedReferenceThermometer is not null)
        {
            _selectedReferenceThermometer.PropertyChanged -= OnReferenceChanged;
        }

        _client.FrameExchanged -= OnFrameExchanged;
        await _client.DisposeAsync();
    }

    #endregion
}
