using VotschVc3.Core.Calibration;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

public class CalibrationChamberStartupRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransientFailureReconnectsBeforeReadingAndNeverWrites(bool failConnect)
    {
        await using var chamber = new Chamber { FailConnect = failConnect, RemainingFailures = 2 };
        var settings = new ChamberConnectionSettings();
        var messages = new List<string>();
        var reading = await CalibrationChamberStartupRecovery.ConnectAndReadAsync(chamber, settings,
            messages.Add, default, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1));
        Assert.Equal(20, reading.Temperature);
        Assert.Equal(3, chamber.ConnectCount);
        Assert.Equal(2, chamber.DisconnectCount);
        Assert.Same(settings, chamber.Settings);
        Assert.Equal(0, chamber.Writes);
        Assert.Equal(0, chamber.Stops);
        Assert.Contains("obnovené", messages.Last());
    }

    [Fact]
    public async Task StopCancelsRetryWithoutOpeningAnotherConnection()
    {
        await using var chamber = new Chamber { RemainingFailures = 100 };
        using var stop = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CalibrationChamberStartupRecovery.ConnectAndReadAsync(chamber, new(), _ => stop.Cancel(), stop.Token));
        Assert.Equal(1, chamber.ConnectCount);
        Assert.Equal(0, chamber.Writes);
    }

    [Fact]
    public async Task RecoveryDeadlineAlsoBoundsAnInFlightRead()
    {
        await using var chamber = new Chamber { BlockRead = true };
        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            CalibrationChamberStartupRecovery.ConnectAndReadAsync(chamber, new(), null, default,
                TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(1)));
        Assert.Contains("checkpoint", error.Message);
        Assert.Equal(0, chamber.Writes);
    }

    [Fact]
    public async Task NonFiniteTemperatureMustRecoverBeforeStartupContinues()
    {
        await using var chamber = new Chamber { InvalidFirstReading = true };
        var reading = await CalibrationChamberStartupRecovery.ConnectAndReadAsync(chamber, new(), null, default,
            TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1));
        Assert.Equal(20, reading.Temperature);
        Assert.Equal(2, chamber.ConnectCount);
    }

    [Fact]
    public async Task NonCommunicationErrorIsNotRetried()
    {
        await using var chamber = new Chamber { PermanentError = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CalibrationChamberStartupRecovery.ConnectAndReadAsync(chamber, new(), null, default));
        Assert.Equal(1, chamber.ConnectCount);
    }

    [Fact]
    public async Task RecoveryPreservesTheTemperatureSafetyInterlock()
    {
        var inner = new Chamber { RemainingFailures = 1 };
        var policy = new TemperatureSafetyPolicy(0, 10);
        await using var chamber = new TemperatureSafetyChamberDevice(inner, policy);
        await CalibrationChamberStartupRecovery.ConnectAndReadAsync(chamber, new(), null, default,
            TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1));
        Assert.True(policy.IsTripped);
        Assert.Equal(1, inner.Stops);
        await Assert.ThrowsAsync<InvalidOperationException>(() => chamber.WriteSetpointsAsync(new[] { 5d }, new()));
        Assert.Equal(0, inner.Writes);
    }

    private sealed class Chamber : IChamberDevice
    {
        public int RemainingFailures;
        public bool FailConnect, BlockRead, InvalidFirstReading, PermanentError;
        public int ConnectCount, DisconnectCount, Writes, Stops;
        public bool IsConnected { get; private set; }
        public ChamberConnectionSettings Settings { get; private set; } = new();
        public event EventHandler<FrameExchangedEventArgs>? FrameExchanged { add { } remove { } }
        public Task ConnectAsync(ChamberConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.False(IsConnected); // Recovery must discard the failed session first.
            ConnectCount++;
            Settings = settings;
            IsConnected = true;
            if (FailConnect && RemainingFailures-- > 0) throw new IOException("offline");
            return Task.CompletedTask;
        }
        public Task DisconnectAsync() { DisconnectCount++; IsConnected = false; return Task.CompletedTask; }
        public async Task<ChamberReading> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (PermanentError) throw new InvalidOperationException("configuration error");
            if (BlockRead) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (!FailConnect && RemainingFailures-- > 0) throw new TimeoutException("Timed out waiting for a response after 5s.");
            return new ChamberReading(DateTimeOffset.Now, "test", new[] { InvalidFirstReading && ConnectCount == 1 ? double.NaN : 20d }, new());
        }
        public Task WriteSetpointsAsync(IReadOnlyList<double> setpoints, DigitalChannels digital, CancellationToken cancellationToken = default)
        { Writes++; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken = default) { Stops++; return Task.CompletedTask; }
        public Task<string> SendRawAsync(string frame, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
