using VotschVc3.Core.Communication;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>
/// Covers the SIKA "step and hold" behaviour: with <c>soakAllSegments</c> set, every
/// segment (ramp or hold) must jump straight to its target, wait for the measured
/// temperature to actually reach it, and only then time its dwell duration before
/// advancing to the next segment.
/// </summary>
public class ProfileRunnerSoakTests
{
    [Fact]
    public async Task SoakAllSegments_writes_target_directly_and_waits_for_reach_before_ramp_duration_starts()
    {
        // The bath starts at 20 °C and only "arrives" at the 60 °C target after a
        // few reads – the runner must not treat the segment as done until then, and
        // must never write an interpolated in-between set point.
        var device = new FakeChamberDevice(startTemperature: 20, reachAfterReads: 3);
        var profile = new TestProfile
        {
            Segments =
            {
                new ProfileSegment { TargetTemperature = 60, IsRamp = true, Duration = TimeSpan.FromMilliseconds(30) },
            },
        };

        var runner = new ProfileRunner(
            device,
            updateInterval: TimeSpan.FromMilliseconds(5),
            defaultSoakTolerance: 0.3,
            soakAllSegments: true);

        await runner.RunAsync(profile, startTemperature: 20, startHumidity: null);

        // Every write during the soak-wait, and the single write of the timed hold,
        // must be the literal target – never an interpolated value.
        Assert.All(device.WrittenTemperatures, t => Assert.Equal(60, t));
        Assert.True(device.ReadCount >= 3, "expected the runner to poll until the target was reached");
    }

    [Fact]
    public async Task SoakAllSegments_off_keeps_ramp_interpolation_for_non_sika_devices()
    {
        var device = new FakeChamberDevice(startTemperature: 20, reachAfterReads: 0);
        var profile = new TestProfile
        {
            Segments =
            {
                new ProfileSegment { TargetTemperature = 60, IsRamp = true, Duration = TimeSpan.FromMilliseconds(20) },
            },
        };

        var runner = new ProfileRunner(device, updateInterval: TimeSpan.FromMilliseconds(5));

        await runner.RunAsync(profile, startTemperature: 20, startHumidity: null);

        // Unchanged default behaviour: a plain ramp interpolates and never reads
        // back the chamber to decide when the segment is done.
        Assert.Equal(0, device.ReadCount);
        Assert.Contains(device.WrittenTemperatures, t => t is > 20 and < 60);
        Assert.Equal(60, device.WrittenTemperatures[^1]);
    }

    /// <summary>
    /// The wait for the set point is the part a SIKA plan can only estimate, so the runner
    /// reports how long it really took – that is what the estimate gets corrected against.
    /// </summary>
    [Fact]
    public async Task SoakCompleted_reports_how_long_the_device_needed_to_reach_the_set_point()
    {
        var device = new FakeChamberDevice(startTemperature: 20, reachAfterReads: 3);
        var profile = new TestProfile
        {
            Segments =
            {
                new ProfileSegment { TargetTemperature = 60, IsRamp = false, Duration = TimeSpan.FromMilliseconds(30) },
            },
        };

        var runner = new ProfileRunner(
            device,
            updateInterval: TimeSpan.FromMilliseconds(5),
            defaultSoakTolerance: 0.3,
            soakAllSegments: true);

        var soaks = new List<ProfileSoakCompletedEventArgs>();
        runner.SoakCompleted += (_, e) => soaks.Add(e);

        await runner.RunAsync(profile, startTemperature: 20, startHumidity: null);

        ProfileSoakCompletedEventArgs soak = Assert.Single(soaks);
        Assert.Equal(0, soak.SegmentIndex);
        Assert.Equal(60, soak.ReachedTemperature);
        Assert.Equal(20, soak.StartTemperature);
        Assert.Equal(40, soak.SpanC);
        Assert.True(soak.Elapsed > TimeSpan.Zero, "the wait took measurable time");
        Assert.NotNull(soak.RateCPerMin);
    }

    /// <summary>A device already sitting on the set point has nothing to settle, so no
    /// measurable rate is reported – the estimate must not be polluted with it.</summary>
    [Fact]
    public async Task A_device_already_on_temperature_reports_no_rate()
    {
        var device = new FakeChamberDevice(startTemperature: 60, reachAfterReads: 1);
        var profile = new TestProfile
        {
            Segments =
            {
                new ProfileSegment { TargetTemperature = 60, IsRamp = false, Duration = TimeSpan.FromMilliseconds(20) },
            },
        };

        var runner = new ProfileRunner(
            device,
            updateInterval: TimeSpan.FromMilliseconds(5),
            defaultSoakTolerance: 0.3,
            soakAllSegments: true);

        var soaks = new List<ProfileSoakCompletedEventArgs>();
        runner.SoakCompleted += (_, e) => soaks.Add(e);

        await runner.RunAsync(profile, startTemperature: 60, startHumidity: null);

        ProfileSoakCompletedEventArgs soak = Assert.Single(soaks);
        Assert.Equal(0, soak.SpanC);
        Assert.Null(soak.RateCPerMin);
    }

    private sealed class FakeChamberDevice : IChamberDevice
    {
        private readonly double _startTemperature;
        private readonly int _reachAfterReads;

        public FakeChamberDevice(double startTemperature, int reachAfterReads)
        {
            _startTemperature = startTemperature;
            _reachAfterReads = reachAfterReads;
        }

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
            double lastTarget = WrittenTemperatures.Count > 0 ? WrittenTemperatures[^1] : _startTemperature;
            double measured = ReadCount >= _reachAfterReads && _reachAfterReads > 0 ? lastTarget : _startTemperature;
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
