using System.Net;
using System.Text;
using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class SylexFosApiClientTests
{
    [Fact]
    public async Task Calibration_context_uses_api_key_and_deserializes_stable_contract()
    {
        string envName = $"SYLEX_FOS_API_KEY_TEST_{Guid.NewGuid():N}";
        string? original = Environment.GetEnvironmentVariable(envName);
        Environment.SetEnvironmentVariable(envName, "test-secret");
        try
        {
            var handler = new StubHandler(request =>
            {
                Assert.Equal("test-secret", request.Headers.GetValues("X-API-Key").Single());
                Assert.True(request.Headers.Contains("X-Correlation-ID"));
                Assert.Equal("/api/v1/calibrations/fbg/context", request.RequestUri?.AbsolutePath);
                Assert.Equal("?serialNumber=123456%2F0001", request.RequestUri?.Query);

                const string json = """
                {
                  "serialNumber": "123456/0001",
                  "productId": "123456",
                  "productDescription": "Temperature sensor assembly",
                  "sensorName": "FBG temperature sensor",
                  "customerCode": "CUST-A",
                  "orderNumber": null,
                  "source": "ISYS product lookup",
                  "retrievedAtUtc": "2026-08-30T00:00:00Z"
                }
                """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            });

            using var http = new HttpClient(handler);
            using var client = new SylexFosApiClient(new SylexFosApiSettings
            {
                BaseUrl = "http://localhost:5080",
                ApiKeyEnvironmentVariable = envName,
            }, http);

            SylexFbgCalibrationContext? context = await client.GetFbgCalibrationContextAsync("123456/0001");
            Assert.NotNull(context);
            Assert.Equal("123456", context.ProductId);
            Assert.Equal("Temperature sensor assembly", context.ProductDescription);
            Assert.Equal("FBG temperature sensor", context.SensorName);
            Assert.Null(context.OrderNumber);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, original);
        }
    }

    [Fact]
    public async Task Provider_maps_context_to_existing_production_metadata_contract()
    {
        var fake = new FakeClient(new SylexFbgCalibrationContext(
            "123456/0001",
            "123456",
            "Temperature sensor assembly",
            "FBG temperature sensor",
            "CUST-A",
            null,
            null,
            null,
            "ISYS product lookup",
            DateTimeOffset.UtcNow));

        using var provider = new SylexFosApiProductionMetadataProvider(fake);
        ProductionMetadata? metadata = await provider.FindAsync("123456/0001", "1");

        Assert.NotNull(metadata);
        Assert.Equal("Temperature sensor assembly", metadata.ProductDescription);
        Assert.Equal("FBG temperature sensor", metadata.SensorName);
        Assert.Equal(string.Empty, metadata.Order);
        Assert.Contains("Sylex FOS API", metadata.Notes);
        Assert.Equal(1, fake.RequestCount);

        _ = await provider.FindAsync("123456/0001", "2");
        Assert.Equal(1, fake.RequestCount);
    }

    [Fact]
    public async Task Health_check_does_not_require_api_key()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal("/health", request.RequestUri?.AbsolutePath);
            Assert.False(request.Headers.Contains("X-API-Key"));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"healthy\"}", Encoding.UTF8, "application/json"),
            };
        });

        using var http = new HttpClient(handler);
        using var client = new SylexFosApiClient(new SylexFosApiSettings
        {
            BaseUrl = "http://localhost:5080",
            ApiKeyEnvironmentVariable = "UNUSED_FOR_HEALTH",
        }, http);

        SylexFosApiHealth health = await client.CheckHealthAsync();
        Assert.True(health.IsReachable);
        Assert.Equal("healthy", health.Status);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class FakeClient(SylexFbgCalibrationContext context) : ISylexFosApiClient
    {
        public int RequestCount { get; private set; }
        public Task<SylexFosApiHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SylexFosApiHealth(true, "healthy", DateTimeOffset.UtcNow));
        public Task<SylexFbgCalibrationContext?> GetFbgCalibrationContextAsync(string serialNumber, CancellationToken cancellationToken = default)
        {
            RequestCount++;
            return Task.FromResult<SylexFbgCalibrationContext?>(context);
        }
    }
}
