namespace VotschVc3.Core.Profiles;

/// <summary>One hold/plateau row shown in compact profile previews.</summary>
public sealed record ProfilePlateauSummary(
    int SegmentIndex,
    double Temperature,
    TimeSpan Duration,
    int Repetitions);

/// <summary>
/// Read-only, UI-independent summary of a saved profile. Kept in Core so the
/// dashboard, profile library and future web UI can all present the same numbers.
/// </summary>
public sealed class ProfilePreviewSummary
{
    public double? MinTemperature { get; init; }
    public double? MaxTemperature { get; init; }
    /// <summary>Sum of the segment durations (what the runner counts down).</summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>
    /// Estimated time the device spends driving to the set points, on a profile whose steps
    /// have no ramp of their own (SIKA). Zero when no settling model was supplied or the
    /// profile ramps in its own segments.
    /// </summary>
    public TimeSpan SettlingDuration { get; init; }

    /// <summary>What the run really takes: dwell time plus the approach time.</summary>
    public TimeSpan TotalWithSettling => TotalDuration + SettlingDuration;

    /// <summary><c>true</c> when the approach time is worth showing next to the total.</summary>
    public bool HasSettling => SettlingDuration > TimeSpan.Zero;
    public int Cycles { get; init; }
    public int PlateauCount { get; init; }
    public int TemperatureLevelCount { get; init; }
    public IReadOnlyList<ProfilePlateauSummary> Plateaus { get; init; } = Array.Empty<ProfilePlateauSummary>();

    /// <param name="profile">The profile to describe.</param>
    /// <param name="settling">
    /// Model for how long a self-settling device needs to reach each set point. Applied only
    /// to SIKA profiles – a Vötsch profile carries its ramps as segments, so their time is
    /// already in <see cref="TotalDuration"/>. Pass <c>null</c> to leave it out.
    /// </param>
    public static ProfilePreviewSummary Analyze(TestProfile profile, SettlingRates? settling = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        TimeSpan settlingDuration = profile.DeviceKind == ProfileDeviceKind.Sika && settling is not null
            ? settling.ForProfile(profile)
            : TimeSpan.Zero;

        if (profile.Segments.Count == 0)
        {
            return new ProfilePreviewSummary
            {
                TotalDuration = profile.TotalDuration,
                Cycles = Math.Max(1, profile.Cycles),
            };
        }

        int cycles = Math.Max(1, profile.Cycles);
        int cycleStart = profile.ResolvedCycleStart;
        int cycleEnd = profile.ResolvedCycleEnd;
        var plateaus = new List<ProfilePlateauSummary>();
        var plateauTemperatures = new HashSet<double>();
        int plateauCount = 0;

        for (int i = 0; i < profile.Segments.Count; i++)
        {
            ProfileSegment segment = profile.Segments[i];
            if (segment.IsRamp)
            {
                continue;
            }

            int repetitions = i >= cycleStart && i <= cycleEnd ? cycles : 1;
            plateaus.Add(new ProfilePlateauSummary(i, segment.TargetTemperature, segment.Duration, repetitions));
            plateauCount += repetitions;
            plateauTemperatures.Add(Math.Round(segment.TargetTemperature, 3));
        }

        return new ProfilePreviewSummary
        {
            MinTemperature = profile.Segments.Min(s => s.TargetTemperature),
            MaxTemperature = profile.Segments.Max(s => s.TargetTemperature),
            TotalDuration = profile.TotalDuration,
            SettlingDuration = settlingDuration,
            Cycles = cycles,
            PlateauCount = plateauCount,
            TemperatureLevelCount = plateauTemperatures.Count,
            Plateaus = plateaus,
        };
    }
}
