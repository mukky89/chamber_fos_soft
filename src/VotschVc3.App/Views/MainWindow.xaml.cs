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
    private bool _exitInProgress;
    private bool _recoveryShown;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _shell;

        DesktopNotifier.ShowRequested = RestoreFromTray;
        DesktopNotifier.ExitRequested = () => Dispatcher.Invoke(RequestExit);

        Closing += OnClosing;
        Loaded += (_, _) => RestoreInterruptedCalibrations();
        _shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.IsLoggedIn) && _shell.IsLoggedIn)
                _ = Dispatcher.InvokeAsync(RestoreInterruptedCalibrations, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
    }

    private void RestoreInterruptedCalibrations()
    {
        if (!_shell.IsLoggedIn || _recoveryShown || _exitInProgress) return;
        _recoveryShown = true;
        var store = CalibrationStorage.CreateStore();
        foreach (var chamber in _shell.Chambers)
        {
            try
            {
                if (store.LoadCheckpoint(chamber.Id) is not null)
                    CalibrationWindow.OpenFor(this, chamber.Id);
            }
            catch (Exception ex)
            {
                VotschVc3.Core.Diagnostics.AppLog.Warn("FBG kalibrácia", $"Otvorenie obnovy pre {chamber.Id}: {ex.Message}");
            }
        }
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

    public async void RequestExit()
    {
        if (_exitInProgress) return;
        if (!IsVisible)
        {
            RestoreFromTray();
        }

        var dialog = new ExitDialog { Owner = this };
        dialog.ShowDialog();

        switch (dialog.Choice)
        {
            case ExitChoice.Exit:
                _exitInProgress = true;
                try
                {
                    // OnMainWindowClose terminates WPF: all asynchronous saves must finish first.
                    await CalibrationWindow.CloseIfOpenAsync();
                    await _shell.DisposeAsync();
                    _exitConfirmed = true;
                    Close();
                }
                catch (Exception ex)
                {
                    _exitInProgress = false;
                    MessageBox.Show(this, "Ukončenie sa nepodarilo dokončiť: " + ex.Message,
                        "Ukončenie aplikácie", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                break;
            case ExitChoice.MinimizeToTray:
                Hide();
                DesktopNotifier.ShowMinimizedToTrayHint();
                break;
        }
    }
}
