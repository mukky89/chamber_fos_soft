using System.Collections.Concurrent;

namespace VotschVc3.Core.Calibration;

/// <summary>
/// Per-device policy for optional outer-loop temperature trim during FBG calibration.
/// The chamber remains in charge of its own local control loop; this policy only applies a
/// slow bounded bias to the chamber setpoint so the physical WIKA reference reaches the target.
/// </summary>
public static class CalibrationReferenceControlRegistry
{
    private static readonly ConcurrentDictionary<Guid, CalibrationReferenceControlOptions> Policies = new();

    public static void Configure(Guid chamberId, CalibrationReferenceControlOptions options)
    {
        if (chamberId == Guid.Empty) return;
        Policies[chamberId] = options.Normalize();
    }

    public static CalibrationReferenceControlOptions Get(Guid chamberId) =>
        chamberId != Guid.Empty && Policies.TryGetValue(chamberId, out CalibrationReferenceControlOptions? options)
            ? options
            : CalibrationReferenceControlOptions.Disabled;
}

public sealed record CalibrationReferenceControlOptions(
    bool Enabled,
    double Gain,
    double DeadbandC,
    double MaxCorrectionC,
    double MaxStepC,
    TimeSpan UpdateInterval)
{
    public static CalibrationReferenceControlOptions Disabled { get; } =
        new(false, 0.35, 0.05, 3.0, 0.30, TimeSpan.FromSeconds(10));

    public CalibrationReferenceControlOptions Normalize() => this with
    {
        Gain = Math.Clamp(double.IsFinite(Gain) ? Gain : 0.35, 0.01, 2.0),
        DeadbandC = Math.Clamp(double.IsFinite(DeadbandC) ? Math.Abs(DeadbandC) : 0.05, 0.01, 1.0),
        MaxCorrectionC = Math.Clamp(double.IsFinite(MaxCorrectionC) ? Math.Abs(MaxCorrectionC) : 3.0, 0.1, 10.0),
        MaxStepC = Math.Clamp(double.IsFinite(MaxStepC) ? Math.Abs(MaxStepC) : 0.30, 0.02, 2.0),
        UpdateInterval = UpdateInterval < TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(2) : UpdateInterval,
    };
}
