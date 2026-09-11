using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using VotschVc3.Core.Calibration;
using Xunit;
namespace VotschVc3.Core.Tests;
public class PeakLoggerChannelLabelTests
{
    [Fact]
    public async Task DiscoveryCountsNineChannelsOnOneInterrogator()
    {
        var payload = JsonSerializer.Serialize(Enumerable.Range(1, 9).SelectMany(channel =>
            Enumerable.Range(1, 2).Select(index => new { channel = channel.ToString(), index, wavelength = 1510 + channel + index * 0.1, device = new { deviceSN = "ONE-LOGGER" } })));
        using var http = new HttpClient(new Handler(payload));
        var probe = typeof(PeakLoggerApiClient).GetMethod("ProbeInstanceAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = await (Task<PeakLoggerApiClient.DiscoveredInstance?>)probe.Invoke(null, new object[] { http, "localhost", 43124, CancellationToken.None })!;
        Assert.NotNull(result);
        Assert.Equal(9, result.ChannelCount);
        Assert.Equal(18, result.PeakCount);
        Assert.Contains("9 aktívnych kanálov", result.Display);
        Assert.DoesNotContain("interrogátorov", result.Display);
    }
    private sealed class Handler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") });
    }
}
