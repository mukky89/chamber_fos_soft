using System.Text.Json;
using VotschVc3.Core.Profiles;

namespace VotschVc3.Agent;

public sealed record DeviceSnapshot(
    string DeviceId, string Name, string Kind, bool Online, bool ReadOnly,
    double? Temperature, double? Setpoint, double? Humidity, double? HumiditySetpoint,
    string SerialNumber, string Status, string Alarm, object? Profile, object? Channels, DateTimeOffset MeasuredAt);

public sealed record FileSnapshot(string RootAlias, string RelativePath, string Name, bool IsDirectory, long Size, DateTimeOffset? ModifiedAt);
public sealed record FolderSnapshot(string Alias, bool Writable);
public sealed record HeartbeatRequest(string HostName, string Version, string[] Capabilities, FolderSnapshot[] Folders, DeviceSnapshot[] Devices, TestProfile[] Profiles, FileSnapshot[]? Files, string LastError);
public sealed class AgentCommand
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public JsonElement Payload { get; set; }
    public bool HasInputFile { get; set; }
    public string InputFileName { get; set; } = "";
}
