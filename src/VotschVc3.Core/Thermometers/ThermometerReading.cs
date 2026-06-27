namespace VotschVc3.Core.Thermometers;

/// <summary>A single decoded reading from an ASL F100 thermometer.</summary>
public sealed class ThermometerReading
{
    public ThermometerReading(DateTimeOffset timestamp, double? temperature, string unit, string raw)
    {
        Timestamp = timestamp;
        Temperature = temperature;
        Unit = unit;
        Raw = raw;
    }

    public DateTimeOffset Timestamp { get; }

    /// <summary>Parsed numeric value, or <c>null</c> when the response had no number.</summary>
    public double? Temperature { get; }

    /// <summary>Detected unit ("°C", "°F", "K", "Ω") or empty.</summary>
    public string Unit { get; }

    /// <summary>The raw response line.</summary>
    public string Raw { get; }
}
