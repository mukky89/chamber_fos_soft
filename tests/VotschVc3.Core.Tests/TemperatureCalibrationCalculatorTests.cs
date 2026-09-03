using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class TemperatureCalibrationCalculatorTests
{
    [Fact]
    public void Temp_Tp03_SyntheticResponse_PassesRecipeAndReconstructsTemperature()
    {
        var mapping = new CalibrationSensorMapping
        {
            SerialNumber = "123456/0001",
            Channel = "1.1",
            PeakId = "P1",
            PeakIndex = 0,
            ProductDescription = "TP-03 temperature sensor",
            Selected = true,
        };
        double[] temperatures = { -20, 0, 20, 40, 60 };
        double[] wavelengths = temperatures
            .Select(t => 1550.0 + 0.022 * (t - 20.0) + 0.000002 * Math.Pow(t - 20.0, 2))
            .ToArray();
        CalibrationRunRecord run = BuildRun(mapping, temperatures, wavelengths);
        var setup = new CalibrationSetup { Mappings = { mapping } };

        TemperatureCalibrationResult result = Assert.Single(new TemperatureCalibrationCalculator().CalculateRun(run, setup));

        Assert.Equal(TemperatureCalibrationCalculationType.TEMP, result.CalculationType);
        Assert.Equal("TP-03", result.RecipeKey);
        Assert.True(result.SensitivityPassed, result.StatusMessage);
        Assert.True(result.ErrorPassed, result.StatusMessage);
        Assert.True(result.OverallPassed, result.StatusMessage);
        Assert.InRange(result.SensitivityPmPerC, 20.0, 25.0);
        Assert.True(result.MaxErrorC < 0.05, result.StatusMessage);
        Assert.True(result.R2 > 0.9999, result.StatusMessage);
        Assert.Equal(temperatures.Length, result.Points.Count);
    }

    [Fact]
    public void Fbgs_Fos4Strain_SyntheticExponentialResponse_PassesRecipe()
    {
        var mapping = new CalibrationSensorMapping
        {
            SerialNumber = "654321/0001",
            Channel = "2.1",
            PeakId = "P1",
            PeakIndex = 0,
            ProductDescription = "FOS4STRAIN_1,75",
            Selected = true,
        };
        double tempConst = 22.5;
        double tRef = 1550.0;
        double s1 = 0.0000120;
        double s2 = 0.0000000010;
        double[] temperatures = { -20, 0, 20, 40, 60 };
        double[] wavelengths = temperatures
            .Select(t =>
            {
                double dt = t - tempConst;
                return tRef * Math.Exp(s1 * dt + s2 * dt * dt);
            })
            .ToArray();
        CalibrationRunRecord run = BuildRun(mapping, temperatures, wavelengths);
        var setup = new CalibrationSetup { Mappings = { mapping } };

        TemperatureCalibrationResult result = Assert.Single(new TemperatureCalibrationCalculator().CalculateRun(run, setup));

        Assert.Equal(TemperatureCalibrationCalculationType.FBGS, result.CalculationType);
        Assert.Equal("FOS4STRAIN_1,75", result.RecipeKey);
        Assert.True(result.SensitivityPassed, result.StatusMessage);
        Assert.True(result.OverallPassed, result.StatusMessage);
        Assert.InRange(result.SensitivityPmPerC, 17.2, 20.2);
        Assert.True(result.MaxErrorC < 0.1, result.StatusMessage);
        Assert.True(result.R2 > 0.999, result.StatusMessage);
        Assert.NotNull(result.S1);
        Assert.NotNull(result.S2);
    }

    [Fact]
    public void Sc01T_Recipe_SkipsSecondPeak()
    {
        var first = new CalibrationSensorMapping
        {
            SerialNumber = "111111/0001",
            Channel = "3.1",
            PeakId = "P1",
            PeakIndex = 0,
            ProductDescription = "SC-01/T",
            Selected = true,
        };
        var second = new CalibrationSensorMapping
        {
            SerialNumber = first.SerialNumber,
            Channel = first.Channel,
            PeakId = "P2",
            PeakIndex = 1,
            ProductDescription = "SC-01/T",
            Selected = true,
        };
        double[] temperatures = { -20, 0, 20, 40 };
        CalibrationRunRecord run = BuildRun(first, temperatures, temperatures.Select(t => 1550 + t * 0.02).ToArray());
        AddTarget(run, second, temperatures.Select(t => 1560 + t * 0.02).ToArray());
        var setup = new CalibrationSetup { Mappings = { first, second } };

        IReadOnlyList<TemperatureCalibrationResult> results = new TemperatureCalibrationCalculator().CalculateRun(run, setup);

        TemperatureCalibrationResult result = Assert.Single(results);
        Assert.Equal("P1", result.PeakId);
    }

    [Fact]
    public void Store_ExportsCalculationResultAndPointFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "calculation-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var mapping = new CalibrationSensorMapping
            {
                SerialNumber = "123456/0001",
                Channel = "1.1",
                PeakId = "P1",
                PeakIndex = 0,
                ProductDescription = "TP-03",
                Selected = true,
            };
            double[] temperatures = { -20, 0, 20, 40 };
            CalibrationRunRecord run = BuildRun(mapping, temperatures, temperatures.Select(t => 1550 + 0.022 * t).ToArray());
            run.CalculationResults = new TemperatureCalibrationCalculator()
                .CalculateRun(run, new CalibrationSetup { Mappings = { mapping } })
                .ToList();
            var store = new CalibrationStore(root);

            store.SaveRun(run);

            string runDir = Path.Combine(root, "Runs", run.RunId.ToString("N"));
            Assert.True(File.Exists(Path.Combine(runDir, "calibration-result.json")));
            Assert.True(File.Exists(Path.Combine(runDir, "calibration-result.csv")));
            Assert.True(File.Exists(Path.Combine(runDir, "calibration-points.csv")));
            Assert.Contains("OverallPass", File.ReadAllText(Path.Combine(runDir, "calibration-result.csv")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static CalibrationRunRecord BuildRun(
        CalibrationSensorMapping mapping,
        IReadOnlyList<double> temperatures,
        IReadOnlyList<double> wavelengths)
    {
        var run = new CalibrationRunRecord
        {
            ProfileName = "Temperature calibration test",
            ChamberName = "Test chamber",
        };
        for (int i = 0; i < temperatures.Count; i++)
        {
            run.Plateaus.Add(new CalibrationPlateauResult
            {
                PlateauIndex = i,
                TargetTemperatureC = temperatures[i],
                ActualTemperatureC = temperatures[i] + 0.05,
                ReferenceTemperatureC = temperatures[i],
                Targets =
                {
                    Measurement(mapping, temperatures[i], wavelengths[i]),
                },
            });
        }
        return run;
    }

    private static void AddTarget(
        CalibrationRunRecord run,
        CalibrationSensorMapping mapping,
        IReadOnlyList<double> wavelengths)
    {
        for (int i = 0; i < run.Plateaus.Count; i++)
        {
            double reference = run.Plateaus[i].ReferenceTemperatureC ?? run.Plateaus[i].ActualTemperatureC;
            run.Plateaus[i].Targets.Add(Measurement(mapping, reference, wavelengths[i]));
        }
    }

    private static CalibrationMeasurementResult Measurement(
        CalibrationSensorMapping mapping,
        double referenceTemperature,
        double wavelength) => new()
    {
        SerialNumber = mapping.SerialNumber,
        Channel = mapping.Channel,
        PeakId = mapping.PeakId,
        PeakIndex = mapping.PeakIndex,
        PeakLoggerDeviceSerialNumber = "INTERROGATOR",
        Status = CalibrationTargetState.Stable,
        SampleCount = 30,
        MeanWavelengthNm = wavelength,
        MedianWavelengthNm = wavelength,
        MinWavelengthNm = wavelength,
        MaxWavelengthNm = wavelength,
        MeanReferenceTemperatureC = referenceTemperature,
        MeanChamberTemperatureC = referenceTemperature + 0.05,
    };
}
