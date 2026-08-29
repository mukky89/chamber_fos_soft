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

    public SylexFosCalibrationIntegration(CalibrationViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        string baseUrl = Environment.GetEnvironmentVariable("SYLEX_FOS_API_URL") ?? SylexFosApiSettings.DefaultBaseUrl;
        var settings = new SylexFosApiSettings { BaseUrl = baseUrl };
        _apiClient = new SylexFosApiClient(settings);
        _metadataProvider = new SylexFosApiProductionMetadataProvider(_apiClient);

        _viewModel.Peaks.CollectionChanged += OnPeaksChanged;
        foreach (CalibrationPeakRowViewModel row in _viewModel.Peaks) AttachRow(row);
        _ = CheckApiAsync();
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
        if (string.IsNullOrWhiteSpace(row.SerialNumber)) return;
        var cts = new CancellationTokenSource();
        _lookups[row] = cts;
        _ = LookupAndApplyAsync(row, row.SerialNumber, cts.Token);
    }

    private async Task LookupAndApplyAsync(CalibrationPeakRowViewModel row, string serialNumber, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            ProductionMetadata? metadata = await _metadataProvider.FindAsync(serialNumber, row.Channel, cancellationToken).ConfigureAwait(false);
            if (metadata is null || cancellationToken.IsCancellationRequested) return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!string.Equals(row.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase)) return;
                if (!string.IsNullOrWhiteSpace(metadata.ProductDescription)) row.ProductDescription = metadata.ProductDescription;
                if (!string.IsNullOrWhiteSpace(metadata.SensorName)) row.Customer = metadata.SensorName;
                if (!string.IsNullOrWhiteSpace(metadata.Order)) row.Order = metadata.Order;
            });

            AppLog.Info("Sylex FOS API", $"FBG SN {serialNumber}: doplnená zakázka, popis výrobku a názov snímača.");
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException ex)
        {
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
            AppLog.Info("Sylex FOS API", $"Centrálne API je dostupné na {ApiClientBaseUrl()}.");
        else
            AppLog.Warn("Sylex FOS API", $"Centrálne API nie je dostupné ({health.Status}). Kalibrácia môže pokračovať bez automatického doplnenia metadata.");
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
