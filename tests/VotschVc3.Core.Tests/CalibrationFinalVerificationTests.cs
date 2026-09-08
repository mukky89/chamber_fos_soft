using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationFinalVerificationTests
{
    [Fact]
    public void PolynomialCheckUsesActualCoefficientsInBothDirections()
    {
        var result = new TemperatureCalibrationResult
        {
            LambdaTRefNm = 1550, ReferenceTemperatureC = 25, SensitivityPmPerC = 10,
            CoefficientA = 0, CoefficientB = 155000, CoefficientC = 25,
        };
        Assert.Equal(1550.05, TemperatureCalibrationAnalyzer.LambdaFromTemperature(result, 30)!.Value, 8);
        Assert.Equal(30, TemperatureCalibrationAnalyzer.TemperatureFromLambda(result, 1550.05)!.Value, 7);
        result.CoefficientA = 1e9;
        result.CoefficientB = 1e6;
        result.CoefficientC = 155000;
        result.CoefficientD = 25;
        double wavelength = TemperatureCalibrationAnalyzer.LambdaFromTemperature(result, 30)!.Value;
        Assert.Equal(30, TemperatureCalibrationAnalyzer.TemperatureFromLambda(result, wavelength)!.Value, 7);
    }

    [Theory]
    [InlineData(0.000006, 0.00000001)]
    [InlineData(-0.000006, -0.00000001)]
    public void FbgsCheckUsesReferenceTemperatureAndSensitivityBranch(double s1, double s2)
    {
        var result = new TemperatureCalibrationResult { LambdaTRefNm = 1550, ReferenceTemperatureC = 22.5, CoefficientS1 = s1, CoefficientS2 = s2 };
        double expected = 1550 * Math.Exp(s1 * 2.5 + s2 * 2.5 * 2.5);
        Assert.Equal(expected, TemperatureCalibrationAnalyzer.LambdaFromTemperature(result, 25)!.Value, 9);
        Assert.Equal(25, TemperatureCalibrationAnalyzer.TemperatureFromLambda(result, expected)!.Value, 7);
    }

    [Fact]
    public void FlaggedPointsProduceCoefficientsAndIndependentBadCheckDoesNotRefitThem()
    {
        var run = new CalibrationRunRecord();
        foreach (double temperature in new[] { 0d, 20d, 40d, 60d })
            run.Plateaus.Add(new CalibrationPlateauResult
            {
                ReferenceTemperatureC = temperature,
                Targets = [new CalibrationMeasurementResult
                {
                    SerialNumber = "SN", Channel = "1", PeakId = "P1", SampleCount = 50,
                    MeanWavelengthNm = 1550 + 0.01 * temperature + 0.00001 * temperature * temperature,
                    Status = CalibrationTargetState.CompletedWithStabilityWarning, Problem = "Prekročený drift 1 pm/min.",
                }],
            });
        var before = TemperatureCalibrationAnalyzer.Analyze(run).Single(r => r.CalibrationType == "2nd · ABC");
        Assert.Equal("PROBLÉM", before.StabilityStatus);
        run.FinalVerification = new CalibrationPlateauResult
        {
            Targets = [new CalibrationMeasurementResult
            {
                SerialNumber = "SN", Channel = "1", PeakId = "P1", Status = CalibrationTargetState.Stable,
                StableSamples = [new CalibrationRawSample { WavelengthNm = 1551, ReferenceTemperatureC = 25 }],
            }],
        };
        var after = TemperatureCalibrationAnalyzer.Analyze(run).Single(r => r.CalibrationType == "2nd · ABC");
        Assert.Equal(before.CoefficientA, after.CoefficientA);
        Assert.Equal(before.LambdaTRefNm, after.LambdaTRefNm);
        Assert.Equal("FAIL", after.FinalCheckStatus);
        Assert.NotNull(after.FinalExpectedLambdaNm);
        Assert.Equal(after.FinalReferenceTemperatureC!.Value + after.FinalTemperatureErrorC!.Value, after.FinalCalculatedTemperatureC!.Value, 8);
        Assert.True(Math.Abs(after.FinalLambdaErrorPm!.Value) > 500);
        string export = Path.GetTempFileName();
        try
        {
            TemperatureCalibrationAnalyzer.ExportCsv([after], export);
            string csv = File.ReadAllText(export);
            Assert.Contains("FinalExpectedLambdaNm", csv);
            Assert.Contains("FinalCalculatedTemperatureC", csv);
            Assert.Contains("FinalErrorPm", csv);
            Assert.Contains("Prekročený drift", csv);
            Assert.Contains(";FAIL;", csv);
        }
        finally { File.Delete(export); }
    }

    [Fact]
    public void CompletionEmailShowsRealCalibratedTemperaturePerPeak()
    {
        var item = new TemperatureCalibrationResult
        {
            SerialNumber = "SN-25", Channel = "1", PeakId = "P1",
            LambdaTRefNm = 1550, ReferenceTemperatureC = 25,
            CoefficientA = 0, CoefficientB = 155000, CoefficientC = 25,
            FinalMeasuredLambdaNm = 1550.01, FinalReferenceTemperatureC = 25,
            FinalTemperatureErrorC = 1, FinalCheckStatus = "FAIL"
        };
        Assert.Equal(26, item.FinalCalculatedTemperatureC!.Value, 7);
        var run = new CalibrationRunRecord { CalibrationResults = [item] };
        var message = VotschVc3.Core.Notifications.CalibrationCompletionEmail.Create(run,
            Path.Combine(Path.GetTempPath(), "missing-verification-" + Guid.NewGuid().ToString("N")));
        Assert.Contains("26,000", message.Text);
        Assert.Contains("26,000", message.Html);
        Assert.Contains("SN-25", message.Html);
        Assert.Contains("FAIL", message.Text);
    }

    [Fact]
    public void MissingDataProducesExplicitEvaluationRowWithoutInventedCoefficients()
    {
        var run = new CalibrationRunRecord { Plateaus = [new CalibrationPlateauResult
        {
            ReferenceTemperatureC = 25,
            Targets = [new CalibrationMeasurementResult { SerialNumber = "SN", Status = CalibrationTargetState.TimedOut, Problem = "Chýbajú dáta." }],
        }] };
        var result = Assert.Single(TemperatureCalibrationAnalyzer.Analyze(run));
        Assert.Equal("N/A", result.Result);
        Assert.Null(result.CoefficientA);
        Assert.Null(result.CoefficientS1);
        Assert.Contains("Chýbajú dáta", result.StabilityProblem);
    }
}