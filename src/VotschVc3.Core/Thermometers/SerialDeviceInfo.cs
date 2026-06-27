namespace VotschVc3.Core.Thermometers;

/// <summary>
/// Describes a serial / USB device that can host an ASL F100 thermometer.
/// The <see cref="SerialNumber"/> lets the user tell apart several identical
/// units that all appear as virtual COM ports.
/// </summary>
public sealed record SerialDeviceInfo(string PortName, string? SerialNumber, string? Description)
{
    /// <summary>Label for the picker, e.g. "COM5 · FT3AB12 (USB Serial Port)".</summary>
    public string Display
    {
        get
        {
            string serial = string.IsNullOrWhiteSpace(SerialNumber) ? "—" : SerialNumber;
            string desc = string.IsNullOrWhiteSpace(Description) ? string.Empty : $" ({Description})";
            return $"{PortName} · {serial}{desc}";
        }
    }
}
