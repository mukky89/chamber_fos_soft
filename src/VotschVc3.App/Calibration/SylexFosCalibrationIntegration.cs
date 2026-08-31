using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.Calibration;

public enum SylexFosLookupState
{
    Idle,
    CheckingApi,
    ApiAvailable,
    Loading,
    Loaded,
    NotFound,
    ConfigurationError,
    ApiUnavailable,
}

public sealed record SylexFosLookupStatus(
    SylexFosLookupState State,
    string Message,
    string? SerialNumber = null,
    string? OrderNumber = null);

/// <summary>
/// UI integration adapter that enriches FBG calibration rows from the central Sylex FOS API.
/// The calibration remains usable when the API is unavailable; production fields stay editable.
/// </summary>
public sealed class SylexFosCalibrationIntegration : IAsyncDisposable
{
    private readonly CalibrationViewModel _viewModel;
    private readonly SylexFosApiClient _apiClient;
    private readonly SylexFosApiProductionMetadataProvider _metadataProvider;
    private readonly Dictionary<CalibrationPeakRowViewModel, CancellationTokenSource> _lookups = new();
    private bool _disposed;
    private bool _configurationWarningLogged;

    public event EventHandler<SylexFosLookupStatus>? LookupStatusChanged;

    public SylexFosCalibrationIntegration(CalibrationViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        string baseUrl = Environment.GetEnvironmentVariable("SYLEX_FOS_API_URL") ?? SylexFosApiSettings.DefaultBaseUrl;
        var settings = new SylexFosApiSettings { BaseUrl = baseUrl };
        _apiClient = new SylexFosApiClient(settings);
        _metadataProvider = new SylexFosApiProductionMetadataProvider(_apiClient);

        _viewModel.Peaks.CollectionChanged += OnPeaksChanged;
        foreach (CalibrationPeakRowViewModel row in _viewModel.Peaks) AttachRow(row);
    }

    public Task InitializeAsync() => CheckApiAsync();

    private void PublishStatus(SylexFosLookupStatus status)
    {
        if (_disposed) return;
        LookupStatusChanged?.Invoke(this, status);
    }

    private void OnPeaksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (CalibrationPeakRowViewModel row in e.OldItems) DetachRow(row);

        if (e.NewItems is not null)
        {
            foreach (CalibrationPeakRowViewModel row in e.NewItems)
            {
                AttachRow(row);
                if (!string.IsNullOrWhiteSpace(row.SerialNumber)) ScheduleLookup(row);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (CalibrationPeakRowViewModel row in _lookups.Keys.ToArray()) DetachRow(row);
            foreach (CalibrationPeakRowViewModel row in _viewModel.Peaks) AttachRow(row);
        }
    }

    private void AttachRow(CalibrationPeakRowViewModel row)
    {
        row.PropertyChanged -= OnRowPropertyChanged;
        row.PropertyChanged += OnRowPropertyChanged;
    }

    private void DetachRow(CalibrationPeakRowViewModel row)
    {
        row.PropertyChanged -= OnRowPropertyChanged;
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
        if (_disposed) return;
        if (_lookups.Remove(row, out CancellationTokenSource? previous))
        {
            previous.Cancel();
            previous.Dispose();
        }

        string serialNumber = row.SerialNumber.Trim();
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            PublishStatus(new SylexFosLookupStatus(SylexFosLookupState.Idle, "FOS API · čaká na FBG SN"));
            return;
        }

        PublishStatus(new SylexFosLookupStatus(
            SylexFosLookupState.Loading,
            $"FOS API · načítavam {serialNumber}…",
            serialNumber));

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
                PublishStatus(new SylexFosLookupStatus(
                    SylexFosLookupState.NotFound,
                    $"FOS API · SN {serialNumber} sa nenašlo",
                    serialNumber));
                AppLog.Warn("Sylex FOS API", $"FBG SN {serialNumber}: produkčné metadata sa nenašli.");
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!string.Equals(row.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase)) return;
                if (!string.IsNullOrWhiteSpace(metadata.ProductDescription)) row.ProductDescription = metadata.ProductDescription;
                if (!string.IsNullOrWhiteSpace(metadata.SensorName)) row.Customer = metadata.SensorName;
                if (!string.IsNullOrWhiteSpace(metadata.Order)) row.Order = metadata.Order;
            });

            string orderSuffix = string.IsNullOrWhiteSpace(metadata.Order) ? string.Empty : $" · Zakázka {metadata.Order}";
            PublishStatus(new SylexFosLookupStatus(
                SylexFosLookupState.Loaded,
                $"FOS API · načítané {serialNumber}{orderSuffix}",
                serialNumber,
                metadata.Order));

            AppLog.Info("Sylex FOS API", $"FBG SN {serialNumber}: doplnená zakázka, popis výrobku a názov snímača.");
        }
        catch (OperationCanceledException)
        {
            // Normal debounce or serial-number replacement.
        }
        catch (InvalidOperationException ex)
        {
            PublishStatus(new SylexFosLookupStatus(
                SylexFosLookupState.ConfigurationError,
                "FOS API · chýba alebo je neplatná konfigurácia",
                serialNumber));
            if (!_configurationWarningLogged)
            {
                _configurationWarningLogged = true;
                AppLog.Warn("Sylex FOS API", ex.Message);
            }
        }
        catch (Exception ex)
        {
            PublishStatus(new SylexFosLookupStatus(
                SylexFosLookupState.ApiUnavailable,
                $"FOS API · nedostupné pre {serialNumber}",
                serialNumber));
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
        PublishStatus(new SylexFosLookupStatus(SylexFosLookupState.CheckingApi, "FOS API · kontrolujem pripojenie…"));
        SylexFosApiHealth health = await _apiClient.CheckHealthAsync().ConfigureAwait(false);
        if (health.IsReachable)
        {
            PublishStatus(new SylexFosLookupStatus(SylexFosLookupState.ApiAvailable, "FOS API · dostupné"));
            AppLog.Info("Sylex FOS API", $"Centrálne API je dostupné na {ApiClientBaseUrl()}.");
        }
        else
        {
            PublishStatus(new SylexFosLookupStatus(SylexFosLookupState.ApiUnavailable, $"FOS API · nedostupné ({health.Status})"));
            AppLog.Warn("Sylex FOS API", $"Centrálne API nie je dostupné ({health.Status}). Kalibrácia môže pokračovať bez automatického doplnenia metadata.");
        }
    }

    private static string ApiClientBaseUrl() => Environment.GetEnvironmentVariable("SYLEX_FOS_API_URL") ?? SylexFosApiSettings.DefaultBaseUrl;

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _viewModel.Peaks.CollectionChanged -= OnPeaksChanged;
        foreach (CalibrationPeakRowViewModel row in _viewModel.Peaks) row.PropertyChanged -= OnRowPropertyChanged;
        foreach (CancellationTokenSource cts in _lookups.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _lookups.Clear();
        _metadataProvider.Dispose();
        return ValueTask.CompletedTask;
    }
}
