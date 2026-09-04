using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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

        // Create the Documents\Lab Control layout (and migrate the old VotschVc3
        // folder once) before anything reads or writes app data.
        AppPaths.Initialize();
        AppLog.Configure(AppPaths.AppLogDir);

        // Dashboard Bridge is deliberately manual-only. Older builds registered a
        // scheduled task and also tried to launch the bridge from ShellViewModel.
        // Reset that legacy state before MainWindow/ShellViewModel is constructed.
        // A later legacy launch attempt therefore exits immediately in Agent/Program.
        ResetDashboardBridgeToManualMode();

        AppLog.Info("App", $"Aplikácia spustená (v{GetType().Assembly.GetName().Version?.ToString(3)}) z {Environment.ProcessPath ?? "—"}.");

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
        Notifications.DesktopNotifier.Shutdown();
        AppLog.Info("App", "Aplikácia ukončená.");
        base.OnExit(e);
    }

    private static void ResetDashboardBridgeToManualMode()
    {
        try
        {
            string configPath = System.IO.Path.Combine(AppPaths.Root, "bridge.json");
            if (System.IO.File.Exists(configPath))
            {
                JsonNode? node = JsonNode.Parse(System.IO.File.ReadAllText(configPath));
                if (node is JsonObject root)
                {
                    root["enabled"] = false;
                    System.IO.File.WriteAllText(
                        configPath,
                        root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Bridge", $"Bridge sa pri štarte nepodarilo prepnúť do manuálneho režimu: {ex.Message}");
        }

        // Stop a bridge left running by an older desktop build or an old logon task.
        try
        {
            foreach (Process process in Process.GetProcessesByName("VotschVc3.Agent"))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(1500);
                }
                catch
                {
                    // Best effort only; a protected/stale process must not block app startup.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Bridge", $"Starý Bridge proces sa nepodarilo ukončiť: {ex.Message}");
        }

        // Legacy installer created an AtLogOn task. Disable and end it so the bridge
        // cannot silently return after the user chose the new manual workflow.
        RunScheduledTaskCommand("/End /TN \"Sylex Lab Control Bridge\"");
        RunScheduledTaskCommand("/Change /TN \"Sylex Lab Control Bridge\" /Disable");
    }

    private static void RunScheduledTaskCommand(string arguments)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            process?.WaitForExit(1500);
        }
        catch
        {
            // The task may not exist or the current account may not own it. Either case
            // is harmless because enabled=false remains the primary safety gate.
        }
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
