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
            File.WriteAllText(Path.Combine(directory, "calibration-coefficients.csv"), "coefficients");
            File.WriteAllBytes(Path.Combine(directory, "calibration-coefficients.xlsx"), [1, 2, 3]);
            string reportDirectory = Path.Combine(directory, "reports", "plato-001");
            Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory, "kalibracny-bod.xlsx"), "xlsx");
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
                CalibrationResults = [new TemperatureCalibrationResult
                {
                    SerialNumber = "289594/0001", Channel = "1.3", PeakId = "P1", CalibrationType = "3rd · ABCD",
                    LambdaTRefNm = 1550.123456, SensitivityPmPerC = 10.2, CoefficientA = 1.2, CoefficientB = 2.3,
                    CoefficientC = 3.4, CoefficientD = 4.5, MaxErrorC = 0.12, RSquared = 0.9999, Result = "PASS",
                }],
            };

            CalibrationCompletionMessage message = CalibrationCompletionEmail.Create(run, directory);

            string html = WebUtility.HtmlDecode(message.Html);
            Assert.Contains("PASS S UPOZORNENIAMI", html);
            Assert.Contains("289594/0001", html);
            Assert.Contains("Nestabilný", html);
            Assert.Contains("50", html);
            Assert.Contains("°C", html);
            Assert.Contains("Otvoriť lokálny priečinok behu", html);
            Assert.Contains("Kalibračné koeficienty", html);
            Assert.Contains("3rd · ABCD", html);
            Assert.Contains("A=1.2", html);
            Assert.Contains("0.9999", html);
            Assert.Equal(4, message.Attachments.Count);
            Assert.Equal("calibration-results.csv", message.Attachments[0].FileName);
            Assert.Equal("calibration-coefficients.csv", message.Attachments[1].FileName);
            Assert.Equal("calibration-coefficients.xlsx", message.Attachments[2].FileName);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", message.Attachments[2].MediaType);
            using var zip = new ZipArchive(new MemoryStream(message.Attachments[3].Content), ZipArchiveMode.Read);
            Assert.Equal(8, zip.Entries.Count);
            Assert.Contains(zip.Entries, entry => entry.FullName == "raw-samples.csv");
            Assert.Contains(zip.Entries, entry => entry.FullName == "diagnostics.log");
            Assert.Contains(zip.Entries, entry => entry.FullName == "reports/plato-001/kalibracny-bod.xlsx");
            Assert.Contains(zip.Entries, entry => entry.FullName == "calibration-coefficients.xlsx");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
