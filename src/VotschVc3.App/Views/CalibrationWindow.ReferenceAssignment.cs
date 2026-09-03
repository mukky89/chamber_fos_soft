using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

/// <summary>
/// Keeps the operator's CTH7000 selection exclusive and persistent per chamber.
///
/// CalibrationViewModel intentionally continues to own the physical COM lifecycle; this layer
/// owns the higher-level business rule "one reference thermometer belongs to one FBG workspace".
/// That rule applies already at selection time, before anybody presses Read or starts a run.
/// </summary>
public partial class CalibrationWindow
{
    private bool _referenceAssignmentAttached;
    private bool _referenceSelectionInternal;
    private bool _referenceDeviceListChanging;
    private ThermometerDeviceViewModel? _acceptedReference;
    private ThermometerDeviceViewModel? _observedReference;

    private void AttachReferenceAssignmentBehavior()
    {
        if (_referenceAssignmentAttached) return;
        _referenceAssignmentAttached = true;

        _viewModel.PropertyChanged += OnReferenceCalibrationPropertyChanged;
        _viewModel.F100Devices.CollectionChanged += OnReferenceDeviceCollectionChanged;
        Closed += OnReferenceAssignmentWindowClosed;

        // The CalibrationViewModel historically auto-selected the first COM port. That is unsafe
        // once assignment is persistent: opening another FBG window must not silently steal the
        // first CTH7000. Restore this chamber's saved assignment, or show no selected reference.
        ReconcileReferenceSelection();
    }

