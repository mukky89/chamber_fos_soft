using VotschVc3.Core.Communication;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>
/// Covers resuming a profile run from a saved <see cref="ProfileRunCheckpoint"/> position –
/// the mechanism behind recovering a run interrupted by a power outage or an app crash.
/// </summary>
public class ProfileRunnerResumeTests
{
    [Fact]
    public async Task ResumeFrom_skips_already_finished_segments_and_continues_mid_segment()
    {
        var device = new FakeChamberDevice();
        var profile = new TestProfile
        {
            Segments =
            {
                new ProfileSegment { TargetTemperature = 40, IsRamp = false, Duration = TimeSpan.FromMilliseconds(40) },
                new ProfileSegment { TargetTemperature = 50, IsRamp = false, Duration = TimeSpan.FromMilliseconds(40) },
                new ProfileSegment { TargetTemperature = 60, IsRamp = false, Duration = TimeSpan.FromMilliseconds(40) },
            },
        };

        var runner = new ProfileRunner(device, updateInterval: TimeSpan.FromMilliseconds(5));
        var visitedSegments = new List<int>();
        runner.Progress += (_, e) => visitedSegments.Add(e.SegmentIndex);

        // Checkpoint says segment 0 already finished and segment 1 (of 3, no cycle region –
        // so everything is phase Cycle/cycle 0) had 35 of its 40 ms already elapsed.
        var resumeFrom = new ProfileRunPosition(
            ProfileRunPhase.Cycle, CycleIndex: 0, SegmentIndex: 1,
            ElapsedInSegment: TimeSpan.FromMilliseconds(35), IsSoaking: false);

        await runner.RunAsync(profile, startTemperature: 40, startHumidity: null, resumeFrom: resumeFrom);

        // Segment 0's target must never have been written – it was fast-forwarded, not run.
        Assert.DoesNotContain(40, device.WrittenTemperatures);
        // Segments 1 and 2 did run.
        Assert.Contains(50, device.WrittenTemperatures);
        Assert.Contains(60, device.WrittenTemperatures);
        Assert.DoesNotContain(0, visitedSegments);
        Assert.Contains(1, visitedSegments);
        Assert.Contains(2, visitedSegments);
    }

    [Fact]
    public async Task ResumeFrom_mid_soak_reenters_soak_wait_instead_of_trusting_saved_elapsed_time()
    {
        // The bath needs 3 reads before it reports having reached the target – exactly the
        // "temperature drifted during the outage, re-check for real" scenario the soak
        // resume path exists for.
        var device = new FakeChamberDevice(reachAfterReads: 3);
        var profile = new TestProfile
        {
            Segments =
            {
                new ProfileSegment
                {
                    TargetTemperature = 60, IsRamp = false, Duration = TimeSpan.FromMilliseconds(20),
                    GuaranteedSoak = true, SoakTolerance = 0.5,
                },
            },
        };

        var runner = new ProfileRunner(device, updateInterval: TimeSpan.FromMilliseconds(5));

        var resumeFrom = new ProfileRunPosition(
            ProfileRunPhase.Cycle, CycleIndex: 0, SegmentIndex: 0,
            ElapsedInSegment: TimeSpan.Zero, IsSoaking: true);

        await runner.RunAsync(profile, startTemperature: 20, startHumidity: null, resumeFrom: resumeFrom);

        // Resuming a mid-soak segment must re-check the real temperature, not assume it is
        // still at target – the device is read repeatedly until it actually reports "reached".
        Assert.True(device.ReadCount >= 3, "expected the soak wait to be re-entered on resume");
        Assert.All(device.WrittenTemperatures, t => Assert.Equal(60, t));
    }

    [Fact]
    public async Task ResumeFrom_mid_ramp_uses_saved_origin_and_preserves_total_elapsed_time()
    {
        var device = new FakeChamberDevice();
        var profile = new TestProfile
        {
            Segments =
            {
                new ProfileSegment
                {
                    TargetTemperature = 100,
                    IsRamp = true,
                    Duration = TimeSpan.FromMilliseconds(100),
                },
            },
        };

        var runner = new ProfileRunner(device, updateInterval: TimeSpan.FromMilliseconds(5));
        var elapsed = new List<TimeSpan>();
        runner.Progress += (_, e) => elapsed.Add(e.ElapsedInSegment);
        var resumeFrom = new ProfileRunPosition(
            ProfileRunPhase.Cycle, CycleIndex: 0, SegmentIndex: 0,
            ElapsedInSegment: TimeSpan.FromMilliseconds(50), IsSoaking: false)
        {
            SegmentStartTemperature = 0,
        };

        // Current measured temperature deliberately differs from the saved ramp
        // origin. Recovery must continue around 50 °C, not restart near 80 °C.
        await runner.RunAsync(profile, startTemperature: 80, startHumidity: null, resumeFrom: resumeFrom);

        Assert.InRange(device.WrittenTemperatures[0], 45, 65);
        Assert.True(elapsed[0] >= TimeSpan.FromMilliseconds(50));
        Assert.True(elapsed[^1] >= profile.Segments[0].Duration);
    }

    private sealed class FakeChamberDevice : IChamberDevice
    {
        private readonly int _reachAfterReads;

        public FakeChamberDevice(int reachAfterReads = 0) => _reachAfterReads = reachAfterReads;

        public List<double> WrittenTemperatures { get; } = new();

        public int ReadCount { get; private set; }

        public bool IsConnected => true;

        public ChamberConnectionSettings Settings { get; } = new();

        public event EventHandler<FrameExchangedEventArgs>? FrameExchanged { add { } remove { } }

        public Task ConnectAsync(ChamberConnectionSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DisconnectAsync() => Task.CompletedTask;

        public Task<ChamberReading> ReadAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            double lastTarget = WrittenTemperatures.Count > 0 ? WrittenTemperatures[^1] : 0;
            double measured = ReadCount >= _reachAfterReads && _reachAfterReads > 0 ? lastTarget : 0;
            var reading = new ChamberReading(
                DateTimeOffset.Now, string.Empty, new List<double> { measured, lastTarget }, new DigitalChannels());
            return Task.FromResult(reading);
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
