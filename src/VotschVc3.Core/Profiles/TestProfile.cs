namespace VotschVc3.Core.Profiles;

/// <summary>
/// An ordered list of <see cref="ProfileSegment"/>s that together describe a
/// temperature / humidity test run, optionally repeated several times.
/// </summary>
public sealed class TestProfile
{
    /// <summary>Display name of the profile.</summary>
    public string Name { get; set; } = "New profile";

    /// <summary>The segments executed in order.</summary>
    public List<ProfileSegment> Segments { get; set; } = new();

    /// <summary>How often the whole sequence of segments is repeated (>= 1).</summary>
    public int Cycles { get; set; } = 1;

    /// <summary>Total duration of a single pass through the segments.</summary>
    public TimeSpan SinglePassDuration =>
        Segments.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);

    /// <summary>Total duration including all cycles.</summary>
    public TimeSpan TotalDuration => SinglePassDuration * Math.Max(1, Cycles);
}
