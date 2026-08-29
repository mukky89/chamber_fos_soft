using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using VotschVc3.App.Mvvm;
using VotschVc3.App.Thermometers;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// Manages the detected ASL F100 thermometers (one entry per USB COM port).
/// Several units can be connected and read simultaneously; the user tells them
/// apart by COM port and USB serial number.
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
    /// Re-enumerates the serial ports, adding new devices and removing
    /// disappeared ones, while keeping any currently connected device intact.
    /// </summary>
    public void Refresh()
    {
        IReadOnlyList<SerialDeviceInfo> found = SerialPortEnumerator.Enumerate();

        // Remove devices that are gone and not connected.
        for (int i = Devices.Count - 1; i >= 0; i--)
        {
            ThermometerDeviceViewModel device = Devices[i];
            bool stillThere = found.Any(f => string.Equals(f.PortName, device.PortName, StringComparison.OrdinalIgnoreCase));
            if (!stillThere && !device.IsConnected)
            {
                Devices.RemoveAt(i);
            }
        }

        // Add newly discovered ports.
        foreach (SerialDeviceInfo info in found)
        {
            if (!Devices.Any(d => string.Equals(d.PortName, info.PortName, StringComparison.OrdinalIgnoreCase)))
            {
                Devices.Add(new ThermometerDeviceViewModel(info));
            }
        }

        StatusMessage = Devices.Count == 0
            ? "Nenašli sa žiadne sériové porty. Pripoj teplomer cez USB a stlač Obnoviť."
            : $"Nájdených {Devices.Count} portov.";

        SelectedDevice ??= Devices.FirstOrDefault();
    }

    /// <summary>
    /// Performs a fresh Windows scan and also releases connected entries whose COM port
    /// disappeared. This is used immediately before operator connect/read actions so a
    /// USB adapter that came back under a different COM number is never reused stale.
    /// Manually entered ports remain as an explicit fallback.
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

            await device.DisposeAsync();
            Devices.RemoveAt(i);
        }

        foreach (SerialDeviceInfo info in found)
        {
            if (!Devices.Any(device => string.Equals(device.PortName, info.PortName, StringComparison.OrdinalIgnoreCase)))
            {
                Devices.Add(new ThermometerDeviceViewModel(info));
            }
        }

        StatusMessage = Devices.Count == 0
            ? "Nový scan: Windows nenašiel žiadny sériový USB/COM port."
            : $"Nový scan: nájdených {Devices.Count} portov.";
        SelectedDevice ??= Devices.FirstOrDefault();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (ThermometerDeviceViewModel device in Devices)
        {
            await device.DisposeAsync();
        }
    }
}
