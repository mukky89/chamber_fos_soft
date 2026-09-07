using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationExportCultureTests
{
    [Fact]
    public void SummaryAndCoefficientCsv_UseSlovakDecimalComma()
    {
        string directory = Path.Combine(Path.GetTempPath(), "VotschVc3-sk-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var run = new CalibrationRunRecord
            {
                ProfileName = "T CAL",
                Plateaus =
                {
                    new CalibrationPlateauResult
                    {
                        TargetTemperatureC = 25.5,
                        ActualTemperatureC = 25.25,
                        Targets = { new CalibrationMeasurementResult { MeanWavelengthNm = 1550.125 } },
                    },
                },
            };
            string summaryPath = Path.Combine(directory, "summary.csv");
            CalibrationStore.ExportSummaryCsv(run, summaryPath);

            string coefficientPath = Path.Combine(directory, "coefficients.csv");
            TemperatureCalibrationAnalyzer.ExportCsv(
                [new TemperatureCalibrationResult { MinimumTemperatureC = -20.5, LambdaTRefNm = 1550.125 }],
                coefficientPath);

            Assert.Contains(";25,5;25,25;", File.ReadAllText(summaryPath));
            Assert.Contains(";-20,5;", File.ReadAllText(coefficientPath));
            Assert.Contains(";1550,125;", File.ReadAllText(coefficientPath));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RawCalibrationCsv_UsesSlovakDecimalComma()
    {
        string root = Path.Combine(Path.GetTempPath(), "VotschVc3-sk-raw-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CalibrationStore(root);
            var run = new CalibrationRunRecord();
            await using (CalibrationRunWriter writer = store.CreateRunWriter(run))
            {
                await writer.AppendAsync([new CalibrationRawSample
                {
                    RunId = run.RunId,
                    ProfileId = run.ProfileId,
                    TargetTemperatureC = 25.5,
                    ActualTemperatureC = 25.25,
                    WavelengthNm = 1550.125,
                }]);
            }

            string path = Path.Combine(store.RunsDirectory, run.RunId.ToString("N"), "raw-samples.csv");
            string content = File.ReadAllText(path);
            Assert.Contains(";25,5;25,25;", content);
            Assert.Contains(";1550,125;", content);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
