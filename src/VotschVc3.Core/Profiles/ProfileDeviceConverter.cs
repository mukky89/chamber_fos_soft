namespace VotschVc3.Core.Profiles;

/// <summary>
/// Converts a profile from one device family to the other.
/// <para>
/// A Vötsch / Weiss chamber is driven with an explicit ramp to every setpoint followed by
/// a hold; a SIKA TP bath reaches the set point on its own, so its profile is nothing but
/// the list of setpoints with a dwell time. Converting a Vötsch profile therefore drops
/// the ramp segments and keeps the holds – the temperatures and how long the specimen
/// sits at each of them are what the test is actually about, and they survive unchanged.
/// The run gets shorter by exactly the ramp time, which is the point: the bath spends that
/// time settling instead.
/// </para>
/// </summary>
public static class ProfileDeviceConverter
{
    /// <summary>Setpoints closer than this are treated as the same plateau.</summary>
    private const double TemperatureEpsilon = 0.05;

    /// <summary>
    /// Builds a SIKA version of <paramref name="source"/>: hold segments only, humidity
    /// dropped (a bath has no humidity channel) and the cycled region remapped onto the
    /// shorter segment list. The result is a new profile with a fresh
    /// <see cref="TestProfile.Id"/> – the original stays untouched in the library.
    /// </summary>
    /// <param name="source">The profile to convert (usually a Vötsch one).</param>
    /// <param name="name">Name for the result; <c>null</c> appends " · SIKA" to the source name.</param>
    public static TestProfile ToSika(TestProfile source, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        // sourceIndex -> index in the converted list, or -1 when the segment was dropped
        // (a ramp) or merged into the plateau before it.
        var map = new int[source.Segments.Count];
        var segments = new List<ProfileSegment>();

        for (int i = 0; i < source.Segments.Count; i++)
        {
            ProfileSegment segment = source.Segments[i];
            map[i] = -1;
            if (segment.IsRamp)
            {
                continue;
            }

            // Without the ramp between them, two holds at the same temperature are one
            // longer hold – emitting both would make the bath re-settle on a value it is
            // already sitting at.
            if (segments.Count > 0 &&
                Math.Abs(segments[^1].TargetTemperature - segment.TargetTemperature) <= TemperatureEpsilon)
            {
                segments[^1].Duration += segment.Duration;
                segments[^1].IsCalibrationPoint |= segment.IsCalibrationPoint;
                map[i] = segments.Count - 1;
                continue;
            }

            segments.Add(Hold(segment));
            map[i] = segments.Count - 1;
        }

        // A profile made only of ramps (no plateau at all) would convert to nothing, so
        // every ramp target becomes a dwell of that ramp's length instead.
        if (segments.Count == 0)
        {
            for (int i = 0; i < source.Segments.Count; i++)
            {
                segments.Add(Hold(source.Segments[i]));
                map[i] = segments.Count - 1;
            }
        }

        TestProfile result = source.Clone();
        result.Id = Guid.NewGuid();
        result.Name = string.IsNullOrWhiteSpace(name) ? BuildName(source.Name) : name.Trim();
        result.OriginalName = string.IsNullOrWhiteSpace(source.OriginalName) ? source.Name : source.OriginalName;
        result.DeviceKind = ProfileDeviceKind.Sika;
        result.Kind = ChamberKind.TemperatureOnly;
        result.CreatedAt = DateTimeOffset.Now;
        result.UpdatedAt = null;
        result.Segments = segments;

        (int start, int end) = RemapCycleRegion(source, map, segments.Count);
        result.CycleStartIndex = start;
        result.CycleEndIndex = end;

        return result;
    }

    /// <summary>The plateau as a SIKA dwell: no ramp, no humidity channel.</summary>
    private static ProfileSegment Hold(ProfileSegment source) => new()
    {
        Name = $"Výdrž {source.TargetTemperature:0.#} °C",
        TargetTemperature = source.TargetTemperature,
        TargetHumidity = null,
        Duration = source.Duration,
        IsRamp = false,
        IsCalibrationPoint = source.IsCalibrationPoint,
        GuaranteedSoak = source.GuaranteedSoak,
        SoakTolerance = source.SoakTolerance,
    };

    /// <summary>
    /// Moves the repeated region onto the converted segment list: the first kept segment at
    /// or after the old start, and the last kept one at or before the old end. Returns
    /// <c>(-1, -1)</c> – "the whole profile" – when the region no longer survives.
    /// </summary>
    private static (int Start, int End) RemapCycleRegion(TestProfile source, int[] map, int count)
    {
        if (source.Segments.Count == 0 || count == 0 || Math.Max(1, source.Cycles) <= 1)
        {
            return (-1, -1);
        }

        int start = -1;
        for (int i = source.ResolvedCycleStart; i < map.Length && start < 0; i++)
        {
            if (map[i] >= 0) start = map[i];
        }

        int end = -1;
        for (int i = source.ResolvedCycleEnd; i >= 0 && end < 0; i--)
        {
            if (map[i] >= 0) end = map[i];
        }

        return start < 0 || end < 0 || end < start ? (-1, -1) : (start, end);
    }

    /// <summary>Appends the " · SIKA" marker once, so converting twice does not stack it.</summary>
    private static string BuildName(string sourceName)
    {
        string trimmed = (sourceName ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "SIKA profil";
        }

        return trimmed.EndsWith("· SIKA", StringComparison.OrdinalIgnoreCase) ? trimmed : $"{trimmed} · SIKA";
    }
}
