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

    /// <summary>
    /// Short, human-readable, library-unique identifier ("P-0007"), assigned by
    /// <see cref="ProfileStore"/> when the profile is first saved. It is what an operator
    /// quotes in a report or on the phone – <see cref="Id"/> is a GUID nobody can read out –
    /// and it prefixes the profile's file name, so the folder sorts in the order profiles
    /// were created. Empty until the profile has been saved.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name of the profile.</summary>
    public string Name { get; set; } = "New profile";

    /// <summary>Code and name as one line for pickers and headings ("P-0007 · Sweep -40…150").</summary>
    [JsonIgnore]
    public string CodeAndName => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} · {Name}";

    /// <summary><c>true</c> once the profile carries a library code.</summary>
    [JsonIgnore]
    public bool HasCode => !string.IsNullOrWhiteSpace(Code);

    /// <summary>Original name as it came from the imported file, kept when the app generates
    /// a new standardized <see cref="Name"/>. Empty for profiles authored directly in the app.</summary>
    public string OriginalName { get; set; } = string.Empty;

    /// <summary>Which chamber type the profile was authored for.</summary>
    public ChamberKind Kind { get; set; } = ChamberKind.TemperatureHumidity;

    /// <summary>
    /// Which device family the profile was built for (Vötsch ramps+plateaus vs. SIKA
    /// setpoint+dwell). <see cref="ProfileDeviceKind.Any"/> – the default – keeps every
    /// previously saved profile visible on every device.
    /// </summary>
    public ProfileDeviceKind DeviceKind { get; set; } = ProfileDeviceKind.Any;

    /// <summary>Short label of <see cref="DeviceKind"/> for pickers and badges ("Vötsch", "SIKA", "Univerzálny").</summary>
    [JsonIgnore]
    public string DeviceKindLabel => DeviceKind.Label();

    /// <summary>Normal test or PeakLogger-backed FBG temperature calibration.</summary>
    public ProfileExecutionMode ExecutionMode { get; set; } = ProfileExecutionMode.Normal;

    /// <summary>When the profile was created / last saved.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// When the profile was last written to the library (stamped by
    /// <see cref="ProfileStore.Save"/>). <c>null</c> for profiles stored before this was
    /// tracked – use <see cref="LastChangedAt"/>, which falls back to <see cref="CreatedAt"/>.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Operator pin: favourite profiles are shown at the top of profile pickers.</summary>
    public bool IsFavorite { get; set; }

    /// <summary>Archived profiles stay stored but are hidden from normal profile pickers.</summary>
    public bool IsArchived { get; set; }

    /// <summary>When the profile was last created or edited – the sort key of the
    /// "Najnovšie" group in the profile picker.</summary>
    [JsonIgnore]
    public DateTimeOffset LastChangedAt => UpdatedAt ?? CreatedAt;

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

    /// <summary>Free-form operator note shown with the profile previews (optional).</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Readiness state. TBT is intentionally the safe default for old and new profiles.</summary>
    public ProfileValidationStatus ValidationStatus { get; set; } = ProfileValidationStatus.TBT;

    /// <summary>Important operator warning shown prominently whenever the profile is selected.</summary>
    public string Warning { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasWarning => !string.IsNullOrWhiteSpace(Warning);

    [JsonIgnore]
    public string ValidationStatusDescription => ValidationStatus.Description();

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
        Code = Code,
        Name = Name,
        OriginalName = OriginalName,
        Kind = Kind,
        DeviceKind = DeviceKind,
        ExecutionMode = ExecutionMode,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        IsFavorite = IsFavorite,
        IsArchived = IsArchived,
        Cycles = Cycles,
        CycleStartIndex = CycleStartIndex,
        CycleEndIndex = CycleEndIndex,
        Sensors = new List<string>(Sensors),
        Customer = Customer,
        Project = Project,
        Notes = Notes,
        ValidationStatus = ValidationStatus,
        Warning = Warning,
        Tags = new List<string>(Tags),
        Segments = Segments.Select(s => new ProfileSegment
        {
            Name = s.Name,
            TargetTemperature = s.TargetTemperature,
            TargetHumidity = s.TargetHumidity,
            Duration = s.Duration,
            IsRamp = s.IsRamp,
            IsCalibrationPoint = s.IsCalibrationPoint,
            GuaranteedSoak = s.GuaranteedSoak,
            SoakTolerance = s.SoakTolerance,
        }).ToList(),
    };
}
