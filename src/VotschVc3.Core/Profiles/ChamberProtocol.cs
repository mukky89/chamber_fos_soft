namespace VotschVc3.Core.Profiles;

/// <summary>Wire protocol used to communicate with a chamber / oven.</summary>
public enum ChamberProtocol
{
    /// <summary>Vötsch / Weiss S!MPAC ASCII-2 over TCP (the original, default protocol).</summary>
    VotschAscii2,

    /// <summary>
    /// POL-EKO LabDesk RPC over an AES-encrypted Eneter TCP channel (default port
    /// 56506). The historic enum name is retained so saved chamber files migrate.
    /// </summary>
    PolEkoModbus,

    /// <summary>
    /// SIKA TP Premium calibration bath / dry block over its HTTP REST-API
    /// (port 8081). One temperature channel (the reference sensor), no humidity,
    /// no remote power on/off.
    /// </summary>
    SikaRestApi,
}
