using VotschVc3.Core.Communication;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

public class ProfileAndClientTests
{
    [Fact]
    public void ProfileSegment_ramp_interpolates_linearly()
    {
        var segment = new ProfileSegment { TargetTemperature = 100, IsRamp = true };
        Assert.Equal(0, segment.TemperatureAt(0.0, 0));
        Assert.Equal(50, segment.TemperatureAt(0.5, 0));
        Assert.Equal(100, segment.TemperatureAt(1.0, 0));
    }

    [Fact]
    public void ProfileSegment_hold_returns_target_immediately()
    {
        var segment = new ProfileSegment { TargetTemperature = 85, IsRamp = false };
        Assert.Equal(85, segment.TemperatureAt(0.0, 20));
        Assert.Equal(85, segment.TemperatureAt(0.5, 20));
    }

    [Fact]
    public void TestProfile_total_duration_accounts_for_cycles()
    {
        var profile = new TestProfile
        {
            Cycles = 3,
            Segments =
            {
                new ProfileSegment { Duration = TimeSpan.FromMinutes(10) },
                new ProfileSegment { Duration = TimeSpan.FromMinutes(20) },
            },
        };

        Assert.Equal(TimeSpan.FromMinutes(30), profile.SinglePassDuration);
        Assert.Equal(TimeSpan.FromMinutes(90), profile.TotalDuration);
    }

    [Fact]
    public async Task ChamberClient_read_parses_fake_transport_response()
    {
        // The chamber returns "<set point> <actual>" per channel (set point 25.0,
        // actual 24.5). Default StartChannelIndex is 1 (the bit that is set while the
        // unit runs), so the start / "condition on" bit is the second character.
        var fake = new FakeTransport("0025.0 0024.5 01000000000000000000000000000000");
        await using var client = new ChamberClient(_ => fake);
        await client.ConnectAsync(new ChamberConnectionSettings { HighResolutionRead = false });

        ChamberReading reading = await client.ReadAsync();

        Assert.Equal("$01I\r", fake.LastRequest);
        Assert.Single(fake.Requests);
        Assert.Equal(24.5, reading.Temperature);
        Assert.Equal(25.0, reading.TemperatureSetpoint);
        Assert.True(reading.DigitalChannels.Start);
        Assert.False(reading.HasHighResolution);
    }

    [Fact]
    public async Task ChamberClient_read_takes_the_measured_value_from_simserv()
    {
        // The ASCII-2 frame carries every analog value in a fixed 0000.0 field, so it
        // rounds the measurement to 0.1 °C. SIMSERV GET ACTUAL VALUE (11004) answers
        // with the controller's own resolution — the same number Simpati shows.
        var fake = new FakeTransport(Ascii("0025.0 0024.5"))
        {
            Responses = { ["11004¶1¶1"] = "1¶24.4812" },
        };
        await using var client = new ChamberClient(_ => fake);
        await client.ConnectAsync(new ChamberConnectionSettings());

        ChamberReading reading = await client.ReadAsync();

        Assert.Equal(24.4812, reading.Temperature);
        Assert.Equal(25.0, reading.TemperatureSetpoint);   // set point stays as sent
        Assert.Equal(Ascii("0025.0 0024.5"), reading.Raw); // the ASCII-2 frame is never rewritten
        Assert.True(reading.HasHighResolution);
        Assert.Contains("11004¶1¶1", reading.HighResolutionRaw ?? string.Empty);
        Assert.Contains("1¶24.4812", reading.HighResolutionRaw ?? string.Empty);
    }

    [Fact]
    public async Task ChamberClient_read_refines_humidity_too()
    {
        var fake = new FakeTransport(Ascii("0025.0 0024.5 0050.0 0049.0"))
        {
            Responses =
            {
                ["11004¶1¶1"] = "1¶24.4812",
                ["11004¶1¶2"] = "1¶49.2130",
            },
        };
        await using var client = new ChamberClient(_ => fake);
        await client.ConnectAsync(new ChamberConnectionSettings());

        ChamberReading reading = await client.ReadAsync();

        Assert.Equal(24.4812, reading.Temperature);
        Assert.Equal(49.2130, reading.Humidity);
    }

