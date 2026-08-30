using System.Text.Json;
using VotschVc3.Core.Profiles;

namespace VotschVc3.Agent;

public sealed record DeviceSnapshot(
    string DeviceId, string Name, string Kind, bool Online, bool ReadOnly,
    double? Temperature, double? Setpoint, double? Humidity, double? HumiditySetpoint,
    string SerialNumber, string Status, string Alarm, object? Profile, object? Channels, DateTimeOffset MeasuredAt);

public sealed record ChamberConfigSnapshot(
    string DeviceId,
    Guid ConfigId,
    string Name,
    ChamberKind Kind,
    ChamberProtocol Protocol,
    string Host,
    int Port,
    int Address,
    int AnalogChannelCount,
    int StartChannelIndex,
    string Terminator,
    double PollIntervalSeconds,
    bool AlarmsEnabled,
    double TempMin,
    double TempMax,
    double HumMin,
    double HumMax,
    bool AutoStopOnAlarm,
    bool AutoReconnect,
    bool AutoRecoverProfile,
    IReadOnlyList<double> QuickPresets,
    bool IsLocked,
    bool Enabled,
    bool AllowControl);

public sealed record AgentSettingsSnapshot(
    int PollSeconds,
    int FileScanEveryCycles,
    int MaxIndexedFiles);

public sealed record FileSnapshot(string RootAlias, string RelativePath, string Name, bool IsDirectory, long Size, DateTimeOffset? ModifiedAt);
public sealed record FolderSnapshot(string Alias, bool Writable);

/// <summary>
/// Versioned outbound contract from the laboratory PC to Dashboard FOS.
/// ContractVersion=2 adds persisted chamber configuration, complete profile documents,
/// source revisions and non-secret bridge settings while keeping the v1 fields intact.
/// </summary>
public sealed record HeartbeatRequest(
    int ContractVersion,
    string HostName,
    string Version,
    string[] Capabilities,
    FolderSnapshot[] Folders,
    DeviceSnapshot[] Devices,
    TestProfile[] Profiles,
    ChamberConfigSnapshot[] Chambers,
    AgentSettingsSnapshot Settings,
    string ProfilesRevision,
    string ChambersRevision,
    FileSnapshot[]? Files,
    string LastError);

public sealed class AgentCommand
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public JsonElement Payload { get; set; }
    public bool HasInputFile { get; set; }
    public string InputFileName { get; set; } = "";
}
