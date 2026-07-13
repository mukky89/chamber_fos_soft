namespace VotschVc3.Core.Protocol;

/// <summary>
/// Immutable snapshot of the values returned by the chamber in response to the
/// ASCII-2 read command (<c>$ddI</c>).
/// <para>
/// On the wire the ASCII-2 read response returns, for every analog channel, the
/// active set point value followed by the measured (actual) value, and finally
/// the block of digital channels. <see cref="Ascii2Protocol.ParseReading"/>
/// normalises this into the actual-first convention used throughout the app (and
/// by the POL-EKO client), so <see cref="AnalogValues"/> holds
/// measured / set point per channel. Because the number of analog channels and
/// their meaning depends on the chamber configuration, use the raw values
/// together with the original <see cref="Raw"/> frame to calibrate the mapping
/// against your specific unit.
/// </para>
/// </summary>
public sealed class ChamberReading
{
    public ChamberReading(
        DateTimeOffset timestamp,
        string raw,
        IReadOnlyList<double> analogValues,
        DigitalChannels digitalChannels)
    {
        Timestamp = timestamp;
        Raw = raw;
        AnalogValues = analogValues;
        DigitalChannels = digitalChannels;
    }

    /// <summary>Moment the reading was decoded (local clock of the controlling PC).</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>The unmodified frame received from the chamber (without the terminator).</summary>
    public string Raw { get; }

    /// <summary>
    /// All decimal values found in the response, in order of appearance. With the
    /// conventional layout these alternate measured / set point per channel:
    /// index 0 = measured temperature, index 1 = temperature set point,
    /// index 2 = measured humidity, index 3 = humidity set point, …
    /// </summary>
    public IReadOnlyList<double> AnalogValues { get; }

    /// <summary>The decoded 32 digital channels.</summary>
    public DigitalChannels DigitalChannels { get; }

    /// <summary>Measured temperature (analog channel&#160;1 actual value), if present.</summary>
    public double? Temperature => GetValue(0);

    /// <summary>Active temperature set point (analog channel&#160;1 set value), if present.</summary>
    public double? TemperatureSetpoint => GetValue(1);

    /// <summary>Measured relative humidity (analog channel&#160;2 actual value), if present.</summary>
    public double? Humidity => GetValue(2);

    /// <summary>Active humidity set point (analog channel&#160;2 set value), if present.</summary>
    public double? HumiditySetpoint => GetValue(3);

    private double? GetValue(int index) =>
        index >= 0 && index < AnalogValues.Count ? AnalogValues[index] : null;
}
