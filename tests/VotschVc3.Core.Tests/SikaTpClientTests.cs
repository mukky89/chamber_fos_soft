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
            ["ajax/getRegister?register=TRset_TR"] =
                ("{\"register\":\"TRset_TR\",\"values\":[{\"value\":21.0,\"times\":1}]}", HttpStatusCode.OK),
            ["ajax/getGradientInfo"] = (
                "{\"TR\":-19.999613,\"SP\":-20.0,\"Stable\":2,\"heatingON\":1,\"systemState\":2}",
                HttpStatusCode.OK),
        };

        await using var client = new SikaTpClient(_ => new HttpClient(handler));
        await client.ConnectAsync(new ChamberConnectionSettings { Host = "10.88.6.28", Port = 80 });

        // Connect probes a register itself, so only the reads issued by ReadAsync count.
        int afterConnect = handler.Requested.Count;
        ChamberReading reading = await client.ReadAsync();

        Assert.Equal(-19.999613, reading.Temperature);
        Assert.Equal(-20.0, reading.TemperatureSetpoint);
        Assert.DoesNotContain(handler.Requested.Skip(afterConnect), u => u.Contains("getRegister"));
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

    /// <summary>
    /// A set point write must use setRegister (Task_SetPointList then TRset_SP), matching
    /// the device's own web UI – not the older setSP command. When the controller is
    /// already on, no START is issued.
    /// </summary>
    [Fact]
    public async Task WriteSetpointsAsync_writes_setpoint_via_setRegister_without_restart_when_running()
    {
        var handler = new RouteHandler
        {
            ["ajax/getInfoReport"] = ("{}", HttpStatusCode.OK),
            ["ajax/getRegister?register=TRset_TR"] =
                ("{\"register\":\"TRset_TR\",\"values\":[{\"value\":21.0,\"times\":1}]}", HttpStatusCode.OK),
            ["ajax/getRegister?register=System_ReglerOnOff"] =
                ("{\"register\":\"System_ReglerOnOff\",\"values\":[{\"value\":1.0,\"times\":1}]}", HttpStatusCode.OK),
            ["ajax/setRegister?register=Task_SetPointList&value=40"] =
                ("{\"value\":\"success\",\"info\":\"value 40.000000 wrote to register Task_SetPointList\"}", HttpStatusCode.OK),
            ["ajax/setRegister?register=TRset_SP&value=40"] =
                ("{\"value\":\"success\",\"info\":\"value 40.000000 wrote to register TRset_SP\"}", HttpStatusCode.OK),
            ["ajax/getRegister?register=TRset_SP"] =
                ("{\"register\":\"TRset_SP\",\"values\":[{\"value\":40.0,\"times\":1}]}", HttpStatusCode.OK),
        };

        await using var client = new SikaTpClient(_ => new HttpClient(handler));
        await client.ConnectAsync(new ChamberConnectionSettings { Host = "10.88.5.81", Port = 80 });

        await client.WriteSetpointsAsync(new[] { 40.0 }, new DigitalChannels { Start = true });

        Assert.Contains(handler.Requested, u => u.EndsWith("setRegister?register=Task_SetPointList&value=40"));
        Assert.Contains(handler.Requested, u => u.EndsWith("setRegister?register=TRset_SP&value=40"));
        Assert.DoesNotContain(handler.Requested, u => u.Contains("setSP"));
        Assert.DoesNotContain(handler.Requested, u => u.Contains("startCurrentTask"));
    }

    /// <summary>
    /// When the controller is off, a set point write with the start channel set must run
    /// the verified START (startCurrentTask + System_ReglerOnOff=1) so the value takes effect.
    /// </summary>
    [Fact]
    public async Task WriteSetpointsAsync_starts_task_when_controller_off()
    {
        var handler = new RouteHandler
        {
            ["ajax/getInfoReport"] = ("{}", HttpStatusCode.OK),
            ["ajax/getRegister?register=TRset_TR"] =
                ("{\"register\":\"TRset_TR\",\"values\":[{\"value\":21.0,\"times\":1}]}", HttpStatusCode.OK),
            ["ajax/getRegister?register=System_ReglerOnOff"] =
                ("{\"register\":\"System_ReglerOnOff\",\"values\":[{\"value\":0.0,\"times\":1}]}", HttpStatusCode.OK),
            ["ajax/setRegister?register=Task_SetPointList&value=100"] =
                ("{\"value\":\"success\",\"info\":\"value 100.000000 wrote to register Task_SetPointList\"}", HttpStatusCode.OK),
            ["ajax/setRegister?register=TRset_SP&value=100"] =
                ("{\"value\":\"success\",\"info\":\"value 100.000000 wrote to register TRset_SP\"}", HttpStatusCode.OK),
            ["ajax/getRegister?register=TRset_SP"] =
                ("{\"register\":\"TRset_SP\",\"values\":[{\"value\":100.0,\"times\":1}]}", HttpStatusCode.OK),
            ["ajax/startCurrentTask"] = ("{\"value\":\"success\",\"info\":\"current task started\"}", HttpStatusCode.OK),
            ["ajax/setRegister?register=System_ReglerOnOff&value=1"] =
                ("{\"value\":\"success\",\"info\":\"value 1.000000 wrote to register System_ReglerOnOff\"}", HttpStatusCode.OK),
        };

        await using var client = new SikaTpClient(_ => new HttpClient(handler));
        await client.ConnectAsync(new ChamberConnectionSettings { Host = "10.88.5.81", Port = 80 });

        await client.WriteSetpointsAsync(new[] { 100.0 }, new DigitalChannels { Start = true });

        Assert.Contains(handler.Requested, u => u.EndsWith("startCurrentTask"));
        Assert.Contains(handler.Requested, u => u.EndsWith("setRegister?register=System_ReglerOnOff&value=1"));

        // START must run *before* the set point write (like turning on a profile):
        // startCurrentTask reloads the task, so a set point written first would be lost.
        int startIndex = handler.Requested.FindIndex(u => u.EndsWith("startCurrentTask"));
        int setpointIndex = handler.Requested.FindIndex(u => u.EndsWith("setRegister?register=TRset_SP&value=100"));
        Assert.True(startIndex >= 0 && setpointIndex >= 0 && startIndex < setpointIndex,
            $"START ({startIndex}) must precede the TRset_SP write ({setpointIndex}).");
    }

    /// <summary>StopAsync must run the verified STOP: stopCurrentTask then System_ReglerOnOff=0.</summary>
    [Fact]
    public async Task StopAsync_stops_task_and_switches_controller_off()
    {
        var handler = new RouteHandler
        {
            ["ajax/getInfoReport"] = ("{}", HttpStatusCode.OK),
            ["ajax/getRegister?register=TRset_TR"] =
                ("{\"register\":\"TRset_TR\",\"values\":[{\"value\":21.0,\"times\":1}]}", HttpStatusCode.OK),
            ["ajax/stopCurrentTask"] =
                ("{\"value\":\"success\",\"info\":\"stop current task and reload current task\"}", HttpStatusCode.OK),
            ["ajax/setRegister?register=System_ReglerOnOff&value=0"] =
                ("{\"value\":\"success\",\"info\":\"value 0.000000 wrote to register System_ReglerOnOff\"}", HttpStatusCode.OK),
        };

        await using var client = new SikaTpClient(_ => new HttpClient(handler));
        await client.ConnectAsync(new ChamberConnectionSettings { Host = "10.88.5.81", Port = 80 });

        await client.StopAsync();

        Assert.Contains(handler.Requested, u => u.EndsWith("stopCurrentTask"));
        Assert.Contains(handler.Requested, u => u.EndsWith("setRegister?register=System_ReglerOnOff&value=0"));
    }

    /// <summary>
    /// The bath's embedded web server sporadically drops a single request. A connect must
    /// survive that instead of reporting the device as unreachable.
    /// </summary>
    [Fact]
    public async Task ConnectAsync_retries_a_transient_failure()
    {
        var handler = new RouteHandler
        {
            ["ajax/getRegister?register=TRset_TR"] =
                ("{\"register\":\"TRset_TR\",\"values\":[{\"value\":21.0,\"times\":1}]}", HttpStatusCode.OK),
        };
        handler.FailFirst = 1;

        await using var client = new SikaTpClient(_ => new HttpClient(handler));
        await client.ConnectAsync(new ChamberConnectionSettings { Host = "10.88.5.226", Port = 8081 });

        Assert.True(client.IsConnected);
        Assert.Equal(2, handler.Requested.Count);
    }

    /// <summary>
    /// Something answering on the port that is not the REST-API (e.g. the web application
    /// port) must fail with an actionable message, not count as a working connection.
    /// </summary>
    [Fact]
    public async Task ConnectAsync_rejects_an_endpoint_that_is_not_the_rest_api()
    {
        var handler = new RouteHandler
        {
            ["ajax/getRegister?register=TRset_TR"] = ("<html>SIKA WebApp</html>", HttpStatusCode.OK),
        };

        await using var client = new SikaTpClient(_ => new HttpClient(handler));

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ConnectAsync(new ChamberConnectionSettings { Host = "10.88.5.226", Port = 80 }));

        Assert.Contains("REST-API", ex.Message);
        Assert.False(client.IsConnected);
    }

    /// <summary>
    /// The device can acknowledge a write and still keep the old set point (manual mode,
    /// running calibration, remote control disabled). The read-back must turn that into a
    /// clear error instead of a silent "nothing happens".
    /// </summary>
    [Fact]
    public async Task WriteSetpointsAsync_throws_when_the_device_kept_the_old_setpoint()
    {
        var handler = new RouteHandler
        {
            ["ajax/getRegister?register=TRset_TR"] =
                ("{\"register\":\"TRset_TR\",\"values\":[{\"value\":21.0,\"times\":1}]}", HttpStatusCode.OK),
            ["ajax/getRegister?register=System_ReglerOnOff"] =
                ("{\"register\":\"System_ReglerOnOff\",\"values\":[{\"value\":1.0,\"times\":1}]}", HttpStatusCode.OK),
            ["ajax/setRegister?register=Task_SetPointList&value=40"] =
                ("{\"value\":\"success\",\"info\":\"value 40.000000 wrote to register Task_SetPointList\"}", HttpStatusCode.OK),
            ["ajax/setRegister?register=TRset_SP&value=40"] =
                ("{\"value\":\"success\",\"info\":\"value 40.000000 wrote to register TRset_SP\"}", HttpStatusCode.OK),
            // The device kept its old set point despite acknowledging the write.
            ["ajax/getRegister?register=TRset_SP"] =
                ("{\"register\":\"TRset_SP\",\"values\":[{\"value\":25.0,\"times\":1}]}", HttpStatusCode.OK),
        };

        await using var client = new SikaTpClient(_ => new HttpClient(handler));
        await client.ConnectAsync(new ChamberConnectionSettings { Host = "10.88.5.226", Port = 8081 });

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.WriteSetpointsAsync(new[] { 40.0 }, new DigitalChannels { Start = true }));

        Assert.Contains("25", ex.Message);
        Assert.Contains("40", ex.Message);
    }

    /// <summary>Routes canned responses by the request URL's ajax command; records every request.</summary>
    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (string Body, HttpStatusCode Status)> _routes = new();

        public List<string> Requested { get; } = new();

        /// <summary>Answer the first N requests with 503, to exercise the retry path.</summary>
        public int FailFirst { get; set; }

        public (string Body, HttpStatusCode Status) this[string ajaxCommand]
        {
            set => _routes[ajaxCommand] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            Requested.Add(url);

            if (FailFirst > 0)
            {
                FailFirst--;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("busy", Encoding.UTF8, "text/plain"),
                });
            }

            var match = _routes.FirstOrDefault(kvp => url.EndsWith(kvp.Key, StringComparison.Ordinal));
            (string body, HttpStatusCode status) = match.Key is null ? ("not found", HttpStatusCode.NotFound) : match.Value;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
