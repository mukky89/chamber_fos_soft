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
        // actual 24.5). Default StartChannelIndex is 1 (Vötsch S!MPAC), so the
        // start / "condition on" bit is the second character of the digital block.
        var fake = new FakeTransport("0025.0 0024.5 01000000000000000000000000000000");
        await using var client = new ChamberClient(_ => fake);
        await client.ConnectAsync(new ChamberConnectionSettings());

        ChamberReading reading = await client.ReadAsync();

        Assert.Equal("$01I\r", fake.LastRequest);
        Assert.Equal(24.5, reading.Temperature);
        Assert.Equal(25.0, reading.TemperatureSetpoint);
        Assert.True(reading.DigitalChannels.Start);
    }

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
        Assert.Contains("14001¶1¶2¶1", sent);                 // start channel (StartChannelIndex 1 -> SIMSERV channel 2)
        Assert.DoesNotContain(sent, r => r.StartsWith("$01E")); // no ASCII-2 write
    }

    private sealed class FakeTransport : ITransport
    {
        private readonly string _response;

        public FakeTransport(string response) => _response = response;

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
            return Task.FromResult(_response);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
