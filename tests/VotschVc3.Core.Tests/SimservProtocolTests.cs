using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

public class SimservProtocolTests
{
    [Fact]
    public void BuildSetNominalValue_matches_manual_example()
    {
        // Manual: "Set the setpoint temperature of the 2nd test system to 25 °C"
        //   11001 ¶ 2 ¶ 1 ¶ 25.0 CR
        Assert.Equal(
            "11001¶2¶1¶25.0\r",
            SimservProtocol.BuildSetNominalValue(simpatiId: 2, index: 1, value: 25.0));
    }

    [Fact]
    public void BuildGetActualValue_has_command_id_and_variable_index()
    {
        Assert.Equal("11004¶2¶1\r", SimservProtocol.BuildGetActualValue(2, 1));
    }

    [Fact]
    public void BuildSetDigitalOut_emits_one_or_zero()
    {
        // Manual: "Set digital channel 1 (start)" -> 14001 ¶ id ¶ 1 ¶ 1
        Assert.Equal("14001¶1¶1¶1\r", SimservProtocol.BuildSetDigitalOut(1, 1, on: true));
        Assert.Equal("14001¶1¶1¶0\r", SimservProtocol.BuildSetDigitalOut(1, 1, on: false));
    }

    [Fact]
    public void Separator_is_ascii_182()
    {
        Assert.Equal(182, (int)SimservProtocol.Separator);
    }

    [Theory]
    [InlineData("1¶23.90", true, 23.90)]
    [InlineData("1", true, null)]
    [InlineData("-4", false, null)]
    public void ParseResponse_reads_status_and_value(string raw, bool success, double? value)
    {
        Assert.Equal(success, SimservProtocol.IsSuccess(raw));
        Assert.Equal(value, SimservProtocol.FirstValue(raw));
    }
}
