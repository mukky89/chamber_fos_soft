namespace VotschVc3.Core.Calibration;

/// <summary>Calculates a bounded allowance for one complete sensor retry from the observed data cadence.</summary>
public static class SensorTimeoutBudget
{
    public static TimeSpan CompleteAttempt(int stableSamples, int measurementSamples, TimeSpan observedCadence)
    {
        double cadenceSeconds = Math.Clamp(observedCadence.TotalSeconds, 0.1, 30.0);
        int sampleCount = Math.Max(2, stableSamples) + Math.Max(2, measurementSamples);
        return TimeSpan.FromSeconds((sampleCount * cadenceSeconds * 1.25) + 30.0);
    }
}
