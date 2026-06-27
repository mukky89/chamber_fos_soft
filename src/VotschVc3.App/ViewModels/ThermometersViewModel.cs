using System.Collections.ObjectModel;
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

    /// <summary>
    /// Re-enumerates the serial ports, adding new devices and removing
    /// disappeared ones, while keeping any currently connected device intact.
    /// </summary>
    private void Refresh()
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

    public async ValueTask DisposeAsync()
    {
        foreach (ThermometerDeviceViewModel device in Devices)
        {
            await device.DisposeAsync();
        }
    }
}
