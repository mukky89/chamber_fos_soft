using System.Net;
using System.Text;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Communication.Sika;
using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

public class SikaTpClientTests
{
    /// <summary>
    /// A TP37200E.2 answers getGradientInfo, so ReadAsync must use it (one call) for
    /// TR / SP and never touch the per-register endpoints.
    /// </summary>
    [Fact]
    public async Task ReadAsync_prefers_getGradientInfo_when_available()
    {
        var handler = new RouteHandler
        {
            ["ajax/getInfoReport"] = ("{\"Device\":\"TP37200E.2\"}", HttpStatusCode.OK),
            ["ajax/getGradientInfo"] = (
                "{\"TR\":-19.999613,\"SP\":-20.0,\"Stable\":2,\"heatingON\":1,\"systemState\":2}",
                HttpStatusCode.OK),
        };

        await using var client = new SikaTpClient(_ => new HttpClient(handler));
        await client.ConnectAsync(new ChamberConnectionSettings { Host = "10.88.6.28", Port = 80 });

        ChamberReading reading = await client.ReadAsync();

        Assert.Equal(-19.999613, reading.Temperature);
        Assert.Equal(-20.0, reading.TemperatureSetpoint);
        Assert.DoesNotContain(handler.Requested, u => u.Contains("getRegister"));
    }

    /// <summary>
    /// Older TP Premium firmware has no getGradientInfo (HTTP 404), so ReadAsync must
    /// fall back to the two getRegister reads.
    /// </summary>
    [Fact]
    public async Task ReadAsync_falls_back_to_registers_when_gradient_missing()
    {
        var handler = new RouteHandler
        {
            ["ajax/getInfoReport"] = ("{}", HttpStatusCode.OK),
            ["ajax/getGradientInfo"] = ("not found", HttpStatusCode.NotFound),
            ["ajax/getRegister?register=TRset_TR"] =
                ("{\"register\":\"TRset_TR\",\"values\":[{\"value\":28.9,\"times\":1}]}", HttpStatusCode.OK),
            ["ajax/getRegister?register=TRset_SP"] =
                ("{\"register\":\"TRset_SP\",\"values\":[{\"value\":25.0,\"times\":1}]}", HttpStatusCode.OK),
        };

        await using var client = new SikaTpClient(_ => new HttpClient(handler));
        await client.ConnectAsync(new ChamberConnectionSettings { Host = "host", Port = 8081 });

        ChamberReading reading = await client.ReadAsync();

        Assert.Equal(28.9, reading.Temperature);
        Assert.Equal(25.0, reading.TemperatureSetpoint);
        Assert.Contains(handler.Requested, u => u.Contains("getRegister?register=TRset_TR"));
    }

    /// <summary>Routes canned responses by the request URL's ajax command; records every request.</summary>
    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (string Body, HttpStatusCode Status)> _routes = new();

        public List<string> Requested { get; } = new();

        public (string Body, HttpStatusCode Status) this[string ajaxCommand]
        {
            set => _routes[ajaxCommand] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            Requested.Add(url);

            var match = _routes.FirstOrDefault(kvp => url.EndsWith(kvp.Key, StringComparison.Ordinal));
            (string body, HttpStatusCode status) = match.Key is null ? ("not found", HttpStatusCode.NotFound) : match.Value;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
