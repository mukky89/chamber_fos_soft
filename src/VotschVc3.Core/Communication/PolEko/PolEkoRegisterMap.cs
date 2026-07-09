namespace VotschVc3.Core.Communication.PolEko;

/// <summary>
/// MODBUS register layout of a POL-EKO SMART controller (SLN / SLW drying ovens
/// and incubators).
/// <para>
/// ⚠ IMPORTANT — VERIFY BEFORE CONTROLLING A REAL OVEN. These addresses follow
/// POL-EKO's published SMART MODBUS-TCP description (present temperature in an
/// input register, set point / on-off in holding registers), but the exact
/// numbers and whether writing is enabled depend on the controller firmware.
/// Basic SMART controllers often expose MODBUS as <b>read-only monitoring</b>;
/// writing then returns a MODBUS exception, which the app surfaces as an error
/// rather than doing anything unsafe. Confirm the values against the unit's
/// manual (or by watching LabDesk traffic) and adjust here if needed.
/// </para>
/// All addresses are 0-based MODBUS protocol addresses.
/// </summary>
public sealed class PolEkoRegisterMap
{
    /// <summary>Input register (FC 0x04) that holds the measured chamber temperature.</summary>
    public ushort MeasuredTemperatureInput { get; set; }

    /// <summary>Holding register (FC 0x03 read / 0x06 write) with the temperature set point.</summary>
    public ushort SetpointHolding { get; set; }

    /// <summary>Holding register that switches the device on/off (0 = off, non-zero = on).</summary>
    public ushort OnOffHolding { get; set; } = 1;

    /// <summary>raw value × this = °C. POL-EKO SMART reports 0.1 °C resolution.</summary>
    public double TemperatureScale { get; set; } = 0.1;

    /// <summary>Temperature registers are signed 16-bit (two's complement for sub-zero).</summary>
    public bool TemperatureSigned { get; set; } = true;

    /// <summary>The documented default layout for the SMART controller.</summary>
    public static PolEkoRegisterMap SmartDefault() => new();
}
