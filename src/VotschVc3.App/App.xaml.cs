using System.IO;
using System.Windows;
using System.Windows.Threading;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App;

/// <summary>Application entry point. Configures the diagnostic log and reports unhandled exceptions.</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VotschVc3");
        AppLog.Configure(Path.Combine(dir, "app.log"));
        AppLog.Info("App", $"Aplikácia spustená (v{GetType().Assembly.GetName().Version?.ToString(3)}).");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("AppDomain", (args.ExceptionObject as Exception)?.ToString() ?? "Neznáma chyba.");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("Task", args.Exception.Message);
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info("App", "Aplikácia ukončená.");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("UI", e.Exception.ToString());
        MessageBox.Show(
            e.Exception.Message,
            "Neočakávaná chyba",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
