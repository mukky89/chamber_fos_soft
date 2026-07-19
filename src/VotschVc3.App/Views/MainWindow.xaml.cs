using System.ComponentModel;
using System.Windows;
using VotschVc3.App.Notifications;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell = new();

    /// <summary>Set only once the user confirms the exit; lets the real close proceed.</summary>
    private bool _exitConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _shell;

        // The close (✕) button minimises to the tray instead of quitting; the tray
        // menu / a dedicated Exit button are the only ways to actually close the app.
        DesktopNotifier.ShowRequested = RestoreFromTray;
        DesktopNotifier.ExitRequested = () => Dispatcher.Invoke(RequestExit);

        Closing += OnClosing;
        Closed += async (_, _) =>
        {
            await _shell.DisposeAsync();
            Application.Current.Shutdown();
        };
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exitConfirmed)
        {
            // Real exit: remove the tray icon so no orphan icon lingers.
            DesktopNotifier.Shutdown();
            return;
        }

        // Plain ✕: keep running, just hide to the tray.
        e.Cancel = true;
        Hide();
        DesktopNotifier.ShowMinimizedToTrayHint();
    }

    private void RestoreFromTray() => Dispatcher.Invoke(() =>
    {
        Show();
        WindowState = WindowState.Maximized;
        Activate();
        Topmost = true;
        Topmost = false;
    });

    /// <summary>
    /// Asks the operator to confirm before the app is really closed (so a lab test
    /// isn't stopped by accident). Called from the Exit button and the tray menu.
    /// </summary>
    public void RequestExit()
    {
        // Make sure the window is visible so the confirmation isn't hidden behind the tray.
        if (!IsVisible)
        {
            RestoreFromTray();
        }

        MessageBoxResult result = MessageBox.Show(
            this,
            "Naozaj chcete ukončiť aplikáciu?\n\nRiadenie a monitorovanie zariadení sa zastaví. "
            + "Ak chcete appku len skryť, zatvorte okno krížikom – ostane bežať v oznamovacej oblasti (tray).",
            "Ukončiť aplikáciu",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
        {
            _exitConfirmed = true;
            Close();
        }
    }
}
