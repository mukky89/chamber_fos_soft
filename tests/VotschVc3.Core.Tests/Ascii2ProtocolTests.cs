using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

public class Ascii2ProtocolTests
{
    [Theory]
    [InlineData(50.0, "0050.0")]
    [InlineData(0.0, "0000.0")]
    [InlineData(23.4, "0023.4")]
    [InlineData(-40.0, "-040.0")]
    [InlineData(-5.5, "-005.5")]
    [InlineData(125.0, "0125.0")]
    public void FormatValue_matches_fixed_width_layout(double value, string expected) =>
        Assert.Equal(expected, Ascii2Protocol.FormatValue(value));

    [Fact]
    public void FormatAddress_pads_to_two_digits() =>
        Assert.Equal("01", Ascii2Protocol.FormatAddress(1));

    [Fact]
    public void BuildReadCommand_produces_expected_frame() =>
        Assert.Equal("$01I\r", Ascii2Protocol.BuildReadCommand(1));

    [Fact]
    public void BuildWriteCommand_includes_setpoints_and_digital_block()
    {
        var digital = new DigitalChannels { StartChannelIndex = 0, Start = true };
        string frame = Ascii2Protocol.BuildWriteCommand(
            address: 1,
            setpoints: new[] { 50.0, 0.0 },
            digital: digital,
            analogChannelCount: 6);

        Assert.Equal(
            "$01E 0050.0 0000.0 0000.0 0000.0 0000.0 0000.0 10000000000000000000000000000000\r",
            frame);
    }

    [Fact]
    public void BuildRawCommand_appends_payload_with_space()
    {
        string frame = Ascii2Protocol.BuildRawCommand(1, 'P', "0001");
        Assert.Equal("$01P 0001\r", frame);
    }

    [Fact]
    public void ParseReading_extracts_analog_and_digital_values()
    {
        // Measured/setpoint pairs for temperature and humidity, then the digital block.
        string raw = "0024.5 0025.0 0048.0 0050.0 10000000000000000000000000000000";

        ChamberReading reading = Ascii2Protocol.ParseReading(raw);

        Assert.Equal(24.5, reading.Temperature);
        Assert.Equal(25.0, reading.TemperatureSetpoint);
        Assert.Equal(48.0, reading.Humidity);
        Assert.Equal(50.0, reading.HumiditySetpoint);
        Assert.True(reading.DigitalChannels[0]);
        Assert.False(reading.DigitalChannels[1]);
        Assert.Equal(4, reading.AnalogValues.Count);
    }

    [Fact]
    public void ParseReading_tolerates_echoed_address_and_negative_values()
    {
        string raw = "01 -040.0 -040.0 00000000000000000000000000000000";

        ChamberReading reading = Ascii2Protocol.ParseReading(raw);

        Assert.Equal(-40.0, reading.Temperature);
        Assert.Equal(-40.0, reading.TemperatureSetpoint);
        Assert.False(reading.DigitalChannels.Start);
    }

    [Fact]
    public void DigitalChannels_round_trip_through_protocol_string()
    {
        var channels = new DigitalChannels();
        channels[0] = true;
        channels[31] = true;

        string text = channels.ToProtocolString();
        Assert.Equal(32, text.Length);
        Assert.Equal('1', text[0]);
        Assert.Equal('1', text[31]);

        DigitalChannels parsed = DigitalChannels.Parse(text);
        Assert.True(parsed[0]);
        Assert.True(parsed[31]);
        Assert.False(parsed[15]);
    }
}
