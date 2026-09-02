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

    [Theory]
    [InlineData("A 23.4567 C", "A", 23.4567)]
    [InlineData("ChB 24.125 C", "B", 24.125)]
    [InlineData("1 22.500 C", "A", 22.500)]
    [InlineData("2 25.750 C", "B", 25.750)]
    public void ParseReading_handles_talk_only_channel_prefix(string raw, string channel, double expected)
    {
        Assert.Equal(channel, F100Protocol.DetectTalkOnlyChannel(raw));
        Assert.Equal(expected, F100Protocol.ParseReading(raw).Temperature!.Value, 4);
    }

    [Fact]
    public void DetectTalkOnlyChannel_allows_frames_without_channel_prefix()
    {
        Assert.Null(F100Protocol.DetectTalkOnlyChannel("23.4567 C"));
    }

    [Fact]
    public void Frame_appends_terminator_once()
    {
        Assert.Equal("READ?\r", F100Protocol.Frame("READ?"));
        Assert.Equal("READ?\r", F100Protocol.Frame("READ?\r"));
    }

    [Theory]
    [InlineData("A", "A")]
    [InlineData("a", "A")]
    [InlineData(" B ", "B")]
    [InlineData("A-B", "A-B")]
    public void NormalizeChannel_accepts_supported_inputs(string input, string expected)
    {
        Assert.Equal(expected, F100Protocol.NormalizeChannel(input));
    }

    [Fact]
    public void Channel_commands_make_probe_explicit()
    {
        Assert.Equal("MEASURE:CHANNEL? 1", F100Protocol.BuildMeasureChannelCommand("A"));
        Assert.Equal("MEASURE:CHANNEL? 2", F100Protocol.BuildMeasureChannelCommand("B"));
        Assert.Equal("MEASURE:CHANNEL? -", F100Protocol.BuildMeasureChannelCommand("A-B"));
        Assert.Equal("CONFIGURE:CHANNEL A", F100Protocol.BuildConfigureChannelCommand("A"));
        Assert.Equal(new[] { "A", "B" }, F100Protocol.ProbeChannels);
    }

    [Theory]
    [InlineData("E4")]
    [InlineData("E5 invalid command")]
    [InlineData("-200")]
    [InlineData("'ERR CMD")]
    [InlineData("1,NoProbe,\"CEL\"")]
    [InlineData("2,No Probe,\"CEL\"")]
    public void ParseReading_does_not_treat_instrument_error_as_temperature(string raw)
    {
        Assert.True(F100Protocol.IsErrorResponse(raw));
        Assert.Null(F100Protocol.ParseReading(raw).Temperature);
    }

    [Theory]
    [InlineData("2,24.559,\"CEL\"", 24.559)]
    [InlineData("1,25.103,\"CEL\"", 25.103)]
    public void ParseReading_handles_real_cth7000_channel_frames(string raw, double expected)
    {
        ThermometerReading reading = F100Protocol.ParseReading(raw);

        Assert.NotNull(reading.Temperature);
        Assert.Equal(expected, reading.Temperature!.Value, 3);
        Assert.Equal("°C", reading.Unit);
    }
}
