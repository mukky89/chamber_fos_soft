using System.Text.Json.Serialization;

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

    /// <summary>
    /// How often the cycled region repeats (>= 1). When no explicit region is marked
    /// (see <see cref="CycleStartIndex"/>) this repeats the whole profile, as before.
    /// </summary>
    public int Cycles { get; set; } = 1;

    /// <summary>
    /// First segment index (inclusive) of the repeated region. <c>-1</c> means "from the
    /// first segment". Segments before the region run once (e.g. the initial ramp).
    /// </summary>
    public int CycleStartIndex { get; set; } = -1;

    /// <summary>
    /// Last segment index (inclusive) of the repeated region. <c>-1</c> means "to the last
    /// segment". Segments after the region run once (e.g. the final ramp to room temperature).
    /// </summary>
    public int CycleEndIndex { get; set; } = -1;

    /// <summary>Resolved first index of the cycled region (defaults to 0).</summary>
    public int ResolvedCycleStart =>
        Segments.Count == 0 ? 0 : (CycleStartIndex < 0 ? 0 : Math.Clamp(CycleStartIndex, 0, Segments.Count - 1));

    /// <summary>Resolved last index of the cycled region (defaults to the last segment).</summary>
    public int ResolvedCycleEnd
    {
        get
        {
            if (Segments.Count == 0)
            {
                return 0;
            }

            int end = CycleEndIndex < 0 ? Segments.Count - 1 : Math.Clamp(CycleEndIndex, 0, Segments.Count - 1);
            return Math.Max(end, ResolvedCycleStart);
        }
    }

    /// <summary>True when a strict sub-range of segments repeats (intro / outro run once).</summary>
    public bool HasCycleRegion =>
        Math.Max(1, Cycles) > 1 && (ResolvedCycleStart > 0 || ResolvedCycleEnd < Segments.Count - 1);

    /// <summary>Free-form tags for grouping / filtering the profile library (e.g. "norma", "vzorka X").</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Sensors / specimens the profile is intended for (one profile can serve several); groups the library tree.</summary>
    public List<string> Sensors { get; set; } = new();

    /// <summary>Customer the profile belongs to (optional).</summary>
    public string Customer { get; set; } = string.Empty;

    /// <summary>Project the profile belongs to (optional).</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>Grouping key for pickers/trees: customer if set, else the first sensor, else "Ostatné".</summary>
    [JsonIgnore]
    public string GroupKey =>
        !string.IsNullOrWhiteSpace(Customer) ? Customer.Trim()
        : (Sensors is { Count: > 0 } && !string.IsNullOrWhiteSpace(Sensors[0]) ? Sensors[0].Trim() : "Ostatné");

    /// <summary>One-line caption for the profile picker: sensors · project · tags (non-empty parts).</summary>
    [JsonIgnore]
    public string PickerCaption
    {
        get
        {
            var parts = new List<string>();
            if (Sensors is { Count: > 0 }) parts.Add(string.Join(" / ", Sensors));
            if (!string.IsNullOrWhiteSpace(Project)) parts.Add(Project.Trim());
            if (Tags is { Count: > 0 }) parts.Add(string.Join(", ", Tags));
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Total duration of a single traversal of every segment.</summary>
    public TimeSpan SinglePassDuration =>
        Segments.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);

    /// <summary>
    /// Total run duration: the segments before the cycled region run once, the region
    /// repeats <see cref="Cycles"/> times, and the segments after it run once. With no
    /// marked region this equals a whole-profile repeat, matching the old behaviour.
    /// </summary>
    public TimeSpan TotalDuration
    {
        get
        {
            int cycles = Math.Max(1, Cycles);
            if (Segments.Count == 0)
            {
                return TimeSpan.Zero;
            }

            int start = ResolvedCycleStart;
            int end = ResolvedCycleEnd;
            TimeSpan intro = TimeSpan.Zero, body = TimeSpan.Zero, outro = TimeSpan.Zero;
            for (int i = 0; i < Segments.Count; i++)
            {
                if (i < start)
                {
                    intro += Segments[i].Duration;
                }
                else if (i <= end)
                {
                    body += Segments[i].Duration;
                }
                else
                {
                    outro += Segments[i].Duration;
                }
            }

            return intro + (body * cycles) + outro;
        }
    }

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
        CycleStartIndex = CycleStartIndex,
        CycleEndIndex = CycleEndIndex,
        Sensors = new List<string>(Sensors),
        Customer = Customer,
        Project = Project,
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
