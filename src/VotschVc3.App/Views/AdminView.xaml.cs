using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

public partial class AdminView : UserControl
{
    private Button? _bridgeStartButton;
    private TextBlock? _bridgeManualStatus;
    private bool _bridgeUiPrepared;

    public AdminView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_bridgeUiPrepared)
        {
            return;
        }

        _bridgeStartButton = FindVisualChildren<Button>(this)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Spustiť Bridge", StringComparison.Ordinal));

        if (_bridgeStartButton is null || VisualTreeHelper.GetParent(_bridgeStartButton) is not Panel buttonPanel)
        {
            return;
        }

        _bridgeUiPrepared = true;

        // The old button called ShellViewModel.EnsureBridgeStarted(), which was also used
        // by the legacy startup path. Rewire it to the new explicit/manual workflow.
        _bridgeStartButton.Command = null;
        _bridgeStartButton.CommandParameter = null;
        _bridgeStartButton.ToolTip = "Bridge sa spustí iba na vyžiadanie administrátora. Pri ďalšom štarte aplikácie bude znovu vypnutý.";
        _bridgeStartButton.Click += StartBridge_Click;

        var stopButton = new Button
        {
            Content = "Zastaviť Bridge",
            ToolTip = "Ukončí Bridge, vypne enabled v bridge.json a zablokuje starú naplánovanú úlohu."
        };
        stopButton.SetResourceReference(FrameworkElement.StyleProperty, "DangerButton");
        stopButton.Click += StopBridge_Click;

        int startIndex = buttonPanel.Children.IndexOf(_bridgeStartButton);
        buttonPanel.Children.Insert(Math.Min(startIndex + 1, buttonPanel.Children.Count), stopButton);

        if (FindVisualParent<StackPanel>(buttonPanel) is StackPanel card)
        {
            _bridgeManualStatus = new TextBlock
            {
                Text = "Bridge sa pri štarte aplikácie nespúšťa. Použi tlačidlo „Spustiť Bridge“ iba vtedy, keď ho potrebuješ.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            _bridgeManualStatus.SetResourceReference(FrameworkElement.StyleProperty, "Caption");
            card.Children.Add(_bridgeManualStatus);

            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "—";
            string executable = Environment.ProcessPath ?? "—";
            var versionInfo = new TextBlock
            {
                Text = $"Verzia aplikácie: v{version} · spustený súbor: {System.IO.Path.GetFileName(executable)}",
                ToolTip = executable,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            versionInfo.SetResourceReference(FrameworkElement.StyleProperty, "Caption");
            card.Children.Add(versionInfo);
        }
    }

    private async void StartBridge_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureBridgeConfigurationExists();
            SetBridgeEnabled(true);

            // Start a fresh instance so it definitely reads enabled=true. The legacy
            // scheduled task stays disabled; the manual button owns the lifecycle.
            StopBridgeProcesses();

            string executable = FindBridgeExecutable()
                ?? throw new System.IO.FileNotFoundException("VotschVc3.Agent.exe sa nenašiel v LabBridge priečinku aplikácie.");
            string configPath = BridgeConfigPath;

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = System.IO.Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            // The agent has a hard manual-only gate. This switch is deliberately supplied
            // only by the Administration button so startup code / Scheduled Tasks cannot
            // establish a dashboard connection by accident.
            startInfo.ArgumentList.Add("--manual-start");
            startInfo.ArgumentList.Add(configPath);
            Process.Start(startInfo)?.Dispose();

            SetManualStatus("Bridge sa spúšťa ručne… stav sa obnoví o niekoľko sekúnd.");
            await Task.Delay(800);
            RefreshShellBridgeStatus();
        }
        catch (Exception ex)
        {
            SetBridgeEnabledBestEffort(false);
            SetManualStatus($"Bridge sa nepodarilo spustiť: {ex.Message}");
        }
    }

    private void StopBridge_Click(object sender, RoutedEventArgs e)
    {
        SetBridgeEnabledBestEffort(false);
        StopBridgeProcesses();
        EndAndDisableLegacyScheduledTask();
        SetManualStatus("Bridge je zastavený a vypnutý. Pri štarte aplikácie sa automaticky nespustí.");
        RefreshShellBridgeStatus();
    }

    private void RefreshShellBridgeStatus()
    {
        if (_bridgeStartButton?.DataContext is ShellViewModel shell)
        {
            shell.RefreshBridgeStatusCommand.Execute(null);
        }
    }

    private void SetManualStatus(string text)
    {
        if (_bridgeManualStatus is not null)
        {
            _bridgeManualStatus.Text = text;
        }
    }

    private static string BridgeConfigPath => System.IO.Path.Combine(AppPaths.Root, "bridge.json");

    private static void EnsureBridgeConfigurationExists()
    {
        if (System.IO.File.Exists(BridgeConfigPath))
        {
            return;
        }

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(BridgeConfigPath)!);
        using Stream? source = typeof(AdminView).Assembly.GetManifestResourceStream("bridge.example.json");
        if (source is null)
        {
            throw new InvalidOperationException("V aplikácii chýba zabudovaný bridge.example.json.");
        }

        using var destination = System.IO.File.Create(BridgeConfigPath);
        source.CopyTo(destination);
    }

    private static void SetBridgeEnabled(bool enabled)
    {
        EnsureBridgeConfigurationExists();
        JsonNode? node = JsonNode.Parse(System.IO.File.ReadAllText(BridgeConfigPath));
        if (node is not JsonObject root)
        {
            throw new InvalidOperationException("bridge.json nemá platný JSON objekt.");
        }

        root["enabled"] = enabled;
        System.IO.File.WriteAllText(
            BridgeConfigPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void SetBridgeEnabledBestEffort(bool enabled)
    {
        try
        {
            SetBridgeEnabled(enabled);
        }
        catch
        {
            // Status UI below still reports the operation; do not crash Administration.
        }
    }

    private static string? FindBridgeExecutable()
    {
        string[] directCandidates =
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "LabBridge", "VotschVc3.Agent.exe"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "VotschVc3.Agent.exe")
        };

        foreach (string candidate in directCandidates)
        {
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }
        }

        try
        {
            return System.IO.Directory
                .EnumerateFiles(AppContext.BaseDirectory, "VotschVc3.Agent.exe", System.IO.SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static void StopBridgeProcesses()
    {
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
                    // Best effort. A stale/protected process is reflected by bridge status.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // Never let process enumeration break the Admin screen.
        }
    }

    private static void EndAndDisableLegacyScheduledTask()
    {
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
            // The legacy task may not exist; that is already the desired state.
        }
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? current = child;
        while (current is Visual)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is T typed)
            {
                return typed;
            }
        }
        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is not Visual)
        {
            yield break;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
