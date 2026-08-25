using System.Diagnostics;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>
/// Covers "finish this plateau now": the operator ends the hold that is running and the
/// run continues with the next ramp and the plateau after it, without dropping anything
/// else from the profile.
/// </summary>
public class ProfileRunnerSkipHoldTests
{
    private static ProfileSegment Ramp(double target, int ms) => new()
    {
        TargetTemperature = target, IsRamp = true, Duration = TimeSpan.FromMilliseconds(ms),
    };

    private static ProfileSegment Hold(double target, int ms) => new()
    {
        TargetTemperature = target, IsRamp = false, Duration = TimeSpan.FromMilliseconds(ms),
    };

    [Fact]
    public async Task SkipCurrentHold_ends_the_running_plateau_and_continues_with_the_next_segments()
    {
        // A very long plateau in the middle: without a skip the run would take minutes.
        var device = new FakeDevice();
        var profile = new TestProfile
        {
            Segments = { Hold(60, 600_000), Ramp(80, 20), Hold(80, 20) },
        };
        var runner = new ProfileRunner(device, updateInterval: TimeSpan.FromMilliseconds(5));

        runner.Progress += (_, e) =>
        {
            // As soon as the long plateau is actually running, cut it short.
            if (e.SegmentIndex == 0 && e.ElapsedInSegment > TimeSpan.Zero)
            {
                runner.SkipCurrentHold();
            }
        };

        var clock = Stopwatch.StartNew();
        await runner.RunAsync(profile, startTemperature: 60, startHumidity: null);
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"run should not wait out the plateau (took {clock.Elapsed}).");

        // The rest of the profile still ran: the ramp interpolated and the last hold landed on 80.
        Assert.Contains(device.WrittenTemperatures, t => t is > 60 and < 80);
        Assert.Equal(80, device.WrittenTemperatures[^1]);
    }

    [Fact]
    public async Task SkipCurrentHold_requested_during_a_ramp_is_dropped_and_does_not_skip_the_next_plateau()
    {
        // Skipping a ramp would let the next segment jump straight to its target, so the
        // request must be discarded when that ramp ends – not carried into the plateau.
        var device = new FakeDevice();
        var profile = new TestProfile
        {
            Segments = { Ramp(80, 60), Hold(80, 120) },
        };
        var runner = new ProfileRunner(device, updateInterval: TimeSpan.FromMilliseconds(5));

        bool asked = false;
        var holdSamples = 0;
        runner.Progress += (_, e) =>
        {
            if (e.SegmentIndex == 0 && !asked)
            {
                asked = true;
                runner.SkipCurrentHold();
            }

            if (e.SegmentIndex == 1)
            {
                holdSamples++;
            }
        };

        await runner.RunAsync(profile, startTemperature: 20, startHumidity: null);

        Assert.True(asked, "the test must have requested a skip during the ramp");
        Assert.True(holdSamples > 1, $"the plateau must have run its dwell, not been skipped ({holdSamples} sample(s)).");
    }

    [Fact]
    public async Task SkipCurrentHold_gives_up_a_soak_wait_that_would_never_reach_the_target()
    {
        // The chamber never arrives, so the guaranteed-soak wait would block forever.
        var device = new FakeDevice(neverReaches: true);
        var profile = new TestProfile
        {
            Segments = { Hold(60, 20) },
        };
        var runner = new ProfileRunner(
            device, updateInterval: TimeSpan.FromMilliseconds(5), soakAllHolds: true, defaultSoakTolerance: 0.3);

        runner.Progress += (_, e) =>
        {
            if (e.IsSoaking)
            {
                runner.SkipCurrentHold();
            }
        };

        Task run = runner.RunAsync(profile, startTemperature: 20, startHumidity: null);
        Task finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(20)));

        Assert.Same(run, finished);
        await run;
    }

    private sealed class FakeDevice : IChamberDevice
    {
        private readonly bool _neverReaches;

        public FakeDevice(bool neverReaches = false) => _neverReaches = neverReaches;

        public List<double> WrittenTemperatures { get; } = new();

        public bool IsConnected => true;

        public ChamberConnectionSettings Settings { get; } = new();

        public event EventHandler<FrameExchangedEventArgs>? FrameExchanged { add { } remove { } }

        public Task ConnectAsync(ChamberConnectionSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DisconnectAsync() => Task.CompletedTask;

        public Task<ChamberReading> ReadAsync(CancellationToken cancellationToken = default)
        {
            double lastTarget = WrittenTemperatures.Count > 0 ? WrittenTemperatures[^1] : 0;
            double measured = _neverReaches ? lastTarget - 50 : lastTarget;
            return Task.FromResult(new ChamberReading(
                DateTimeOffset.Now, string.Empty, new List<double> { measured, lastTarget }, new DigitalChannels()));
        }

        public Task WriteSetpointsAsync(
            IReadOnlyList<double> setpoints, DigitalChannels digital, CancellationToken cancellationToken = default)
        {
            WrittenTemperatures.Add(setpoints[0]);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> SendRawAsync(string frame, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
