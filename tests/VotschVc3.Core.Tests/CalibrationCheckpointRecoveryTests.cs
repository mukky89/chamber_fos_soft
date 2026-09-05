using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationCheckpointRecoveryTests
{
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
}
