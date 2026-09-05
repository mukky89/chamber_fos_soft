using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationRunResumeStorageTests
{
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
