namespace VotschVc3.Core.Calibration;

/// <summary>Per-peak deadlines measured from the first opening of the temperature gate.</summary>
public static class SensorTimeoutBudget
{
    public static readonly TimeSpan HardLimit = TimeSpan.FromMinutes(90);
    public static readonly TimeSpan ExtensionStep = TimeSpan.FromMinutes(10);

    public static TimeSpan CompleteAttempt(int stableSamples, int measurementSamples, TimeSpan observedCadence)
    {
        double cadenceSeconds = double.IsFinite(observedCadence.TotalSeconds)
            ? Math.Clamp(observedCadence.TotalSeconds, 0.1, 300.0) : 30;
        double sampleCount = (double)Math.Max(2, stableSamples) + Math.Max(2, measurementSamples);
        return TimeSpan.FromSeconds(Math.Min(HardLimit.TotalSeconds, sampleCount * cadenceSeconds * 1.2));
    }

    public static TimeSpan BaseLimit(int stableSamples, int measurementSamples, TimeSpan cadence, TimeSpan configuredMinimum) =>
        TimeSpan.FromSeconds(Math.Min(HardLimit.TotalSeconds,
            Math.Max(configuredMinimum.TotalSeconds, CompleteAttempt(stableSamples, measurementSamples, cadence).TotalSeconds)));

    public static TimeSpan Extend(TimeSpan currentLimit, TimeSpan elapsed, bool recentProgress) =>
        recentProgress && elapsed >= currentLimit && elapsed < HardLimit
            ? TimeSpan.FromTicks(Math.Min(HardLimit.Ticks, currentLimit.Ticks + ExtensionStep.Ticks))
            : currentLimit;
}
