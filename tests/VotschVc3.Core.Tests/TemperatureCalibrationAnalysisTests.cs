using VotschVc3.Core.Calibration;
using Xunit;
using ClosedXML.Excel;

namespace VotschVc3.Core.Tests;

public sealed class TemperatureCalibrationAnalysisTests
{
    [Fact]
    public void WorkbookLimitPercentageUsesRowToleranceAndPreservesResultColumns()
    {
        var run = new CalibrationRunRecord();
        foreach (double temperature in new[] { 20d, 30d, 40d, 50d, 60d })
            run.Plateaus.Add(new CalibrationPlateauResult
            {
                ReferenceTemperatureC = temperature,
                Targets = [new CalibrationMeasurementResult
                {
                    SerialNumber = "SN", Channel = "1", PeakId = "P1", SampleCount = 50,
                    Status = CalibrationTargetState.Stable, MeanWavelengthNm = 1550 + temperature * 0.01,
                }],
            });
        string directory = Path.Combine(Path.GetTempPath(), "calibration-limit-" + Guid.NewGuid().ToString("N"));
        try
        {
            TemperatureCalibrationAnalyzer.Export(run, directory);
            using var book = new XLWorkbook(Path.Combine(directory, "calibration-coefficients.xlsx"));
            var sheet = book.Worksheet("Koeficienty");
            Assert.Equal("Limit [°C]", sheet.Cell("T4").GetString());
            Assert.Equal("Limit [% rozsahu]", sheet.Cell("U4").GetString());
            Assert.Equal(0.01, sheet.Cell("U5").GetDouble(), 8);
            Assert.Equal("0.###%", sheet.Cell("U5").Style.NumberFormat.Format);
            Assert.Equal("Výsledok", sheet.Cell("W4").GetString());
            Assert.Equal(run.CalibrationResults[0].Result, sheet.Cell("W5").GetString());
            Assert.Equal(34, sheet.AutoFilter.Range.ColumnCount());
            sheet.Cell("T5").Value = 1;
            book.RecalculateAllFormulas();
            Assert.Equal(0.025, sheet.Cell("U5").GetDouble(), 8);
            sheet.Cell("I5").Value = sheet.Cell("H5").Value;
            book.RecalculateAllFormulas();
            Assert.Equal("N/A", sheet.Cell("U5").GetString());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void LinearSensorProducesPaliCompatibleReferenceSensitivityAndPassResult()
    {
        double[] temperatures = [-20, 0, 20, 40, 60];
        var run = new CalibrationRunRecord();
        for (int index = 0; index < temperatures.Length; index++)
        {
            double temperature = temperatures[index];
            run.Plateaus.Add(new CalibrationPlateauResult
            {
                PlateauIndex = index,
                ReferenceTemperatureC = temperature,
                Targets =
                [
                    new CalibrationMeasurementResult
                    {
                        SerialNumber = "289594/0001",
                        Channel = "1.3",
                        PeakId = "P1",
                        PeakIndex = 1,
                        Status = CalibrationTargetState.Stable,
                        SampleCount = 50,
                        MeanWavelengthNm = 1550 + 0.01 * temperature,
                    },
                ],
            });
        }

        List<TemperatureCalibrationResult> results = TemperatureCalibrationAnalyzer.Analyze(run);
        TemperatureCalibrationResult result = Assert.Single(results, item => item.CalibrationType == "2nd · ABC");
        Assert.Single(results, item => item.CalibrationType == "3rd · ABCD");

        Assert.Equal(1550.225, result.LambdaTRefNm, 6);
        Assert.Equal(10, result.SensitivityPmPerC, 6);
        Assert.True(result.MaxErrorC < 1e-6);
        Assert.Equal(1, result.RSquared, 8);
        Assert.Equal("PASS", result.Result);
    }

    [Fact]
    public void CurvedSensorAlsoProducesFbgsS1S2Calibration()
    {
        double[] temperatures = [-20, 0, 20, 40, 60, 80];
        var run = new CalibrationRunRecord();
        for (int index = 0; index < temperatures.Length; index++)
        {
            double temperature = temperatures[index];
            run.Plateaus.Add(new CalibrationPlateauResult
            {
                ReferenceTemperatureC = temperature,
                Targets = [new CalibrationMeasurementResult
                {
                    SerialNumber = "FBGS", Channel = "1", PeakId = "P1", Status = CalibrationTargetState.Stable,
                    SampleCount = 50, MeanWavelengthNm = 1550 * Math.Exp(7e-6 * (temperature - 22.5) + 1e-8 * Math.Pow(temperature - 22.5, 2)),
                }],
            });
        }

        TemperatureCalibrationResult result = Assert.Single(
            TemperatureCalibrationAnalyzer.Analyze(run), item => item.CalibrationType == "FBGS · s1/s2");

        Assert.NotNull(result.CoefficientS1);
        Assert.NotNull(result.CoefficientS2);
        Assert.True(result.MaxErrorC < 0.1);
        Assert.Equal("PASS", result.Result);
    }

    [Fact]
    public void CompletedSamplingWithStabilityWarningIsAnalyzedAndMarked()
    {
        double[] temperatures = [-20, 0, 20, 40];
        var run = new CalibrationRunRecord();
        for (int index = 0; index < temperatures.Length; index++)
        {
            double temperature = temperatures[index];
            run.Plateaus.Add(new CalibrationPlateauResult
            {
                ReferenceTemperatureC = temperature,
                Targets = [new CalibrationMeasurementResult
                {
                    SerialNumber = "WARN-SN", Channel = "1", PeakId = "P1", SampleCount = 50,
                    Status = index == 1 ? CalibrationTargetState.CompletedWithStabilityWarning : CalibrationTargetState.Stable,
                    Problem = index == 1 ? "Peak mal problém so stabilizáciou." : null,
                    MeanWavelengthNm = 1550 + 0.01 * temperature,
                }],
            });
        }

        List<TemperatureCalibrationResult> results = TemperatureCalibrationAnalyzer.Analyze(run);

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.Equal("PROBLÉM", result.StabilityStatus));
        Assert.All(results, result => Assert.Contains("problém so stabilizáciou", result.StabilityProblem));
        Assert.Contains(results, result => result.Result == "PASS");
    }

    [Fact]
    public void FewerThanThreeTemperaturesDoesNotProduceMisleadingCoefficients()
    {
        var run = new CalibrationRunRecord
        {
            Plateaus =
            [
                Point(0, 1550),
                Point(20, 1550.2),
            ],
        };

        var result = Assert.Single(TemperatureCalibrationAnalyzer.Analyze(run));
        Assert.Equal("N/A", result.Result);
        Assert.Null(result.CoefficientA);
        Assert.Contains("tri rôzne", result.StabilityProblem);
    }

    private static CalibrationPlateauResult Point(double temperature, double wavelength) => new()
    {
        ReferenceTemperatureC = temperature,
        Targets = [new CalibrationMeasurementResult { SerialNumber = "SN", Channel = "1", PeakId = "P1", Status = CalibrationTargetState.Stable, SampleCount = 50, MeanWavelengthNm = wavelength }],
    };
}
