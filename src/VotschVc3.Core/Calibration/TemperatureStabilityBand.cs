namespace VotschVc3.Core.Calibration;

/// <summary>Inclusive temperature limits used by the calibration stability gate.</summary>
public readonly record struct TemperatureStabilityBand(double LowerC, double UpperC)
{
    public static TemperatureStabilityBand Around(double targetC, double toleranceC)
    {
        double tolerance = Math.Abs(toleranceC);
        return new TemperatureStabilityBand(targetC - tolerance, targetC + tolerance);
    }
}
