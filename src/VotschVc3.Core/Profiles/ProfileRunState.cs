namespace VotschVc3.Core.Profiles;

/// <summary>
/// Persisted checkpoint of a running profile (or profile queue). The chamber view
/// model saves it on every set point write; after an application crash the stored
/// state describes exactly where the run stopped so it can be resumed from that
/// point instead of starting over.
/// </summary>
public sealed class ProfileRunState
{
    /// <summary>Which chamber the run belongs to (<c>ChamberConfig.Id</c>).</summary>
    public Guid ChamberId { get; set; }

    /// <summary>When this checkpoint was written (freshness of the interruption).</summary>
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// Snapshot of the profile sequence that was running (a single profile, the
    /// test queue or a chain). Stored in full so the run can resume even when the
    /// profile was never saved to the history.
    /// </summary>
    public List<TestProfile> Profiles { get; set; } = new();

    /// <summary>Zero based index of the profile that was executing in <see cref="Profiles"/>.</summary>
    public int ProfileIndex { get; set; }

    /// <summary>Zero based cycle the run was in.</summary>
    public int Cycle { get; set; }

    /// <summary>Zero based segment the run was in.</summary>
    public int SegmentIndex { get; set; }

    /// <summary>Test time already spent inside the interrupted segment (seconds).</summary>
    public double ElapsedInSegmentSeconds { get; set; }

    /// <summary>Temperature the interrupted segment's ramp started from.</summary>
    public double SegmentStartTemperature { get; set; }

    /// <summary>Humidity the interrupted segment's ramp started from.</summary>
    public double? SegmentStartHumidity { get; set; }

    /// <summary>The interrupted profile (clamped to a valid index), or <c>null</c> when empty.</summary>
    public TestProfile? CurrentProfile =>
        Profiles.Count == 0 ? null : Profiles[Math.Clamp(ProfileIndex, 0, Profiles.Count - 1)];

    /// <summary>The runner position to resume the interrupted profile from.</summary>
    public ProfileRunPosition ToPosition() => new(
        Cycle,
        SegmentIndex,
        TimeSpan.FromSeconds(Math.Max(0, ElapsedInSegmentSeconds)),
        SegmentStartTemperature,
        SegmentStartHumidity);

    /// <summary>
    /// Test time completed across the whole sequence up to this checkpoint:
    /// finished profiles, finished cycles and segments of the current profile,
    /// plus the elapsed part of the interrupted segment. Used to restore the
    /// progress bar / remaining-time estimate on resume.
    /// </summary>
    public TimeSpan CompletedDuration()
    {
        if (Profiles.Count == 0)
        {
            return TimeSpan.Zero;
        }

        int profileIndex = Math.Clamp(ProfileIndex, 0, Profiles.Count - 1);
        TimeSpan done = TimeSpan.Zero;
        for (int i = 0; i < profileIndex; i++)
        {
            done += Profiles[i].TotalDuration;
        }

        TestProfile current = Profiles[profileIndex];
        int cycle = Math.Clamp(Cycle, 0, Math.Max(1, current.Cycles) - 1);
        done += current.SinglePassDuration * cycle;

        int segmentIndex = Math.Clamp(SegmentIndex, 0, Math.Max(0, current.Segments.Count - 1));
        for (int i = 0; i < segmentIndex; i++)
        {
            done += current.Segments[i].Duration;
        }

        if (current.Segments.Count > 0)
        {
            TimeSpan inSegment = TimeSpan.FromSeconds(Math.Max(0, ElapsedInSegmentSeconds));
            TimeSpan segmentDuration = current.Segments[segmentIndex].Duration;
            done += inSegment < segmentDuration ? inSegment : segmentDuration;
        }

        return done;
    }

    /// <summary>Total planned duration of the whole interrupted sequence.</summary>
    public TimeSpan TotalDuration() =>
        Profiles.Aggregate(TimeSpan.Zero, (sum, p) => sum + p.TotalDuration);
}
