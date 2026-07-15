namespace VotschVc3.Core.Profiles;

/// <summary>Wire protocol used to communicate with a chamber / oven.</summary>
public enum ChamberProtocol
{
    /// <summary>Vötsch / Weiss S!MPAC ASCII-2 over TCP (the original, default protocol).</summary>
    VotschAscii2,

    /// <summary>
    /// POL-EKO SMART controller over MODBUS TCP (e.g. the SLN / SLW drying ovens,
    /// SLW / CLW incubators). Port 502, one temperature channel, no humidity.
    /// </summary>
    PolEkoModbus,

    /// <summary>
    /// SIKA TP Premium calibration bath / dry block over its HTTP REST-API
    /// (port 8081). One temperature channel (the reference sensor), no humidity,
    /// no remote power on/off.
    /// </summary>
    SikaRestApi,
}
