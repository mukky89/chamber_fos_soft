using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationRunResumeStorageTests
{
    [Fact]
    public void CreateFromHistoricalRun_PreservesCompletedPlateausAndSetup()
    {
        Guid profileId = Guid.NewGuid();
        Guid chamberId = Guid.NewGuid();
        var run = new CalibrationRunRecord
        {
            ProfileId = profileId,
            ChamberId = chamberId,
            State = CalibrationRunState.Aborted,
            Plateaus =
            {
                new CalibrationPlateauResult { PlateauIndex = 0, TargetTemperatureC = -40 },
                new CalibrationPlateauResult { PlateauIndex = 1, TargetTemperatureC = -30 },
            },
        };
        var setup = new CalibrationSetup
        {
            ProfileId = profileId,
            ChamberId = chamberId,
            CalibrationSegmentIndices = new List<int> { 1, 3, 5 },
            Mappings =
            {
                new CalibrationSensorMapping
                {
                    Selected = true,
                    SerialNumber = "289594/0001",
                    Channel = "1.3",
                    PeakId = "P1",
                },
            },
        };
        setup.Settings.RequiredMeasurementSamples = 75;

        CalibrationCheckpoint checkpoint = CalibrationCheckpointRecovery.CreateFromHistoricalRun(run, setup);

        Assert.Equal(run.RunId, checkpoint.RunId);
        Assert.Equal(2, checkpoint.CompletedPlateaus.Count);
        Assert.Equal(2, checkpoint.CurrentPlateauIndex);
        Assert.Equal(-30, checkpoint.CurrentTargetTemperatureC);
        Assert.Equal(new[] { 1, 3, 5 }, checkpoint.CalibrationSegmentIndices);
        Assert.Single(checkpoint.Mappings);
        Assert.Equal(75, checkpoint.SettingsSnapshot!.RequiredMeasurementSamples);
    }

    [Fact]
    public void CreateFromHistoricalRun_RejectsRunWithoutSavedWiring()
    {
        Guid profileId = Guid.NewGuid();
        Guid chamberId = Guid.NewGuid();
        var run = new CalibrationRunRecord
        {
            ProfileId = profileId,
            ChamberId = chamberId,
            Plateaus = { new CalibrationPlateauResult { PlateauIndex = 0 } },
        };
        var setup = new CalibrationSetup { ProfileId = profileId, ChamberId = chamberId };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => CalibrationCheckpointRecovery.CreateFromHistoricalRun(run, setup));

        Assert.Contains("zapojenie", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateFromHistoricalRun_RebuildsMissingSetupMappingsFromMeasuredTargets()
    {
        Guid profileId = Guid.NewGuid();
        Guid chamberId = Guid.NewGuid();
        var run = new CalibrationRunRecord
        {
            ProfileId = profileId,
            ChamberId = chamberId,
            Plateaus =
            {
                new CalibrationPlateauResult
                {
                    PlateauIndex = 28,
                    Targets =
                    {
                        new CalibrationMeasurementResult
                        {
                            SerialNumber = "289594/0001",
                            PeakLoggerDeviceSerialNumber = "SIACCT",
                            Channel = "1.3",
                            PeakId = "P1",
                            PeakIndex = 1,
                            MeanWavelengthNm = 1552.3685,
                        },
                    },
                },
            },
        };
        var setup = new CalibrationSetup
        {
            ProfileId = profileId,
            ChamberId = chamberId,
            CalibrationSegmentIndices = new List<int> { 1, 3, 5 },
        };

        CalibrationCheckpoint checkpoint = CalibrationCheckpointRecovery.CreateFromHistoricalRun(run, setup);

        CalibrationSensorMapping mapping = Assert.Single(checkpoint.Mappings);
        Assert.True(mapping.Selected);
        Assert.Equal("289594/0001", mapping.SerialNumber);
        Assert.Equal("SIACCT", mapping.PeakLoggerDeviceSerialNumber);
        Assert.Equal("1.3", mapping.Channel);
        Assert.Equal("P1", mapping.PeakId);
        Assert.Equal(1552.3685, mapping.CurrentWavelengthNm);
    }

    [Fact]
    public async Task AppendWriter_PreservesExistingRunFilesAndDoesNotDuplicateHeaders()
    {
        string root = Path.Combine(Path.GetTempPath(), "VotschVc3-resume-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CalibrationStore(root);
            var run = new CalibrationRunRecord { ProfileName = "Resume", ChamberName = "Chamber" };
            await using (CalibrationRunWriter first = store.CreateRunWriter(run))
            {
                first.WriteDiagnostic("INFO", "BEFORE_RESTART", "preserve");
                await first.AppendWavelengthTraceAsync(new[]
                {
                    new CalibrationWavelengthTraceSample
                    {
                        RunId = run.RunId,
                        Timestamp = DateTimeOffset.Now,
                        SerialNumber = "123456/0001",
                        PeakLoggerDeviceSerialNumber = "logger",
                        Channel = "1.1",
                        PeakId = "P1",
                        PeakIndex = 1,
                        WavelengthNm = 1550,
                    },
                });
            }

            await using (CalibrationRunWriter resumed = store.CreateRunWriter(run, append: true))
                resumed.WriteDiagnostic("INFO", "AFTER_RESTART", "continued");

            string directory = Path.Combine(store.RunsDirectory, run.RunId.ToString("N"));
            string diagnostics = File.ReadAllText(Path.Combine(directory, "diagnostics.log"));
            string[] wavelengthLines = File.ReadAllLines(Path.Combine(directory, "wavelength-trace.csv"));
            Assert.Contains("BEFORE_RESTART", diagnostics);
            Assert.Contains("AFTER_RESTART", diagnostics);
            Assert.Contains("RUN_LOG_RESUMED", diagnostics);
            Assert.Equal(2, wavelengthLines.Length);
            Assert.Single(wavelengthLines, line => line.StartsWith("RunId;", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
