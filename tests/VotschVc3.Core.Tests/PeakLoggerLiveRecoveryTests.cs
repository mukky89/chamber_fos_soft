using System.Net;
using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public class PeakLoggerLiveRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutageAndFailedReconnectRecoverAtOriginalEndpoint(bool timeout)
    {
        var handler = new Handler();
        using var http = new HttpClient(handler);
        await using var client = new PeakLoggerApiClient(http);
        var settings = new PeakLoggerSettings { Host = "localhost", Port = 43124 };
        await client.ConnectAsync(settings);
        var before = Assert.Single(await PeakLoggerLiveRecovery.ReadAsync(client, settings, default));

        handler.Failure = timeout ? new TaskCanceledException("request timeout") : new HttpRequestException("offline");
        await Assert.ThrowsAnyAsync<Exception>(() => PeakLoggerLiveRecovery.ReadAsync(client, settings, default));
        Assert.False(client.IsConnected);
        await Assert.ThrowsAnyAsync<Exception>(() => PeakLoggerLiveRecovery.ReadAsync(client, settings, default));
        Assert.False(client.IsConnected);

        handler.Failure = null;
        var after = Assert.Single(await PeakLoggerLiveRecovery.ReadAsync(client, settings, default));
        Assert.True(client.IsConnected);
        Assert.Equal(before.PeakId, after.PeakId);
        Assert.Equal(before.SerialNumber, after.SerialNumber);
        Assert.All(handler.Requests, uri =>
        {
            Assert.Equal(43124, uri.Port);
            Assert.Equal("localhost", uri.Host);
            Assert.Equal("/api/v1/peaks", uri.AbsolutePath);
        });
    }

    [Fact]
    public async Task CancelledMonitorDoesNotReconnect()
    {
        var handler = new Handler();
        using var http = new HttpClient(handler);
        await using var client = new PeakLoggerApiClient(http);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PeakLoggerLiveRecovery.ReadAsync(client, new PeakLoggerSettings(), cts.Token));
        Assert.Empty(handler.Requests);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public Exception? Failure { get; set; }
        public List<Uri> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (Failure is not null) return Task.FromException<HttpResponseMessage>(Failure);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"index":1,"channel":"3.2","wavelength":1550.1,"device":{"deviceSN":"LOGGER"}}]""")
            });
        }
    }
}
