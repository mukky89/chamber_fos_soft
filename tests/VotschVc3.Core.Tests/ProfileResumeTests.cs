using VotschVc3.Core.Communication;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>
/// Crash-recovery: resuming an interrupted profile run from a saved checkpoint
/// (<see cref="ProfileRunPosition"/>).
/// </summary>
public class ProfileResumeTests
{
    [Fact]
    public async Task Runner_resume_skips_completed_segments_and_cycles()
    {
        var device = new FakeChamberDevice();
        var runner = new ProfileRunner(device, TimeSpan.FromMilliseconds(20));
        var profile = new TestProfile
        {
            Cycles = 2,
            Segments =
            {
                new ProfileSegment { TargetTemperature = 50, Duration = TimeSpan.FromMilliseconds(60), IsRamp = true },
                new ProfileSegment { TargetTemperature = 80, Duration = TimeSpan.FromMilliseconds(60), IsRamp = true },
            },
        };

        var progress = new List<ProfileProgressEventArgs>();
        runner.Progress += (_, e) => progress.Add(e);

        // Resume in cycle 1 (second pass), segment 1 – everything before must be skipped.
        var resume = new ProfileRunPosition(
            Cycle: 1, SegmentIndex: 1, ElapsedInSegment: TimeSpan.Zero,
            SegmentStartTemperature: 50, SegmentStartHumidity: null);
        await runner.RunAsync(profile, startTemperature: 20, startHumidity: null, resume);

        Assert.NotEmpty(progress);
        Assert.All(progress, e => Assert.Equal(1, e.Cycle));
        Assert.All(progress, e => Assert.Equal(1, e.SegmentIndex));
        // The resumed ramp starts from the checkpointed segment start (50 °C), not
        // from the fresh-run start temperature (20 °C).
        Assert.Equal(50, progress[0].SegmentStartTemperature);
        Assert.InRange(device.WrittenTemperatures[0], 50, 80);
    }

    [Fact]
    public async Task Runner_resume_continues_mid_segment_without_restarting_it()
    {
        var device = new FakeChamberDevice();
        var runner = new ProfileRunner(device, TimeSpan.FromMilliseconds(20));
        var profile = new TestProfile
        {
            Segments =
            {
                new ProfileSegment { TargetTemperature = 100, Duration = TimeSpan.FromMilliseconds(200), IsRamp = true },
            },
        };

        var fractions = new List<double>();
        runner.Progress += (_, e) => fractions.Add(e.Fraction);

        // Half of the segment already elapsed before the crash.
        var resume = new ProfileRunPosition(
            Cycle: 0, SegmentIndex: 0, ElapsedInSegment: TimeSpan.FromMilliseconds(100),
            SegmentStartTemperature: 0, SegmentStartHumidity: null);
        await runner.RunAsync(profile, startTemperature: 0, startHumidity: null, resume);

        // The very first written set point must already be at ≥ 50 % of the ramp.
        Assert.True(fractions[0] >= 0.5, $"first fraction was {fractions[0]}");
        Assert.True(device.WrittenTemperatures[0] >= 50);
    }

    [Fact]
    public async Task Runner_resume_mid_segment_skips_guaranteed_soak()
    {
        // The device never reports a temperature near the target, so a repeated
        // guaranteed soak would block forever – a resumed dwell must skip it.
        var device = new FakeChamberDevice { MeasuredTemperature = -100 };
        var runner = new ProfileRunner(device, TimeSpan.FromMilliseconds(20));
        var profile = new TestProfile
        {
            Segments =
            {
                new ProfileSegment
                {
                    TargetTemperature = 90, Duration = TimeSpan.FromMilliseconds(80),
                    IsRamp = false, GuaranteedSoak = true, SoakTolerance = 1,
                },
            },
        };

        var resume = new ProfileRunPosition(
            Cycle: 0, SegmentIndex: 0, ElapsedInSegment: TimeSpan.FromMilliseconds(40),
            SegmentStartTemperature: 90, SegmentStartHumidity: null);

        Task run = runner.RunAsync(profile, 90, null, resume);
        Task finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(run, finished);
        await run;
    }

    private sealed class FakeChamberDevice : IChamberDevice
    {
        public double MeasuredTemperature { get; set; } = 25;

        public List<double> WrittenTemperatures { get; } = new();

        public bool IsConnected => true;

        public ChamberConnectionSettings Settings { get; } = new();

        public event EventHandler<FrameExchangedEventArgs>? FrameExchanged { add { } remove { } }

        public Task ConnectAsync(ChamberConnectionSettings settings, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DisconnectAsync() => Task.CompletedTask;

        public Task<ChamberReading> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ChamberReading(
                DateTimeOffset.Now, string.Empty,
                new[] { MeasuredTemperature, MeasuredTemperature }, new DigitalChannels()));

        public Task WriteSetpointsAsync(
            IReadOnlyList<double> setpoints, DigitalChannels digital, CancellationToken cancellationToken = default)
        {
            WrittenTemperatures.Add(setpoints[0]);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> SendRawAsync(string frame, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
