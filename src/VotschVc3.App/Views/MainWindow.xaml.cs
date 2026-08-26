using System.ComponentModel;
using System.Windows;
using VotschVc3.App.Notifications;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell = new();
    private CalibrationWindow? _calibrationWindow;

    /// <summary>Set only once the user confirms the exit; lets the real close proceed.</summary>
    private bool _exitConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _shell;

        DesktopNotifier.ShowRequested = RestoreFromTray;
        DesktopNotifier.ExitRequested = () => Dispatcher.Invoke(RequestExit);

        Closing += OnClosing;
        Closed += async (_, _) =>
        {
            if (_calibrationWindow is not null)
            {
                _calibrationWindow.Close();
                _calibrationWindow = null;
            }
            await _shell.DisposeAsync();
            Application.Current.Shutdown();
        };
    }

    private void Calibration_Click(object sender, RoutedEventArgs e)
    {
        if (_calibrationWindow is { IsLoaded: true })
        {
            _calibrationWindow.Activate();
            return;
        }

        _calibrationWindow = new CalibrationWindow { Owner = this };
        _calibrationWindow.Closed += (_, _) => _calibrationWindow = null;
        _calibrationWindow.Show();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exitConfirmed)
        {
            DesktopNotifier.Shutdown();
            return;
        }

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

    public void RequestExit()
    {
        if (!IsVisible)
        {
            RestoreFromTray();
        }

        var dialog = new ExitDialog { Owner = this };
        dialog.ShowDialog();

        switch (dialog.Choice)
        {
            case ExitChoice.Exit:
                _exitConfirmed = true;
                Close();
                break;
            case ExitChoice.MinimizeToTray:
                Hide();
                DesktopNotifier.ShowMinimizedToTrayHint();
                break;
        }
    }
}
