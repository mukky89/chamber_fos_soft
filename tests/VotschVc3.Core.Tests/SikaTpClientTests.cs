using System.Net;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Communication.Sika;
using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

public class SikaTpClientTests
{
    private const string RegisterJson = "{\"register\":\"TRset_TR\",\"values\":[{\"value\":24.5,\"times\":1}]}";
    private const string SetpointJson = "{\"register\":\"TRset_SP\",\"values\":[{\"value\":60.0,\"times\":1}]}";
    private const string SetSpOkJson = "{\"value\":\"success\",\"info\":\"60.000000\"}";

    /// <summary>Fake HTTP handler: routes each request URL through a delegate.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _respond;
        public List<string> Requests { get; } = new();

        public FakeHandler(Func<string, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            Requests.Add(url);
            return Task.FromResult(_respond(url));
        }
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static SikaTpClient CreateClient(FakeHandler handler) =>
        new(_ => new HttpClient(handler));

    private static ChamberConnectionSettings Settings() => new() { Host = "10.88.5.81", Port = 8081 };

    [Fact]
    public async Task Connect_probes_with_cheap_getRegister_not_getInfoReport()
    {
        var handler = new FakeHandler(_ => Ok(RegisterJson));
        await using var client = CreateClient(handler);

        await client.ConnectAsync(Settings());

        Assert.True(client.IsConnected);
        Assert.Contains("getRegister?register=TRset_TR", handler.Requests[0]);
        Assert.DoesNotContain(handler.Requests, r => r.Contains("getInfoReport"));
    }

    [Fact]
    public async Task Connect_retries_a_sporadic_embedded_server_404()
    {
        int calls = 0;
        var handler = new FakeHandler(_ => ++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Ok(RegisterJson));
        await using var client = CreateClient(handler);

        await client.ConnectAsync(Settings());

        Assert.True(client.IsConnected);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Connect_rejects_a_server_that_is_not_the_rest_api()
    {
        var handler = new FakeHandler(_ => Ok("<html>WebApp</html>"));
        await using var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync(Settings()));

        Assert.False(client.IsConnected);
        Assert.Contains("REST-API", ex.Message);
    }

    [Fact]
    public async Task WriteSetpoints_sends_setSP_and_verifies_readback()
    {
        var handler = new FakeHandler(url =>
            url.Contains("setSP") ? Ok(SetSpOkJson)
            : url.Contains("TRset_SP") ? Ok(SetpointJson)
            : Ok(RegisterJson));
        await using var client = CreateClient(handler);
        await client.ConnectAsync(Settings());

        await client.WriteSetpointsAsync(new[] { 60.0 }, new DigitalChannels());

        Assert.Contains(handler.Requests, r => r.Contains("setSP?value=60"));
        Assert.Contains(handler.Requests, r => r.Contains("getRegister?register=TRset_SP"));
    }

    [Fact]
    public async Task WriteSetpoints_reports_old_firmware_when_setSP_is_unknown()
    {
        var handler = new FakeHandler(url =>
            url.Contains("setSP")
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Ok(RegisterJson));
        await using var client = CreateClient(handler);
        await client.ConnectAsync(Settings());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.WriteSetpointsAsync(new[] { 60.0 }, new DigitalChannels()));

        Assert.Contains("30.35", ex.Message);
    }

    [Fact]
    public async Task WriteSetpoints_fails_when_device_acknowledges_but_keeps_old_setpoint()
    {
        const string staleSetpoint = "{\"register\":\"TRset_SP\",\"values\":[{\"value\":25.0,\"times\":1}]}";
        var handler = new FakeHandler(url =>
            url.Contains("setSP") ? Ok(SetSpOkJson)
            : url.Contains("TRset_SP") ? Ok(staleSetpoint)
            : Ok(RegisterJson));
        await using var client = CreateClient(handler);
        await client.ConnectAsync(Settings());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.WriteSetpointsAsync(new[] { 60.0 }, new DigitalChannels()));

        Assert.Contains("zápis sa nevykonal", ex.Message);
    }
}
