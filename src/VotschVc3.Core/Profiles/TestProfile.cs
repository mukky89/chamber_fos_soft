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

    /// <summary>Original name as it came from the imported file, kept when the app generates
    /// a new standardized <see cref="Name"/>. Empty for profiles authored directly in the app.</summary>
    public string OriginalName { get; set; } = string.Empty;

    /// <summary>Which chamber type the profile was authored for.</summary>
    public ChamberKind Kind { get; set; } = ChamberKind.TemperatureHumidity;

    /// <summary>When the profile was created / last saved.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>The segments executed in order.</summary>
    public List<ProfileSegment> Segments { get; set; } = new();

    /// <summary>How often the whole sequence of segments is repeated (>= 1).</summary>
    public int Cycles { get; set; } = 1;

    /// <summary>Free-form tags for grouping / filtering the profile library (e.g. "norma", "vzorka X").</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Sensors / specimens the profile is intended for (one profile can serve several); groups the library tree.</summary>
    public List<string> Sensors { get; set; } = new();

    /// <summary>Total duration of a single pass through the segments.</summary>
    public TimeSpan SinglePassDuration =>
        Segments.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);

    /// <summary>Total duration including all cycles.</summary>
    public TimeSpan TotalDuration => SinglePassDuration * Math.Max(1, Cycles);

    /// <summary>Deep copy of the profile (segments included). The copy keeps the same
    /// <see cref="Id"/>; callers that persist a copy as a new profile assign a fresh one.</summary>
    public TestProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        OriginalName = OriginalName,
        Kind = Kind,
        CreatedAt = CreatedAt,
        Cycles = Cycles,
        Sensors = new List<string>(Sensors),
        Tags = new List<string>(Tags),
        Segments = Segments.Select(s => new ProfileSegment
        {
            Name = s.Name,
            TargetTemperature = s.TargetTemperature,
            TargetHumidity = s.TargetHumidity,
            Duration = s.Duration,
            IsRamp = s.IsRamp,
            GuaranteedSoak = s.GuaranteedSoak,
            SoakTolerance = s.SoakTolerance,
        }).ToList(),
    };
}
