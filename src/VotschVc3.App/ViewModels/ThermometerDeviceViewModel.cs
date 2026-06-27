using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using VotschVc3.App.Charting;
using VotschVc3.App.Mvvm;
using VotschVc3.App.Thermometers;
using VotschVc3.Core.Recording;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// View model for one ASL F100 thermometer (one USB COM port). Several of these
/// run independently so multiple identical units can be read at once.
/// </summary>
public sealed class ThermometerDeviceViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxTerminalLines = 500;
    private const int LiveWindow = 600;
    private static readonly Brush TempBrush = CreateBrush(0x4F, 0xC1, 0x7A);

    private readonly List<(DateTimeOffset time, double value)> _live = new();
    private F100Client? _client;
    private CancellationTokenSource? _pollingCts;
    private ThermometerCsvRecorder? _recorder;

    public ThermometerDeviceViewModel(SerialDeviceInfo info)
    {
        Info = info;
        _recordingPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            $"f100_{info.PortName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected, ReportError);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected, ReportError);
        IdentifyCommand = new AsyncRelayCommand(IdentifyAsync, () => IsConnected, ReportError);
        ReadOnceCommand = new AsyncRelayCommand(ReadOnceAsync, () => IsConnected, ReportError);
        SendTerminalCommand = new AsyncRelayCommand(SendTerminalAsync, () => IsConnected && !string.IsNullOrWhiteSpace(TerminalInput), ReportError);
        ClearTerminalCommand = new RelayCommand(() => TerminalLines.Clear());
        StartRecordingCommand = new RelayCommand(StartRecording, () => !IsRecording);
        StopRecordingCommand = new RelayCommand(StopRecording, () => IsRecording);
        BrowseRecordingPathCommand = new RelayCommand(BrowseRecordingPath);
    }

    public SerialDeviceInfo Info { get; }
    public string PortName => Info.PortName;
    public string Display => Info.Display;
    public string? SerialNumber => Info.SerialNumber;

    public IReadOnlyList<int> BaudRates { get; } = F100Protocol.BaudRates;

    private int _baudRate = F100Protocol.DefaultBaudRate;
    public int BaudRate { get => _baudRate; set => SetProperty(ref _baudRate, value); }

    private string _readCommand = F100Protocol.DefaultReadCommand;
    public string ReadCommand { get => _readCommand; set => SetProperty(ref _readCommand, value); }

    private double _pollIntervalSeconds = 2;
    public double PollIntervalSeconds { get => _pollIntervalSeconds; set => SetProperty(ref _pollIntervalSeconds, Math.Max(0.5, value)); }

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

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(ConnectionState));
                ConnectCommand.RaiseCanExecuteChanged();
                DisconnectCommand.RaiseCanExecuteChanged();
                IdentifyCommand.RaiseCanExecuteChanged();
                ReadOnceCommand.RaiseCanExecuteChanged();
                SendTerminalCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ConnectionState => IsConnected ? $"Pripojené · {PortName} @ {BaudRate} bd" : "Odpojené";

    private double? _temperature;
    public double? Temperature { get => _temperature; private set => SetProperty(ref _temperature, value); }

    private string _unit = "°C";
    public string Unit { get => _unit; private set => SetProperty(ref _unit, value); }

    private string _identity = "—";
    public string Identity { get => _identity; private set => SetProperty(ref _identity, value); }

    private DateTimeOffset? _lastUpdate;
    public DateTimeOffset? LastUpdate { get => _lastUpdate; private set => SetProperty(ref _lastUpdate, value); }

    private string _statusMessage = "Pripravené.";
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    private IReadOnlyList<ChartSeries> _liveSeries = Array.Empty<ChartSeries>();
    public IReadOnlyList<ChartSeries> LiveSeries { get => _liveSeries; private set => SetProperty(ref _liveSeries, value); }

    public ObservableCollection<string> TerminalLines { get; } = new();

    private string _terminalInput = "*IDN?";
    public string TerminalInput
    {
        get => _terminalInput;
        set { if (SetProperty(ref _terminalInput, value)) SendTerminalCommand.RaiseCanExecuteChanged(); }
    }

    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand IdentifyCommand { get; }
    public AsyncRelayCommand ReadOnceCommand { get; }
    public AsyncRelayCommand SendTerminalCommand { get; }
    public RelayCommand ClearTerminalCommand { get; }

    private async Task ConnectAsync()
    {
        _client = new F100Client(PortName, BaudRate);
        StatusMessage = $"Otváram {PortName}…";
        await _client.OpenAsync();
        IsConnected = true;
        StatusMessage = "Pripojené.";

        try
        {
            await IdentifyAsync();
        }
        catch
        {
            // Identification is best-effort; some firmware may not answer *IDN?.
        }

        if (PollingEnabled)
        {
            StartPolling();
        }
    }

    private async Task DisconnectAsync()
    {
        StopPolling();
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }

        IsConnected = false;
        StatusMessage = "Odpojené.";
    }

    private async Task IdentifyAsync()
    {
        if (_client is null)
        {
            return;
        }

        string response = await _client.SendReceiveAsync(F100Protocol.IdentifyCommand);
        LogTerminal(F100Protocol.IdentifyCommand, response);
        if (!string.IsNullOrWhiteSpace(response))
        {
            Identity = response.Trim();
        }
    }

    private async Task ReadOnceAsync()
    {
        if (_client is null)
        {
            return;
        }

        ThermometerReading reading = await _client.ReadAsync(ReadCommand);
        LogTerminal(ReadCommand, reading.Raw);
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
        while (!token.IsCancellationRequested && _client is not null)
        {
            try
            {
                ThermometerReading reading = await _client.ReadAsync(ReadCommand, token);
                ApplyReading(reading);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba čítania: {ex.Message}";
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

    private void ApplyReading(ThermometerReading reading)
    {
        if (reading.Temperature is { } t)
        {
            Temperature = t;
            if (!string.IsNullOrEmpty(reading.Unit))
            {
                Unit = reading.Unit;
            }

            _live.Add((reading.Timestamp, t));
            if (_live.Count > LiveWindow)
            {
                _live.RemoveRange(0, _live.Count - LiveWindow);
            }

            BuildLiveChart();
        }

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

    private void BuildLiveChart()
    {
        if (_live.Count == 0)
        {
            LiveSeries = Array.Empty<ChartSeries>();
            return;
        }

        DateTimeOffset t0 = _live[0].time;
        var points = _live
            .Select(s => new Point((s.time - t0).TotalMinutes, s.value))
            .ToList();
        LiveSeries = new[] { new ChartSeries("Teplota", TempBrush, points) };
    }

    private async Task SendTerminalAsync()
    {
        if (_client is null)
        {
            return;
        }

        string command = TerminalInput;
        string response = await _client.SendReceiveAsync(command);
        LogTerminal(command, response);
    }

    private void LogTerminal(string tx, string rx)
    {
        string ts = DateTime.Now.ToString("HH:mm:ss.fff");
        AppendTerminal($"{ts}  TX  {tx}");
        AppendTerminal($"{ts}  RX  {rx.Replace("\r", "<CR>").Replace("\n", "<LF>")}");
    }

    private void AppendTerminal(string line)
    {
        TerminalLines.Add(line);
        while (TerminalLines.Count > MaxTerminalLines)
        {
            TerminalLines.RemoveAt(0);
        }
    }

    private string _recordingPath;
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
            _recorder = new ThermometerCsvRecorder(RecordingPath);
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
            Title = "Súbor záznamu teplomera",
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

    private void ReportError(Exception ex) => StatusMessage = $"Chyba: {ex.Message}";

    private static Brush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public async ValueTask DisposeAsync()
    {
        StopPolling();
        StopRecording();
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }
}
