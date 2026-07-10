using System.Windows;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _shell;

        // Remove the tray notification icon the moment the window starts closing –
        // otherwise a leftover icon makes the app look "minimised to tray" instead
        // of closed. The explicit Shutdown guarantees the process exits even while
        // WinForms (NotifyIcon) native handles exist.
        Closing += (_, _) => Notifications.DesktopNotifier.Shutdown();
        Closed += async (_, _) =>
        {
            await _shell.DisposeAsync();
            Application.Current.Shutdown();
        };
    }
}
