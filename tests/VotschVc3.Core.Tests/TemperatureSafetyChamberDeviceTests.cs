using VotschVc3.Core.Communication;
using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class TemperatureSafetyChamberDeviceTests
{
    [Fact]
    public async Task ReadingOutsideLimitsStopsOutputAndLatchesFurtherWrites()
    {
        var raw = new FakeDevice(81);
        var policy = new TemperatureSafetyPolicy(-45, 80);
        await using var guarded = new TemperatureSafetyChamberDevice(raw, policy);
        TemperatureSafetyTrippedEventArgs? trip = null;
        guarded.SafetyTripped += (_, e) => trip = e;

        ChamberReading reading = await guarded.ReadAsync();

        Assert.Equal(81, reading.Temperature);
        Assert.Equal(1, raw.StopCount);
        Assert.True(policy.IsTripped);
        Assert.NotNull(trip);
        Assert.True(trip!.StopSucceeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guarded.WriteSetpointsAsync(new[] { 25d }, new DigitalChannels()));
    }

    [Fact]
    public async Task InRangeReadingDoesNotStopAndOutOfRangeSetpointIsRejectedBeforeWrite()
    {
        var raw = new FakeDevice(25);
        await using var guarded = new TemperatureSafetyChamberDevice(raw, new(-40, 60));

        await guarded.ReadAsync();
        Assert.Equal(0, raw.StopCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guarded.WriteSetpointsAsync(new[] { 61d }, new DigitalChannels()));
        Assert.Empty(raw.Writes);
    }

    [Fact]
    public async Task FailedPhysicalStopIsReportedAsFailureAndStillLatchesControl()
    {
        var raw = new FakeDevice(-50) { FailStop = true };
        var policy = new TemperatureSafetyPolicy(-40, 60);
        await using var guarded = new TemperatureSafetyChamberDevice(raw, policy);
        TemperatureSafetyTrippedEventArgs? trip = null;
        guarded.SafetyTripped += (_, e) => trip = e;

        await guarded.ReadAsync();

        Assert.NotNull(trip);
        Assert.False(trip!.StopSucceeded);
        Assert.Contains("stop failed", trip.StopError);
        Assert.True(policy.IsTripped);
        Assert.Equal(3, raw.StopCount);
    }

    private sealed class FakeDevice(double temperature) : IChamberDevice
    {
        public int StopCount { get; private set; }
        public bool FailStop { get; init; }
        public List<double> Writes { get; } = [];
        public bool IsConnected => true;
        public ChamberConnectionSettings Settings { get; } = new();
        public event EventHandler<FrameExchangedEventArgs>? FrameExchanged { add { } remove { } }
        public Task ConnectAsync(ChamberConnectionSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<ChamberReading> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(
            new ChamberReading(DateTimeOffset.Now, string.Empty, new[] { temperature, temperature }, new DigitalChannels()));
        public Task WriteSetpointsAsync(IReadOnlyList<double> setpoints, DigitalChannels digital, CancellationToken cancellationToken = default)
        {
            Writes.Add(setpoints[0]);
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return FailStop ? Task.FromException(new IOException("stop failed")) : Task.CompletedTask;
        }
        public Task<string> SendRawAsync(string frame, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
