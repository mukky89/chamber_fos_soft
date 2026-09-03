using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using VotschVc3.App.Mvvm;
using VotschVc3.App.Thermometers;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// Manages the detected ASL F100 / WIKA CTH7000 thermometers (one entry per USB COM port).
/// Startup enumeration is intentionally lightweight: WMI metadata is enriched only by the
/// asynchronous detailed refresh so constructing the FBG calibration workspace cannot block UI.
/// </summary>
public sealed class ThermometersViewModel : ObservableObject, IAsyncDisposable
{
    public ThermometersViewModel()
    {
        RefreshCommand = new RelayCommand(Refresh);
        Refresh();
        SelectedDevice = Devices.FirstOrDefault();
    }

    public ObservableCollection<ThermometerDeviceViewModel> Devices { get; } = new();

    private ThermometerDeviceViewModel? _selectedDevice;
    public ThermometerDeviceViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set => SetProperty(ref _selectedDevice, value);
    }

    private string _statusMessage = "Pripoj teplomer cez USB a stlač Obnoviť.";
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public RelayCommand RefreshCommand { get; }

    public ThermometerDeviceViewModel AddManualPort(string portName)
    {
        string normalized = (portName ?? string.Empty).Trim().ToUpperInvariant();
        if (!Regex.IsMatch(normalized, @"^COM\d+$"))
        {
            throw new ArgumentException("Port zadaj vo formáte COM4.", nameof(portName));
        }

        ThermometerDeviceViewModel? existing = Devices.FirstOrDefault(d =>
            string.Equals(d.PortName, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var device = new ThermometerDeviceViewModel(new SerialDeviceInfo(normalized, null, "ručne zadaný port"));
        Devices.Add(device);
        return device;
    }

    /// <summary>
    /// Fast UI refresh. Uses SerialPort.GetPortNames plus already-cached metadata and therefore
    /// never performs a synchronous WMI query on the dispatcher thread.
    /// </summary>
    public void Refresh()
    {
        IReadOnlyList<SerialDeviceInfo> found = SerialPortEnumerator.EnumerateFast();
        ApplyFastSnapshot(found);
        StatusMessage = Devices.Count == 0
            ? "Nenašli sa žiadne sériové porty. Pripoj teplomer cez USB a stlač Obnoviť."
            : $"Nájdených {Devices.Count} portov.";
        SelectedDevice ??= Devices.FirstOrDefault();
    }

    private void ApplyFastSnapshot(IReadOnlyList<SerialDeviceInfo> found)
    {
        // Remove devices that disappeared, but never tear down an active connection from a
        // lightweight UI refresh.
        for (int i = Devices.Count - 1; i >= 0; i--)
        {
            ThermometerDeviceViewModel device = Devices[i];
            bool manual = string.Equals(device.Info.Description, "ručne zadaný port", StringComparison.OrdinalIgnoreCase);
            bool stillThere = found.Any(f => string.Equals(f.PortName, device.PortName, StringComparison.OrdinalIgnoreCase));
            if (!manual && !stillThere && !device.IsConnected)
            {
                if (ReferenceEquals(SelectedDevice, device)) SelectedDevice = null;
                Devices.RemoveAt(i);
            }
        }

        foreach (SerialDeviceInfo info in found)
        {
            if (!Devices.Any(d => string.Equals(d.PortName, info.PortName, StringComparison.OrdinalIgnoreCase)))
            {
                Devices.Add(new ThermometerDeviceViewModel(info));
            }
        }
    }

    /// <summary>
    /// Performs a fresh detailed Windows scan on a worker thread and enriches entries with USB
    /// serial number / PnP description. Connected devices remain intact. This is used only for
    /// explicit or deferred refreshes, never while the calibration window is being constructed.
    /// </summary>
    public async Task RefreshAsync()
    {
        IReadOnlyList<SerialDeviceInfo> found = await Task.Run(SerialPortEnumerator.Enumerate);

        for (int i = Devices.Count - 1; i >= 0; i--)
        {
            ThermometerDeviceViewModel device = Devices[i];
            bool manual = string.Equals(device.Info.Description, "ručne zadaný port", StringComparison.OrdinalIgnoreCase);
            bool stillThere = found.Any(info =>
                string.Equals(info.PortName, device.PortName, StringComparison.OrdinalIgnoreCase));
            if (stillThere || manual) continue;

            if (ReferenceEquals(SelectedDevice, device)) SelectedDevice = null;
            await device.DisposeAsync();
            Devices.RemoveAt(i);
        }

        foreach (SerialDeviceInfo info in found)
        {
            int existingIndex = -1;
            for (int i = 0; i < Devices.Count; i++)
            {
                if (string.Equals(Devices[i].PortName, info.PortName, StringComparison.OrdinalIgnoreCase))
                {
                    existingIndex = i;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                Devices.Add(new ThermometerDeviceViewModel(info));
                continue;
            }

            ThermometerDeviceViewModel existing = Devices[existingIndex];
            if (existing.IsConnected || SameMetadata(existing.Info, info)) continue;

            bool wasSelected = ReferenceEquals(SelectedDevice, existing);
            var replacement = new ThermometerDeviceViewModel(info)
            {
                BaudRate = existing.BaudRate,
                SelectedChannel = existing.SelectedChannel,
                ReadCommand = existing.ReadCommand,
                PollIntervalSeconds = existing.PollIntervalSeconds,
                PollingEnabled = existing.PollingEnabled,
            };
            await existing.DisposeAsync();
            Devices[existingIndex] = replacement;
            if (wasSelected) SelectedDevice = replacement;
        }

        StatusMessage = Devices.Count == 0
            ? "Nový scan: Windows nenašiel žiadny sériový USB/COM port."
            : $"Nový scan: nájdených {Devices.Count} portov.";
        SelectedDevice ??= Devices.FirstOrDefault();
    }

    private static bool SameMetadata(SerialDeviceInfo left, SerialDeviceInfo right) =>
        string.Equals(left.SerialNumber, right.SerialNumber, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Description, right.Description, StringComparison.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        foreach (ThermometerDeviceViewModel device in Devices)
        {
            await device.DisposeAsync();
        }
    }
}
