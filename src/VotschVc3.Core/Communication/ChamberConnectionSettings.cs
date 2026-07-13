using VotschVc3.Core.Protocol;

namespace VotschVc3.Core.Communication;

/// <summary>
/// Connection and protocol parameters for a single chamber. The defaults match
/// the documented Ethernet behaviour of the VC3 / SIMPAC controllers
/// (fixed TCP port 1080, address 1, carriage return terminator).
/// </summary>
public sealed class ChamberConnectionSettings
{
    /// <summary>IP address or host name of the chamber's network interface.</summary>
    public string Host { get; set; } = "192.168.0.1";

    /// <summary>
    /// TCP port of the ASCII-2 interface. The VC3 / CTS controllers use the
    /// fixed port 1080; some installations use 2049. Confirm against your unit.
    /// </summary>
    public int Port { get; set; } = 1080;

    /// <summary>2 digit chamber address used in every frame (usually 1).</summary>
    public int Address { get; set; } = 1;

    /// <summary>Frame terminator. Carriage return by default; "\r\n" on some firmware.</summary>
    public string Terminator { get; set; } = Ascii2Protocol.DefaultTerminator;

    /// <summary>Number of analog set point fields sent with a write command.</summary>
    public int AnalogChannelCount { get; set; } = Ascii2Protocol.DefaultAnalogChannelCount;

    /// <summary>
    /// Index (0-based) of the digital channel that switches the chamber on. On the
    /// Vötsch / Weiss Simpac controllers this is the channel labelled "Start" on
    /// the panel, which is the first digital output (index 0). SET DIGITALOUT uses
    /// the same index as the ASCII-2 read-back block, so this value is both the
    /// SIMSERV channel and the read-back bit. Verify on your unit if in doubt.
    /// </summary>
    public int StartChannelIndex { get; set; }

    /// <summary>Timeout for establishing the TCP connection.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Timeout for receiving a complete response frame. Kept generous because
    /// some S!MPAC controllers (and serial-to-Ethernet gateways) are slow to
    /// acknowledge a write, which otherwise shows up as sporadic write timeouts.
    /// </summary>
    public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Creates a deep copy so a UI can edit settings without affecting a live client.</summary>
    public ChamberConnectionSettings Clone() => new()
    {
        Host = Host,
        Port = Port,
        Address = Address,
        Terminator = Terminator,
        AnalogChannelCount = AnalogChannelCount,
        StartChannelIndex = StartChannelIndex,
        ConnectTimeout = ConnectTimeout,
        ReadTimeout = ReadTimeout,
    };
}
