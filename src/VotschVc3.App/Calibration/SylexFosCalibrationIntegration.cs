using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.Calibration;

/// <summary>
/// UI integration adapter that enriches FBG calibration rows from the central Sylex FOS API.
/// The calibration remains usable when the API is unavailable; production fields stay editable.
/// Per-symbol lookups are deliberately quiet in the UI; only overall API health is reported.
/// </summary>
public sealed class SylexFosCalibrationIntegration : IAsyncDisposable
{
    private readonly CalibrationViewModel _viewModel;
    private readonly SylexFosApiClient _apiClient;
    private readonly SylexFosApiProductionMetadataProvider _metadataProvider;
    private readonly Dictionary<CalibrationPeakRowViewModel, CancellationTokenSource> _lookups = new();
    private readonly HashSet<CalibrationPeakRowViewModel> _attachedRows = new();
    private bool _disposed;
    private bool _configurationWarningLogged;
    private int _apiAvailable;
    private readonly CancellationTokenSource _healthLifetime = new();

    public event EventHandler<SylexFosLookupStatus>? LookupStatusChanged;
    public event EventHandler<SylexFosRowValidationIssue>? RowValidationFailed;

    public SylexFosCalibrationIntegration(CalibrationViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        var settings = new SylexFosApiSettingsStore(Path.Combine(AppPaths.SettingsDir, "sylex-fos-api.json")).Load();
        _apiClient = new SylexFosApiClient(settings);
        _metadataProvider = new SylexFosApiProductionMetadataProvider(_apiClient);

        _viewModel.Peaks.CollectionChanged += OnPeaksChanged;
        foreach (CalibrationPeakRowViewModel row in _viewModel.Peaks) AttachRow(row);
    }

    public async Task InitializeAsync()
    {
        Report(SylexFosLookupState.CheckingApi, "FOS API · kontrolujem pripojenie…");
        await CheckApiAsync().ConfigureAwait(false);
        if (!_disposed && Volatile.Read(ref _apiAvailable) == 0) _ = RetryHealthAsync();
    }

