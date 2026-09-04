using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.Views;

/// <summary>
/// Restores the expected operator behaviour around PeakLogger startup and the refresh button.
/// Startup discovery is deliberately deferred until after the first WPF frame, so the calibration
/// window stays responsive while local REST endpoints are scanned.
/// </summary>
internal static class CalibrationWindowAutomaticHardwareRecoveryBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(CalibrationWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded),
            handledEventsToo: true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is CalibrationWindow window)
            window.InitializeAutomaticHardwareRecovery();
    }
}

public partial class CalibrationWindow
{
    private bool _automaticHardwareRecoveryInitialized;
    private Button? _peakLoggerRecoveryButton;
    private bool _peakLoggerRecoveryRunning;

    internal void InitializeAutomaticHardwareRecovery()
    {
        if (_automaticHardwareRecoveryInitialized) return;
        _automaticHardwareRecoveryInitialized = true;

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                HookPeakLoggerRecoveryButton();
                _ = RecoverPeakLoggerAsync(startup: true);
            }));
    }

    private void HookPeakLoggerRecoveryButton()
    {
        if (_peakLoggerRecoveryButton is not null) return;

        foreach (Button button in FindVisualChildrenHardwareRecovery<Button>(this))
        {
            if (!ReferenceEquals(button.Command, _viewModel.RefreshSensorsCommand)) continue;

            // The old command was disabled whenever PeakLogger was disconnected, which made the
            // refresh button useless exactly when recovery was needed. This button now performs
            // discovery + reconnect when disconnected and a normal sensor refresh when connected.
            button.Command = null;
            button.IsEnabled = true;
            button.ToolTip = "Obnoviť PeakLogger: ak API nie je pripojené, automaticky ho vyhľadá a pripojí; ak je pripojené, znovu načíta interrogátory, kanály a peaky.";
            button.Click += PeakLoggerRecoveryButton_Click;
            _peakLoggerRecoveryButton = button;
            break;
        }
    }

    private async void PeakLoggerRecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await RecoverPeakLoggerAsync(startup: false);
    }

    private async Task RecoverPeakLoggerAsync(bool startup)
    {
        if (_peakLoggerRecoveryRunning || _disposing || _viewModel.IsRunning || _viewModel.UseSimulator)
            return;

        _peakLoggerRecoveryRunning = true;
        try
        {
            if (_viewModel.PeakLoggerConnected)
            {
                if (!startup && _viewModel.RefreshSensorsCommand.CanExecute(null))
                {
                    _viewModel.RefreshSensorsCommand.Execute(null);
                    await WaitForCommandAsync(_viewModel.RefreshSensorsCommand, TimeSpan.FromSeconds(10));
                }
                return;
            }

            if (_viewModel.DiscoverPeakLoggerApisCommand.CanExecute(null))
            {
                AppLog.Info("PeakLogger", startup
                    ? "Automatický discovery scan po otvorení FBG kalibrácie."
                    : "Obnova PeakLoggera: spúšťam discovery scan.");
                _viewModel.DiscoverPeakLoggerApisCommand.Execute(null);
                await WaitForCommandAsync(_viewModel.DiscoverPeakLoggerApisCommand, TimeSpan.FromSeconds(15));
            }

            if (_disposing || _viewModel.IsRunning || _viewModel.PeakLoggerConnected)
                return;

            if (_viewModel.SelectedPeakLoggerInstance is not null &&
                _viewModel.ConnectPeakLoggerCommand.CanExecute(null))
            {
                AppLog.Info("PeakLogger",
                    $"Automaticky pripájam nájdené API {_viewModel.PeakLoggerHost}:{_viewModel.PeakLoggerPort}.");
                _viewModel.ConnectPeakLoggerCommand.Execute(null);
                await WaitForCommandAsync(_viewModel.ConnectPeakLoggerCommand, TimeSpan.FromSeconds(15));
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("PeakLogger", $"Automatická obnova/discovery: {ex.Message}");
        }
        finally
        {
            _peakLoggerRecoveryRunning = false;
        }
    }

    private static async Task WaitForCommandAsync(VotschVc3.App.Mvvm.AsyncRelayCommand command, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;

        // Execute() is async-void by ICommand contract. Wait until it enters IsRunning first,
        // then until it finishes, without blocking the WPF dispatcher.
        for (int i = 0; i < 20 && !command.IsRunning; i++)
            await Task.Delay(25);

        while (command.IsRunning && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(50);
    }

    private static IEnumerable<T> FindVisualChildrenHardwareRecovery<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (T nested in FindVisualChildrenHardwareRecovery<T>(child))
                yield return nested;
        }
    }
}
