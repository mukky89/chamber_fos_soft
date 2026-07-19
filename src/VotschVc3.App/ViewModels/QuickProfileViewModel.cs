using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using VotschVc3.App.Charting;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// Quick builder for a symmetric temperature-sweep profile: ramp from a low to a
/// high temperature through evenly spaced intermediate steps and (optionally)
/// back down again, holding a plateau at every step. Temperatures between the
/// endpoints are computed automatically from the requested step count, and the
/// total time can be shortened, which reduces every plateau evenly.
/// </summary>
public sealed class QuickProfileViewModel : ObservableObject
{
    private static readonly Brush TempBrush = Freeze(0xFF, 0x8A, 0x5C);

    private readonly ProfileStore _store;

    public QuickProfileViewModel(ProfileStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        SaveToLibraryCommand = new RelayCommand(SaveToLibrary);
        EditInEditorCommand = new RelayCommand(EditInEditor);
        NewCommand = new RelayCommand(NewProfile);
        DeleteFromLibraryCommand = new RelayCommand(DeleteFromLibrary);
        ResetNameCommand = new RelayCommand(EnableAutoName);
        Recalculate(); // also generates the initial automatic name
    }

    /// <summary>
    /// Set by the shell so the "Editovať profil" action can hand the saved profile
    /// over to the standalone profile editor (opens it there, loaded and ready).
    /// </summary>
    public Action<Guid>? OpenInEditorRequested { get; set; }

    /// <summary>True while the name is being set programmatically, so the user-edit
    /// detection in <see cref="ProfileName"/> ignores our own writes.</summary>
    private bool _settingNameInternally;

    private bool _autoName = true;
    /// <summary>
    /// When true the profile name is generated automatically from the sweep
    /// parameters (see <see cref="ComposeAutoName"/>). Editing the name box by hand
    /// switches this off; <see cref="ResetNameCommand"/> turns it back on.
    /// </summary>
    public bool IsAutoName
    {
        get => _autoName;
        private set { if (SetProperty(ref _autoName, value)) OnPropertyChanged(nameof(NameModeText)); }
    }

    public string NameModeText => IsAutoName
        ? "Názov sa generuje automaticky z parametrov."
        : "Názov si upravil ručne. Tlačidlom „Automaticky“ sa vráti generovaný názov.";

    private string _namePrefix = string.Empty;
    /// <summary>Optional user prefix placed in front of the generated name (e.g. a project or DUT code).</summary>
    public string NamePrefix
    {
        get => _namePrefix;
        set { if (SetProperty(ref _namePrefix, value)) UpdateAutoName(); }
    }

    private string _profileName = string.Empty;
    public string ProfileName
    {
        get => _profileName;
        set
        {
            if (SetProperty(ref _profileName, value) && !_settingNameInternally)
            {
                // A keystroke in the name box: respect the manual name from now on.
                IsAutoName = false;
            }
        }
    }

    private double _lowTemperature = -20;
    public double LowTemperature { get => _lowTemperature; set { if (SetProperty(ref _lowTemperature, value)) { OnPropertyChanged(nameof(HasLeadIn)); Recalculate(); } } }

    private double _highTemperature = 60;
    public double HighTemperature { get => _highTemperature; set { if (SetProperty(ref _highTemperature, value)) Recalculate(); } }

    private int _intermediateSteps = 7;
    /// <summary>Number of temperatures strictly between the low and high endpoints.</summary>
    public int IntermediateSteps { get => _intermediateSteps; set { if (SetProperty(ref _intermediateSteps, Math.Clamp(value, 0, 50))) Recalculate(); } }

    private bool _useTemperatureStep;
    /// <summary>
    /// When on, the number of intermediate temperatures is derived from a requested
    /// temperature step (<see cref="TemperatureStep"/>) between the endpoints instead of
    /// the explicit <see cref="IntermediateSteps"/> count.
    /// </summary>
    public bool UseTemperatureStep
    {
        get => _useTemperatureStep;
        set { if (SetProperty(ref _useTemperatureStep, value)) { OnPropertyChanged(nameof(UseStepCount)); Recalculate(); } }
    }

    /// <summary>Inverse of <see cref="UseTemperatureStep"/> – true when the explicit step count is used.</summary>
    public bool UseStepCount => !UseTemperatureStep;

    private double _temperatureStep = 10;
    /// <summary>Requested temperature difference (°C) between two consecutive setpoints.</summary>
    public double TemperatureStep { get => _temperatureStep; set { if (SetProperty(ref _temperatureStep, Math.Max(0.1, value))) Recalculate(); } }

