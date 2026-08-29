using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.Views;

public partial class CalibrationWindow : Window
{
    /// <summary>Each chamber owns one independent, reusable calibration workspace.</summary>
    private static readonly Dictionary<Guid, CalibrationWindow> Instances = new();

    private readonly CalibrationViewModel _viewModel;
    private readonly Guid _chamberId;
    private bool _disposing;
    private bool _shutdownRequested;

    public CalibrationWindow(Guid chamberId)
    {
        _chamberId = chamberId;
        _viewModel = new CalibrationViewModel(chamberId);
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
        Instances.TryGetValue(chamberId, out CalibrationWindow? existing);
        if (existing is null || !existing.IsLoaded || existing._disposing)
        {
            var window = new CalibrationWindow(chamberId) { Owner = owner };
            window.Closed += (_, _) =>
            {
                if (Instances.TryGetValue(chamberId, out CalibrationWindow? current) && ReferenceEquals(current, window))
                {
                    Instances.Remove(chamberId);
                }
            };
            Instances[chamberId] = window;
            existing = window;
            existing.Show();
        }
        else
        {
            if (!existing.IsVisible)
            {
                existing.Show();
            }

            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
        }

    }

    /// <summary>Closes the workspace if one is open (called when the app shuts down).</summary>
    public static void CloseIfOpen()
    {
        foreach (CalibrationWindow window in Instances.Values.ToArray())
        {
            window._shutdownRequested = true;
            window.Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

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
        if (!_shutdownRequested)
        {
            e.Cancel = true;
            Hide();
            return;
        }

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
