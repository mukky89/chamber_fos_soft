using VotschVc3.Core.Communication;
using Xunit;

namespace VotschVc3.Core.Tests;

public class SharedChamberTransportTests
{
    private static ChamberConnectionSettings Settings() => new() { Host = Guid.NewGuid().ToString("N") };

    [Fact]
    public async Task DashboardAndCalibrationShareOneSocketAndReleaseOnlyTheirOwnLease()
    {
        var settings = Settings();
        var wire = new Wire();
        await using var dashboard = new SharedChamberTransport(settings, () => wire);
        await using var calibration = new SharedChamberTransport(settings, () => throw new Exception("Second socket"));
        await dashboard.ConnectAsync();
        await calibration.ConnectAsync();
        Assert.Equal(1, wire.Connects);
        Assert.Equal("dashboard", await dashboard.SendReceiveAsync("dashboard"));
        Assert.Equal("calibration", await calibration.SendReceiveAsync("calibration"));
        await calibration.DisconnectAsync();
        Assert.True(dashboard.IsConnected);
        Assert.Equal(0, wire.Disposals);
        await calibration.ConnectAsync();
        await dashboard.DisposeAsync();
        Assert.Equal("still alive", await calibration.SendReceiveAsync("still alive"));
        await calibration.DisposeAsync();
        Assert.Equal(1, wire.Disposals);
    }

    [Fact]
    public async Task ConcurrentOwnersCannotInterleaveRequestsAndResponses()
    {
        var settings = Settings();
        var wire = new Wire { Slow = true };
        await using var a = new SharedChamberTransport(settings, () => wire);
        await using var b = new SharedChamberTransport(settings, () => wire);
        await Task.WhenAll(a.ConnectAsync(), b.ConnectAsync());
        var replies = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => (i % 2 == 0 ? a : b).SendReceiveAsync(i.ToString())));
        Assert.Equal(Enumerable.Range(0, 20).Select(i => i.ToString()), replies);
        Assert.Equal(1, wire.MaximumActive);
        Assert.Equal(1, wire.Connects);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedExchangeIsNotReplayedAndNextOwnerGetsFreshSession(bool cancelled)
    {
        var settings = Settings();
        var wire = new Wire();
        await using var a = new SharedChamberTransport(settings, () => wire);
        await using var b = new SharedChamberTransport(settings, () => wire);
        await a.ConnectAsync();
        await b.ConnectAsync();
        wire.Failure = cancelled ? new OperationCanceledException() : new TimeoutException("late reply");
        await Assert.ThrowsAnyAsync<Exception>(() => a.SendReceiveAsync("write"));
        Assert.False(b.IsConnected);
        wire.Failure = null;
        Assert.Equal("read", await b.SendReceiveAsync("read"));
        Assert.Equal(new[] { "write", "read" }, wire.Commands);
        Assert.Equal(2, wire.Connects);
    }

    [Fact]
    public async Task CancellingQueuedOwnerDoesNotInterruptCurrentExchange()
    {
        var settings = Settings();
        var wire = new Wire { Block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var a = new SharedChamberTransport(settings, () => wire);
        await using var b = new SharedChamberTransport(settings, () => wire);
        await a.ConnectAsync();
        await b.ConnectAsync();
        var first = a.SendReceiveAsync("first");
        await wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var stop = new CancellationTokenSource();
        var queued = b.SendReceiveAsync("cancelled", stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        wire.Block.SetResult();
        Assert.Equal("first", await first);
        Assert.True(a.IsConnected);
        Assert.Single(wire.Commands);
    }

    [Fact]
    public async Task LastOwnerDisposalWaitsForItsInFlightRead()
    {
        var wire = new Wire { Block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var owner = new SharedChamberTransport(Settings(), () => wire);
        await owner.ConnectAsync();
        var read = owner.SendReceiveAsync("read");
        await wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var dispose = owner.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);
        wire.Block.SetResult();
        await read;
        await dispose;
        Assert.Equal(1, wire.Disposals);
    }

    [Fact]
    public async Task DifferentChambersKeepSeparateConnections()
    {
        var first = new Wire(); var second = new Wire();
        await using var a = new SharedChamberTransport(Settings(), () => first);
        await using var b = new SharedChamberTransport(Settings(), () => second);
        await Task.WhenAll(a.ConnectAsync(), b.ConnectAsync());
        Assert.Equal(1, first.Connects);
        Assert.Equal(1, second.Connects);
    }

    private sealed class Wire : ITransport
    {
        public int Connects, Disposals, MaximumActive;
        private int _active;
        public bool Slow;
        public Exception? Failure;
        public TaskCompletionSource? Block;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Commands = new();
        public bool IsConnected { get; private set; }
        public Task ConnectAsync(CancellationToken cancellationToken = default)
        { Connects++; IsConnected = true; return Task.CompletedTask; }
        public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }
        public async Task<string> SendReceiveAsync(string command, CancellationToken cancellationToken = default)
        {
            MaximumActive = Math.Max(MaximumActive, Interlocked.Increment(ref _active));
            try
            {
                Commands.Add(command);
                Entered.TrySetResult();
                if (Failure is not null) throw Failure;
                if (Block is not null) await Block.Task.WaitAsync(cancellationToken);
                if (Slow) await Task.Delay(2, cancellationToken);
                return command;
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public ValueTask DisposeAsync() { Disposals++; IsConnected = false; return ValueTask.CompletedTask; }
    }
}