    private double _plateauMinutes = 30;
    /// <summary>Base plateau (soak) length at each temperature, before optimisation.</summary>
    public double PlateauMinutes { get => _plateauMinutes; set { if (SetProperty(ref _plateauMinutes, Math.Max(0, value))) Recalculate(); } }

    private double _rampMinutes = 20;
    /// <summary>Ramp length between two consecutive temperatures.</summary>
    public double RampMinutes { get => _rampMinutes; set { if (SetProperty(ref _rampMinutes, Math.Max(0, value))) Recalculate(); } }

    private bool _includeDescending = true;
    /// <summary>Also sweep back down from the high temperature to the low one.</summary>
    public bool IncludeDescending { get => _includeDescending; set { if (SetProperty(ref _includeDescending, value)) Recalculate(); } }

    private bool _doublePeak = true;
    /// <summary>
    /// Split the peak into two highest points with a plateau <see cref="PeakDipCelsius"/>
    /// lower between them, so a temperature change occurs at the top. On by default.
    /// </summary>
    public bool DoublePeak { get => _doublePeak; set { if (SetProperty(ref _doublePeak, value)) Recalculate(); } }

    private bool _startFromCurrent = true;
    /// <summary>
    /// Prepend a ramp from the current chamber temperature (assumed
    /// <see cref="StartTemperature"/> for the preview) to the first sweep temperature,
    /// over <see cref="StartRampMinutes"/> minutes. On by default, so a run always eases
    /// from the actual temperature to the first setpoint instead of jumping to it.
    /// </summary>
    public bool StartFromCurrent { get => _startFromCurrent; set { if (SetProperty(ref _startFromCurrent, value)) { OnPropertyChanged(nameof(HasLeadIn)); Recalculate(); } } }

    private double _startTemperature = 25;
    /// <summary>Assumed current temperature (°C) the initial ramp starts from (preview only;
    /// a real run ramps from the chamber's measured temperature).</summary>
    public double StartTemperature { get => _startTemperature; set { if (SetProperty(ref _startTemperature, value)) { OnPropertyChanged(nameof(HasLeadIn)); Recalculate(); } } }

    private double _startRampMinutes = 60;
    /// <summary>Length (min) of the initial ramp from the current temperature to the first setpoint.</summary>
    public double StartRampMinutes { get => _startRampMinutes; set { if (SetProperty(ref _startRampMinutes, Math.Max(0, value))) { OnPropertyChanged(nameof(HasLeadIn)); Recalculate(); } } }

    /// <summary>True when an initial lead-in ramp is actually added (enabled, non-zero, and not already at the first setpoint).</summary>
    public bool HasLeadIn => StartFromCurrent && StartRampMinutes > 0 && Math.Abs(StartTemperature - LowTemperature) > 0.05;

    private double _peakDipCelsius = 10;
    /// <summary>How much lower (°C) the notch between the two peaks is.</summary>
    public double PeakDipCelsius { get => _peakDipCelsius; set { if (SetProperty(ref _peakDipCelsius, Math.Max(0, value))) Recalculate(); } }

    private double _shortenByHours;
    /// <summary>Reduce the total time by this many hours, spread evenly across the plateaus.</summary>
    public double ShortenByHours { get => _shortenByHours; set { if (SetProperty(ref _shortenByHours, Math.Max(0, value))) Recalculate(); } }

    private string _summary = string.Empty;
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    private string _baseTotalText = "—";
    public string BaseTotalText { get => _baseTotalText; private set => SetProperty(ref _baseTotalText, value); }

    private string _optimizedTotalText = "—";
    public string OptimizedTotalText { get => _optimizedTotalText; private set => SetProperty(ref _optimizedTotalText, value); }

    private string _effectivePlateauText = "—";
    public string EffectivePlateauText { get => _effectivePlateauText; private set => SetProperty(ref _effectivePlateauText, value); }

    private string _rampRateText = "—";
    public string RampRateText { get => _rampRateText; private set => SetProperty(ref _rampRateText, value); }

    private int _segmentCount;
    public int SegmentCount { get => _segmentCount; private set => SetProperty(ref _segmentCount, value); }

    private IReadOnlyList<ChartSeries> _tempPreview = Array.Empty<ChartSeries>();
    public IReadOnlyList<ChartSeries> TempPreview { get => _tempPreview; private set => SetProperty(ref _tempPreview, value); }

    /// <summary>The computed step temperatures, for an informational list.</summary>
    public ObservableCollection<string> StepTemperatures { get; } = new();

