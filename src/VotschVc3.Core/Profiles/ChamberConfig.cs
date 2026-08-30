using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Profiles;

/// <summary>
/// Persisted, user-editable configuration of one chamber (connection, channel
/// mapping and safety limits). The <see cref="Kind"/> is used to match a saved
/// config back to its chamber on startup.
/// </summary>
public sealed class ChamberConfig
{
    /// <summary>Stable identity used to match a saved config to its chamber.</summary>
    [JsonConverter(typeof(EmptyGuidJsonConverter))]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name of the chamber.</summary>
    public string Name { get; set; } = "Komora";

    public ChamberKind Kind { get; set; }

    /// <summary>Wire protocol (Vötsch ASCII-2 by default, or POL-EKO MODBUS).</summary>
    public ChamberProtocol Protocol { get; set; } = ChamberProtocol.VotschAscii2;

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1080;
    public int Address { get; set; } = 1;
    public int AnalogChannelCount { get; set; } = 6;
    public int StartChannelIndex { get; set; }
    public string Terminator { get; set; } = "CR (\\r)";
    public double PollIntervalSeconds { get; set; } = 2;

    public bool AlarmsEnabled { get; set; }
    public double TempMin { get; set; } = -45;
    public double TempMax { get; set; } = 190;
    public double HumMin { get; set; }
    public double HumMax { get; set; } = 100;
    public bool AutoStopOnAlarm { get; set; } = true;
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, a profile interrupted by a power outage / app crash is offered
    /// for resume (with operator confirmation) the next time this chamber connects.
    /// </summary>
    public bool AutoRecoverProfile { get; set; } = true;

    /// <summary>
    /// Dashboard quick-set temperature presets (°C) for this device. Null or empty
    /// falls back to protocol defaults. Editable by admins per device.
    /// </summary>
    public List<double>? QuickPresets { get; set; }

    /// <summary>Nameplate / type-plate details (from the chamber's rating label).</summary>
    public ChamberNameplate? Nameplate { get; set; }

    /// <summary>
    /// When <c>true</c> the device is locked: all control buttons are disabled so a
    /// running profile / temperature can't be changed by an accidental press.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// SHA-256 hash of the optional unlock password. <c>null</c>/empty means the lock
    /// can be released without a password (a quick safety lock).
    /// </summary>
    public string? LockPasswordHash { get; set; }
}

/// <summary>
/// Dashboard-created configurations have no id until the local desktop store assigns one.
/// System.Text.Json normally rejects an empty string for Guid; accepting it as Guid.Empty
/// keeps the wire contract backward-compatible without weakening validation of non-empty ids.
/// </summary>
internal sealed class EmptyGuidJsonConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("GUID musí byť JSON string.");
        string? value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value)) return Guid.Empty;
        return Guid.TryParse(value, out Guid id) ? id : throw new JsonException("Neplatný GUID.");
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
