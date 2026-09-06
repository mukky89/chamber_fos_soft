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

        TemperatureCalibrationResult result = Assert.Single(TemperatureCalibrationAnalyzer.Analyze(run));

        Assert.Equal(1550.225, result.LambdaTRefNm, 6);
        Assert.Equal(10, result.SensitivityPmPerC, 6);
        Assert.True(result.MaxErrorC < 1e-6);
        Assert.Equal(1, result.RSquared, 8);
        Assert.Equal("PASS", result.Result);
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
