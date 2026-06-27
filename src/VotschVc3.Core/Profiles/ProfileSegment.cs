namespace VotschVc3.Core.Profiles;

/// <summary>
/// One step of a temperature / humidity profile.
/// <para>
/// A segment either <em>ramps</em> linearly from the value reached at the end of
/// the previous segment to <see cref="TargetTemperature"/> /
/// <see cref="TargetHumidity"/> over <see cref="Duration"/>, or <em>holds</em>
/// the target value for the whole duration (a plateau / "Plato"). The profile is
/// executed by the controlling PC (see <see cref="ProfileRunner"/>), which makes
/// it independent of the chamber's built-in program memory.
/// </para>
/// </summary>
public sealed class ProfileSegment
{
    /// <summary>Optional human readable name shown in the editor.</summary>
    public string Name { get; set; } = "Segment";

    /// <summary>Target temperature in °C reached at the end of the segment.</summary>
    public double TargetTemperature { get; set; }

    /// <summary>Target relative humidity in %. <c>null</c> leaves humidity uncontrolled.</summary>
    public double? TargetHumidity { get; set; }

    /// <summary>Duration of the segment.</summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// <c>true</c> to ramp linearly towards the target over the duration;
    /// <c>false</c> to jump to the target immediately and hold it (plateau).
    /// </summary>
    public bool IsRamp { get; set; } = true;

    /// <summary>
    /// "Guaranteed soak": for a hold segment, the dwell time only starts counting
    /// once the measured temperature is within <see cref="SoakTolerance"/> of the
    /// target. Ensures the specimen actually reaches the set point before the
    /// plateau is timed.
    /// </summary>
    public bool GuaranteedSoak { get; set; }

    /// <summary>Tolerance band (°C) for <see cref="GuaranteedSoak"/>.</summary>
    public double SoakTolerance { get; set; } = 1.0;

    /// <summary>
    /// Interpolates the temperature at a given fraction (0..1) of the segment,
    /// starting from <paramref name="startTemperature"/>.
    /// </summary>
    public double TemperatureAt(double fraction, double startTemperature)
    {
        if (!IsRamp)
        {
            return TargetTemperature;
        }

        fraction = Math.Clamp(fraction, 0d, 1d);
        return startTemperature + (TargetTemperature - startTemperature) * fraction;
    }

    /// <summary>
    /// Interpolates the humidity at a given fraction (0..1) of the segment,
    /// starting from <paramref name="startHumidity"/>. Returns <c>null</c> when
    /// the segment does not control humidity.
    /// </summary>
    public double? HumidityAt(double fraction, double? startHumidity)
    {
        if (TargetHumidity is not { } target)
        {
            return null;
        }

        if (!IsRamp || startHumidity is not { } start)
        {
            return target;
        }

        fraction = Math.Clamp(fraction, 0d, 1d);
        return start + (target - start) * fraction;
    }
}
