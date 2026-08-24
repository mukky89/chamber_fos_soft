using VotschVc3.Core.Communication;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>
/// Crash-recovery: resuming an interrupted profile run from a saved checkpoint
/// (<see cref="ProfileRunPosition"/> / <see cref="ProfileRunState"/>).
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

    [Fact]
    public void RunState_position_and_completed_duration_round_trip()
    {
        var state = new ProfileRunState
        {
            ChamberId = Guid.NewGuid(),
            Profiles =
            {
                new TestProfile
                {
                    Cycles = 2,
                    Segments =
                    {
                        new ProfileSegment { Duration = TimeSpan.FromMinutes(10) },
                        new ProfileSegment { Duration = TimeSpan.FromMinutes(20) },
                    },
                },
                new TestProfile
                {
                    Segments = { new ProfileSegment { Duration = TimeSpan.FromMinutes(15) } },
                },
            },
            ProfileIndex = 1,
            Cycle = 0,
            SegmentIndex = 0,
            ElapsedInSegmentSeconds = 300,
            SegmentStartTemperature = 42.5,
        };

        ProfileRunPosition position = state.ToPosition();
        Assert.Equal(TimeSpan.FromMinutes(5), position.ElapsedInSegment);
        Assert.Equal(42.5, position.SegmentStartTemperature);

        // Whole first profile (60 min) + 5 min of the second one.
        Assert.Equal(TimeSpan.FromMinutes(65), state.CompletedDuration());
        Assert.Equal(TimeSpan.FromMinutes(75), state.TotalDuration());
    }

    [Fact]
    public void RunStateStore_saves_loads_and_deletes_per_chamber()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vc3_runstate_{Guid.NewGuid():N}.json");
        try
        {
            var store = new ProfileRunStateStore(path);
            var chamberA = Guid.NewGuid();
            var chamberB = Guid.NewGuid();

            Assert.Null(store.Load(chamberA));

            store.Save(new ProfileRunState
            {
                ChamberId = chamberA,
                Profiles = { new TestProfile { Name = "Mrazenie", Segments = { new ProfileSegment { TargetTemperature = -20 } } } },
                Cycle = 1,
                SegmentIndex = 2,
                ElapsedInSegmentSeconds = 12.5,
                SegmentStartTemperature = -5,
                SegmentStartHumidity = 45,
            });
            store.Save(new ProfileRunState { ChamberId = chamberB });

            ProfileRunState? loaded = store.Load(chamberA);
            Assert.NotNull(loaded);
            Assert.Equal("Mrazenie", loaded!.CurrentProfile?.Name);
            Assert.Equal(1, loaded.Cycle);
            Assert.Equal(2, loaded.SegmentIndex);
            Assert.Equal(12.5, loaded.ElapsedInSegmentSeconds);
            Assert.Equal(-5, loaded.SegmentStartTemperature);
            Assert.Equal(45, loaded.SegmentStartHumidity);

            // Saving again replaces (no duplicates), deleting removes only that chamber.
            store.Save(new ProfileRunState { ChamberId = chamberA, Cycle = 3 });
            Assert.Equal(3, store.Load(chamberA)!.Cycle);
            Assert.True(store.Delete(chamberA));
            Assert.Null(store.Load(chamberA));
            Assert.NotNull(store.Load(chamberB));
            Assert.False(store.Delete(chamberA));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RunStateStore_survives_corrupt_file()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vc3_runstate_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ this is not json");
            var store = new ProfileRunStateStore(path);
            Assert.Null(store.Load(Guid.NewGuid()));
        }
        finally
        {
            File.Delete(path);
        }
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
