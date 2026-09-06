using System.Text.Json;
using VotschVc3.Core.Communication.PolEko;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class PolEkoLabDeskProtocolTests
{
    [Theory]
    [InlineData("{\"TEMPERATURE_MAIN\":24.23}", 24.23)]
    [InlineData("{\"TEMPERATURE_MAIN\":{\"valueProbe\":24.23}}", 24.23)]
    [InlineData("{\"temperatureMain\":\"24.23\"}", 24.23)]
    public void Main_temperature_accepts_real_and_legacy_status_shapes(string json, double expected)
    {
        using JsonDocument status = JsonDocument.Parse(json);

        Assert.True(PolEkoLabDeskProtocol.TryReadMainTemperature(status.RootElement, out double actual));
        Assert.Equal(expected, actual, precision: 6);
    }

    [Fact]
    public void Main_temperature_rejects_missing_or_non_finite_value()
    {
        using JsonDocument status = JsonDocument.Parse("{\"TEMPERATURE_MAIN\":\"NaN\"}");

        Assert.False(PolEkoLabDeskProtocol.TryReadMainTemperature(status.RootElement, out _));
    }

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
