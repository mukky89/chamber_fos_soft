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
    public void BuildGradientInfoUrl_produces_expected_url() =>
        Assert.Equal(
            "http://10.88.6.28:80/ajax/getGradientInfo",
            SikaRestApiProtocol.BuildGradientInfoUrl("10.88.6.28", 80));

    [Fact]
    public void ParseGradientInfo_reads_reference_setpoint_and_flags()
    {
        // Trimmed real payload from a TP37200E.2 (10.88.6.28) getGradientInfo response.
        string json = """
            {"Gradient_TR":-0.000002,"StepTest_State":5,"TR":-19.999613,"SP":-20.000000,
             "Stable":2,"heatingMode":0,"heatingON":1,"systemState":2,"MaxTemp":200.000000}
            """;

        SikaRestApiProtocol.SikaGradientInfo? info = SikaRestApiProtocol.ParseGradientInfo(json);

        Assert.NotNull(info);
        Assert.Equal(-19.999613, info!.Value.ReferenceTemperature);
        Assert.Equal(-20.0, info.Value.Setpoint);
        Assert.True(info.Value.HeatingOn);
        Assert.Equal(2, info.Value.SystemState);
    }

    [Fact]
    public void ParseGradientInfo_returns_null_without_TR() =>
        Assert.Null(SikaRestApiProtocol.ParseGradientInfo("{\"SP\":25.0,\"Stable\":2}"));

    [Fact]
    public void ParseGradientInfo_returns_null_for_malformed_json() =>
        Assert.Null(SikaRestApiProtocol.ParseGradientInfo("<html>not json</html>"));

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

    [Fact]
    public void BuildSetRegisterUrl_formats_register_and_value() =>
        Assert.Equal(
            "http://10.88.5.81:80/ajax/setRegister?register=TRset_SP&value=40",
            SikaRestApiProtocol.BuildSetRegisterUrl("10.88.5.81", 80, SikaRestApiProtocol.SetpointRegister, 40));

    [Fact]
    public void ParseSetRegisterResponse_returns_written_value_on_success()
    {
        // Real success payload from a TP3M165E.2 setRegister write.
        string json = "{\"value\":\"success\",\"info\":\"value 40.000000 wrote to register TRset_SP\"}";
        Assert.Equal(40.0, SikaRestApiProtocol.ParseSetRegisterResponse(json));
    }

    [Fact]
    public void ParseSetRegisterResponse_throws_when_not_success() =>
        Assert.Throws<InvalidOperationException>(() =>
            SikaRestApiProtocol.ParseSetRegisterResponse("{\"value\":\"failure\",\"info\":\"could not write register\"}"));

    [Fact]
    public void ParseSetRegisterResponse_throws_on_malformed_json() =>
        Assert.Throws<InvalidOperationException>(() => SikaRestApiProtocol.ParseSetRegisterResponse("not json"));
}
