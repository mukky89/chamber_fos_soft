namespace VotschVc3.Core.Calibration;

public static partial class TemperatureCalibrationAnalyzer
{
    public static double? TemperatureFromLambda(TemperatureCalibrationResult result, double wavelength)
    {
        if (!double.IsFinite(wavelength) || wavelength <= 0 || result.LambdaTRefNm <= 0) return null;
        double x = (wavelength - result.LambdaTRefNm) / result.LambdaTRefNm;
        double value;
        if (result.CoefficientS1 is { } s1 && result.CoefficientS2 is { } s2)
        {
            double log = Math.Log(wavelength / result.LambdaTRefNm);
            if (Math.Abs(s2) < 1e-20) value = result.ReferenceTemperatureC + log / s1;
            else
            {
                double discriminant = s1 * s1 + 4 * s2 * log;
                if (discriminant < 0) return null;
                value = result.ReferenceTemperatureC + (-s1 + Math.CopySign(Math.Sqrt(discriminant), s1)) / (2 * s2);
            }
        }
        else if (result.CoefficientA is { } a && result.CoefficientB is { } b && result.CoefficientC is { } c)
            value = result.CoefficientD is { } d ? ((a * x + b) * x + c) * x + d : (a * x + b) * x + c;
        else return null;
        return double.IsFinite(value) ? value : null;
    }

    public static double? LambdaFromTemperature(TemperatureCalibrationResult result, double temperature)
    {
        if (result.LambdaTRefNm <= 0 || !double.IsFinite(temperature)) return null;
        if (result.CoefficientS1 is { } s1 && result.CoefficientS2 is { } s2)
        {
            double delta = temperature - result.ReferenceTemperatureC;
            double value = result.LambdaTRefNm * Math.Exp(s1 * delta + s2 * delta * delta);
            return double.IsFinite(value) && value > 0 ? value : null;
        }
        // Invert the actual released T(lambda) coefficients, starting on the calibrated branch.
        double lambda = result.LambdaTRefNm + result.SensitivityPmPerC * (temperature - result.ReferenceTemperatureC) / 1000d;
        for (int iteration = 0; iteration < 40; iteration++)
        {
            var estimate = TemperatureFromLambda(result, lambda);
            var above = TemperatureFromLambda(result, lambda + 0.000001);
            var below = TemperatureFromLambda(result, lambda - 0.000001);
            if (estimate is null || above is null || below is null) return null;
            if (Math.Abs(estimate.Value - temperature) < 1e-8) return lambda;
            double slope = (above.Value - below.Value) / 0.000002;
            if (Math.Abs(slope) < 1e-12) return null;
            lambda -= Math.Clamp((estimate.Value - temperature) / slope, -10, 10);
            if (!double.IsFinite(lambda) || lambda <= 0) return null;
        }
        return null;
    }

    private static void ApplyFinalCheck(TemperatureCalibrationResult result, CalibrationPlateauResult? check)
    {
        if (check is null) { result.FinalCheckProblem = "Kontrolný odber pri 25 °C ešte neprebehol."; return; }
        var target = check.Targets.FirstOrDefault(t => t.SerialNumber == result.SerialNumber &&
            t.PeakLoggerDeviceSerialNumber == result.PeakLoggerDeviceSerialNumber && t.Channel == result.Channel &&
            t.PeakId == result.PeakId && t.PeakIndex == result.PeakIndex);
        if (target is null || target.Status == CalibrationTargetState.SkippedIdentityUncertain || target.StableSamples.Count == 0)
        {
            result.FinalCheckProblem = target?.Problem ?? "Chýbajú kontrolné vzorky.";
            return;
        }
        result.FinalSampleCount = target.StableSamples.Count;
        double reference = target.StableSamples.Average(v => v.ReferenceTemperatureC ?? double.NaN);
        double measured = target.StableSamples.Average(v => v.WavelengthNm);
        if (!double.IsFinite(reference) || !double.IsFinite(measured)) { result.FinalCheckProblem = "Neplatná referencia alebo wavelength."; return; }
        result.FinalReferenceTemperatureC = reference;
        result.FinalMeasuredLambdaNm = measured;
        result.FinalExpectedLambdaNm = LambdaFromTemperature(result, reference);
        var inferredTemperature = TemperatureFromLambda(result, measured);
        if (result.FinalExpectedLambdaNm is not { } expected || inferredTemperature is null)
        {
            result.FinalCheckProblem = "Koeficienty nemožno použiť na kontrolu. " + result.StabilityProblem;
            return;
        }
        result.FinalLambdaErrorPm = (measured - expected) * 1000;
        result.FinalTemperatureErrorC = inferredTemperature.Value - reference;
        result.FinalCheckStatus = target.Status != CalibrationTargetState.Stable ? "WARNING" :
            Math.Abs(result.FinalTemperatureErrorC.Value) <= result.ErrorToleranceC ? "PASS" : "FAIL";
        result.FinalCheckProblem = target.Problem;
        if (reference < result.MinimumTemperatureC || reference > result.MaximumTemperatureC)
        {
            result.FinalCheckStatus = "WARNING";
            result.FinalCheckProblem += " Kontrola leží mimo kalibrovaného teplotného rozsahu.";
        }
    }
}
