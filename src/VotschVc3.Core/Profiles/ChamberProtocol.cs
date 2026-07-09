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
}
