namespace VotschVc3.Core.Profiles;

/// <summary>
/// An ordered list of <see cref="ProfileSegment"/>s that together describe a
/// temperature / humidity test run, optionally repeated several times.
/// </summary>
public sealed class TestProfile
{
    /// <summary>Stable identity used by the profile history store.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name of the profile.</summary>
    public string Name { get; set; } = "New profile";

    /// <summary>Which chamber type the profile was authored for.</summary>
    public ChamberKind Kind { get; set; } = ChamberKind.TemperatureHumidity;

    /// <summary>When the profile was created / last saved.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

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
