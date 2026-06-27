namespace VotschVc3.Core.Profiles;

/// <summary>
/// Persisted, user-editable configuration of one chamber (connection, channel
/// mapping and safety limits). The <see cref="Kind"/> is used to match a saved
/// config back to its chamber on startup.
/// </summary>
public sealed class ChamberConfig
{
    /// <summary>Stable identity used to match a saved config to its chamber.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name of the chamber.</summary>
    public string Name { get; set; } = "Komora";

    public ChamberKind Kind { get; set; }

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
}
