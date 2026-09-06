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
    public void Authenticated_request_uses_admin_credentials()
    {
        using JsonDocument json = JsonDocument.Parse(
            PolEkoLabDeskProtocol.BuildRequest("GET_PROGRAMS", null, true));

        JsonElement credential = json.RootElement.GetProperty("userCredential");
        Assert.Equal("admin", credential.GetProperty("username").GetString());
        Assert.Equal("admin", credential.GetProperty("password").GetString());
    }

    [Fact]
    public void Authenticated_request_accepts_locally_configured_device_credentials()
    {
        using JsonDocument json = JsonDocument.Parse(
            PolEkoLabDeskProtocol.BuildRequest(
                "GET_PROGRAMS", null, true, username: "device-user", password: "device-secret"));

        JsonElement credential = json.RootElement.GetProperty("userCredential");
        Assert.Equal("device-user", credential.GetProperty("username").GetString());
        Assert.Equal("device-secret", credential.GetProperty("password").GetString());
    }

    [Fact]
    public void Diagnostic_request_redacts_password()
    {
        using JsonDocument json = JsonDocument.Parse(
            PolEkoLabDeskProtocol.BuildRequest("STOP", null, true, redactPassword: true));

        JsonElement credential = json.RootElement.GetProperty("userCredential");
        Assert.Equal("admin", credential.GetProperty("username").GetString());
        Assert.Equal("***", credential.GetProperty("password").GetString());
    }

    [Fact]
    public void Diagnostic_request_never_logs_locally_configured_password()
    {
        string request = PolEkoLabDeskProtocol.BuildRequest(
            "STOP", null, true, redactPassword: true, username: "device-user", password: "device-secret");

        Assert.DoesNotContain("device-secret", request, StringComparison.Ordinal);
        Assert.Contains("\"password\":\"***\"", request, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(25.0, 25.0)]
    [InlineData(-20.0, -20.0)]
    [InlineData(20.05, 20.05)]
    public void Program_uses_celsius_temperature_and_dotted_edge_fields(double celsius, double expected)
    {
        using JsonDocument json = JsonDocument.Parse(
            PolEkoLabDeskProtocol.BuildSingleSetpointProgram(17, celsius));

        JsonElement segment = json.RootElement.GetProperty("segments")[0];
        Assert.Equal(expected, segment.GetProperty("temperature").GetDouble(), precision: 6);
        Assert.True(segment.GetProperty("IsInfinityEnabled").GetBoolean());
        Assert.True(segment.TryGetProperty("edge.duration", out _));
        Assert.True(segment.TryGetProperty("edge.fan", out _));
        Assert.True(segment.TryGetProperty("edge.airFlap", out _));
        Assert.True(segment.TryGetProperty("edge.enable", out _));
    }

    [Fact]
    public void Manual_program_uses_existing_fos_lab_profile_and_infinite_hold()
    {
        using JsonDocument json = JsonDocument.Parse(
            PolEkoLabDeskProtocol.BuildSingleSetpointProgram(PolEkoClient.ManualProgramId, 25));

        Assert.Equal(11, json.RootElement.GetProperty("programId").GetInt64());
        Assert.Equal("FOS LAB", json.RootElement.GetProperty("name").GetString());
        Assert.True(json.RootElement.GetProperty("segments")[0].GetProperty("IsInfinityEnabled").GetBoolean());
    }

    [Fact]
    public void Manual_program_uses_requested_temperature_protection()
    {
        using JsonDocument json = JsonDocument.Parse(
            PolEkoLabDeskProtocol.BuildSingleSetpointProgram(PolEkoClient.ManualProgramId, 20, -45, 190));

        JsonElement protection = json.RootElement.GetProperty("tempProtection");
        Assert.Equal(-45, protection.GetProperty("underTemperatureLimit").GetDouble());
        Assert.Equal(190, protection.GetProperty("overTemperatureLimit").GetDouble());
    }

    [Fact]
    public void Program_catalog_temperature_is_read_back_in_celsius()
    {
        const string programs = "[{\"programId\":11,\"segments\":[{\"temperature\":20}]}]";

        Assert.True(PolEkoLabDeskProtocol.TryReadProgramTemperature(programs, 11, out double temperature));
        Assert.Equal(20, temperature);
    }

    [Fact]
    public void Program_catalog_reports_count_and_preserves_all_program_fields()
    {
        string result = PolEkoLabDeskProtocol.FormatProgramCatalog(
            "[{\"programId\":1,\"name\":\"A\",\"segments\":[]},{\"programId\":99,\"name\":\"LabControl MANUAL\",\"segments\":[{\"temperature\":250}]}]");

        Assert.Contains("POL-EKO programy: 2", result);
        Assert.Contains("\"programId\": 99", result);
        Assert.Contains("\"temperature\": 250", result);
    }

    [Theory]
    [InlineData("DATA_CORRUPTED", true)]
    [InlineData("data_corrupted", true)]
    [InlineData("GENERAL_ERROR", false)]
    [InlineData("OK", false)]
    public void Only_broken_program_catalog_status_is_treated_as_unavailable(string status, bool expected)
    {
        Assert.Equal(expected, PolEkoClient.IsUnavailableProgramCatalog(status));
    }
}
