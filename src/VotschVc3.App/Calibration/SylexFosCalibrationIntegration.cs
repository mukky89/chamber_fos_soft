using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.Calibration;

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

    public async Task InitializeAsync()
    {
        Report(SylexFosLookupState.CheckingApi, "FOS API · kontrolujem pripojenie…");
        await CheckApiAsync().ConfigureAwait(false);
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
        SylexFosSensorNameStore.Remove(row);
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
        SylexFosSensorNameStore.Remove(row);
        if (string.IsNullOrWhiteSpace(row.SerialNumber)) return;
        var cts = new CancellationTokenSource();
        _lookups[row] = cts;
        Report(SylexFosLookupState.Loading, $"FOS API · načítavam {row.SerialNumber}…");
        _ = LookupAndApplyAsync(row, row.SerialNumber, cts.Token);
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
                Report(SylexFosLookupState.NotFound, $"FOS API · SN {serialNumber} sa nenašlo");
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!string.Equals(row.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase)) return;
                if (!string.IsNullOrWhiteSpace(metadata.ProductDescription)) row.ProductDescription = metadata.ProductDescription;
                SylexFosSensorNameStore.Set(row, metadata.SensorName);
                if (!string.IsNullOrWhiteSpace(metadata.Order)) row.Order = metadata.Order;
                if (!string.IsNullOrWhiteSpace(metadata.CustomerName)) row.Customer = metadata.CustomerName;
            });

            string suffix = string.IsNullOrWhiteSpace(metadata.Order) ? string.Empty : $" · Zakázka {metadata.Order}";
            Report(SylexFosLookupState.Loaded, $"FOS API · načítané {serialNumber}{suffix}");
            AppLog.Info("Sylex FOS API", $"FBG SN {serialNumber}: doplnená zakázka, popis výrobku, názov snímača a zákazník.");
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException ex)
        {
            Report(SylexFosLookupState.ConfigurationError, "FOS API · chýba alebo je neplatná konfigurácia");
            if (!_configurationWarningLogged)
            {
                _configurationWarningLogged = true;
                AppLog.Warn("Sylex FOS API", ex.Message);
            }
        }
        catch (Exception ex)
        {
            Report(SylexFosLookupState.ApiUnavailable, $"FOS API · nedostupné ({ex.Message})");
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

    private static string ApiClientBaseUrl() => Environment.GetEnvironmentVariable("SYLEX_FOS_API_URL") ?? SylexFosApiSettings.DefaultBaseUrl;

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _viewModel.Peaks.CollectionChanged -= OnPeaksChanged;
        foreach (CalibrationPeakRowViewModel row in _viewModel.Peaks)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
            SylexFosSensorNameStore.Remove(row);
        }
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
