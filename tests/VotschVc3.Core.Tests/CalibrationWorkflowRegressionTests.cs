using VotschVc3.Core.Calibration;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationWorkflowRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryReconnectsDisconnectedPeakLoggerAndRetriesFailedHandshake(bool failFirstHandshake)
    {
        string root = TempDirectory();
        try
        {
            await using var logger = new FakePeakLoggerClient();
            await logger.ConnectAsync(new PeakLoggerSettings());
            var setup = StableSetup(Guid.NewGuid());
            setup.Settings.OperatorSupervisionEnabled = false;
            var store = new CalibrationStore(root);
            var run = new CalibrationRunRecord { ChamberId = Guid.NewGuid() };
            await using var writer = store.CreateRunWriter(run);
            int reads = 0, peakReconnects = 0;
            var orchestrator = new CalibrationOrchestrator(logger)
            {
                ReconnectChamberAsync = _ => Task.CompletedTask,
                ReconnectPeakLoggerAsync = async token =>
                {
                    peakReconnects++;
                    if (failFirstHandshake && peakReconnects == 1)
                        throw new HttpRequestException("network still unavailable");
                    await logger.ConnectAsync(new PeakLoggerSettings(), token);
                }
            };
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(failFirstHandshake ? 12 : 7));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                orchestrator.CollectFinalVerificationAsync(setup, run, writer,
                    async _ =>
                    {
                        if (++reads == 1)
                        {
                            await logger.DisconnectAsync();
                            throw new TimeoutException("chamber and PeakLogger network lost");
                        }
                        return 25d;
                    },
                    _ => Task.FromResult<double?>(30d), cancel.Token));
            Assert.Equal(failFirstHandshake ? 2 : 1, peakReconnects);
            Assert.True(logger.IsConnected);
            Assert.True(reads > (failFirstHandshake ? 3 : 2));
            Assert.Null(run.FinalVerification);
            Assert.Empty(run.Plateaus);
        }
        finally { DeleteTempDirectory(root); }
    }
    [Fact]
    public async Task CommunicationRecoveryReconnectsAndRechecksStabilityUntilCancelled()
    {
        string root = TempDirectory();
        try
        {
            await using var logger = new FakePeakLoggerClient();
            await logger.ConnectAsync(new PeakLoggerSettings());
            var setup = StableSetup(Guid.NewGuid());
            setup.Settings.OperatorSupervisionEnabled = false;
            var store = new CalibrationStore(root);
            var run = new CalibrationRunRecord { ChamberId = Guid.NewGuid() };
            await using var writer = store.CreateRunWriter(run);
            int reads = 0, reconnects = 0;
            var orchestrator = new CalibrationOrchestrator(logger)
            {
                ReconnectChamberAsync = _ => { reconnects++; return Task.CompletedTask; }
            };
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                orchestrator.CollectFinalVerificationAsync(setup, run, writer,
                    _ => ++reads == 1 ? Task.FromException<double>(new TimeoutException("network lost")) : Task.FromResult(25d),
                    _ => Task.FromResult<double?>(30d), cancel.Token));
            Assert.Equal(1, reconnects);
            Assert.True(reads > 1);
            Assert.Null(run.FinalVerification);
            Assert.Empty(run.Plateaus);
        }
        finally { DeleteTempDirectory(root); }
    }

    [Fact]
    public async Task FinalVerificationWaitsForReferenceInsteadOfSamplingUnstableTemperature()
    {
        string root = TempDirectory();
        try
        {
            await using var logger = new FakePeakLoggerClient();
            await logger.ConnectAsync(new PeakLoggerSettings());
            var setup = StableSetup(Guid.NewGuid());
            var store = new CalibrationStore(root);
            var run = new CalibrationRunRecord { ChamberId = Guid.NewGuid() };
            await using var writer = store.CreateRunWriter(run);
            int progressCount = 0;
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new CalibrationOrchestrator(logger).CollectFinalVerificationAsync(setup, run, writer,
                    _ => Task.FromResult(25d), _ => Task.FromResult<double?>(30d), cancel.Token,
                    (count, total, reference, chamber, elapsed) => { Assert.Equal(0, count); progressCount++; }));
            Assert.True(progressCount > 0);
            Assert.Null(run.FinalVerification);
            Assert.Empty(run.Plateaus);
        }
        finally { DeleteTempDirectory(root); }
    }
    [Fact]
    public async Task FirstPlateauHasCheckpointBeforeMovementAndRefreshesWhileWaiting()
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
                Name = "Power recovery", ExecutionMode = ProfileExecutionMode.TemperatureCalibration,
                Segments = { new ProfileSegment { TargetTemperature = 20, IsCalibrationPoint = true, IsRamp = false } }
            };
            var setup = StableSetup(profile.Id);
            setup.CalibrationSegmentIndices.Add(0);
            setup.Settings.ChamberStableDuration = TimeSpan.FromMinutes(10);
            setup.Settings.ChamberStabilityTimeout = TimeSpan.FromMinutes(30);
            var store = new CalibrationStore(root);
            var run = new CalibrationRunRecord { ProfileId = profile.Id, ChamberId = Guid.NewGuid() };
            await using var writer = store.CreateRunWriter(run);
            var runner = new CalibrationProfileRunner(chamber, new CalibrationOrchestrator(peakLogger), store);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            DateTimeOffset? firstWaitingSave = null;
            bool refreshed = false;
            bool beforeMovement = false;
            chamber.BeforeWrite = () =>
            {
                var checkpoint = new CalibrationStore(root).LoadCheckpoint(run.ChamberId);
                beforeMovement = checkpoint?.RunId == run.RunId && checkpoint.Mappings.Count > 0;
            };
            runner.Progress += snapshot =>
            {
                // Fresh store emulates reopening the app; inspect disk, never in-memory state.
                var checkpoint = new CalibrationStore(root).LoadCheckpoint(run.ChamberId);
                if (snapshot.State != CalibrationRunState.WaitingForChamberStability || checkpoint is null) return;
                firstWaitingSave ??= checkpoint.SavedAt;
                if (checkpoint.SavedAt - firstWaitingSave >= TimeSpan.FromSeconds(14))
                {
                    refreshed = true;
                    cancellation.Cancel();
                }
            };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                runner.RunAsync(profile, setup, run, writer, 20, null, _ => Task.FromResult<double?>(35), cancellation.Token));
            Assert.True(beforeMovement);
            Assert.True(refreshed);
            Assert.Empty(store.LoadCheckpoint(run.ChamberId)!.CompletedPlateaus);
            Assert.NotNull(new CalibrationStore(root).LoadRun(run.RunId));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ChamberPrerequisiteKeepsWikaWindowEmptyWhileStillReadingReference()
    {
        string root = TempDirectory();
        try
        {
            await using var peakLogger = new FakePeakLoggerClient();
            await peakLogger.ConnectAsync(new PeakLoggerSettings());
            var setup = StableSetup(Guid.NewGuid());
            setup.Settings.ChamberEntryEnabled = true;
            var run = new CalibrationRunRecord { ProfileId = setup.ProfileId };
            var store = new CalibrationStore(root);
            await using var writer = store.CreateRunWriter(run);
            var orchestrator = new CalibrationOrchestrator(peakLogger);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            CalibrationProgressSnapshot? observed = null;
            int referenceReads = 0;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => orchestrator.WaitForPlateauAsync(
                run, setup, 0, 1, 20,
                _ => Task.FromResult(18d),
                _ => { referenceReads++; return Task.FromResult<double?>(20); },
                writer,
                progress: snapshot => { observed = snapshot; cancellation.Cancel(); },
                cancellationToken: cancellation.Token));
            Assert.True(referenceReads > 0);
            Assert.NotNull(observed);
            Assert.Contains("KOMORA ČAKÁ", observed!.Message);
            Assert.Equal(0, observed.TemperatureStableScoreSeconds);
            Assert.False(observed.TemperatureGateOpen);
            Assert.True(observed.ChamberEntry!.Enabled);
            Assert.False(observed.ChamberEntry.IsOpen);
            Assert.Equal(2, observed.ChamberEntry.DeviationC);
            var history = Assert.Single(run.SensorSettlingAttempts);
            Assert.Equal("Prerušené", history.Status);
            Assert.NotNull(history.Chamber.DurationSeconds);
            Assert.Null(history.Wika.DurationSeconds);
            Assert.All(history.Peaks, p => Assert.Null(p.Fbg.DurationSeconds));
            Assert.Single(store.LoadRun(run.RunId)!.SensorSettlingAttempts);
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(false, 91, 1000)]
    [InlineData(false, 61, 50)]
    [InlineData(true, 91, 1000)]
    public async Task TimeoutCollectsFlaggedSamplesOrReportsMissingReference(bool loseReference, int elapsedMinutes, int stableSamples)
    {
        string root = TempDirectory();
        try
        {
            await using var peakLogger = new FakePeakLoggerClient();
            await peakLogger.ConnectAsync(new PeakLoggerSettings());
            CalibrationSetup setup = StableSetup(Guid.NewGuid());
            setup.Settings.RequiredStableSamples = stableSamples;
            setup.Settings.RequiredMeasurementSamples = 2;
            setup.Settings.DefaultSensorStabilizationTimeout = TimeSpan.FromSeconds(1);
            setup.Settings.SensorTimeoutPolicy = CalibrationFailurePolicy.ContinueAndFlag;
            bool referenceLost = false;
            bool advanced = false;
            var clock = new ManualSensorClock();
            var run = new CalibrationRunRecord { ProfileId = setup.ProfileId, ProfileName = "Flagged sampling" };
            var store = new CalibrationStore(root);
            await using CalibrationRunWriter writer = store.CreateRunWriter(run);
            var orchestrator = new CalibrationOrchestrator(peakLogger, clock);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            CalibrationPlateauResult plateau = await orchestrator.WaitForPlateauAsync(
                run, setup, 0, 1, 20,
                _ => Task.FromResult(20d),
                _ => Task.FromResult<double?>(referenceLost ? 30 : 20),
                writer,
                progress: snapshot =>
                {
                    if (snapshot.TemperatureGateOpen == true && !advanced)
                    {
                        advanced = true;
                        referenceLost = loseReference;
                        clock.Advance(TimeSpan.FromMinutes(loseReference ? 1 : elapsedMinutes));
                    }
                    else if (referenceLost) clock.Advance(TimeSpan.FromMinutes(91));
                },
                cancellationToken: timeout.Token);

            CalibrationMeasurementResult target = Assert.Single(plateau.Targets);
            Assert.Equal(loseReference ? CalibrationTargetState.TimedOut : CalibrationTargetState.CompletedWithStabilityWarning, target.Status);
            Assert.Equal(loseReference ? 0 : 2, target.SampleCount);
            Assert.Equal(target.SampleCount, target.StableSamples.Count);
            Assert.Contains("stabilita nepotvrdená", target.Problem);
            Assert.Contains("odber finálnych vzoriek", target.Problem);
            Assert.Contains(run.Warnings, warning => warning.Code == "SENSOR_STABILITY_TIMEOUT");
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task ValidFinalSamplesEarnOneAuditedExtensionAndFinishNormally()
    {
        string root = TempDirectory();
        try
        {
            await using var peakLogger = new FakePeakLoggerClient();
            await peakLogger.ConnectAsync(new PeakLoggerSettings());
            CalibrationSetup setup = StableSetup(Guid.NewGuid());
            setup.Settings.RequiredMeasurementSamples = 3;
            var clock = new ManualSensorClock();
            var run = new CalibrationRunRecord { ProfileId = setup.ProfileId };
            await using CalibrationRunWriter writer = new CalibrationStore(root).CreateRunWriter(run);
            var orchestrator = new CalibrationOrchestrator(peakLogger, clock);
            bool advanced = false;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await orchestrator.WaitForPlateauAsync(run, setup, 0, 1, 20,
                _ => Task.FromResult(20d), null, writer,
                progress: snapshot =>
                {
                    if (!advanced && snapshot.Targets.Any(t => t.MeasurementSamples == 1))
                    {
                        advanced = true;
                        clock.Advance(TimeSpan.FromSeconds(10));
                    }
                }, cancellationToken: cancellation.Token);
            Assert.Equal(CalibrationTargetState.Stable, Assert.Single(result.Targets).Status);
            Assert.Single(run.Warnings, warning => warning.Code == "SENSOR_STABILITY_TIMEOUT_EXTENDED");
            Assert.DoesNotContain(run.Warnings, warning => warning.Code == "SENSOR_STABILITY_TIMEOUT");
        }
        finally { DeleteTempDirectory(root); }
    }
    [Fact]
    public async Task Running_plateau_applies_changed_stability_limits_and_audits_reset()
    {
        string root = TempDirectory();
        try
        {
            await using var peakLogger = new FakePeakLoggerClient();
            await peakLogger.ConnectAsync(new PeakLoggerSettings());
            CalibrationSetup setup = StableSetup(Guid.NewGuid());
            setup.Settings.RequiredStableSamples = 100;
            setup.Settings.DefaultSensorStabilizationTimeout = TimeSpan.FromSeconds(15);
            var run = new CalibrationRunRecord { ProfileId = setup.ProfileId, ProfileName = "Runtime settings" };
            var store = new CalibrationStore(root);
            await using CalibrationRunWriter writer = store.CreateRunWriter(run);
            var orchestrator = new CalibrationOrchestrator(peakLogger);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            Task<CalibrationPlateauResult> active = orchestrator.WaitForPlateauAsync(
                run, setup, 0, 1, 20,
                _ => Task.FromResult(20d),
                null,
                writer,
                cancellationToken: timeout.Token);

            await Task.Delay(1200, timeout.Token);
            setup.Settings.RequiredStableSamples = 2;
            CalibrationPlateauResult result = await active;

            Assert.Single(result.Targets);
            Assert.Equal(CalibrationTargetState.Stable, result.Targets[0].Status);
            Assert.Contains(run.Warnings, warning => warning.Code == "STABILITY_SETTINGS_CHANGED" && warning.Message.Contains("100 → 2"));
            var history = Assert.Single(run.SensorSettlingAttempts);
            Assert.True(history.CriteriaChanged);
            Assert.Equal("Dokončené", history.Status);
            Assert.Null(history.Wika.DurationSeconds);
            Assert.Equal("Úspešné", Assert.Single(history.Peaks).Fbg.Status);
            Assert.True(history.Peaks[0].Fbg.Seconds < result.Targets[0].StabilizationTime.TotalSeconds);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

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

            runner.Progress += snapshot => { if (snapshot.State == CalibrationRunState.PlateauCompleted) setup.Settings.EnableSetpointRamp = false; };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await runner.RunAsync(profile, setup, run, writer, 40, null,
                cancellationToken: timeout.Token, resumeFrom: checkpoint);

            Assert.NotEmpty(chamber.WrittenTemperatures);
            Assert.Equal(40, chamber.WrittenTemperatures[0], 6);
            Assert.Equal(25, chamber.WrittenTemperatures[^1]);
            Assert.All(chamber.WrittenTemperatures.SkipLast(1), temperature => Assert.True(temperature >= 40));
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
                    new ProfileSegment
                    {
                        Name = "Unchecked final 25 C calibration plateau",
                        IsRamp = false,
                        IsCalibrationPoint = true,
                        TargetTemperature = 25,
                    },
                },
            };
            var setup = StableSetup(profile.Id);
            setup.CalibrationSegmentIndices.Add(1);
            setup.Settings.FinalConditioningDuration = TimeSpan.FromHours(1); // Legacy saved value must not impose a hold.
            chamber.FinalConditioningReadOffsetC = 0;

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
            CalibrationProgressSnapshot completed = Assert.Single(updates, u => u.State == CalibrationRunState.PlateauCompleted);
            Assert.Equal(plateau.Targets.Count, completed.Targets.Count);
            Assert.Equal(plateau.Targets[0].Status, completed.Targets[0].State);
            Assert.Equal(plateau.Targets[0].SampleCount, completed.Targets[0].MeasurementSamples);
            var dashboard = new VotschVc3.App.ViewModels.CalibrationDashboardViewModel();
            dashboard.Configure("Test", "Komora", new[] { 40d }, true, "");
            dashboard.Apply(completed, DateTimeOffset.Now);
            Assert.Equal("✓ ÚSPEŠNÉ", dashboard.Points[0].Badge);
            Assert.Contains("Stabilita 1/1", dashboard.Points[0].Detail);
            Assert.Single(plateau.Targets);
            Assert.DoesNotContain(run.Plateaus, item => Math.Abs(item.TargetTemperatureC - 25) < 0.001);
            Assert.Equal(25, chamber.WrittenTemperatures[^1], 6);
            Assert.Equal(25, run.FinalConditioningTemperatureC, 6);
            Assert.NotNull(run.FinalConditioningStartedAt);
            Assert.NotNull(run.FinalConditioningCompletedAt);
            Assert.Equal(TimeSpan.Zero, run.FinalConditioningRequiredDuration);
            Assert.NotNull(run.FinalVerification);
            Assert.All(run.FinalVerification.Targets, target => Assert.Equal(CalibrationTargetState.Stable, target.Status));
            Assert.Equal(1, chamber.StopCount);
            Assert.Contains(updates, update => update.State == CalibrationRunState.FinalConditioning &&
                                               update.PlateauIndex == -1 &&
                                               Math.Abs(update.TargetTemperatureC - 25) < 0.001 &&
                                               update.Targets.Count > 0);
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
            Assert.Equal(CalibrationRunState.CompletedWithWarnings, run.State);
            Assert.NotNull(run.FinalVerification);
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
            await runner.RunAsync(profile, setup, run, writer, 20, null, _ => Task.FromResult<double?>(chamber.WrittenTemperatures.LastOrDefault() == 25 ? 25 : 35), timeout.Token);

            Assert.Equal(CalibrationRunState.CompletedWithWarnings, run.State);
            Assert.Contains(run.Warnings, warning => warning.Code == "TEMPERATURE_STABILITY_FORCED");
            Assert.Single(run.Plateaus);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Theory]
    [InlineData(CalibrationOperatorDecision.Skip)]
    [InlineData(CalibrationOperatorDecision.Extend15)]
    [InlineData(CalibrationOperatorDecision.Extend30)]
    [InlineData(CalibrationOperatorDecision.Retry)]
    public async Task Supervision_SensorDeadlineCollectsFlaggedDataWithoutBlocking(CalibrationOperatorDecision decision)
    {
        string root = TempDirectory();
        try
        {
            await using var peakLogger = new FakePeakLoggerClient();
            await peakLogger.ConnectAsync(new PeakLoggerSettings());
            var setup = StableSetup(Guid.NewGuid());
            setup.Settings.OperatorSupervisionEnabled = true;
            var clock = new ManualSensorClock();
            var run = new CalibrationRunRecord { ProfileId = setup.ProfileId, Operator = "Test operator" };
            var store = new CalibrationStore(root);
            await using var writer = store.CreateRunWriter(run);
            var orchestrator = new CalibrationOrchestrator(peakLogger, clock);
            int requests = 0;
            bool advanced = false;
            orchestrator.OperatorAttentionRequired += request =>
            {
                requests++;
                Assert.Equal(CalibrationRunState.AwaitingOperator, run.State);
                Assert.NotNull(run.OperatorDecisionDeadline);
                Assert.Equal(CalibrationOperatorIssue.Sensors, request.Issue);
                Assert.True(request.Submit(decision, "Overené operátorom"));
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var plateau = await orchestrator.WaitForPlateauAsync(run, setup, 0, 1, 20,
                _ => Task.FromResult(20d), _ => Task.FromResult<double?>(20d), writer,
                progress: snapshot =>
                {
                    if (!advanced && snapshot.TemperatureGateOpen == true)
                    {
                        advanced = true;
                        clock.Advance(TimeSpan.FromMinutes(91));
                    }
                }, cancellationToken: timeout.Token);
            Assert.Equal(0, requests);
            Assert.Null(run.PendingOperatorIssue);
            Assert.Null(run.OperatorDecisionDeadline);
            var target = Assert.Single(plateau.Targets);
            Assert.Equal(CalibrationTargetState.CompletedWithStabilityWarning, target.Status);
            Assert.Contains(run.Warnings, warning => warning.Code == "SENSOR_STABILITY_TIMEOUT");
        }
        finally { DeleteTempDirectory(root); }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Supervision_StopOrMissingOperatorSendsChamberStop(bool attachOperator, bool expire)
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
                Name = "Supervised stop", ExecutionMode = ProfileExecutionMode.TemperatureCalibration,
                Segments = { new ProfileSegment { Name = "20 C", IsRamp = false, IsCalibrationPoint = true, TargetTemperature = 20 } },
            };
            var setup = StableSetup(profile.Id);
            setup.Settings.OperatorSupervisionEnabled = true;
            setup.Settings.ChamberStabilityTimeout = TimeSpan.FromMilliseconds(1);
            setup.CalibrationSegmentIndices.Add(0);
            var run = new CalibrationRunRecord { ProfileId = profile.Id, ChamberId = Guid.NewGuid() };
            var store = new CalibrationStore(root);
            await using var writer = store.CreateRunWriter(run);
            var orchestrator = new CalibrationOrchestrator(peakLogger);
            if (attachOperator) orchestrator.OperatorAttentionRequired += request =>
            {
                if (expire) _ = request.WaitAsync(CancellationToken.None, TimeSpan.FromMilliseconds(5));
                else request.Submit(CalibrationOperatorDecision.Stop, "Ukončiť");
            };
            var runner = new CalibrationProfileRunner(chamber, orchestrator, store);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<CalibrationSupervisionStoppedException>(() =>
                runner.RunAsync(profile, setup, run, writer, 20, null, _ => Task.FromResult<double?>(chamber.WrittenTemperatures.LastOrDefault() == 25 ? 25 : 35), timeout.Token));
            Assert.Equal(1, chamber.StopCount);
            Assert.Equal(CalibrationRunState.Aborted, run.State);
            Assert.NotNull(run.CompletedAt);
            Assert.Null(run.PendingOperatorIssue);
            Assert.True(run.OperatorSupervisionEnabled);
            Assert.Empty(run.Plateaus);
        }
        finally { DeleteTempDirectory(root); }
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Supervision_RechecksReferenceAfterRetryOrExtension(bool communicationFailure)
    {
        string root = TempDirectory();
        try
        {
            await using var peakLogger = new FakePeakLoggerClient();
            await peakLogger.ConnectAsync(new PeakLoggerSettings());
            var setup = StableSetup(Guid.NewGuid());
            setup.Settings.OperatorSupervisionEnabled = true;
            setup.Settings.ChamberStabilityTimeout = TimeSpan.FromMilliseconds(1);
            var run = new CalibrationRunRecord { ProfileId = setup.ProfileId };
            var store = new CalibrationStore(root);
            await using var writer = store.CreateRunWriter(run);
            var orchestrator = new CalibrationOrchestrator(peakLogger);
            bool recovered = false;
            int requests = 0;
            orchestrator.OperatorAttentionRequired += request =>
            {
                requests++;
                Assert.Equal(communicationFailure ? CalibrationOperatorIssue.Communication : CalibrationOperatorIssue.Temperature, request.Issue);
                recovered = true;
                Assert.True(request.Submit(communicationFailure ? CalibrationOperatorDecision.Retry : CalibrationOperatorDecision.Extend15, "Referencia obnovená"));
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var plateau = await orchestrator.WaitForPlateauAsync(run, setup, 0, 1, 20,
                _ => Task.FromResult(20d),
                _ => !recovered && communicationFailure ? Task.FromException<double?>(new IOException("WIKA offline")) : Task.FromResult<double?>(recovered ? 20 : 35),
                writer, cancellationToken: timeout.Token);
            Assert.Equal(1, requests);
            Assert.Equal(CalibrationTargetState.Stable, Assert.Single(plateau.Targets).Status);
            Assert.Equal(20, plateau.ReferenceTemperatureC);
        }
        finally { DeleteTempDirectory(root); }
    }

    [Fact]
    public async Task Supervision_ValidationRetryArchivesAttemptAndSkipNeverApprovesIt()
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
                Name = "Validation retry", ExecutionMode = ProfileExecutionMode.TemperatureCalibration,
                Segments =
                {
                    new ProfileSegment { Name = "20 C", IsRamp = false, IsCalibrationPoint = true, TargetTemperature = 20 },
                    new ProfileSegment { Name = "30 C", IsRamp = false, IsCalibrationPoint = true, TargetTemperature = 30 },
                },
            };
            var setup = StableSetup(profile.Id);
            setup.Settings.OperatorSupervisionEnabled = true;
            setup.Settings.AllowValidationOverride = true; // Saved overrides must not bypass supervision.
            setup.Settings.ValidationMinimumWavelengthResponsePm = 100000;
            setup.CalibrationSegmentIndices.AddRange(new[] { 0, 1 });
            var run = new CalibrationRunRecord { ProfileId = profile.Id, ChamberId = Guid.NewGuid() };
            var store = new CalibrationStore(root);
            await using var writer = store.CreateRunWriter(run);
            var orchestrator = new CalibrationOrchestrator(peakLogger);
            int requests = 0;
            orchestrator.OperatorAttentionRequired += request =>
            {
                Assert.Equal(CalibrationOperatorIssue.Validation, request.Issue);
                Assert.True(request.Submit(++requests == 1 ? CalibrationOperatorDecision.Retry : CalibrationOperatorDecision.Skip, "Overená odozva"));
            };
            var runner = new CalibrationProfileRunner(chamber, orchestrator, store);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await runner.RunAsync(profile, setup, run, writer, 20, null, cancellationToken: timeout.Token);
            Assert.Equal(2, requests);
            Assert.Equal(2, run.Plateaus.Count);
            Assert.Single(run.SupersededPlateaus);
            Assert.Equal(CalibrationTargetState.NoTemperatureResponse, Assert.Single(run.Plateaus[1].Targets).Status);
            Assert.Equal(CalibrationRunState.CompletedWithWarnings, run.State);
        }
        finally { DeleteTempDirectory(root); }
    }
    private sealed class ManualSensorClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
    private static CalibrationSetup StableSetup(Guid profileId) => new()
    {
        ProfileId = profileId,
        Settings = new CalibrationProfileSettings
        {
            EnableSetpointRamp = false,
            SampleAcquisitionIntervalSeconds = 1,
            RequiredStableSamples = 2,
            RequiredMeasurementSamples = 2,
            MaxWavelengthRangePm = 0,
            MaxWavelengthStdDevPm = 0,
            MaxWavelengthDriftPmPerMinute = 0,
            ChamberToleranceC = 0.5,
            ChamberStableDuration = TimeSpan.Zero,
            MaxChamberDriftCPerMinute = 0,
            ChamberStabilityTimeout = TimeSpan.FromSeconds(5),
            FinalConditioningDuration = TimeSpan.Zero,
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
        public Action? BeforeWrite { get; set; }
        public double FinalConditioningReadOffsetC { get; set; }
        public int StopCount { get; private set; }
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
            _temperature = _setpoint + (Math.Abs(_setpoint - 25) < 0.001 ? FinalConditioningReadOffsetC : 0);
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
                BeforeWrite?.Invoke();
                _setpoint = setpoints[0];
                _temperature = _setpoint;
                WrittenTemperatures.Add(_setpoint);
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            return Task.CompletedTask;
        }

        public Task<string> SendRawAsync(string frame, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
