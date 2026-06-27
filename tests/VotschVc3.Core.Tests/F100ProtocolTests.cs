using VotschVc3.Core.Thermometers;
using Xunit;

namespace VotschVc3.Core.Tests;

public class F100ProtocolTests
{
    [Theory]
    [InlineData("23.4567 C", 23.4567, "°C")]
    [InlineData("+0023.45 C", 23.45, "°C")]
    [InlineData("  -5.20 C\r", -5.20, "°C")]
    [InlineData("100.1234", 100.1234, "")]
    [InlineData("123.45 Ohms", 123.45, "Ω")]
    [InlineData("310.15 K", 310.15, "K")]
    public void ParseReading_extracts_value_and_unit(string raw, double expected, string unit)
    {
        ThermometerReading reading = F100Protocol.ParseReading(raw);

        Assert.NotNull(reading.Temperature);
        Assert.Equal(expected, reading.Temperature!.Value, 4);
        Assert.Equal(unit, reading.Unit);
    }

    [Fact]
    public void ParseReading_handles_comma_decimal()
    {
        ThermometerReading reading = F100Protocol.ParseReading("23,4 C");
        Assert.Equal(23.4, reading.Temperature!.Value, 4);
    }

    [Fact]
    public void Frame_appends_terminator_once()
    {
        Assert.Equal("READ?\r", F100Protocol.Frame("READ?"));
        Assert.Equal("READ?\r", F100Protocol.Frame("READ?\r"));
    }
}
