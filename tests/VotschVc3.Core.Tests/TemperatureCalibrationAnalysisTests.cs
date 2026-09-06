using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class TemperatureCalibrationAnalysisTests
{
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

        Assert.Empty(TemperatureCalibrationAnalyzer.Analyze(run));
    }

    private static CalibrationPlateauResult Point(double temperature, double wavelength) => new()
    {
        ReferenceTemperatureC = temperature,
        Targets = [new CalibrationMeasurementResult { SerialNumber = "SN", Channel = "1", PeakId = "P1", Status = CalibrationTargetState.Stable, SampleCount = 50, MeanWavelengthNm = wavelength }],
    };
}
