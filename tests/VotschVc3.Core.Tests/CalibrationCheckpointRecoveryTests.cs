using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationCheckpointRecoveryTests
{
    [Fact]
    public void HistoricalRecoveryQueuesUnmeasuredPointsAndPreservesOriginalResults()
    {
        var setup = new CalibrationSetup { ProfileId = Guid.NewGuid(), ChamberId = Guid.NewGuid(),
            Mappings = { new CalibrationSensorMapping { Selected = true, SerialNumber = "123456/0001" } } };
        var run = new CalibrationRunRecord { ProfileId = setup.ProfileId, ChamberId = setup.ChamberId,
            State = CalibrationRunState.CompletedWithWarnings };
        run.Plateaus.Add(new CalibrationPlateauResult { PlateauIndex = 0,
            Targets = { new CalibrationMeasurementResult { Status = CalibrationTargetState.Stable, SampleCount = 30 } } });
        run.Plateaus.Add(new CalibrationPlateauResult { PlateauIndex = 5,
            Targets = { new CalibrationMeasurementResult { Status = CalibrationTargetState.SkippedIdentityUncertain } } });
        var checkpoint = CalibrationCheckpointRecovery.CreateFromHistoricalRun(run, setup);
        Assert.Equal(new[] { 5 }, checkpoint.DeferredPlateauIndices);
        Assert.Equal(2, checkpoint.CompletedPlateaus.Count);
        Assert.Same(run.Plateaus[1], checkpoint.CompletedPlateaus[1]);
        Assert.Null(checkpoint.OperatorIdentityConfirmation);
    }

    [Fact]
    public void CorruptedCheckpointFallsBackAndDeliberateDeleteRemovesBothCopies()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CalibrationStore(root);
            var checkpoint = new CalibrationCheckpoint { ChamberId = Guid.NewGuid(), RunId = Guid.NewGuid(), CurrentPlateauIndex = 1 };
            store.SaveCheckpoint(checkpoint);
            checkpoint.CurrentPlateauIndex = 2;
            store.SaveCheckpoint(checkpoint);
            string path = Path.Combine(store.CheckpointsDirectory, checkpoint.ChamberId.ToString("N") + ".json");
            Assert.Equal(2, new CalibrationStore(root).LoadCheckpoint(checkpoint.ChamberId)!.CurrentPlateauIndex);
            File.WriteAllText(path, "{interrupted");
            Assert.Equal(1, new CalibrationStore(root).LoadCheckpoint(checkpoint.ChamberId)!.CurrentPlateauIndex);
            store.DeleteCheckpoint(checkpoint.ChamberId);
            Assert.Null(new CalibrationStore(root).LoadCheckpoint(checkpoint.ChamberId));
            Assert.False(File.Exists(path + ".bak"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CorruptedSummaryFallsBackToDurablePreviousSummary()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CalibrationStore(root);
            var run = new CalibrationRunRecord { ProfileName = "Recovery" };
            store.SaveRun(run);
            run.ProfileName = "Updated";
            store.SaveRun(run);
            File.WriteAllText(Path.Combine(store.GetRunDirectory(run), "summary.json"), "{interrupted");
            Assert.Equal("Recovery", new CalibrationStore(root).LoadRun(run.RunId)!.ProfileName);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void EmptyDiscoveredRows_AreReplacedByCheckpointSerialNumberMappings()
    {
        var setup = new CalibrationSetup
        {
            Mappings =
            {
                new CalibrationSensorMapping
                {
                    PeakLoggerDeviceSerialNumber = "SIACCT",
                    Channel = "1.3",
                    PeakId = "P1",
                    Selected = false,
                },
            },
        };
        var checkpoint = new CalibrationCheckpoint
        {
            Mappings =
            {
                new CalibrationSensorMapping
                {
                    SerialNumber = "289594/0001",
                    ChannelSerialNumber = "289594/0001",
                    PeakLoggerDeviceSerialNumber = "SIACCT",
                    Channel = "1.3",
                    PeakId = "P1",
                    PeakIndex = 1,
                    Selected = true,
                },
            },
        };

        bool restored = CalibrationCheckpointRecovery.RestoreMappingsIfMissing(setup, checkpoint);

        CalibrationSensorMapping mapping = Assert.Single(setup.Mappings);
        Assert.True(restored);
        Assert.True(mapping.Selected);
        Assert.Equal("289594/0001", mapping.SerialNumber);
        Assert.Equal("SIACCT|1.3|P1", mapping.SourceIdentity);
    }

    [Fact]
    public void ExistingOperatorSelection_IsNeverOverwrittenByCheckpoint()
    {
        var setup = new CalibrationSetup
        {
            Mappings = { new CalibrationSensorMapping { SerialNumber = "CURRENT/0001", Selected = true } },
        };
        var checkpoint = new CalibrationCheckpoint
        {
            Mappings = { new CalibrationSensorMapping { SerialNumber = "OLDER/0001", Selected = true } },
        };

        Assert.False(CalibrationCheckpointRecovery.RestoreMappingsIfMissing(setup, checkpoint));
        Assert.Equal("CURRENT/0001", Assert.Single(setup.Mappings).SerialNumber);
    }

    [Fact]
    public void Resume_RestoresExactDecisionSettingsAndPlateauSelection()
    {
        var setup = new CalibrationSetup
        {
            CalibrationSegmentIndices = { 1 },
            Settings = new CalibrationProfileSettings
            {
                ChamberStableDuration = TimeSpan.FromMinutes(1),
                RequiredStableSamples = 10,
                RequiredMeasurementSamples = 10,
                SampleAcquisitionIntervalSeconds = 30,
            },
        };
        var checkpoint = new CalibrationCheckpoint
        {
            CalibrationSegmentIndices = { 3, 5, 7 },
            SettingsSnapshot = new CalibrationProfileSettings
            {
                ChamberStableDuration = TimeSpan.FromMinutes(10),
                RequiredStableSamples = 50,
                RequiredMeasurementSamples = 50,
                SampleAcquisitionIntervalSeconds = 1,
            },
        };

        Assert.True(CalibrationCheckpointRecovery.RestoreRunConfiguration(setup, checkpoint));
        Assert.Equal(new[] { 3, 5, 7 }, setup.CalibrationSegmentIndices);
        Assert.Equal(TimeSpan.FromMinutes(10), setup.Settings.ChamberStableDuration);
        Assert.Equal(50, setup.Settings.RequiredStableSamples);
        Assert.Equal(50, setup.Settings.RequiredMeasurementSamples);
        Assert.Equal(1, setup.Settings.SampleAcquisitionIntervalSeconds);

        checkpoint.SettingsSnapshot.RequiredStableSamples = 999;
        Assert.Equal(50, setup.Settings.RequiredStableSamples);
    }

    [Fact]
    public void LegacyCheckpoint_LeavesPersistedSettingsUntouched()
    {
        var setup = new CalibrationSetup
        {
            Settings = new CalibrationProfileSettings { RequiredStableSamples = 73 },
        };

        Assert.False(CalibrationCheckpointRecovery.RestoreRunConfiguration(setup, new CalibrationCheckpoint()));
        Assert.Equal(73, setup.Settings.RequiredStableSamples);
    }

    [Fact]
    public void CurrentDefaults_CanBeExplicitlyAppliedWithoutChangingCompletedWorkOrWiring()
    {
        var completed = new CalibrationPlateauResult { PlateauIndex = 4, TargetTemperatureC = 20 };
        var mapping = new CalibrationSensorMapping { SerialNumber = "295441/0001", Selected = true };
        var setup = new CalibrationSetup
        {
            Settings = new CalibrationProfileSettings { SampleAcquisitionIntervalSeconds = 1 },
            Mappings = { mapping },
        };
        var checkpoint = new CalibrationCheckpoint
        {
            SettingsSnapshot = new CalibrationProfileSettings { SampleAcquisitionIntervalSeconds = 1 },
            CompletedPlateaus = { completed },
            Mappings = { mapping },
        };
        var defaults = new CalibrationProfileSettings
        {
            ChamberToleranceC = 1,
            ChamberStableDuration = TimeSpan.FromMinutes(5),
            MaxChamberDriftCPerMinute = 0.03,
            SampleAcquisitionIntervalSeconds = 10,
        };

        CalibrationCheckpointRecovery.ApplyCurrentDefaults(setup, checkpoint, defaults);

        Assert.Equal(10, setup.Settings.SampleAcquisitionIntervalSeconds);
        Assert.Equal(TimeSpan.FromMinutes(5), checkpoint.SettingsSnapshot!.ChamberStableDuration);
        Assert.Equal(0.03, checkpoint.SettingsSnapshot.MaxChamberDriftCPerMinute);
        Assert.Same(completed, Assert.Single(checkpoint.CompletedPlateaus));
        Assert.Same(mapping, Assert.Single(checkpoint.Mappings));

        defaults.SampleAcquisitionIntervalSeconds = 30;
        Assert.Equal(10, setup.Settings.SampleAcquisitionIntervalSeconds);
        Assert.Equal(10, checkpoint.SettingsSnapshot.SampleAcquisitionIntervalSeconds);
    }
}