    /// <summary>
    /// Loads production data before a sensor is connected. Sequential wiring uses this preview
    /// to let the operator verify the scanned SN before it is armed for the next new channel.
    /// </summary>
    public Task<ProductionMetadata?> PreviewAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SylexFosCalibrationIntegration));
        string normalized = SylexFosRowMetadataStore.ParseSerialNumber(serialNumber);
        return string.IsNullOrWhiteSpace(normalized)
            ? Task.FromResult<ProductionMetadata?>(null)
            : _metadataProvider.FindAsync(normalized, string.Empty, cancellationToken);
    }

    private void OnPeaksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Move) return;

        // Reset is special: ObservableCollection does not guarantee OldItems, therefore detach
        // from our own tracked set first. This also makes sensor discovery/clear idempotent and
        // prevents stale row subscriptions from surviving a complete PeakLogger refresh.
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (CalibrationPeakRowViewModel row in _attachedRows.ToArray()) DetachRow(row);
            foreach (CalibrationPeakRowViewModel row in _viewModel.Peaks) AttachRow(row);
            return;
        }

        if (e.OldItems is not null)
        {
            foreach (object? item in e.OldItems)
            {
                if (item is CalibrationPeakRowViewModel row) DetachRow(row);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (object? item in e.NewItems)
            {
                if (item is not CalibrationPeakRowViewModel row) continue;
                AttachRow(row);
            }
        }
    }

    private void AttachRow(CalibrationPeakRowViewModel? row)
    {
        if (row is null || _disposed || !_attachedRows.Add(row)) return;
        row.PropertyChanged += OnRowPropertyChanged;
        ScheduleLookup(row);
    }

    private void DetachRow(CalibrationPeakRowViewModel? row)
    {
        // PeakLogger discovery can clear/rebuild the collection while async API lookups are still
        // finishing. Treat detach as null-safe/idempotent instead of allowing a UI refresh to crash.
        if (row is null) return;
        _attachedRows.Remove(row);
        row.PropertyChanged -= OnRowPropertyChanged;
        // Discovery reuses these same row instances after Peaks.Clear(). Detaching only
        // ends subscriptions/lookups; keep their metadata visible during reattachment
        // and API retries. SetParsedSerial clears it when the assigned SN changes.
        if (_lookups.Remove(row, out CancellationTokenSource? cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is CalibrationPeakRowViewModel row && e.PropertyName == nameof(CalibrationPeakRowViewModel.SerialNumber))
            ScheduleLookup(row);
    }

    private void ScheduleLookup(CalibrationPeakRowViewModel row)
    {
        if (_disposed || !_attachedRows.Contains(row)) return;
        if (_lookups.Remove(row, out CancellationTokenSource? previous))
        {
            previous.Cancel();
            previous.Dispose();
        }

        SylexFosRowMetadataStore.SetParsedSerial(row, row.SerialNumber);
        string serialNumber = SylexFosRowMetadataStore.GetSerialNumber(row);
        if (string.IsNullOrWhiteSpace(serialNumber)) return;

        var cts = new CancellationTokenSource();
        _lookups[row] = cts;
        _ = LookupAndApplyAsync(row, serialNumber, cts.Token);
    }

    private async Task LookupAndApplyAsync(CalibrationPeakRowViewModel row, string serialNumber, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            ProductionMetadata? metadata = await _metadataProvider.FindAsync(serialNumber, row.Channel, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) return;

            if (metadata is null)
            {
                AppLog.Info("Sylex FOS API", $"FBG SN {serialNumber}: záznam nebol nájdený alebo sa nezhoduje kanál/sonda.");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested || !_attachedRows.Contains(row) ||
                        !string.Equals(SylexFosRowMetadataStore.ParseSerialNumber(row.SerialNumber), serialNumber, StringComparison.OrdinalIgnoreCase)) return;
                    RowValidationFailed?.Invoke(this, new SylexFosRowValidationIssue(
                        row,
                        serialNumber,
                        $"Sylex FOS: SN {serialNumber} sa nenašlo pre kanál {row.Channel} alebo sa sonda nezhoduje."));
                });
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested || !_attachedRows.Contains(row) ||
                        !string.Equals(SylexFosRowMetadataStore.ParseSerialNumber(row.SerialNumber), serialNumber, StringComparison.OrdinalIgnoreCase)) return;
                var sensorRows = _viewModel.Peaks.Where(p => !p.IsDisconnected &&
                    string.Equals(p.Channel, row.Channel, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.PeakLoggerDeviceSerialNumber, row.PeakLoggerDeviceSerialNumber, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(SylexFosRowMetadataStore.ParseSerialNumber(p.SerialNumber), serialNumber, StringComparison.OrdinalIgnoreCase)).ToArray();
                var types = FbgWavelengthComparison.ExplainTypes(sensorRows.Select(p => p.CurrentWavelengthNm).ToArray(), metadata.Fbg);
                for (int i = 0; i < sensorRows.Length; i++)
                {
                    var target = sensorRows[i];
                    SylexFosSensorNameStore.Set(target, metadata.SensorName);
                    if (!string.IsNullOrWhiteSpace(metadata.ProductDescription)) target.ProductDescription = metadata.ProductDescription;
                    if (!string.IsNullOrWhiteSpace(metadata.Order)) target.Order = metadata.Order;
                    if (!string.IsNullOrWhiteSpace(metadata.CustomerName)) target.Customer = metadata.CustomerName;
                    SylexFosRowMetadataStore.SetApiMetadata(target, metadata.SylexSerialNumber ?? serialNumber,
                        metadata.Fbg is { Count: > 0 } ? types[i].Label :
                            string.IsNullOrWhiteSpace(metadata.FbgType) ? "Typ v API chýba" : metadata.FbgType);
                    target.ApiMetadata.FbgTypeDetail = $"SN {serialNumber} · kanál {target.Channel} · {target.PeakId}\n" +
                        (metadata.Fbg is { Count: > 0 } || string.IsNullOrWhiteSpace(metadata.FbgType)
                            ? types[i].Detail : "Typ prevzatý z výrobného záznamu API.");
                }
            });

            AppLog.Info(
                "Sylex FOS API",
                $"FBG SN {serialNumber}: doplnený Sylex SN, Typ FBG, zakázka, popis výrobku, názov snímača a zákazník.");
            if (Interlocked.Exchange(ref _apiAvailable, 1) != 1)
                Report(SylexFosLookupState.ApiAvailable, "FOS API · dostupné");
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException ex)
        {
            // Do not flash a warning for every scanned symbol. The health/configuration badge is
            // managed separately and the detailed error remains available in AppLog.
            if (!_configurationWarningLogged)
            {
                _configurationWarningLogged = true;
                AppLog.Warn("Sylex FOS API", ex.Message);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Sylex FOS API", $"FBG SN {serialNumber}: metadata sa nepodarilo načítať ({ex.Message}). Polia zostávajú editovateľné.");
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (_lookups.TryGetValue(row, out CancellationTokenSource? current) && current.Token == cancellationToken)
                {
                    _lookups.Remove(row);
                    current.Dispose();
                }
            });
        }
    }

    private async Task CheckApiAsync()
    {
        SylexFosApiHealth health = await _apiClient.CheckHealthAsync(_healthLifetime.Token).ConfigureAwait(false);
        if (health.IsReachable)
        {
            if (!_disposed && Interlocked.Exchange(ref _apiAvailable, 1) != 1)
                Report(SylexFosLookupState.ApiAvailable, "FOS API · dostupné");
            AppLog.Info("Sylex FOS API", $"Centrálne API je dostupné na {ApiClientBaseUrl()}.");
        }
        else
        {
            if (_disposed || Volatile.Read(ref _apiAvailable) == 1) return;
            Report(SylexFosLookupState.CheckingApi, "FOS API · dostupnosť zatiaľ nepotvrdená, kontrolu zopakujem…");
            AppLog.Warn("Sylex FOS API", $"Kontrola dostupnosti nebola potvrdená ({health.Status}); požiadavky na údaje zostávajú povolené a kontrola sa zopakuje.");
        }
    }

    private async Task RetryHealthAsync()
    {
        try
        {
            while (!_disposed && Volatile.Read(ref _apiAvailable) == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), _healthLifetime.Token).ConfigureAwait(false);
                await CheckApiAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_healthLifetime.IsCancellationRequested) { }
    }
    private void Report(SylexFosLookupState state, string message) =>
        LookupStatusChanged?.Invoke(this, new SylexFosLookupStatus(state, message));

    private static string ApiClientBaseUrl() =>
        new SylexFosApiSettingsStore(Path.Combine(AppPaths.SettingsDir, "sylex-fos-api.json")).Load().BaseUrl;

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _healthLifetime.Cancel();
        _viewModel.Peaks.CollectionChanged -= OnPeaksChanged;
        foreach (CalibrationPeakRowViewModel row in _attachedRows.ToArray()) DetachRow(row);
        foreach (CancellationTokenSource cts in _lookups.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _lookups.Clear();
        _attachedRows.Clear();
        _metadataProvider.Dispose();
        return ValueTask.CompletedTask;
    }
}

public enum SylexFosLookupState
{
    CheckingApi,
    ApiAvailable,
    Loading,
    Loaded,
    NotFound,
    ConfigurationError,
    ApiUnavailable,
}

public sealed record SylexFosLookupStatus(SylexFosLookupState State, string Message);

public sealed record SylexFosRowValidationIssue(
    CalibrationPeakRowViewModel Row,
    string SerialNumber,
    string Message);
