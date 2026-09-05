using System.Text.Json;
using VotschVc3.Core.Communication.PolEko;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class PolEkoLabDeskProtocolTests
{
    [Fact]
    public void Status_request_uses_camel_case_and_version_2()
    {
        using JsonDocument json = JsonDocument.Parse(
            PolEkoLabDeskProtocol.BuildRequest("GET_STATUS", "version-2", false));

        Assert.Equal("GET_STATUS", json.RootElement.GetProperty("requestCommand").GetString());
        Assert.Equal("version-2", json.RootElement.GetProperty("data").GetString());
        Assert.False(json.RootElement.TryGetProperty("RequestCommand", out _));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("userCredential").ValueKind);
    }

    [Fact]
    public void Authenticated_request_uses_admin_empty_password()
    {
        using JsonDocument json = JsonDocument.Parse(
            PolEkoLabDeskProtocol.BuildRequest("GET_PROGRAMS", null, true));

        JsonElement credential = json.RootElement.GetProperty("userCredential");
        Assert.Equal("admin", credential.GetProperty("username").GetString());
        Assert.Equal(string.Empty, credential.GetProperty("password").GetString());
    }

    [Theory]
    [InlineData(25.0, 250)]
    [InlineData(-20.0, -200)]
    [InlineData(20.05, 201)]
    public void Program_scales_temperature_and_uses_dotted_edge_fields(double celsius, int expected)
    {
        using JsonDocument json = JsonDocument.Parse(
            PolEkoLabDeskProtocol.BuildSingleSetpointProgram(17, celsius));

        JsonElement segment = json.RootElement.GetProperty("segments")[0];
        Assert.Equal(expected, segment.GetProperty("temperature").GetInt32());
        Assert.True(segment.GetProperty("IsInfinityEnabled").GetBoolean());
        Assert.True(segment.TryGetProperty("edge.duration", out _));
        Assert.True(segment.TryGetProperty("edge.fan", out _));
        Assert.True(segment.TryGetProperty("edge.airFlap", out _));
        Assert.True(segment.TryGetProperty("edge.enable", out _));
    }
}