    private string _status = "Nastav rozsah a kroky, potom ulož do knižnice.";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private bool _isSaveSuccess;
    /// <summary>True right after a successful save/update, so the view can show a green confirmation banner.</summary>
    public bool IsSaveSuccess { get => _isSaveSuccess; private set => SetProperty(ref _isSaveSuccess, value); }

    private string _namePatternHint = string.Empty;
    /// <summary>Human-readable preview of the naming pattern used for the generated name.</summary>
    public string NamePatternHint { get => _namePatternHint; private set => SetProperty(ref _namePatternHint, value); }

    public RelayCommand SaveToLibraryCommand { get; }

    /// <summary>Saves the profile and opens it in the standalone profile editor.</summary>
    public RelayCommand EditInEditorCommand { get; }

    /// <summary>Resets every parameter to defaults – start a new quick profile from scratch.</summary>
    public RelayCommand NewCommand { get; }

    /// <summary>Deletes the profile with the current name from the shared library (two-click confirm).</summary>
    public RelayCommand DeleteFromLibraryCommand { get; }

    /// <summary>Re-enables automatic naming and regenerates the name from the parameters.</summary>
    public RelayCommand ResetNameCommand { get; }

    private int TemperaturePointCount()
    {
        if (UseTemperatureStep && TemperatureStep > 0)
        {
            double span = Math.Abs(HighTemperature - LowTemperature);
            int intervals = Math.Max(1, (int)Math.Round(span / TemperatureStep));
            return intervals + 1;
        }

        return Math.Max(2, IntermediateSteps + 2);
    }

    private int PlateauCount()
    {
        int n = TemperaturePointCount();
        int count = n + (IncludeDescending ? n - 1 : 0);
        return count + (DoublePeak ? 2 : 0);
    }

    private int RampCount()
    {
        int n = TemperaturePointCount();
        int count = (n - 1) + (IncludeDescending ? n - 1 : 0);
        return count + (DoublePeak ? 2 : 0);
    }

    private double EffectivePlateauMinutes()
    {
        int plateaus = PlateauCount();
        if (plateaus == 0)
        {
            return 0;
        }

        double reduction = Math.Min(ShortenByHours * 60, PlateauMinutes * plateaus);
        return Math.Max(0, PlateauMinutes - reduction / plateaus);
    }

    private List<double> AscendingTemps()
    {
        int n = TemperaturePointCount();
        double delta = (HighTemperature - LowTemperature) / (n - 1);
        var temps = new List<double>(n);
        for (int i = 0; i < n; i++)
        {
            temps.Add(LowTemperature + delta * i);
        }

        temps[n - 1] = HighTemperature; // pin the endpoint to avoid float drift
        return temps;
    }

    private List<ProfileSegment> BuildSegments()
    {
        double plateau = EffectivePlateauMinutes();
        List<double> up = AscendingTemps();
        var segs = new List<ProfileSegment>();

        // Optional lead-in: ease from the current temperature to the first setpoint,
        // so the run ramps up over StartRampMinutes instead of jumping straight to it.
        if (HasLeadIn)
        {
            segs.Add(Ramp(up[0], StartRampMinutes));
        }

        segs.Add(Plateau(up[0], plateau));

        for (int i = 1; i < up.Count; i++)
        {
            segs.Add(Ramp(up[i], RampMinutes));
            segs.Add(Plateau(up[i], plateau));
        }

        // Optional double peak: dip 10 °C (configurable) below the top and back up,
        // giving two highest plateaus with a lower plateau between them.
        if (DoublePeak)
        {
            double dip = HighTemperature - PeakDipCelsius;
            segs.Add(Ramp(dip, RampMinutes));
            segs.Add(Plateau(dip, plateau));
            segs.Add(Ramp(HighTemperature, RampMinutes));
            segs.Add(Plateau(HighTemperature, plateau));
        }

        if (IncludeDescending)
        {
            for (int i = up.Count - 2; i >= 0; i--)
            {
                segs.Add(Ramp(up[i], RampMinutes));
                segs.Add(Plateau(up[i], plateau));
            }
        }

        return segs;
    }

    private static ProfileSegment Ramp(double target, double minutes) => new()
    {
        Name = $"Nábeh {target:0.#} °C",
        TargetTemperature = target,
        Duration = TimeSpan.FromMinutes(minutes),
        IsRamp = true,
    };

    private static ProfileSegment Plateau(double target, double minutes) => new()
    {
        Name = $"Plato {target:0.#} °C",
        TargetTemperature = target,
        Duration = TimeSpan.FromMinutes(minutes),
        IsRamp = false,
    };

