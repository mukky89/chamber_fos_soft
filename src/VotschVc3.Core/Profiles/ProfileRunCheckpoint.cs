namespace VotschVc3.Core.Profiles;

/// <summary>
/// Snapshot of an in-progress profile run, written to disk on every set-point
/// update so it survives a power outage or an application crash. On the next
/// successful connect to the chamber, <c>ProfileRunCheckpointStore</c> can hand
/// this back so the run can resume from exactly where it stopped instead of
/// being lost.
/// </summary>
public sealed class ProfileRunCheckpoint
{
    /// <summary>The chamber this run belonged to (matches <see cref="ChamberConfig.Id"/>).</summary>
    public Guid ChamberId { get; set; }

    /// <summary>Chamber display name at the time of the write (for the recovery prompt only).</summary>
    public string ChamberName { get; set; } = string.Empty;

    /// <summary>The exact profile that was running (a snapshot, not a library reference).</summary>
    public TestProfile Profile { get; set; } = new();

    /// <summary>Profiles still queued to run after <see cref="Profile"/> finishes.</summary>
    public List<TestProfile> RemainingQueue { get; set; } = new();

    /// <summary>Absolute index into <see cref="Profile"/>'s segments.</summary>
    public int SegmentIndex { get; set; }

    /// <summary>Zero-based cycle index at the moment of the write.</summary>
    public int CycleIndex { get; set; }

    public ProfileRunPhase Phase { get; set; }

    /// <summary>Seconds already elapsed inside the current segment's own dwell/ramp clock.</summary>
    public double ElapsedInSegmentSeconds { get; set; }

    /// <summary>
    /// Actual ramp origin captured when the segment started. Required to restore
    /// the exact interpolated setpoint instead of recalculating from the measured
    /// chamber temperature after a restart.
    /// </summary>
    public double? SegmentStartTemperature { get; set; }

    public double? SegmentStartHumidity { get; set; }

    /// <summary>
    /// <c>true</c> when the run was waiting for a guaranteed-soak tolerance at the moment
    /// of the write. On resume the soak wait is re-entered from scratch (the measured
    /// temperature is re-checked rather than trusting stale elapsed time).
    /// </summary>
    public bool IsSoaking { get; set; }

    /// <summary>When the run originally started (UTC).</summary>
    public DateTime StartedUtc { get; set; }

    /// <summary>When this checkpoint was last written (UTC) – used to show the operator how long ago the run was interrupted.</summary>
    public DateTime LastWriteUtc { get; set; }
}
