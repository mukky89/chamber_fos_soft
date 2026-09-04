using VotschVc3.Core.Calibration;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationWorkflowRegressionTests
{
    [Fact]
    public async Task Runner_UsesOnlyExplicitUiSelectedCalibrationPlateaus_AndSkipsProfileRamp()
    {
        string root = TempDirectory();
        try
        {
            await using var peakLogger = new FakePeakLoggerClient();
            await peakLogger.ConnectAsync(new PeakLoggerSettings());
            await using var chamber = new StableFakeChamber(20);
            await chamber.ConnectAsync(new ChamberConnectionSettings());

            var profile = new TestProfile
            {
                Name = "Explicit plateau selection",
                ExecutionMode = ProfileExecutionMode.TemperatureCalibration,
                Segments =
                {
                    new ProfileSegment
                    {
                        Name = "Initial ramp",
                        IsRamp = true,
                        IsCalibrationPoint = true,
                        TargetTemperature = 20,
                        Duration = TimeSpan.FromHours(1),
                    },
                    new ProfileSegment
                    {
                        Name = "Calibrate this",
                        IsRamp = false,
                        IsCalibrationPoint = true,
                        TargetTemperature = 40,
                        Duration = TimeSpan.FromHours(8),
                    },
                },
            };
            var setup = StableSetup(profile.Id);
            setup.CalibrationSegmentIndices.Add(1);

            var store = new CalibrationStore(root);
            var run = new CalibrationRunRecord
            {
                ProfileId = profile.Id,
                ProfileName = profile.Name,
                ChamberId = Guid.NewGuid(),
            };
            await using CalibrationRunWriter writer = store.CreateRunWriter(run);
            var runner = new CalibrationProfileRunner(chamber, new CalibrationOrchestrator(peakLogger), store, TimeSpan.FromMilliseconds(10));
            var updates = new List<CalibrationProgressSnapshot>();
            runner.Progress += updates.Add;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await runner.RunAsync(profile, setup, run, writer, 20, null, cancellationToken: timeout.Token);

            Assert.Equal(CalibrationRunState.Preflight, updates[0].State);
            Assert.DoesNotContain(updates, s => s.PlateauIndex == -1 && s.Message.Contains("Do konca časového kroku", StringComparison.Ordinal));
            Assert.Contains(updates, s => s.PlateauIndex == 0 && s.Message.Contains("rampy", StringComparison.OrdinalIgnoreCase));
            CalibrationPlateauResult plateau = Assert.Single(run.Plateaus);
            Assert.Equal(40, plateau.TargetTemperatureC, 6);
            Assert.Single(plateau.Targets);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task Runner_IgnoresProfileHoldDurationForSelectedFbgCalibrationPoint()
    {
        string root = TempDirectory();
        try
        {
            await using var peakLogger = new FakePeakLoggerClient();
            await peakLogger.ConnectAsync(new PeakLoggerSettings());
            await using var chamber = new StableFakeChamber(20);
            await chamber.ConnectAsync(new ChamberConnectionSettings());

            var profile = new TestProfile
            {
                Name = "Profile duration must not gate FBG calibration",
                ExecutionMode = ProfileExecutionMode.TemperatureCalibration,
                Segments =
                {
                    new ProfileSegment
                    {
                        Name = "20 C with intentionally huge profile hold",
                        IsRamp = false,
                        IsCalibrationPoint = true,
                        TargetTemperature = 20,
                        Duration = TimeSpan.FromHours(8),
                    },
                },
            };
            var setup = StableSetup(profile.Id);
            setup.CalibrationSegmentIndices.Add(0);

            var store = new CalibrationStore(root);
            var run = new CalibrationRunRecord
            {
                ProfileId = profile.Id,
                ProfileName = profile.Name,
                ChamberId = Guid.NewGuid(),
            };
            await using CalibrationRunWriter writer = store.CreateRunWriter(run);
            var runner = new CalibrationProfileRunner(chamber, new CalibrationOrchestrator(peakLogger), store);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));

            await runner.RunAsync(profile, setup, run, writer, 20, null, cancellationToken: timeout.Token);

            CalibrationPlateauResult plateau = Assert.Single(run.Plateaus);
            Assert.Equal(20, plateau.TargetTemperatureC, 6);
            Assert.Equal(CalibrationTargetState.Stable, Assert.Single(plateau.Targets).Status);
            Assert.Equal(CalibrationRunState.Completed, run.State);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task Runner_WithReference_DoesNotStartPeakStabilityUntilWikaIsStable()
    {
        string root = TempDirectory();
        try
        {
            await using var peakLogger = new FakePeakLoggerClient();
            await peakLogger.ConnectAsync(new PeakLoggerSettings());
            await using var chamber = new StableFakeChamber(20);
            await chamber.ConnectAsync(new ChamberConnectionSettings());

            var profile = new TestProfile
            {
                Name = "WIKA gate",
                ExecutionMode = ProfileExecutionMode.TemperatureCalibration,
                Segments =
                {
                    new ProfileSegment
                    {
                        Name = "20 C",
                        IsRamp = false,
                        IsCalibrationPoint = true,
                        TargetTemperature = 20,
                        Duration = TimeSpan.FromMilliseconds(1),
                    },
                },
            };
            var setup = StableSetup(profile.Id);
            setup.CalibrationSegmentIndices.Add(0);
            setup.Settings.ChamberStableDuration = TimeSpan.Zero;
            setup.Settings.ChamberStabilityTimeout = TimeSpan.FromMilliseconds(200);

            var store = new CalibrationStore(root);
            var run = new CalibrationRunRecord
            {
                ProfileId = profile.Id,
                ProfileName = profile.Name,
                ChamberId = Guid.NewGuid(),
            };
            await using CalibrationRunWriter writer = store.CreateRunWriter(run);
            var runner = new CalibrationProfileRunner(chamber, new CalibrationOrchestrator(peakLogger), store);

            CalibrationOperatorActionRequiredException ex = await Assert.ThrowsAsync<CalibrationOperatorActionRequiredException>(
                () => runner.RunAsync(
                    profile,
                    setup,
                    run,
                    writer,
                    20,
                    null,
                    _ => Task.FromResult<double?>(35.0)));

            Assert.Equal("REFERENCE_STABILITY_TIMEOUT", ex.Warning.Code);
            Assert.Empty(run.Plateaus);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static CalibrationSetup StableSetup(Guid profileId) => new()
    {
        ProfileId = profileId,
        Settings = new CalibrationProfileSettings
        {
            RequiredStableSamples = 2,
            MaxWavelengthRangePm = 0,
            MaxWavelengthStdDevPm = 0,
            MaxWavelengthDriftPmPerMinute = 0,
            ChamberToleranceC = 0.5,
            ChamberStableDuration = TimeSpan.Zero,
            MaxChamberDriftCPerMinute = 0,
            ChamberStabilityTimeout = TimeSpan.FromSeconds(5),
            DefaultSensorStabilizationTimeout = TimeSpan.FromSeconds(5),
            SensorTimeoutPolicy = CalibrationFailurePolicy.AbortCalibration,
            PeakLostPolicy = CalibrationFailurePolicy.AbortCalibration,
            PeakLoggerDisconnectPolicy = CalibrationFailurePolicy.AbortCalibration,
        },
        Mappings =
        {
            new CalibrationSensorMapping
            {
                SerialNumber = "123456/0001",
                PeakLoggerDeviceSerialNumber = "242805A000004",
                Channel = "3.2",
                PeakId = "P4",
                PeakIndex = 4,
                Selected = true,
            },
        },
    };

    private static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), "VotschVc3-cal-regression-" + Guid.NewGuid().ToString("N"));

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private sealed class StableFakeChamber : IChamberDevice
    {
        private double _temperature;
        private double _setpoint;

        public StableFakeChamber(double initialTemperature)
        {
            _temperature = initialTemperature;
            _setpoint = initialTemperature;
        }

        public bool IsConnected { get; private set; }
        public ChamberConnectionSettings Settings { get; private set; } = new();
        public event EventHandler<FrameExchangedEventArgs>? FrameExchanged;

        public Task ConnectAsync(ChamberConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            Settings = settings.Clone();
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<ChamberReading> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _temperature = _setpoint;
            return Task.FromResult(new ChamberReading(
                DateTimeOffset.Now,
                "fake",
                new[] { _temperature, _setpoint },
                new DigitalChannels { StartChannelIndex = Settings.StartChannelIndex }));
        }

        public Task WriteSetpointsAsync(
            IReadOnlyList<double> setpoints,
            DigitalChannels digital,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (setpoints.Count > 0)
            {
                _setpoint = setpoints[0];
                _temperature = _setpoint;
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> SendRawAsync(string frame, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
