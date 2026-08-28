using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.Views;

public partial class CalibrationWindow : Window
{
    /// <summary>The one open calibration workspace, so every device button reuses it
    /// instead of opening a second window with its own PeakLogger connection.</summary>
    private static CalibrationWindow? _instance;

    private readonly CalibrationViewModel _viewModel = new();
    private bool _disposing;

    public CalibrationWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Closing += OnClosing;
    }

    /// <summary>
    /// Opens (or re-activates) the shared FBG calibration workspace and preselects the
    /// device the operator pressed the button on – every chamber has its own button, and
    /// each of them should land on that chamber.
    /// </summary>
    public static void OpenFor(Window? owner, Guid chamberId)
    {
        // A window that is already tearing its devices down must not be handed back out.
        CalibrationWindow? existing = _instance;
        if (existing is null || !existing.IsLoaded || existing._disposing)
        {
            var window = new CalibrationWindow { Owner = owner };
            // Only clear the shared slot if it still points at this window – a slow close
            // of the previous one must not wipe the workspace that replaced it.
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_instance, window))
                {
                    _instance = null;
                }
            };
            _instance = window;
            existing = window;
            existing.Show();
        }
        else
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
        }

        existing.SelectChamber(chamberId);
    }

    /// <summary>Closes the workspace if one is open (called when the app shuts down).</summary>
    public static void CloseIfOpen()
    {
        _instance?.Close();
        _instance = null;
    }

    private void SelectChamber(Guid chamberId) => _viewModel.SelectChamber(chamberId);

    /// <summary>
    /// Tears the long-running calibration resources down before the window really closes.
    /// The close is cancelled, the disposal runs, and the real <see cref="Window.Close"/>
    /// is posted back to the dispatcher – calling it inline would re-enter WPF while it is
    /// still inside this Closing event ("Cannot set Visibility to Visible or call Show,
    /// ShowDialog, Close … while a Window is closing") whenever the disposal happens to
    /// complete synchronously.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_disposing)
        {
            return;
        }

        e.Cancel = true;
        _disposing = true;
        Closing -= OnClosing;
        _ = DisposeThenCloseAsync();
    }

    private async Task DisposeThenCloseAsync()
    {
        try
        {
            await _viewModel.DisposeAsync();
        }
        catch (Exception ex)
        {
            // The window must close even if a device hangs on shutdown – log and carry on.
            AppLog.Warn("FBG kalibrácia", $"Ukončenie kalibračného okna: {ex.Message}");
        }

        try
        {
            await Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Close));
        }
        catch (Exception ex)
        {
            AppLog.Warn("FBG kalibrácia", $"Zatvorenie kalibračného okna: {ex.Message}");
        }
    }
}
