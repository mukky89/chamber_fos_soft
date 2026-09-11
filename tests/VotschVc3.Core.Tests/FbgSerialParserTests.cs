using VotschVc3.Core.Calibration;
using Xunit;
namespace VotschVc3.Core.Tests;
public class FbgSerialParserTests
{
    [Theory]
    [InlineData("291875/0002", true)]
    [InlineData("291875/00O2", false)]
    [InlineData("291875/002", false)]
    [InlineData("291875/00022", false)]
    [InlineData("291875-0002", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("291875A000002", true)]
    public void PairingValidatesCanonicalSerialAfterBarcodeParsing(string? input, bool valid) =>
        Assert.Equal(valid, FbgSerialParser.IsProductionSerial(FbgSerialParser.Parse(input)));

    [Theory]
    [InlineData("297970A000002", "297970/0002")]
    [InlineData("297970B000002", "297970/0002")]
    [InlineData("297970X000002", "297970/0002")]
    [InlineData(" 297970a000002\r\n", "297970/0002")]
    [InlineData("297970/0002", "297970/0002")]
    [InlineData("297970A001234", "297970/1234")]
    [InlineData("297970A123456", "297970A123456")]
    [InlineData("297970A0000027", "297970A0000027")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ParsesWithoutDiscardingSignificantDigits(string? input, string expected) =>
        Assert.Equal(expected, FbgSerialParser.Parse(input));
}
