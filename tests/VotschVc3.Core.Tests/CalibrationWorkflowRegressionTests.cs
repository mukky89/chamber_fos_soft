using VotschVc3.Core.Calibration;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationWorkflowRegressionTests
{
    [Fact]
    public async Task Runner_ResumePreservesCompletedPlateausAndStartsAtFirstUnfinishedPlateau()
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
                Name = "Resume calibration",
                ExecutionMode = ProfileExecutionMode.TemperatureCalibration,
                Segments =
                {
                    new ProfileSegment { Name = "Plateau 1", TargetTemperature = 20, IsRamp = false, IsCalibrationPoint = true },
                    new ProfileSegment { Name = "Plateau 2", TargetTemperature = 30, IsRamp = false, IsCalibrationPoint = true },
                },
            };
            CalibrationSetup setup = StableSetup(profile.Id);
            setup.CalibrationSegmentIndices.Add(0);
            setup.CalibrationSegmentIndices.Add(1);
            Guid chamberId = Guid.NewGuid();
            var completed = new CalibrationPlateauResult
            {
                PlateauIndex = 0,
                TargetTemperatureC = 20,
                ActualTemperatureC = 20,
                StartedAt = DateTimeOffset.Now.AddMinutes(-2),
                CompletedAt = DateTimeOffset.Now.AddMinutes(-1),
            };
            var run = new CalibrationRunRecord { ProfileId = profile.Id, ProfileName = profile.Name, ChamberId = chamberId };
            var checkpoint = new CalibrationCheckpoint
            {
                RunId = run.RunId,
                ProfileId = profile.Id,
                ChamberId = chamberId,
                CurrentPlateauIndex = 0,
                CurrentTargetTemperatureC = 20,
                State = CalibrationRunState.PlateauCompleted,
                CompletedPlateaus = { completed },
                Mappings = setup.Mappings.ToList(),
            };
            var store = new CalibrationStore(root);
            await using CalibrationRunWriter writer = store.CreateRunWriter(run);
            var runner = new CalibrationProfileRunner(chamber, new CalibrationOrchestrator(peakLogger), store, TimeSpan.FromMilliseconds(10));
            var updates = new List<CalibrationProgressSnapshot>();
            runner.Progress += updates.Add;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await runner.RunAsync(profile, setup, run, writer, 20, null,
                cancellationToken: timeout.Token, resumeFrom: checkpoint);

            Assert.Equal(2, run.Plateaus.Count);
            Assert.Same(completed, run.Plateaus[0]);
            Assert.Equal(30, run.Plateaus[1].TargetTemperatureC, 6);
            Assert.DoesNotContain(updates, update => update.PlateauIndex == 0 && update.State == CalibrationRunState.MovingToPlateau);
            Assert.Contains(updates, update => update.PlateauIndex == 1 && update.State == CalibrationRunState.MovingToPlateau);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task Runner_ResumeRampStartsFromFreshChamberTemperature_NotCheckpointTarget()
    {
        string root = TempDirectory();
        try
        {
            await using var peakLogger = new FakePeakLoggerClient();
            await peakLogger.ConnectAsync(new PeakLoggerSettings());
            await using var chamber = new StableFakeChamber(40);
            await chamber.ConnectAsync(new ChamberConnectionSettings());

            var profile = new TestProfile
            {
                Name = "Resume ramp safety",
                ExecutionMode = ProfileExecutionMode.TemperatureCalibration,
                Segments =
                {
                    new ProfileSegment { Name = "Plateau 1", TargetTemperature = 30, IsRamp = false, IsCalibrationPoint = true },
                    new ProfileSegment { Name = "Plateau 2", TargetTemperature = 40, IsRamp = false, IsCalibrationPoint = true },
                },
            };
            CalibrationSetup setup = StableSetup(profile.Id);
            setup.Settings.EnableSetpointRamp = true;
            setup.Settings.SetpointRampCPerMinute = 1;
            setup.CalibrationSegmentIndices.Add(0);
            setup.CalibrationSegmentIndices.Add(1);
            Guid chamberId = Guid.NewGuid();
            var completed = new CalibrationPlateauResult
            {
                PlateauIndex = 0,
                TargetTemperatureC = 30,
                ActualTemperatureC = 30,
                StartedAt = DateTimeOffset.Now.AddMinutes(-2),
                CompletedAt = DateTimeOffset.Now.AddMinutes(-1),
            };
            var run = new CalibrationRunRecord { ProfileId = profile.Id, ProfileName = profile.Name, ChamberId = chamberId };
            var checkpoint = new CalibrationCheckpoint
            {
                RunId = run.RunId,
                ProfileId = profile.Id,
                ChamberId = chamberId,
                CurrentPlateauIndex = 0,
                CurrentTargetTemperatureC = 30,
                State = CalibrationRunState.PlateauCompleted,
                CompletedPlateaus = { completed },
                Mappings = setup.Mappings.ToList(),
            };
            var store = new CalibrationStore(root);
            await using CalibrationRunWriter writer = store.CreateRunWriter(run);
            var runner = new CalibrationProfileRunner(chamber, new CalibrationOrchestrator(peakLogger), store, TimeSpan.FromMilliseconds(10));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await runner.RunAsync(profile, setup, run, writer, 40, null,
                cancellationToken: timeout.Token, resumeFrom: checkpoint);

            Assert.NotEmpty(chamber.WrittenTemperatures);
            Assert.Equal(40, chamber.WrittenTemperatures[0], 6);
            Assert.DoesNotContain(chamber.WrittenTemperatures, temperature => temperature < 40);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

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
            Assert.Contains(updates, s => s.PlateauIndex == 0 && s.Message.Contains("nábeh", StringComparison.OrdinalIgnoreCase));
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
            setup.Settings.ChamberStabilityExtensionStep = TimeSpan.FromMilliseconds(100);
            setup.Settings.MaxAutomaticChamberStabilityExtension = TimeSpan.FromMilliseconds(200);

            var store = new CalibrationStore(root);
            var run = new CalibrationRunRecord
            {
                ProfileId = profile.Id,
                ProfileName = profile.Name,
                ChamberId = Guid.NewGuid(),
            };
            await using CalibrationRunWriter writer = store.CreateRunWriter(run);
            var orchestrator = new CalibrationOrchestrator(peakLogger);
            var warnings = new List<CalibrationWarning>();
            orchestrator.WarningRaised += warnings.Add;
            var runner = new CalibrationProfileRunner(chamber, orchestrator, store);

            CalibrationOperatorActionRequiredException ex = await Assert.ThrowsAsync<CalibrationOperatorActionRequiredException>(
                () => runner.RunAsync(
                    profile,
                    setup,
                    run,
                    writer,
                    20,
                    null,
                    _ => Task.FromResult<double?>(35.0),
                    CancellationToken.None,
                    resumeFrom: null));

            Assert.Equal("REFERENCE_STABILITY_TIMEOUT", ex.Warning.Code);
            Assert.Equal(2, warnings.Count(warning => warning.Code == "REFERENCE_STABILITY_TIMEOUT_EXTENDED"));
            Assert.Contains("maximálnom čase", ex.Message);
            Assert.Contains("Automatický postup bol bezpečne zastavený", ex.Message);
            Assert.Empty(run.Plateaus);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task Runner_DefersUnstablePlateau_CompletesNextPlateau_ThenRetriesOnce()
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
                Name = "Deferred plateau",
                ExecutionMode = ProfileExecutionMode.TemperatureCalibration,
                Segments =
                {
                    new ProfileSegment { Name = "20 C", IsRamp = false, IsCalibrationPoint = true, TargetTemperature = 20 },
                    new ProfileSegment { Name = "30 C", IsRamp = false, IsCalibrationPoint = true, TargetTemperature = 30 },
                },
            };
            var setup = StableSetup(profile.Id);
            setup.CalibrationSegmentIndices.Add(0);
            setup.CalibrationSegmentIndices.Add(1);
            setup.Settings.RequiredStableSamples = 1;
            setup.Settings.RequiredMeasurementSamples = 1;
            setup.Settings.ChamberStabilityTimeout = TimeSpan.FromMilliseconds(100);
            setup.Settings.MaxAutomaticChamberStabilityExtension = TimeSpan.Zero;
            var store = new CalibrationStore(root);
            var run = new CalibrationRunRecord { ProfileId = profile.Id, ProfileName = profile.Name, ChamberId = Guid.NewGuid() };
            await using CalibrationRunWriter writer = store.CreateRunWriter(run);
            var orchestrator = new CalibrationOrchestrator(peakLogger);
            var warningCodes = new List<string>();
            orchestrator.WarningRaised += warning => warningCodes.Add(warning.Code);
            var runner = new CalibrationProfileRunner(chamber, orchestrator, store, TimeSpan.FromMilliseconds(10));
            var movedTo = new List<int>();
            runner.Progress += snapshot =>
            {
                if (snapshot.State == CalibrationRunState.MovingToPlateau)
                    movedTo.Add(snapshot.PlateauIndex);
            };

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            CalibrationOperatorActionRequiredException failure = await Assert.ThrowsAsync<CalibrationOperatorActionRequiredException>(() =>
                runner.RunAsync(profile, setup, run, writer, 20, null,
                    _ => Task.FromResult<double?>(chamber.WrittenTemperatures.LastOrDefault() >= 25 ? 30 : 35),
                    timeout.Token, resumeFrom: null));

            Assert.Equal(new[] { 0, 1, 0 }, movedTo);
            Assert.Contains("REFERENCE_STABILITY_DEFERRED", warningCodes);
            Assert.Equal("REFERENCE_STABILITY_TIMEOUT", failure.Warning.Code);
            Assert.Contains(run.Plateaus, plateau => plateau.PlateauIndex == 1);
            CalibrationCheckpoint checkpoint = Assert.IsType<CalibrationCheckpoint>(store.LoadCheckpoint(run.ChamberId));
            Assert.Contains(0, checkpoint.DeferredPlateauIndices);
            Assert.Contains(checkpoint.CompletedPlateaus, plateau => plateau.PlateauIndex == 1);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task Runner_ReferenceControlCorrectsOnlyOutsideToleranceAndKeepsStepBounded()
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
                Name = "WIKA correction",
                ExecutionMode = ProfileExecutionMode.TemperatureCalibration,
                Segments = { new ProfileSegment { Name = "20 C", IsRamp = false, IsCalibrationPoint = true, TargetTemperature = 20 } },
            };
            Guid chamberId = Guid.NewGuid();
            var setup = StableSetup(profile.Id);
            setup.ChamberId = chamberId;
            setup.CalibrationSegmentIndices.Add(0);
            setup.Settings.ChamberStabilityTimeout = TimeSpan.FromMilliseconds(200);
            setup.Settings.MaxAutomaticChamberStabilityExtension = TimeSpan.Zero;
            CalibrationReferenceControlRegistry.Configure(chamberId, new CalibrationReferenceControlOptions(
                true, 0.35, 0.05, 3.0, 0.30, TimeSpan.FromSeconds(10)));

            var store = new CalibrationStore(root);
            var run = new CalibrationRunRecord { ProfileId = profile.Id, ProfileName = profile.Name, ChamberId = chamberId };
            await using CalibrationRunWriter writer = store.CreateRunWriter(run);
            var runner = new CalibrationProfileRunner(chamber, new CalibrationOrchestrator(peakLogger), store);

            await Assert.ThrowsAsync<CalibrationOperatorActionRequiredException>(() => runner.RunAsync(
                profile, setup, run, writer, 20, null,
                _ => Task.FromResult<double?>(20.6), CancellationToken.None, resumeFrom: null));

            Assert.True(chamber.WrittenTemperatures.Count >= 2);
            Assert.Equal(20, chamber.WrittenTemperatures[0], 6);
            Assert.InRange(chamber.WrittenTemperatures[1], 19.70, 19.999999);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task Runner_OperatorCanForceCurrentTemperatureGate_AndOverrideIsAudited()
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
                Name = "Forced WIKA gate",
                ExecutionMode = ProfileExecutionMode.TemperatureCalibration,
                Segments = { new ProfileSegment { Name = "20 C", IsRamp = false, IsCalibrationPoint = true, TargetTemperature = 20 } },
            };
            var setup = StableSetup(profile.Id);
            setup.CalibrationSegmentIndices.Add(0);
            setup.Settings.ChamberStabilityTimeout = TimeSpan.FromSeconds(10);
            var store = new CalibrationStore(root);
            var run = new CalibrationRunRecord { ProfileId = profile.Id, ProfileName = profile.Name, ChamberId = Guid.NewGuid() };
            await using CalibrationRunWriter writer = store.CreateRunWriter(run);
            var runner = new CalibrationProfileRunner(chamber, new CalibrationOrchestrator(peakLogger), store);
            bool requested = false;
            runner.Progress += snapshot =>
            {
                if (!requested && snapshot.State == CalibrationRunState.WaitingForChamberStability)
                {
                    requested = true;
                    runner.RequestTemperatureGateOverride();
                }
            };

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await runner.RunAsync(profile, setup, run, writer, 20, null, _ => Task.FromResult<double?>(35), timeout.Token);

            Assert.Equal(CalibrationRunState.CompletedWithWarnings, run.State);
            Assert.Contains(run.Warnings, warning => warning.Code == "TEMPERATURE_STABILITY_FORCED");
            Assert.Single(run.Plateaus);
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
            EnableSetpointRamp = false,
            RequiredStableSamples = 2,
            RequiredMeasurementSamples = 2,
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
        public List<double> WrittenTemperatures { get; } = new();
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
                WrittenTemperatures.Add(_setpoint);
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
