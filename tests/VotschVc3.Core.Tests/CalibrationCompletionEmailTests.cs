using System.Net;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Notifications;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationCompletionEmailTests
{
    [Fact]
    public void MissingServerPathNeverFallsBackToLocalFiles()
    {
        var run = new CalibrationRunRecord { LocalRunDirectory = @"C:\private\run" };
        var message = CalibrationCompletionEmail.Create(run, null);
        Assert.Empty(message.Attachments);
        Assert.Contains("Serverový priečinok nie je nastavený", message.Text);
        Assert.DoesNotContain(@"C:\private", message.Html);
    }
    [Fact]
    public void CreatesPassFailTableWithServerLinkAndNoAttachments()
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
                CalibrationResults = [
                    new TemperatureCalibrationResult
                    {
                        SerialNumber = "289594/0001", Channel = "1.3", PeakId = "P1", CalibrationType = "3rd · ABCD",
                        LambdaTRefNm = 1550.123456, SensitivityPmPerC = 10.2, CoefficientA = 1.2, CoefficientB = 2.3,
                        CoefficientC = 3.4, CoefficientD = 4.5, MaxErrorC = 0.12, RSquared = 0.9999, Result = "PASS",
                    },
                    new TemperatureCalibrationResult
                    {
                        SerialNumber = "289594/0001", Channel = "1.3", PeakId = "P2", CalibrationType = "FBGS · s1/s2",
                        LambdaTRefNm = 1551.654321, SensitivityPmPerC = 10.4, CoefficientS1 = 0.01, CoefficientS2 = 0.02,
                        MaxErrorC = 0.08, RSquared = 0.9998, Result = "PASS",
                    },
                ],
            };

            CalibrationCompletionMessage message = CalibrationCompletionEmail.Create(run, @"G:\Kalibrácie & výsledky\09_September\run 01");

            string html = WebUtility.HtmlDecode(message.Html);
            Assert.Contains("PASS S UPOZORNENIAMI", html);
            Assert.Contains("289594/0001", html);
            Assert.Contains("Nestabilný", html);
            Assert.Contains("50", html);
            Assert.Contains("°C", html);
            Assert.Contains("Otvoriť priečinok behu na serveri", html);
            Assert.Contains("Kalibračné koeficienty", html);
            Assert.Contains("3rd · ABCD", html);
            Assert.Contains("1.3 / P2 · FBGS · s1/s2", html);
            Assert.Contains("A=1,2", html);
            Assert.Contains("s1=0,01", html);
            Assert.Contains("0,9999", html);
            string coefficientTable = html[(html.IndexOf("Kalibračné koeficienty", StringComparison.Ordinal))..
                html.IndexOf("Súbory kalibrácie", StringComparison.Ordinal)];
            Assert.Equal(1, coefficientTable.Split("289594/0001").Length - 1);
            Assert.Equal(1, coefficientTable.Split("<tbody><tr>").Length - 1);
            Assert.Empty(message.Attachments);
            Assert.Contains(@"G:\Kalibrácie & výsledky\09_September\run 01", message.Text);
            Assert.Contains("file:///G:/", message.Html);
            Assert.DoesNotContain(directory, message.Text);
            Assert.DoesNotContain("ZIP", message.Html);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
