using System.Runtime.CompilerServices;
using System.Windows;
using VotschVc3.App.Calibration;
using VotschVc3.App.Notifications;

namespace VotschVc3.App.Views;

/// <summary>
/// Hooks every CalibrationWindow into the application-wide popup notification pipeline.
/// The old FOS API badge remains instantiated for source compatibility but is kept hidden;
/// transient API health messages now use the same popup UX as the rest of the app.
/// </summary>
internal static class CalibrationWindowNotificationBootstrap
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
            window.AttachCentralAppNotifications();
    }
}

public partial class CalibrationWindow
{
    private bool _centralAppNotificationsAttached;

    private void AttachCentralAppNotifications()
    {
        if (_centralAppNotificationsAttached) return;
        _centralAppNotificationsAttached = true;

        // Replace the old local API badge handler. The central handler preserves the
        // safe wiring-grid refresh behavior, but only overall API health becomes a popup;
        // per-symbol lookups remain deliberately quiet.
        _sylexFosIntegration.LookupStatusChanged -= OnSylexFosLookupStatusChanged;
        _sylexFosIntegration.LookupStatusChanged += OnCentralSylexFosLookupStatusChanged;

        _fosApiStatusHideTimer.Stop();
        _fosApiStatusBadge.Visibility = Visibility.Collapsed;
    }

    private void OnCentralSylexFosLookupStatusChanged(object? sender, SylexFosLookupStatus status)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            _fosApiStatusHideTimer.Stop();
            _fosApiStatusBadge.Visibility = Visibility.Collapsed;

            // Preserve the original table refresh contract if these states are used by
            // future metadata-loading code, but do not create one popup per scanned SN.
            if (status.State is SylexFosLookupState.Loaded or SylexFosLookupState.NotFound)
                RefreshWiringGridWhenSafe();

            switch (status.State)
            {
                case SylexFosLookupState.ApiAvailable:
                    AppNotificationService.Success(
                        "Sylex FOS API",
                        "Centrálne API je pripojené a dostupné.",
                        "sylex-fos-api:available");
                    break;

                case SylexFosLookupState.ApiUnavailable:
                    AppNotificationService.Warning(
                        "Sylex FOS API",
                        status.Message.Replace("FOS API · ", string.Empty, StringComparison.OrdinalIgnoreCase),
                        "sylex-fos-api:unavailable");
                    break;

                case SylexFosLookupState.ConfigurationError:
                    AppNotificationService.Error(
                        "Sylex FOS API",
                        status.Message,
                        "sylex-fos-api:configuration");
                    break;

                // Checking/loading are transient progress states and Loaded/NotFound may
                // happen per production symbol. They stay silent to avoid popup spam.
                case SylexFosLookupState.CheckingApi:
                case SylexFosLookupState.Loading:
                case SylexFosLookupState.Loaded:
                case SylexFosLookupState.NotFound:
                default:
                    break;
            }
        });
    }
}
