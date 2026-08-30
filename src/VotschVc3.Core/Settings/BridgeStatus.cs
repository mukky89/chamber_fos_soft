using System.Text.Json;

namespace VotschVc3.Core.Settings;

/// <summary>Local health snapshot shared by the Lab Bridge and desktop UI.</summary>
public sealed class BridgeStatus
{
    public int ContractVersion { get; set; } = 2;
    public bool Running { get; set; }
    public bool DashboardReachable { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? LastHeartbeatUtc { get; set; }
    public string DashboardUrl { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string LastError { get; set; } = string.Empty;
}

public static class BridgeStatusFile
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static BridgeStatus? Read(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<BridgeStatus>(File.ReadAllText(path), Json)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Write(string path, BridgeStatus status)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(status, Json));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Status reporting must never stop chamber communication.
        }
    }
}
