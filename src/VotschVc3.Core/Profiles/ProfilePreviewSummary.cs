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
    public TimeSpan TotalDuration { get; init; }
    public int Cycles { get; init; }
    public int PlateauCount { get; init; }
    public int TemperatureLevelCount { get; init; }
    public IReadOnlyList<ProfilePlateauSummary> Plateaus { get; init; } = Array.Empty<ProfilePlateauSummary>();

    public static ProfilePreviewSummary Analyze(TestProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

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
            Cycles = cycles,
            PlateauCount = plateauCount,
            TemperatureLevelCount = plateauTemperatures.Count,
            Plateaus = plateaus,
        };
    }
}
