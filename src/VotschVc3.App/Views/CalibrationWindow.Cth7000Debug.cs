using System.Windows;
using System.Windows.Controls;

namespace VotschVc3.App.Views;

/// <summary>
/// Adds the CTH7000 raw serial debugger next to the existing USB/PnP diagnostics button
/// without coupling the calibration view model to diagnostic-only serial code.
/// </summary>
public partial class CalibrationWindow
{
    private bool _cthDebugButtonInjected;

    static CalibrationWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(CalibrationWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnCalibrationWindowLoadedForCthDebug));
    }

    private static void OnCalibrationWindowLoadedForCthDebug(object sender, RoutedEventArgs e)
    {
        if (sender is CalibrationWindow window)
        {
            window.EnsureCth7000DebugButton();
        }
    }

    private void EnsureCth7000DebugButton()
    {
        if (_cthDebugButtonInjected) return;

        Button? usbDiagnosticsButton = FindButtonByCommand(this, _viewModel.ToggleUsbDiagnosticsCommand);
        if (usbDiagnosticsButton?.Parent is not Panel parent) return;

        var debugButton = new Button
        {
            Content = "RAW debug",
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Otvorí izolovaný low-level COM/SCPI terminál. Nič neposiela automaticky; ukazuje presné TX/RX ASCII, HEX a časovanie.",
        };
        if (TryFindResource("GhostButton") is Style style)
        {
            debugButton.Style = style;
        }
        DockPanel.SetDock(debugButton, Dock.Right);
        debugButton.Click += OpenCth7000RawDebug_Click;

        int index = parent.Children.IndexOf(usbDiagnosticsButton);
        parent.Children.Insert(Math.Max(0, index), debugButton);
        _cthDebugButtonInjected = true;
    }

    private void OpenCth7000RawDebug_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsRunning)
        {
            MessageBox.Show(
                this,
                "RAW USB debug nie je možné otvoriť počas bežiacej kalibrácie. Zastav kalibráciu, aby debug režim mohol bezpečne prevziať COM port referenčného teplomera.",
                "CTH7000 RAW debug",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_viewModel.SelectedF100 is not { } thermometer)
        {
            MessageBox.Show(
                this,
                "Najprv vyber USB/COM port referenčného teplomera.",
                "CTH7000 RAW debug",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var debug = new Cth7000DebugWindow(thermometer)
        {
            Owner = this,
        };
        debug.ShowDialog();
    }
}
