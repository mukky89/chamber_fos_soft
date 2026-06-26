using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;
using VotschVc3.Core.Recording;

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
    private CancellationTokenSource? _pollingCts;
    private CancellationTokenSource? _profileCts;
    private CsvRecorder? _recorder;
    private DateTime? _profileActualStart;

    public ChamberViewModel(string name, ChamberKind kind, string defaultHost, ProfileStore store)
    {
        Name = name;
        Kind = kind;
        _host = defaultHost;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _client.FrameExchanged += OnFrameExchanged;

        Segments = new ObservableCollection<SegmentViewModel>();
        Segments.CollectionChanged += OnSegmentsChanged;

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected, ReportError);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected, ReportError);
        ReadOnceCommand = new AsyncRelayCommand(ReadOnceAsync, () => IsConnected, ReportError);
        ApplySetpointCommand = new AsyncRelayCommand(ApplySetpointAsync, () => IsConnected, ReportError);
        StopChamberCommand = new AsyncRelayCommand(StopChamberAsync, () => IsConnected, ReportError);

        StartProfileCommand = new AsyncRelayCommand(StartProfileAsync, () => IsConnected && !IsProfileRunning && Segments.Count > 0, ReportError);
        StopProfileCommand = new RelayCommand(StopProfile, () => IsProfileRunning);
        AddSegmentCommand = new RelayCommand(AddSegment);
        RemoveSegmentCommand = new RelayCommand(RemoveSegment, () => SelectedSegment is not null);
        MoveSegmentUpCommand = new RelayCommand(() => MoveSegment(-1), () => SelectedSegment is not null);
        MoveSegmentDownCommand = new RelayCommand(() => MoveSegment(+1), () => SelectedSegment is not null);

        SaveToHistoryCommand = new RelayCommand(SaveToHistory, () => Segments.Count > 0);
        LoadFromHistoryCommand = new RelayCommand(LoadFromHistory, () => SelectedHistoryProfile is not null);
        DeleteFromHistoryCommand = new RelayCommand(DeleteFromHistory, () => SelectedHistoryProfile is not null);
        ImportProfileCommand = new RelayCommand(ImportProfile);

        StartRecordingCommand = new RelayCommand(StartRecording, () => !IsRecording);
        StopRecordingCommand = new RelayCommand(StopRecording, () => IsRecording);
        BrowseRecordingPathCommand = new RelayCommand(BrowseRecordingPath);

        SendTerminalCommand = new AsyncRelayCommand(SendTerminalAsync, () => IsConnected && !string.IsNullOrWhiteSpace(TerminalInput), ReportError);
        ClearTerminalCommand = new RelayCommand(() => TerminalLines.Clear());

        SeedDefaultProfile();
        RefreshHistory();
        RecalculateTiming();
    }

    /// <summary>Stable identity (used as a key in the shell).</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Human readable chamber name shown on the home page and header.</summary>
    public string Name { get; }

    /// <summary>Chamber capabilities.</summary>
    public ChamberKind Kind { get; }

    /// <summary><c>true</c> when the chamber supports humidity control.</summary>
    public bool SupportsHumidity => Kind == ChamberKind.TemperatureHumidity;

    /// <summary>Short capability label for the UI.</summary>
    public string KindLabel => SupportsHumidity ? "Teplota + vlhkosť" : "Teplota";

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

    private async Task ConnectAsync()
    {
        var settings = new ChamberConnectionSettings
        {
            Host = Host,
            Port = Port,
            Address = Address,
            Terminator = TerminatorValue,
            AnalogChannelCount = AnalogChannelCount,
            StartChannelIndex = StartChannelIndex,
        };

        StatusMessage = $"Pripájam sa na {Endpoint}…";
        await _client.ConnectAsync(settings);
        IsConnected = true;
        StatusMessage = "Pripojené.";

        if (PollingEnabled)
        {
            StartPolling();
        }
    }

    private async Task DisconnectAsync()
    {
        StopPolling();
        StopProfile();
        await _client.DisconnectAsync();
        IsConnected = false;
        StatusMessage = "Odpojené.";
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
                ApplyReading(await _client.ReadAsync(token));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba pollingu: {ex.Message}";
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

        if (IsRecording)
        {
            try
            {
                _recorder?.Record(reading);
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
    }

    private async Task StopChamberAsync()
    {
        DigitalChannels digital = ParseDigitalText();
        digital.StartChannelIndex = StartChannelIndex;
        digital.Start = false;

        var setpoints = new List<double> { ManualTemperature, 0d };
        await _client.WriteSetpointsAsync(setpoints, digital);
        StatusMessage = "Komora zastavená (štart kanál vynulovaný).";
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
                $"Cyklus {e.Cycle + 1}/{profile.Cycles} · segment {e.SegmentIndex + 1}/{profile.Segments.Count} " +
                $"\"{e.Segment.Name}\" · {e.TemperatureSetpoint:0.0} °C" +
                (e.HumiditySetpoint is { } h ? $", {h:0.0} %" : string.Empty);
        });

        _profileCts = new CancellationTokenSource();
        _profileActualStart = DateTime.Now;
        IsProfileRunning = true;
        ProfileProgress = 0;
        ProfileStatus = "Profil spustený.";
        StatusMessage = $"Beží profil \"{ProfileName}\".";
        RecalculateTiming();

        try
        {
            await runner.RunAsync(profile, startTemp, startHum, _profileCts.Token);
            ProfileProgress = 100;
            ProfileStatus = "Profil dokončený.";
            StatusMessage = "Profil dokončený.";
        }
        catch (OperationCanceledException)
        {
            ProfileStatus = "Profil zrušený.";
            StatusMessage = "Profil zrušený.";
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
        else
        {
            DateTime end = DateTime.Now + total;
            ProfileScheduleText = $"Ak spustíš teraz, koniec ~ {end:HH:mm} ({end:d})";
        }
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
        StopPolling();
        StopProfile();
        StopRecording();
        _client.FrameExchanged -= OnFrameExchanged;
        await _client.DisposeAsync();
    }

    #endregion
}
