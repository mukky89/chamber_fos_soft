using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.Views;

/// <summary>
/// Keeps the operator's CTH7000 selection exclusive and persistent per chamber.
///
/// CalibrationViewModel intentionally continues to own the physical COM lifecycle; this layer
/// owns the higher-level business rule "one reference thermometer belongs to one FBG workspace".
/// A persisted assignment must not behave like a permanent lock after the previous workspace is
/// no longer using the COM port, therefore stale/disconnected assignments can be taken over.
/// </summary>
public partial class CalibrationWindow
{
    private bool _referenceAssignmentAttached;
    private bool _referenceSelectionInternal;
    private bool _referenceDeviceListChanging;
    private ThermometerDeviceViewModel? _acceptedReference;
    private ThermometerDeviceViewModel? _observedReference;
    private string? _lastRejectedReferenceKey;

    private void AttachReferenceAssignmentBehavior()
    {
        if (_referenceAssignmentAttached) return;
        _referenceAssignmentAttached = true;

        _viewModel.PropertyChanged += OnReferenceCalibrationPropertyChanged;
        _viewModel.F100Devices.CollectionChanged += OnReferenceDeviceCollectionChanged;
        Closed += OnReferenceAssignmentWindowClosed;

        // The CalibrationViewModel historically auto-selected the first COM port. That is unsafe
        // once assignment is persistent: opening another FBG window must not silently steal an
        // actively used CTH7000. Restore this chamber's saved assignment, or show no selection.
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
                GetWorkspaceName(),
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

        bool assigned = CalibrationReferenceStatusStore.Instance.TryAssign(
            _chamberId,
            workspaceName,
            candidate.PortName,
            candidate.SerialNumber,
            _viewModel.SelectedF100Channel,
            out string occupiedBy);

        if (!assigned)
        {
            // A saved assignment from an old/disconnected workspace must not permanently block a
            // physical COM port. Runtime reservations are the authoritative protection against two
            // live calibrations using the same device. If there is no live reservation, remove the
            // stale persisted owner and retry the operator's explicit selection once.
            string resourceKey = CalibrationResourceRegistry.F100Key(candidate.PortName);
            bool activelyReserved = CalibrationResourceRegistry.IsReservedByOther(
                resourceKey,
                _chamberId,
                out string activeOwner);

            CalibrationChamberOption? staleOwner = _viewModel.Chambers.FirstOrDefault(chamber =>
                string.Equals(chamber.Config.Name, occupiedBy, StringComparison.OrdinalIgnoreCase));

            if (!activelyReserved && staleOwner is not null && staleOwner.Config.Id != _chamberId)
            {
                CalibrationReferenceStatusStore.Instance.ReleaseAssignment(staleOwner.Config.Id);
                assigned = CalibrationReferenceStatusStore.Instance.TryAssign(
                    _chamberId,
                    workspaceName,
                    candidate.PortName,
                    candidate.SerialNumber,
                    _viewModel.SelectedF100Channel,
                    out occupiedBy);

                if (assigned)
                {
                    AppLog.Info(
                        "FBG referencia",
                        $"{candidate.PortName}: odstránené neaktívne priradenie „{staleOwner.Config.Name}“ a referencia bola priradená „{workspaceName}“.");
                }
            }

            if (!assigned)
            {
                ThermometerDeviceViewModel? restore = FindAssignedReference(previous);
                SetReferenceSelectionInternal(restore, previous.Channel);
                ObserveReference(restore);
                if (restore is null)
                {
                    CalibrationReferenceStatusStore.Instance.MarkDisconnected(_chamberId);
                }

                // A rescan can raise the same selection several times. Show one warning per
                // rejected port/owner instead of trapping the operator in a popup loop.
                string rejectionKey = $"{candidate.PortName}|{occupiedBy}|{activeOwner}";
                if (!string.Equals(_lastRejectedReferenceKey, rejectionKey, StringComparison.OrdinalIgnoreCase))
                {
                    _lastRejectedReferenceKey = rejectionKey;
                    MessageBox.Show(
                        this,
                        $"WIKA CTH7000 na porte {candidate.PortName} je práve používaný FBG kalibráciou zariadenia „{(string.IsNullOrWhiteSpace(activeOwner) ? occupiedBy : activeOwner)}“.\n\n" +
                        "Aktívny COM port nemožno prevziať. Vyber iný port alebo najprv zastav/uvoľni kalibráciu v pôvodnom okne.",
                        "Referencia je práve používaná",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return;
            }
        }

        _lastRejectedReferenceKey = null;

        // If this chamber deliberately switched from one reference to another, immediately free
        // the old process-level COM reservation too. The VM will acquire the new one on first read.
        // Use the persisted previous port rather than the live VM object: the old USB device may
        // already have disappeared from the current COM enumeration.
        if (previous.IsAssigned &&
            !string.Equals(previous.PortName, candidate.PortName, StringComparison.OrdinalIgnoreCase))
        {
            CalibrationResourceRegistry.Release(CalibrationResourceRegistry.F100Key(previous.PortName), _chamberId);
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
            SetReferenceSelectionInternal(null);
            _acceptedReference = null;
            ObserveReference(null);
            return;
        }

        ThermometerDeviceViewModel? match = FindAssignedReference(assignment);

        // Restore the persisted channel BEFORE assigning the device. SelectedF100's setter copies
        // SelectedF100Channel into the device, so doing this in the opposite order silently reset
        // a saved channel B back to the VM default A after an application restart.
        SetReferenceSelectionInternal(match, assignment.Channel);
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

    private void SetReferenceSelectionInternal(ThermometerDeviceViewModel? device, string? channel = null)
    {
        _referenceSelectionInternal = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(channel))
            {
                string restoredChannel = string.Equals(channel, "B", StringComparison.OrdinalIgnoreCase) ? "B" : "A";
                _viewModel.SelectedF100Channel = restoredChannel;
                if (device is not null) device.SelectedChannel = restoredChannel;
            }

            if (!ReferenceEquals(_viewModel.SelectedF100, device))
            {
                _viewModel.SelectedF100 = device;
            }
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
        // Persistent assignment is kept for convenient restoration. It is no longer a permanent
        // lock: another workspace may take it over when there is no live resource reservation.
    }
}
