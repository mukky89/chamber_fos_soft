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

    public event EventHandler<SylexFosLookupStatus>? LookupStatusChanged;
    public event EventHandler<CalibrationPeakRowViewModel>? MetadataApplied;
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
    }

    private void OnPeaksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
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
                if (!string.IsNullOrWhiteSpace(row.SerialNumber)) ScheduleLookup(row);
            }
        }
    }

    private void AttachRow(CalibrationPeakRowViewModel? row)
    {
        if (row is null || _disposed || !_attachedRows.Add(row)) return;
        row.PropertyChanged += OnRowPropertyChanged;
        SylexFosRowMetadataStore.SetParsedSerial(row, row.SerialNumber);
    }

    private void DetachRow(CalibrationPeakRowViewModel? row)
    {
        // PeakLogger discovery can clear/rebuild the collection while async API lookups are still
        // finishing. Treat detach as null-safe/idempotent instead of allowing a UI refresh to crash.
        if (row is null) return;
        _attachedRows.Remove(row);
        row.PropertyChanged -= OnRowPropertyChanged;
        SylexFosSensorNameStore.Remove(row);
        SylexFosRowMetadataStore.Remove(row);
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

        SylexFosSensorNameStore.Remove(row);
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
            if (cancellationToken.IsCancellationRequested || !_attachedRows.Contains(row)) return;

            if (metadata is null)
            {
                AppLog.Info("Sylex FOS API", $"FBG SN {serialNumber}: záznam nebol nájdený alebo sa nezhoduje kanál/sonda.");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!_attachedRows.Contains(row)) return;
                    RowValidationFailed?.Invoke(this, new SylexFosRowValidationIssue(
                        row,
                        serialNumber,
                        $"Sylex FOS: SN {serialNumber} sa nenašlo pre kanál {row.Channel} alebo sa sonda nezhoduje."));
                    MetadataApplied?.Invoke(this, row);
                });
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!_attachedRows.Contains(row)) return;
                string currentParsed = SylexFosRowMetadataStore.ParseSerialNumber(row.SerialNumber);
                if (!string.Equals(currentParsed, serialNumber, StringComparison.OrdinalIgnoreCase)) return;
                if (!string.IsNullOrWhiteSpace(metadata.ProductDescription)) row.ProductDescription = metadata.ProductDescription;
                SylexFosSensorNameStore.Set(row, metadata.SensorName);
                SylexFosRowMetadataStore.SetApiMetadata(row, metadata.SylexSerialNumber ?? serialNumber, metadata.FbgType);
                if (!string.IsNullOrWhiteSpace(metadata.Order)) row.Order = metadata.Order;
                if (!string.IsNullOrWhiteSpace(metadata.CustomerName)) row.Customer = metadata.CustomerName;
                MetadataApplied?.Invoke(this, row);
            });

            AppLog.Info(
                "Sylex FOS API",
                $"FBG SN {serialNumber}: doplnený Sylex SN, Typ FBG, zakázka, popis výrobku, názov snímača a zákazník.");
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
            if (_lookups.TryGetValue(row, out CancellationTokenSource? current) && current.Token == cancellationToken)
            {
                _lookups.Remove(row);
                current.Dispose();
            }
        }
    }

    private async Task CheckApiAsync()
    {
        SylexFosApiHealth health = await _apiClient.CheckHealthAsync().ConfigureAwait(false);
        if (health.IsReachable)
        {
            Report(SylexFosLookupState.ApiAvailable, "FOS API · dostupné");
            AppLog.Info("Sylex FOS API", $"Centrálne API je dostupné na {ApiClientBaseUrl()}.");
        }
        else
        {
            Report(SylexFosLookupState.ApiUnavailable, $"FOS API · nedostupné ({health.Status})");
            AppLog.Warn("Sylex FOS API", $"Centrálne API nie je dostupné ({health.Status}). Kalibrácia môže pokračovať bez automatického doplnenia metadata.");
        }
    }

    private void Report(SylexFosLookupState state, string message) =>
        LookupStatusChanged?.Invoke(this, new SylexFosLookupStatus(state, message));

    private static string ApiClientBaseUrl() =>
        new SylexFosApiSettingsStore(Path.Combine(AppPaths.SettingsDir, "sylex-fos-api.json")).Load().BaseUrl;

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
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