    [Fact]
    public async Task ChamberClient_read_keeps_the_ascii_value_when_simserv_answers_with_an_error()
    {
        // -4 = test system not available. A controller that cannot answer 11004 must
        // not be asked on every poll, so the reading falls back to ASCII-2 for good.
        var fake = new FakeTransport(Ascii("0025.0 0024.5")) { Responses = { ["11004¶1¶1"] = "-4" } };
        await using var client = new ChamberClient(_ => fake);
        await client.ConnectAsync(new ChamberConnectionSettings());

        ChamberReading first = await client.ReadAsync();
        ChamberReading second = await client.ReadAsync();

        Assert.Equal(24.5, first.Temperature);
        Assert.Equal(24.5, second.Temperature);
        Assert.False(second.HasHighResolution);
        Assert.Single(fake.Requests, r => r.StartsWith("11004", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChamberClient_read_ignores_a_simserv_value_that_is_a_different_channel()
    {
        // A number that disagrees with the ASCII-2 measurement is not the same channel;
        // the measured value must stay the one the chamber reported.
        var fake = new FakeTransport(Ascii("0025.0 0024.5")) { Responses = { ["11004¶1¶1"] = "1¶95.0" } };
        await using var client = new ChamberClient(_ => fake);
        await client.ConnectAsync(new ChamberConnectionSettings());

        for (int i = 0; i < 5; i++)
        {
            ChamberReading reading = await client.ReadAsync();
            Assert.Equal(24.5, reading.Temperature);
            Assert.False(reading.HasHighResolution);
        }

        // …and after a few tries in a row the extra frame is dropped.
        Assert.Equal(3, fake.Requests.Count(r => r.StartsWith("11004", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ChamberClient_read_survives_a_controller_that_drops_the_simserv_frame()
    {
        var fake = new FakeTransport(Ascii("0025.0 0024.5")) { FailOn = "11004" };
        await using var client = new ChamberClient(_ => fake);
        await client.ConnectAsync(new ChamberConnectionSettings());

        ChamberReading reading = await client.ReadAsync();

        Assert.Equal(24.5, reading.Temperature);
        Assert.False(reading.HasHighResolution);
    }

    private static string Ascii(string analog) =>
        analog + " 01000000000000000000000000000000";

    [Fact]
    public async Task ChamberClient_write_emits_simserv_commands()
    {
        // Simpac controllers are controlled with SIMSERV, not the ASCII-2 $ddE
        // frame: SET NOMINAL VALUE (11001) per control variable, then SET
        // DIGITALOUT (14001) for the start channel. The controller answers "1".
        var fake = new FakeTransport("1");
        await using var client = new ChamberClient(_ => fake);
        await client.ConnectAsync(new ChamberConnectionSettings()); // StartChannelIndex defaults to 1

        await client.SetTemperatureAndHumidityAsync(50.0, null, start: true);

        var sent = fake.Requests.Select(r => r.TrimEnd('\r')).ToList();
        Assert.Contains("11001¶1¶1¶50.0", sent);              // temperature set point
        Assert.Contains("14001¶1¶1¶1", sent);                 // start channel = StartChannelIndex (1 = running bit)
        Assert.DoesNotContain(sent, r => r.StartsWith("$01E")); // no ASCII-2 write
    }

    private sealed class FakeTransport : ITransport
    {
        private readonly string _response;

        public FakeTransport(string response) => _response = response;

        /// <summary>Answers keyed by the request with its terminator stripped.</summary>
        public Dictionary<string, string> Responses { get; } = new(StringComparer.Ordinal);

        /// <summary>Requests starting with this prefix throw, like a dropped frame.</summary>
        public string? FailOn { get; set; }

        public List<string> Requests { get; } = new();

        public string? LastRequest => Requests.Count > 0 ? Requests[^1] : null;

        public bool IsConnected { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<string> SendReceiveAsync(string command, CancellationToken cancellationToken = default)
        {
            Requests.Add(command);
            string key = command.TrimEnd('\r', '\n');
            if (FailOn is { Length: > 0 } prefix && key.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new TimeoutException($"No answer to \"{key}\".");
            }

            return Task.FromResult(Responses.TryGetValue(key, out string? answer) ? answer : _response);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
