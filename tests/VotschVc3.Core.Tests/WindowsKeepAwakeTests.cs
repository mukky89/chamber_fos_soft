using System.Collections.Concurrent;
using VotschVc3.App;
using Xunit;

namespace VotschVc3.Core.Tests;

public class WindowsKeepAwakeTests
{
    [Fact]
    public void RequestsAndReleasesOnSameThreadAndStopsActivity()
    {
        var calls = new ConcurrentQueue<(uint Flags, int Thread)>();
        using var pulsed = new ManualResetEventSlim();
        int activity = 0;
        var service = new WindowsKeepAwake(flags =>
        {
            calls.Enqueue((flags, Environment.CurrentManagedThreadId));
            return 1;
        }, () => { Interlocked.Increment(ref activity); pulsed.Set(); return true; },
            _ => throw new Exception("Unexpected warning"), TimeSpan.FromMilliseconds(10));
        Assert.True(pulsed.Wait(TimeSpan.FromSeconds(2)));
        service.Dispose();
        service.Dispose();
        Assert.Equal(WindowsKeepAwake.Required, calls.First().Flags);
        Assert.Equal(WindowsKeepAwake.Continuous, calls.Last().Flags);
        Assert.Single(calls.Select(c => c.Thread).Distinct());
        Assert.True(activity > 0);
        Assert.All(calls.SkipLast(1), call => Assert.Equal(WindowsKeepAwake.Required, call.Flags));
    }

    [Fact]
    public void RejectedNativeRequestsAreReportedOnceAndCleanupStillRuns()
    {
        var warnings = new ConcurrentQueue<string>();
        var powers = new ConcurrentQueue<uint>();
        using var twice = new ManualResetEventSlim();
        int count = 0;
        using (var service = new WindowsKeepAwake(flags => { powers.Enqueue(flags); return 0; },
            () => { if (Interlocked.Increment(ref count) >= 3) twice.Set(); return false; },
            warnings.Enqueue, TimeSpan.FromMilliseconds(10)))
        {
            Assert.True(twice.Wait(TimeSpan.FromSeconds(2)));
        }
        Assert.Equal(2, warnings.Count);
        Assert.Equal(WindowsKeepAwake.Continuous, powers.Last());
    }
}