    private void OnReferenceCalibrationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_referenceSelectionInternal) return;

        if (e.PropertyName == nameof(CalibrationViewModel.SelectedF100))
        {
            if (_referenceDeviceListChanging)
            {
                ScheduleReferenceReconcile();
                return;
            }

            HandleOperatorReferenceSelection(_viewModel.SelectedF100);
            return;
        }

        if (e.PropertyName == nameof(CalibrationViewModel.SelectedF100Channel) && _viewModel.SelectedF100 is { } selected)
        {
            CalibrationReferenceStatusStore.Instance.TryAssign(
                _chamberId,
                selected.Info.Description is null ? selected.PortName : GetWorkspaceName(),
                selected.PortName,
                selected.SerialNumber,
                _viewModel.SelectedF100Channel,
                out _);
            PublishReferenceState(selected);
        }
    }

    private void HandleOperatorReferenceSelection(ThermometerDeviceViewModel? candidate)
    {
        if (candidate is null)
        {
            // Null can mean USB unplug/rescan. Never release a persistent assignment implicitly.
            ObserveReference(null);
            CalibrationReferenceStatusStore.Instance.MarkDisconnected(_chamberId);
            return;
        }

        CalibrationReferenceSnapshot previous = CalibrationReferenceStatusStore.Instance.GetSnapshot(_chamberId);
        string workspaceName = GetWorkspaceName();
        if (!CalibrationReferenceStatusStore.Instance.TryAssign(
                _chamberId,
                workspaceName,
                candidate.PortName,
                candidate.SerialNumber,
                _viewModel.SelectedF100Channel,
                out string occupiedBy))
        {
            ThermometerDeviceViewModel? restore = FindAssignedReference(previous);
            SetReferenceSelectionInternal(restore);
            ObserveReference(restore);
            if (restore is null)
            {
                CalibrationReferenceStatusStore.Instance.MarkDisconnected(_chamberId);
            }

            MessageBox.Show(
                this,
                $"WIKA CTH7000 na porte {candidate.PortName} je už priradený FBG kalibrácii zariadenia „{occupiedBy}“.\n\n" +
                "Jeden referenčný teplomer môže byť priradený iba jednému zariadeniu.",
                "Referencia je už priradená",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // If this chamber deliberately switched from one reference to another, immediately free
        // the old process-level COM reservation too. The VM will acquire the new one on first read.
        if (_acceptedReference is { } old &&
            !string.Equals(old.PortName, candidate.PortName, StringComparison.OrdinalIgnoreCase))
        {
            CalibrationResourceRegistry.Release(CalibrationResourceRegistry.F100Key(old.PortName), _chamberId);
        }

        _acceptedReference = candidate;
        ObserveReference(candidate);
        PublishReferenceState(candidate);
    }

    private void OnReferenceDeviceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _referenceDeviceListChanging = true;
        ScheduleReferenceReconcile();
    }

    private void ScheduleReferenceReconcile()
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (_disposing) return;
            _referenceDeviceListChanging = false;
            ReconcileReferenceSelection();
        }));
    }

    private void ReconcileReferenceSelection()
    {
        CalibrationReferenceSnapshot assignment = CalibrationReferenceStatusStore.Instance.GetSnapshot(_chamberId);
        if (!assignment.IsAssigned)
        {
            // No operator assignment exists yet. Clear the VM's historical "first COM wins"
            // default so simply opening this window never claims a thermometer.
            SetReferenceSelectionInternal(null);
            _acceptedReference = null;
            ObserveReference(null);
            return;
        }

        ThermometerDeviceViewModel? match = FindAssignedReference(assignment);
        SetReferenceSelectionInternal(match);
        _acceptedReference = match;
        ObserveReference(match);

        if (match is null)
        {
            CalibrationReferenceStatusStore.Instance.MarkDisconnected(_chamberId);
        }
        else
        {
            PublishReferenceState(match);
        }
    }

    private ThermometerDeviceViewModel? FindAssignedReference(CalibrationReferenceSnapshot assignment)
    {
        if (!assignment.IsAssigned) return null;

        if (!string.IsNullOrWhiteSpace(assignment.UsbSerialNumber))
        {
            ThermometerDeviceViewModel? bySerial = _viewModel.F100Devices.FirstOrDefault(device =>
                !string.IsNullOrWhiteSpace(device.SerialNumber) &&
                string.Equals(device.SerialNumber, assignment.UsbSerialNumber, StringComparison.OrdinalIgnoreCase));
            if (bySerial is not null) return bySerial;
        }

        return _viewModel.F100Devices.FirstOrDefault(device =>
            string.Equals(device.PortName, assignment.PortName, StringComparison.OrdinalIgnoreCase));
    }

    private void SetReferenceSelectionInternal(ThermometerDeviceViewModel? device)
    {
        if (ReferenceEquals(_viewModel.SelectedF100, device)) return;
        _referenceSelectionInternal = true;
        try
        {
            _viewModel.SelectedF100 = device;
        }
        finally
        {
            _referenceSelectionInternal = false;
        }
    }

    private void ObserveReference(ThermometerDeviceViewModel? device)
    {
        if (ReferenceEquals(_observedReference, device)) return;
        if (_observedReference is not null)
        {
            _observedReference.PropertyChanged -= OnAssignedReferencePropertyChanged;
        }

        _observedReference = device;
        if (_observedReference is not null)
        {
            _observedReference.PropertyChanged += OnAssignedReferencePropertyChanged;
        }
    }

    private void OnAssignedReferencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ThermometerDeviceViewModel device) return;
        if (e.PropertyName is nameof(ThermometerDeviceViewModel.Temperature)
            or nameof(ThermometerDeviceViewModel.IsConnected)
            or nameof(ThermometerDeviceViewModel.SelectedChannel))
        {
            PublishReferenceState(device);
        }
    }

    private void PublishReferenceState(ThermometerDeviceViewModel device)
    {
        CalibrationReferenceStatusStore.Instance.PublishReading(
            _chamberId,
            device.PortName,
            device.SerialNumber,
            _viewModel.SelectedF100Channel,
            device.Temperature,
            device.IsConnected);
    }

    private string GetWorkspaceName() =>
        _viewModel.SelectedChamber?.Config.Name ?? "Neznáme zariadenie";

    private void OnReferenceAssignmentWindowClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnReferenceCalibrationPropertyChanged;
        _viewModel.F100Devices.CollectionChanged -= OnReferenceDeviceCollectionChanged;
        ObserveReference(null);
        Closed -= OnReferenceAssignmentWindowClosed;
        // Intentionally DO NOT release CalibrationReferenceStatusStore assignment here.
        // It is a persistent equipment assignment, not a window lifetime lock.
    }
}
