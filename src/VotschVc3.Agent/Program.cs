namespace VotschVc3.Agent;

using VotschVc3.Core.Settings;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string configPath = args.FirstOrDefault(a => !a.StartsWith('-'))
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Lab Control", "bridge.json");
        string statusPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Lab Control",
            "bridge-status.json");

        try
        {
            BridgeOptions options = BridgeOptions.Load(Path.GetFullPath(Environment.ExpandEnvironmentVariables(configPath)));

            // Dashboard communication is opt-in. Older bridge.json files do not contain
            // the Enabled property and therefore deserialize to false as well.
            if (!options.Enabled)
            {
                BridgeStatusFile.Write(statusPath, new BridgeStatus
                {
                    Running = false,
                    DashboardReachable = false,
                    UpdatedUtc = DateTime.UtcNow,
                    MachineName = Environment.MachineName,
                    DashboardUrl = options.DashboardUrl,
                    LastError = "FOS Dashboard Bridge je vypnutý v konfigurácii (enabled=false).",
                });
                Console.WriteLine("Lab Control Bridge je vypnutý (enabled=false). Dashboard sa nekontaktuje.");
                return 0;
            }

            options.Validate();
            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
            await using var bridge = new BridgeClient(options);
            await bridge.RunAsync(stop.Token);
            return 0;
        }
        catch (Exception ex)
        {
            BridgeStatusFile.Write(
                statusPath,
                new BridgeStatus { Running = false, UpdatedUtc = DateTime.UtcNow, MachineName = Environment.MachineName, LastError = ex.Message });
            Console.Error.WriteLine("Lab Control Bridge sa nespustil: " + ex.Message);
            Console.Error.WriteLine("Nastavenie: " + configPath);
            return 1;
        }
    }
}
