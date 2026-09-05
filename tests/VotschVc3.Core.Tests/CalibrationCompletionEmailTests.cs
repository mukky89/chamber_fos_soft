using System.IO.Compression;
using System.Net;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Notifications;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationCompletionEmailTests
{
    [Fact]
    public void CreatesPassFailTableAndAttachesAllRunFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"calibration-email-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "summary.csv"), "result");
            File.WriteAllText(Path.Combine(directory, "raw-samples.csv"), "raw");
            File.WriteAllText(Path.Combine(directory, "wavelength-trace.csv"), "trace");
            File.WriteAllText(Path.Combine(directory, "diagnostics.log"), "log");
            File.WriteAllText(Path.Combine(directory, "summary.json"), "{}");
            var run = new CalibrationRunRecord
            {
                HumanRunId = "01-2026-09-05", ProfileCode = "P-001", ProfileName = "Výrobná kalibrácia",
                ChamberName = "Komora 1", Operator = "Operátor", State = CalibrationRunState.CompletedWithWarnings,
                ReferenceThermometerPort = "COM7", ReferenceThermometerChannel = "A", ReferenceThermometerSerialNumber = "WIKA-1",
                StartedAt = DateTimeOffset.Now.AddHours(-1), CompletedAt = DateTimeOffset.Now,
                Plateaus = [new CalibrationPlateauResult { PlateauIndex = 0, TargetTemperatureC = 50, ReferenceTemperatureC = 50.01,
                    Targets = [
                        new CalibrationMeasurementResult { SerialNumber = "289594/0001", Channel = "1.3", PeakId = "P1", Status = CalibrationTargetState.Stable, MeanWavelengthNm = 1552.1, SampleCount = 50 },
                        new CalibrationMeasurementResult { SerialNumber = "289594/0002", Channel = "2.3", PeakId = "P1", Status = CalibrationTargetState.TimedOut, Problem = "Nestabilný", MeanWavelengthNm = 1551.2, SampleCount = 20 },
                    ] }],
            };

            CalibrationCompletionMessage message = CalibrationCompletionEmail.Create(run, directory);

            string html = WebUtility.HtmlDecode(message.Html);
            Assert.Contains("PASS S UPOZORNENIAMI", html);
            Assert.Contains("289594/0001", html);
            Assert.Contains("Nestabilný", html);
            Assert.Contains("50", html);
            Assert.Contains("°C", html);
            Assert.Contains("Otvoriť lokálny priečinok behu", html);
            Assert.Equal(2, message.Attachments.Count);
            Assert.Equal("calibration-results.csv", message.Attachments[0].FileName);
            using var zip = new ZipArchive(new MemoryStream(message.Attachments[1].Content), ZipArchiveMode.Read);
            Assert.Equal(5, zip.Entries.Count);
            Assert.Contains(zip.Entries, entry => entry.FullName == "raw-samples.csv");
            Assert.Contains(zip.Entries, entry => entry.FullName == "diagnostics.log");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