    private TestProfile BuildProfile() => new()
    {
        Id = Guid.NewGuid(),
        Name = string.IsNullOrWhiteSpace(ProfileName) ? "Rýchly profil" : ProfileName.Trim(),
        Kind = ChamberKind.TemperatureOnly,
        Cycles = 1,
        CreatedAt = DateTimeOffset.Now,
        Segments = BuildSegments(),
    };

    private void Recalculate()
    {
        List<double> up = AscendingTemps();
        StepTemperatures.Clear();
        foreach (double t in up)
        {
            StepTemperatures.Add($"{t:0.#} °C");
        }

        double plateau = EffectivePlateauMinutes();
        double leadIn = HasLeadIn ? StartRampMinutes : 0;
        double optimized = leadIn + RampCount() * RampMinutes + PlateauCount() * plateau;
        double baseTotal = leadIn + RampCount() * RampMinutes + PlateauCount() * PlateauMinutes;

        BaseTotalText = Format(baseTotal);
        OptimizedTotalText = Format(optimized);
        EffectivePlateauText = $"{plateau:0.#} min / plato";
        SegmentCount = RampCount() + PlateauCount() + (HasLeadIn ? 1 : 0);

        double delta = up.Count > 1 ? (HighTemperature - LowTemperature) / (up.Count - 1) : 0;
        RampRateText = RampMinutes > 0
            ? $"{Math.Abs(delta) / RampMinutes:0.##} °C/min  ({Math.Abs(delta):0.#} °C / krok)"
            : "skok (0 min)";

        Summary = $"{LowTemperature:0.#} → {HighTemperature:0.#} °C, " +
            (UseTemperatureStep
                ? $"krok {TemperatureStep:0.#} °C ({TemperaturePointCount()} bodov)"
                : $"{IntermediateSteps} medzikrokov") +
            (IncludeDescending ? " a späť dole" : string.Empty) +
            (HasLeadIn ? $" · nábeh z {StartTemperature:0.#} °C ({StartRampMinutes:0} min)" : string.Empty);

        UpdateAutoName();
        BuildPreview();
    }

    /// <summary>Re-enables automatic naming (used by the "Automaticky" button).</summary>
    private void EnableAutoName()
    {
        IsAutoName = true;
        UpdateAutoName();
    }

    /// <summary>Refreshes the pattern hint and, when auto-naming is on, the name itself.</summary>
    private void UpdateAutoName()
    {
        string generated = ComposeAutoName();
        NamePatternHint = $"Vzor: {generated}";

        if (!IsAutoName)
        {
            return;
        }

        _settingNameInternally = true;
        ProfileName = generated;
        _settingNameInternally = false;
    }

    /// <summary>
    /// Builds the automatic profile name. Pattern:
    /// <c>[predpona ]Sweep {od}…{do} °C · {N} bodov[ · obojsmerný][ · 2 vrcholy]</c>,
    /// where <c>N</c> is the number of distinct temperature points.
    /// </summary>
    private string ComposeAutoName()
    {
        string core = $"Sweep {LowTemperature:0.#}…{HighTemperature:0.#} °C · {TemperaturePointCount()} bodov";
        if (IncludeDescending)
        {
            core += " · obojsmerný";
        }

        if (DoublePeak)
        {
            core += " · 2 vrcholy";
        }

        string prefix = NamePrefix?.Trim() ?? string.Empty;
        return prefix.Length > 0 ? $"{prefix} {core}" : core;
    }

    private void BuildPreview()
    {
        List<ProfileSegment> segs = BuildSegments();
        var pts = new List<Point>();
        double t = 0;
        double start = HasLeadIn
            ? StartTemperature
            : (segs.Count > 0 ? segs[0].TargetTemperature : LowTemperature);
        pts.Add(new Point(0, start));

        foreach (ProfileSegment s in segs)
        {
            double dur = s.Duration.TotalMinutes;
            if (s.IsRamp)
            {
                t += dur;
                pts.Add(new Point(t, s.TargetTemperature));
            }
            else
            {
                pts.Add(new Point(t, s.TargetTemperature));
                t += dur;
                pts.Add(new Point(t, s.TargetTemperature));
            }
        }

        TempPreview = new[] { new ChartSeries("Teplota", TempBrush, pts) };
    }

