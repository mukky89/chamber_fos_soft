using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;
using VotschVc3.Core.Recording;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// Application view model: owns the chamber connection and exposes connection,
/// live monitoring, manual set point, profile, recording and raw terminal
/// features to the WPF views.
/// </summary>
public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxTerminalLines = 1000;

    private readonly ChamberClient _client = new();
    private CancellationTokenSource? _pollingCts;
    private CancellationTokenSource? _profileCts;
    private CsvRecorder? _recorder;

    public MainViewModel()
    {
        _client.FrameExchanged += OnFrameExchanged;

        Segments = new ObservableCollection<SegmentViewModel>
        {
            new(new ProfileSegment { Name = "Ramp up", TargetTemperature = 85, Duration = TimeSpan.FromMinutes(30), IsRamp = true }),
            new(new ProfileSegment { Name = "Plato (hold)", TargetTemperature = 85, Duration = TimeSpan.FromMinutes(60), IsRamp = false }),
            new(new ProfileSegment { Name = "Ramp down", TargetTemperature = -40, Duration = TimeSpan.FromMinutes(30), IsRamp = true }),
            new(new ProfileSegment { Name = "Plato (hold)", TargetTemperature = -40, Duration = TimeSpan.FromMinutes(60), IsRamp = false }),
        };

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

        StartRecordingCommand = new RelayCommand(StartRecording, () => !IsRecording);
        StopRecordingCommand = new RelayCommand(StopRecording, () => IsRecording);
        BrowseRecordingPathCommand = new RelayCommand(BrowseRecordingPath);

        SendTerminalCommand = new AsyncRelayCommand(SendTerminalAsync, () => IsConnected && !string.IsNullOrWhiteSpace(TerminalInput), ReportError);
        ClearTerminalCommand = new RelayCommand(() => TerminalLines.Clear());
    }

    #region Connection

    private string _host = "192.168.0.1";
    public string Host { get => _host; set => SetProperty(ref _host, value); }

    private int _port = 1080;
    public int Port { get => _port; set => SetProperty(ref _port, value); }

    private int _address = 1;
    public int Address { get => _address; set => SetProperty(ref _address, value); }

    private int _analogChannelCount = Ascii2Protocol.DefaultAnalogChannelCount;
    public int AnalogChannelCount { get => _analogChannelCount; set => SetProperty(ref _analogChannelCount, Math.Max(1, value)); }

    private int _startChannelIndex;
    public int StartChannelIndex { get => _startChannelIndex; set => SetProperty(ref _startChannelIndex, Math.Clamp(value, 0, DigitalChannels.Count - 1)); }

    /// <summary>Terminator choices offered in the UI.</summary>
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

    public string ConnectionState => IsConnected ? $"Connected to {Host}:{Port}" : "Disconnected";

    private string _statusMessage = "Ready.";
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

        StatusMessage = $"Connecting to {Host}:{Port}…";
        await _client.ConnectAsync(settings);
        IsConnected = true;
        StatusMessage = "Connected.";

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
        StatusMessage = "Disconnected.";
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
                if (value)
                {
                    StartPolling();
                }
                else
                {
                    StopPolling();
                }
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

    private async Task ReadOnceAsync()
    {
        ChamberReading reading = await _client.ReadAsync();
        ApplyReading(reading);
    }

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
        // Started from the UI thread; not using ConfigureAwait(false) keeps the
        // continuations on the dispatcher so property updates are thread safe.
        while (!token.IsCancellationRequested)
        {
            try
            {
                ChamberReading reading = await _client.ReadAsync(token);
                ApplyReading(reading);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Polling error: {ex.Message}";
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
        MeasuredHumidity = reading.Humidity;
        MeasuredHumiditySetpoint = reading.HumiditySetpoint;
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
                StatusMessage = $"Recording error: {ex.Message}";
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

    private string _digitalChannelsText = new string('0', DigitalChannels.Count);
    /// <summary>Advanced editable 32 character digital channel string used by the manual write.</summary>
    public string DigitalChannelsText { get => _digitalChannelsText; set => SetProperty(ref _digitalChannelsText, value); }

    public AsyncRelayCommand ApplySetpointCommand { get; }
    public AsyncRelayCommand StopChamberCommand { get; }

    private async Task ApplySetpointAsync()
    {
        DigitalChannels digital = ParseDigitalText();
        digital.StartChannelIndex = StartChannelIndex;
        digital.Start = true;

        var setpoints = new List<double> { ManualTemperature, ControlHumidity ? ManualHumidity : 0d };
        await _client.WriteSetpointsAsync(setpoints, digital);
        StatusMessage = $"Set point written: {ManualTemperature:0.0} °C" +
            (ControlHumidity ? $", {ManualHumidity:0.0} %" : string.Empty);
    }

    private async Task StopChamberAsync()
    {
        DigitalChannels digital = ParseDigitalText();
        digital.StartChannelIndex = StartChannelIndex;
        digital.Start = false;

        var setpoints = new List<double> { ManualTemperature, ControlHumidity ? ManualHumidity : 0d };
        await _client.WriteSetpointsAsync(setpoints, digital);
        StatusMessage = "Chamber stopped (start channel cleared).";
    }

    private DigitalChannels ParseDigitalText() =>
        DigitalChannels.Parse(DigitalChannelsText, StartChannelIndex);

    #endregion

    #region Profile

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

    private string _profileName = "Profile 1";
    public string ProfileName { get => _profileName; set => SetProperty(ref _profileName, value); }

    private int _cycles = 1;
    public int Cycles { get => _cycles; set => SetProperty(ref _cycles, Math.Max(1, value)); }

    private double _profileUpdateIntervalSeconds = 5;
    public double ProfileUpdateIntervalSeconds { get => _profileUpdateIntervalSeconds; set => SetProperty(ref _profileUpdateIntervalSeconds, Math.Max(1, value)); }

    private bool _isProfileRunning;
    public bool IsProfileRunning
    {
        get => _isProfileRunning;
        private set
        {
            if (SetProperty(ref _isProfileRunning, value))
            {
                RefreshCommands();
            }
        }
    }

    private string _profileStatus = "Idle.";
    public string ProfileStatus { get => _profileStatus; private set => SetProperty(ref _profileStatus, value); }

    private double _profileProgress;
    /// <summary>Overall profile completion in percent (0..100).</summary>
    public double ProfileProgress { get => _profileProgress; private set => SetProperty(ref _profileProgress, value); }

    public AsyncRelayCommand StartProfileCommand { get; }
    public RelayCommand StopProfileCommand { get; }
    public RelayCommand AddSegmentCommand { get; }
    public RelayCommand RemoveSegmentCommand { get; }
    public RelayCommand MoveSegmentUpCommand { get; }
    public RelayCommand MoveSegmentDownCommand { get; }

    private async Task StartProfileAsync()
    {
        var profile = new TestProfile
        {
            Name = ProfileName,
            Cycles = Cycles,
            Segments = Segments.Select(s => s.ToModel()).ToList(),
        };

        // Start ramps from the current measured value when available.
        double startTemp = MeasuredTemperature ?? profile.Segments[0].TargetTemperature;
        double? startHum = MeasuredHumidity;

        var runner = new ProfileRunner(_client, TimeSpan.FromSeconds(ProfileUpdateIntervalSeconds));
        double totalSeconds = Math.Max(1, profile.TotalDuration.TotalSeconds);
        double singlePassSeconds = Math.Max(1, profile.SinglePassDuration.TotalSeconds);

        runner.Progress += (_, e) =>
        {
            RunOnUi(() =>
            {
                double completedBeforeSegment = ElapsedBeforeSegment(profile, e.SegmentIndex);
                double doneThisPass = completedBeforeSegment + e.Segment.Duration.TotalSeconds * e.Fraction;
                double overallSeconds = e.Cycle * singlePassSeconds + doneThisPass;
                ProfileProgress = Math.Clamp(overallSeconds / totalSeconds * 100d, 0, 100);
                ProfileStatus =
                    $"Cycle {e.Cycle + 1}/{profile.Cycles} · segment {e.SegmentIndex + 1}/{profile.Segments.Count} " +
                    $"\"{e.Segment.Name}\" · {e.TemperatureSetpoint:0.0} °C" +
                    (e.HumiditySetpoint is { } h ? $", {h:0.0} %" : string.Empty);
            });
        };

        _profileCts = new CancellationTokenSource();
        IsProfileRunning = true;
        ProfileStatus = "Profile started.";
        StatusMessage = $"Running profile \"{ProfileName}\".";

        try
        {
            await runner.RunAsync(profile, startTemp, startHum, _profileCts.Token);
            ProfileProgress = 100;
            ProfileStatus = "Profile finished.";
            StatusMessage = "Profile finished.";
        }
        catch (OperationCanceledException)
        {
            ProfileStatus = "Profile cancelled.";
            StatusMessage = "Profile cancelled.";
        }
        finally
        {
            IsProfileRunning = false;
            _profileCts?.Dispose();
            _profileCts = null;
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

    private void StopProfile()
    {
        _profileCts?.Cancel();
    }

    private void AddSegment()
    {
        var segment = new SegmentViewModel(new ProfileSegment
        {
            Name = $"Segment {Segments.Count + 1}",
            TargetTemperature = MeasuredTemperature ?? 25,
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
            StatusMessage = $"Recording to {_recorder.FilePath}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cannot start recording: {ex.Message}";
        }
    }

    private void StopRecording()
    {
        _recorder?.Dispose();
        _recorder = null;
        IsRecording = false;
        StatusMessage = "Recording stopped.";
    }

    private void BrowseRecordingPath()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Recording file",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
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
        set
        {
            if (SetProperty(ref _terminalInput, value))
            {
                SendTerminalCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand SendTerminalCommand { get; }
    public RelayCommand ClearTerminalCommand { get; }

    private async Task SendTerminalAsync()
    {
        string command = TerminalInput;
        await _client.SendRawAsync(command);
        // The response is appended by the FrameExchanged handler.
    }

    private void OnFrameExchanged(object? sender, FrameExchangedEventArgs e)
    {
        RunOnUi(() =>
        {
            AppendTerminal($"{e.Timestamp:HH:mm:ss.fff}  TX  {Visualise(e.Request)}");
            AppendTerminal($"{e.Timestamp:HH:mm:ss.fff}  RX  {Visualise(e.Response)}");
        });
    }

    private void AppendTerminal(string line)
    {
        TerminalLines.Add(line);
        while (TerminalLines.Count > MaxTerminalLines)
        {
            TerminalLines.RemoveAt(0);
        }
    }

    private static string Visualise(string frame) =>
        frame.Replace("\r", "<CR>").Replace("\n", "<LF>");

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
    }

    private void ReportError(Exception ex) => StatusMessage = $"Error: {ex.Message}";

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
