using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

public class SikaRestApiProtocolTests
{
    [Fact]
    public void BuildGetRegisterUrl_produces_expected_url() =>
        Assert.Equal(
            "http://192.168.0.50:8081/ajax/getRegister?register=TRset_TR",
            SikaRestApiProtocol.BuildGetRegisterUrl("192.168.0.50", 8081, "TRset_TR"));

    [Fact]
    public void BuildSetSpUrl_formats_value_invariantly() =>
        Assert.Equal(
            "http://192.168.0.50:8081/ajax/setSP?value=25.5",
            SikaRestApiProtocol.BuildSetSpUrl("192.168.0.50", 8081, 25.5));

    [Theory]
    [InlineData("getInfoReport", "http://h:8081/ajax/getInfoReport")]
    [InlineData("ajax/getInfoReport", "http://h:8081/ajax/getInfoReport")]
    [InlineData("/ajax/getInfoReport", "http://h:8081/ajax/getInfoReport")]
    public void BuildCommandUrl_adds_ajax_prefix_only_when_missing(string command, string expected) =>
        Assert.Equal(expected, SikaRestApiProtocol.BuildCommandUrl("h", 8081, command));

    [Fact]
    public void ParseRegisterValue_reads_first_value()
    {
        string json = """
            {
                "register": "TRset_TR",
                "values": [
                    { "value": 28.91499, "times": 1693488480 }
                ]
            }
            """;

        Assert.Equal(28.91499, SikaRestApiProtocol.ParseRegisterValue(json));
    }

    [Fact]
    public void ParseRegisterValue_returns_null_for_malformed_json() =>
        Assert.Null(SikaRestApiProtocol.ParseRegisterValue("<html>not json</html>"));

    [Fact]
    public void ParseRegisterValue_returns_null_when_values_missing() =>
        Assert.Null(SikaRestApiProtocol.ParseRegisterValue("{\"register\":\"TRset_TR\",\"values\":[]}"));

    [Fact]
    public void ParseSetSpResponse_returns_applied_value_on_success()
    {
        string json = "{ \"value\": \"success\", \"info\": \"25.500000\" }";
        Assert.Equal(25.5, SikaRestApiProtocol.ParseSetSpResponse(json));
    }

    [Fact]
    public void ParseSetSpResponse_throws_when_not_success()
    {
        string json = "{ \"value\": \"error\", \"info\": \"Set point outside valid range\" }";
        Assert.Throws<InvalidOperationException>(() => SikaRestApiProtocol.ParseSetSpResponse(json));
    }

    [Fact]
    public void ParseSetSpResponse_throws_on_malformed_json() =>
        Assert.Throws<InvalidOperationException>(() => SikaRestApiProtocol.ParseSetSpResponse("not json"));
}