    /// <summary>
    /// Upserts the current sweep into the shared library (matched by name so re-saving
    /// updates instead of duplicating) and returns the persisted profile.
    /// </summary>
    private TestProfile Persist(out bool created)
    {
        TestProfile profile = BuildProfile();
        TestProfile? existing = _store.LoadAll()
            .FirstOrDefault(p => string.Equals(p.Name.Trim(), profile.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        created = existing is null;
        if (existing is not null)
        {
            profile.Id = existing.Id;
        }

        _store.Save(profile);
        return profile;
    }

    private void SaveToLibrary()
    {
        DisarmDelete();
        try
        {
            TestProfile profile = Persist(out bool created);
            IsSaveSuccess = true;
            Status = (created
                    ? $"✔ Profil „{profile.Name}“ uložený do knižnice"
                    : $"✔ Profil „{profile.Name}“ aktualizovaný v knižnici") +
                $" ({profile.Segments.Count} segmentov, {Format(profile.SinglePassDuration.TotalMinutes)}). " +
                "Nájdeš ho v Editore profilov aj v rýchlom spustení na karte komory.";
        }
        catch (Exception ex)
        {
            IsSaveSuccess = false;
            Status = $"Uloženie zlyhalo: {ex.Message}";
        }
    }

    /// <summary>Saves the profile and asks the shell to open it in the standalone profile editor.</summary>
    private void EditInEditor()
    {
        DisarmDelete();
        try
        {
            TestProfile profile = Persist(out _);
            IsSaveSuccess = true;
            Status = $"✔ Profil „{profile.Name}“ uložený a otvorený v Editore profilov.";
            OpenInEditorRequested?.Invoke(profile.Id);
        }
        catch (Exception ex)
        {
            IsSaveSuccess = false;
            Status = $"Otvorenie v editore zlyhalo: {ex.Message}";
        }
    }

    /// <summary>Resets every parameter to its default – starts a brand-new quick profile.</summary>
    private void NewProfile()
    {
        DisarmDelete();
        NamePrefix = string.Empty;
        LowTemperature = -20;
        HighTemperature = 60;
        UseTemperatureStep = false;
        IntermediateSteps = 7;
        TemperatureStep = 10;
        PlateauMinutes = 30;
        RampMinutes = 20;
        IncludeDescending = true;
        DoublePeak = true;
        PeakDipCelsius = 10;
        StartFromCurrent = true;
        StartTemperature = 25;
        StartRampMinutes = 60;
        ShortenByHours = 0;
        EnableAutoName(); // back to an auto-generated name
        IsSaveSuccess = false;
        Status = "Nový rýchly profil – parametre vynulované na predvolené.";
    }

    private System.Threading.CancellationTokenSource? _deleteArmCts;

    private bool _isDeleteArmed;
    /// <summary>Two-step delete of the library copy: the first click arms, the second (within 3 s) deletes.</summary>
    public bool IsDeleteArmed
    {
        get => _isDeleteArmed;
        private set { if (SetProperty(ref _isDeleteArmed, value)) OnPropertyChanged(nameof(DeleteButtonText)); }
    }

    /// <summary>Delete button caption reflecting the confirmation state.</summary>
    public string DeleteButtonText => IsDeleteArmed ? "Naozaj vymazať?" : "Vymazať z knižnice";

    private void DisarmDelete()
    {
        _deleteArmCts?.Cancel();
        _deleteArmCts = null;
        IsDeleteArmed = false;
    }

    /// <summary>Deletes the saved profile whose name matches the current one (two-click confirm).</summary>
    private void DeleteFromLibrary()
    {
        string name = (ProfileName ?? string.Empty).Trim();
        TestProfile? existing = _store.LoadAll()
            .FirstOrDefault(p => string.Equals(p.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            IsSaveSuccess = false;
            Status = $"Profil „{name}“ nie je v knižnici – niet čo vymazať.";
            return;
        }

        if (!IsDeleteArmed)
        {
            IsDeleteArmed = true;
            _deleteArmCts?.Cancel();
            _deleteArmCts = new System.Threading.CancellationTokenSource();
            System.Threading.CancellationToken token = _deleteArmCts.Token;
            _ = Task.Delay(TimeSpan.FromSeconds(3), token).ContinueWith(
                _ => IsDeleteArmed = false,
                token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
            Status = $"Vymazať profil „{existing.Name}“ z knižnice? Potvrď druhým klikom do 3 sekúnd.";
            return;
        }

        DisarmDelete();
        _store.Delete(existing.Id);
        IsSaveSuccess = false;
        Status = $"Profil „{existing.Name}“ vymazaný z knižnice.";
    }

    private static string Format(double minutes)
    {
        if (minutes < 1)
        {
            return "< 1 min";
        }

        var ts = TimeSpan.FromMinutes(minutes);
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours} h {ts.Minutes} min" : $"{ts.Minutes} min";
    }

    private static Brush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
