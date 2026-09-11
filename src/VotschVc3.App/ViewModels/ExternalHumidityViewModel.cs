using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Windows;
using System.Windows.Media;
using VotschVc3.App.Charting;
using VotschVc3.App.Mvvm;
using VotschVc3.App.Thermometers;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Charting;
using VotschVc3.Core.Recording;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.App.ViewModels;

/// <summary>One supplementary acquisition owner per chamber. Never receives an IChamberDevice.</summary>
public sealed class ExternalHumidityViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ExternalHumiditySettingsStore _store;
    private readonly Func<string, ITesto645Transport> _transportFactory;
    private readonly Func<IPeakLoggerClient> _apiFactory;
    private readonly SemaphoreSlim _logGate = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private ExternalHumiditySettings _settings = new();
    private CancellationTokenSource? _measurementStop;
    private Task? _pollTask;
    private ExternalHumidityLog? _log;
    private bool _initialized, _busy, _running, _logging, _disposed, _stopping;
    private string _status = "Vypnuté", _logStatus = "Záznam nie je spustený";
    private string? _loadedEndpoint;
    private bool _append;
    private DateTimeOffset? _lastValid;
    private readonly List<Point> _humidityPoints = [];
    private readonly List<Point> _temperaturePoints = [];
    private DateTimeOffset _chartStart;
    private double? _humidity, _temperature;
    private DateTimeOffset? _currentReceived;
    private readonly System.Windows.Threading.DispatcherTimer _freshness = new() { Interval = TimeSpan.FromSeconds(1) };
    private IReadOnlyList<ChartSeries> _humiditySeries = [], _temperatureSeries = [];

    public ExternalHumidityViewModel(Guid chamberId, string? settingsDirectory = null,
        Func<string, ITesto645Transport>? transportFactory = null, Func<IPeakLoggerClient>? apiFactory = null)
    {
        ChamberId = chamberId;
        _store = new ExternalHumiditySettingsStore(settingsDirectory ?? AppPaths.SettingsDir);
        _transportFactory = transportFactory ?? (port => new Testo645SerialTransport(port));
        _apiFactory = apiFactory ?? (() => new PeakLoggerApiClient { RequireStableIdentity = true });
        OpenCommand = new RelayCommand(() => Views.ExternalHumidityWindow.Open(this));
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanConfigure, Report);
        RefreshPortsCommand = new AsyncRelayCommand(RefreshPortsAsync, () => CanConfigure, Report);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => CanConfigure && Enabled, Report);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsRunning, Report);
        DiscoverApisCommand = new AsyncRelayCommand(DiscoverApisAsync, () => CanConfigure && Enabled, Report);
        LoadPeaksCommand = new AsyncRelayCommand(LoadPeaksAsync, () => CanConfigure && Enabled, Report);
        StartLogCommand = new AsyncRelayCommand(StartLogAsync, () => IsRunning && !IsLogging && !_busy && !_stopping && !_disposed, ReportLog);
        StopLogCommand = new AsyncRelayCommand(StopLogAsync, () => IsLogging, ReportLog);
        _freshness.Tick += FreshnessTick;
    }

    public Guid ChamberId { get; }
    public string ChamberName { get; set; } = "Komora";
    public RelayCommand OpenCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand RefreshPortsCommand { get; }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand DiscoverApisCommand { get; }
    public AsyncRelayCommand LoadPeaksCommand { get; }
    public AsyncRelayCommand StartLogCommand { get; }
    public AsyncRelayCommand StopLogCommand { get; }
    public ObservableCollection<string> Ports { get; } = [];
    public ObservableCollection<PeakLoggerApiClient.DiscoveredInstance> Apis { get; } = [];
    public ObservableCollection<ExternalPeakChoice> Peaks { get; } = [];
    public bool CanConfigure => _initialized && !_busy && !IsRunning && !_disposed;
    public bool CanChooseFile => !IsLogging && !_busy && !_disposed;
    public bool IsRunning => _running;
    public bool IsLogging => _logging;
    public bool Enabled { get => _settings.Enabled; set { if (!CanConfigure) return; _settings.Enabled = value; Changed(); } }
    public string Port { get => _settings.Port; set { if (CanConfigure) { _settings.Port = value; OnPropertyChanged(); } } }
    public double IntervalSeconds { get => _settings.IntervalSeconds; set { if (CanConfigure) { _settings.IntervalSeconds = value; OnPropertyChanged(); } } }
    public double MaxAgeSeconds { get => _settings.MaxAgeSeconds; set { if (CanConfigure) { _settings.MaxAgeSeconds = value; OnPropertyChanged(); } } }
    public bool UsePeaks { get => _settings.UsePeaks; set { if (CanConfigure) { _settings.UsePeaks = value; OnPropertyChanged(); } } }
    public string ApiHost { get => _settings.ApiHost; set { if (CanConfigure) { _settings.ApiHost = value; _loadedEndpoint = null; OnPropertyChanged(); } } }
    public int ApiPort { get => _settings.ApiPort; set { if (CanConfigure) { _settings.ApiPort = value; _loadedEndpoint = null; OnPropertyChanged(); } } }
    private PeakLoggerApiClient.DiscoveredInstance? _selectedApi;
    public PeakLoggerApiClient.DiscoveredInstance? SelectedApi
    {
        get => _selectedApi;
        set { if (!CanConfigure || !SetProperty(ref _selectedApi, value) || value is null) return; ApiHost = value.Host; ApiPort = value.Port; }
    }
    public string OutputPath => _settings.OutputPath;
    public string FileModeText => _append ? "Pripisovanie do existujúceho TXT – nová relácia" : "Nový TXT (existujúci súbor sa neprepíše)";
    public string Status { get => _status; private set { SetProperty(ref _status, value); OnPropertyChanged(nameof(Summary)); } }
    public string LogStatus { get => _logStatus; private set => SetProperty(ref _logStatus, value); }
    public string HumidityText => _humidity.HasValue ? $"{_humidity:F1} %RH" : "— %RH";
    public string TemperatureText => _temperature.HasValue ? $"{_temperature:F1} °C" : "— °C";
    public string LastValidText => _lastValid?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss.fff") ?? "—";
    public string Summary => Enabled ? $"Testo 645 · {HumidityText} · {TemperatureText} · {(IsRunning ? "meranie" : "odpojené")}" : "Externá vlhkosť – Testo 645";
    public IReadOnlyList<ChartSeries> HumiditySeries => _humiditySeries;
    public IReadOnlyList<ChartSeries> TemperatureSeries => _temperatureSeries;
    public string ChartCaption => _chartStart == default ? "Zatiaľ bez merania" : $"Od {_chartStart.ToLocalTime():dd.MM.yyyy HH:mm:ss} · čas v minútach · časy prijatia PC";

    public async Task InitializeAsync()
    {
        if (_initialized || _busy || _disposed) return;
        _busy = true; Changed();
        try
        {
            _settings = await Task.Run(() => _store.Load(ChamberId));
            foreach (var key in _settings.Peaks ?? []) Peaks.Add(new ExternalPeakChoice(key) { Selected = true });
            _append = File.Exists(OutputPath);
            Status = Enabled ? "Odpojené – pripojte ručne" : "Vypnuté";
        }
        catch (Exception ex) { Report(ex); }
        finally { _initialized = true; _busy = false; OnPropertyChanged(""); Changed(); }
        try { await RefreshPortsAsync(); } catch (Exception ex) { Report(ex); }
    }

    private ExternalHumiditySettings Snapshot()
    {
        _settings.Peaks = Peaks.Where(x => x.Selected).Select(x => x.Key).ToList();
        _settings.Validate();
        return System.Text.Json.JsonSerializer.Deserialize<ExternalHumiditySettings>(System.Text.Json.JsonSerializer.Serialize(_settings))!;
    }
    private async Task SaveAsync()
    {
        var snapshot = Snapshot();
        _busy = true; Changed();
        try { await Task.Run(() => _store.Save(ChamberId, snapshot)); Status = "Nastavenia uložené"; }
        finally { _busy = false; Changed(); }
    }
    private async Task RefreshPortsAsync()
    {
        var ports = await Task.Run(SerialPort.GetPortNames);
        Ports.Clear(); foreach (var port in ports.OrderBy(x => x)) Ports.Add(port);
    }
    private string Endpoint => $"{ApiHost.Trim()}:{ApiPort}";
    private PeakLoggerSettings ApiSettings() => new() { Host = ApiHost.Trim(), Port = ApiPort, UseSimulator = false, RequestTimeout = TimeSpan.FromSeconds(2) };
    private async Task DiscoverApisAsync()
    {
        _busy = true; Changed();
        try
        {
            Status = "Hľadám dostupné PeakLogger API…";
            var report = await PeakLoggerApiClient.DiscoverInstancesAsync(ApiHost, ApiPort > 0 ? ApiPort : PeakLoggerApiClient.DefaultPort, cancellationToken: _lifetime.Token);
            Apis.Clear(); foreach (var api in report.Instances) Apis.Add(api);
            Status = $"Nájdené API: {Apis.Count}. Vyberte konkrétne API a načítajte peaky.";
        }
        finally { _busy = false; Changed(); }
    }
    private async Task LoadPeaksAsync()
    {
        if (ApiPort < 1 || ApiPort > 65535) throw new ArgumentException("Zadajte konkrétny port API (1–65535).");
        _busy = true; Changed();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await using var client = _apiFactory();
            await client.ConnectAsync(ApiSettings(), timeout.Token);
            var sensors = await client.DiscoverSensorsAsync(timeout.Token);
            var chosen = Peaks.Where(x => x.Selected).Select(x => x.Key).ToHashSet();
            Peaks.Clear();
            foreach (var sensor in sensors)
                foreach (var peak in sensor.Peaks)
                {
                    var key = new ExternalPeakKey(sensor.SerialNumber, sensor.Channel, peak.PeakIndex);
                    Peaks.Add(new ExternalPeakChoice(key) { Selected = chosen.Contains(key) });
                }
            _loadedEndpoint = Endpoint;
            Status = $"{Endpoint} · {Peaks.Count} peakov; označte peaky na záznam.";
        }
        finally { _busy = false; Changed(); }
    }

    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(Port)) throw new ArgumentException("Vyberte COM port Testo 645.");
        var settings = Snapshot();
        if (UsePeaks && (_loadedEndpoint != Endpoint || settings.Peaks.Count == 0))
            throw new ArgumentException("Načítajte peaky zo zvoleného API a označte aspoň jeden peak, alebo vypnite PeakLogger.");
        await SaveAsync();
        _measurementStop?.Dispose();
        _measurementStop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _running = true; _stopping = false; _humidityPoints.Clear(); _temperaturePoints.Clear(); _chartStart = DateTimeOffset.Now;
        _freshness.Start();
        Status = "Pripájam Testo 645…";
        _humiditySeries = []; _temperatureSeries = []; OnPropertyChanged(nameof(ChartCaption));
        Changed();
        _pollTask = PollAsync(settings, _measurementStop.Token);
    }

    private async Task PollAsync(ExternalHumiditySettings settings, CancellationToken token)
    {
        await using var testo = new Testo645Session(_transportFactory(settings.Port));
        await using var api = _apiFactory();
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var testoTask = ReadTestoAsync(testo, token);
                var apiTask = ReadApiAsync(api, settings, token);
                await Task.WhenAll(testoTask, apiTask);
                var t = await testoTask; var p = await apiTask;
                token.ThrowIfCancellationRequested();
                var sample = new ExternalHumiditySample(DateTimeOffset.Now, t.Time, t.Value, p.Values, p.Time, t.Error + " " + p.Error);
                ApplySample(sample, settings.MaxAgeSeconds);
                await _logGate.WaitAsync(token);
                try
                {
                    if (_log is not null)
                    {
                        try { await _log.AppendAsync(sample); LogStatus = "Zapisujem · " + sample.RecordedAt.ToLocalTime().ToString("HH:mm:ss") + " · " + _log.LastDataStatus; }
                        catch (Exception ex) { ReportLog(ex); await CloseLogCoreAsync(); }
                    }
                }
                finally { _logGate.Release(); }
                if (t.Fatal) { Status = t.Error + " · Odpojte a pripojte znova."; break; }
                await Task.Delay(TimeSpan.FromSeconds(settings.IntervalSeconds), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Status = "Odpojené"; }
        catch (Exception ex) { Report(ex); }
        finally
        {
            _stopping = true; Changed();
            await StopLogAsync();
            try { await testo.DisposeAsync(); }
            catch (Exception ex) { Report(ex); }
            finally { _freshness.Stop(); _running = false; _humidity = null; _temperature = null; Changed(); }
        }
    }

    private static async Task<(Testo645Reading? Value, DateTimeOffset? Time, string Error, bool Fatal)> ReadTestoAsync(Testo645Session session, CancellationToken token)
    {
        try { var reading = await session.ReadAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false); return (reading, DateTimeOffset.Now, "", false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (TimeoutException ex) { return (null, null, "TESTO_TIMEOUT: " + ex.Message, false); }
        catch (Exception ex) { return (null, null, "TESTO_ERROR: " + ex.Message, true); }
    }
    private static async Task<(IReadOnlyList<PeakLoggerMeasurement> Values, DateTimeOffset? Time, string Error)> ReadApiAsync(IPeakLoggerClient api, ExternalHumiditySettings settings, CancellationToken token)
    {
        if (!settings.UsePeaks) return (Array.Empty<PeakLoggerMeasurement>(), null, "");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            if (!api.IsConnected) await api.ConnectAsync(new PeakLoggerSettings { Host = settings.ApiHost, Port = settings.ApiPort, UseSimulator = false, RequestTimeout = TimeSpan.FromSeconds(2) }, timeout.Token).ConfigureAwait(false);
            var values = await api.ReadMeasurementsAsync(timeout.Token).ConfigureAwait(false);
            return (values, api.LastDataTimestamp ?? DateTimeOffset.Now, "");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { return (Array.Empty<PeakLoggerMeasurement>(), null, "API_ERROR: " + ex.Message); }
    }

    private void ApplySample(ExternalHumiditySample sample, double maxAge)
    {
        double age = sample.TestoReceivedAt.HasValue ? (sample.RecordedAt - sample.TestoReceivedAt.Value).TotalSeconds : double.PositiveInfinity;
        bool fresh = age >= 0 && age <= maxAge;
        _humidity = fresh ? sample.Testo?.HumidityPercent : null;
        _temperature = fresh ? sample.Testo?.TemperatureC : null;
        _currentReceived = sample.TestoReceivedAt;
        if (fresh && sample.Testo?.IsValid == true) _lastValid = sample.TestoReceivedAt;
        Status = string.IsNullOrWhiteSpace(sample.Status)
            ? sample.Testo?.IsValid == true ? "Pripojené · protokol čiastočne overený" : sample.Testo?.Status ?? "Bez údajov"
            : sample.Status;
        if (!fresh && sample.Testo is not null) Status += " · STARÉ ÚDAJE";
        double x = (sample.RecordedAt - _chartStart).TotalMinutes;
        _humidityPoints.Add(new Point(x, _humidity ?? double.NaN));
        _temperaturePoints.Add(new Point(x, _temperature ?? double.NaN));
        _humiditySeries = MakeSeries(_humidityPoints, "Testo – externá vlhkosť", "HumidityBrush");
        _temperatureSeries = MakeSeries(_temperaturePoints, "Testo – externá teplota", "TemperatureBrush");
        Changed();
    }
    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
    private static IReadOnlyList<ChartSeries> MakeSeries(List<Point> points, string name, string brush)
    {
        var result = new List<ChartSeries>();
        var segment = new List<Point>();
        void Flush()
        {
            if (segment.Count == 0) return;
            result.Add(new ChartSeries(name, Brush(brush), TimeSeriesEnvelopeReducer.Reduce(segment, p => p.Y, 2000)));
            segment.Clear();
        }
        foreach (var point in points) { if (double.IsFinite(point.Y)) segment.Add(point); else Flush(); }
        Flush(); return result;
    }
    private void FreshnessTick(object? sender, EventArgs e)
    {
        if (_currentReceived.HasValue && (DateTimeOffset.Now - _currentReceived.Value).TotalSeconds > MaxAgeSeconds && (_humidity.HasValue || _temperature.HasValue))
        {
            _humidity = _temperature = null; Status = "STARÉ ÚDAJE – čakám na ďalšiu vzorku"; Changed();
        }
    }
    public ExternalHumidityObservation Observation()
    {
        double age = _currentReceived.HasValue ? (DateTimeOffset.Now - _currentReceived.Value).TotalSeconds : double.PositiveInfinity;
        bool fresh = IsRunning && age >= 0 && age <= MaxAgeSeconds;
        return new(_currentReceived, fresh ? _humidity : null, fresh ? _temperature : null, fresh ? Status : "NA / STALE / OFF");
    }

    public void SelectFile(string path, bool append)
    {
        if (!CanChooseFile) return;
        _settings.OutputPath = Path.GetFullPath(path); _append = append;
        OnPropertyChanged(nameof(OutputPath)); OnPropertyChanged(nameof(FileModeText));
    }
    private async Task StartLogAsync()
    {
        if (string.IsNullOrWhiteSpace(OutputPath)) throw new ArgumentException("Vyberte cieľový TXT súbor.");
        _busy = true; Changed();
        try
        {
            var settings = Snapshot();
            await Task.Run(() => _store.Save(ChamberId, settings));
            await _logGate.WaitAsync();
            try
            {
                if (!IsRunning || _stopping || _disposed) throw new InvalidOperationException("Meranie už nie je spustené alebo sa odpája.");
                _log = await Task.Run(() => ExternalHumidityLog.OpenAsync(OutputPath, _append, ChamberId, settings.Port,
                    settings.UsePeaks ? settings.ApiHost + ":" + settings.ApiPort : "OFF",
                    settings.UsePeaks ? settings.Peaks : [], TimeSpan.FromSeconds(settings.MaxAgeSeconds)));
                _logging = true; _append = true; LogStatus = "Záznam spustený";
            }
            finally { _logGate.Release(); }
        }
        finally { _busy = false; Changed(); }
    }
    private async Task CloseLogCoreAsync()
    {
        var log = _log; _log = null; _logging = false;
        try { if (log is not null) await log.DisposeAsync(); }
        catch (Exception ex) { ReportLog(ex); }
        Changed();
    }
    private async Task StopLogAsync()
    {
        await _logGate.WaitAsync();
        try { if (_log is not null) { await CloseLogCoreAsync(); if (!LogStatus.StartsWith("CHYBA")) LogStatus = "Záznam ukončený"; } }
        finally { _logGate.Release(); }
    }
    public async Task DisconnectAsync()
    {
        _stopping = true; Changed();
        _measurementStop?.Cancel();
        if (_pollTask is not null) await _pollTask;
        _measurementStop?.Dispose(); _measurementStop = null; _pollTask = null;
    }
    private void Report(Exception ex) => Status = "CHYBA: " + ex.Message;
    private void ReportLog(Exception ex) => LogStatus = "CHYBA ZÁPISU: " + ex.Message;
    private void Changed()
    {
        foreach (string name in new[] { nameof(Enabled), nameof(CanConfigure), nameof(CanChooseFile), nameof(IsRunning), nameof(IsLogging), nameof(HumidityText), nameof(TemperatureText), nameof(LastValidText), nameof(Summary), nameof(HumiditySeries), nameof(TemperatureSeries), nameof(FileModeText) }) OnPropertyChanged(name);
        foreach (var command in new[] { SaveCommand, RefreshPortsCommand, ConnectCommand, DisconnectCommand, DiscoverApisCommand, LoadPeaksCommand, StartLogCommand, StopLogCommand }) command.RaiseCanExecuteChanged();
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel();
        _freshness.Stop(); _freshness.Tick -= FreshnessTick;
        await DisconnectAsync(); await StopLogAsync(); Changed();
    }
}

public sealed class ExternalPeakChoice(ExternalPeakKey key) : ObservableObject
{
    public ExternalPeakKey Key { get; } = key;
    private bool _selected;
    public bool Selected { get => _selected; set => SetProperty(ref _selected, value); }
    public string Display => Key.ToString();
}

public static class ExternalHumidityRegistry
{
    private static readonly Dictionary<Guid, ExternalHumidityViewModel> Items = [];
    public static ExternalHumidityViewModel Get(Guid chamberId, string? name = null)
    {
        if (!Items.TryGetValue(chamberId, out var vm))
        {
            Items[chamberId] = vm = new ExternalHumidityViewModel(chamberId);
            _ = vm.InitializeAsync();
        }
        if (name is not null) vm.ChamberName = name;
        return vm;
    }
    public static async Task ReleaseAsync(Guid chamberId)
    {
        if (Items.Remove(chamberId, out var vm)) await vm.DisposeAsync();
    }
}
