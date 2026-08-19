using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.ViewModels;

/// <summary>How the sweep is described: symmetric parametric range, or a typed sequence of temperatures.</summary>
public enum QuickProfileMode
{
    /// <summary>Low/high range + step count/size, optionally descending back down (the original builder).</summary>
    Parametric,

    /// <summary>An explicit, editable list of temperature points, each with its own plateau (hold) length.</summary>
    Sequence,
}

/// <summary>
/// Quick builder for a temperature profile, in two modes: a symmetric sweep (ramp
/// from a low to a high temperature through evenly spaced steps and, optionally,
/// back down again, holding a plateau at every step), or a typed sequence of
/// temperatures that is turned into alternating ramps and plateaus. Also loads an
/// existing profile back in (as a sequence) so it can be edited and re-saved, and
/// exposes the built segments for direct drag-editing in the profile chart.
/// </summary>
public sealed class QuickProfileViewModel : ObservableObject
{
    private readonly ProfileStore _store;

    public QuickProfileViewModel(ProfileStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        SaveToLibraryCommand = new RelayCommand(SaveToLibrary);
        EditInEditorCommand = new RelayCommand(EditInEditor);
        NewCommand = new RelayCommand(NewProfile);
        DeleteFromLibraryCommand = new RelayCommand(DeleteFromLibrary);
        ResetNameCommand = new RelayCommand(EnableAutoName);
        LoadSelectedProfileCommand = new RelayCommand(
            () => { if (SelectedLibraryProfile is { } p) LoadProfile(p); },
            () => SelectedLibraryProfile is not null);
        AddSequenceStepCommand = new RelayCommand(AddSequenceStep);
        AddSequenceStepsFromTextCommand = new RelayCommand(AddSequenceStepsFromText);
        RemoveSequenceStepCommand = new RelayCommand<SequenceStepViewModel>(RemoveSequenceStep);
        MoveSequenceStepUpCommand = new RelayCommand<SequenceStepViewModel>(s => MoveSequenceStep(s, -1));
        MoveSequenceStepDownCommand = new RelayCommand<SequenceStepViewModel>(s => MoveSequenceStep(s, +1));
        RefreshLibraryProfiles(); // also loads known sensors/tags/customers/projects
        ReplaceSequenceSteps(DefaultSequenceSteps());
        Recalculate(); // also generates the initial automatic name
    }

    /// <summary>Sensors the profile is for (chips).</summary>
    public ObservableCollection<string> EditorSensors { get; } = new();

    /// <summary>Tags of the profile (chips).</summary>
    public ObservableCollection<string> EditorTags { get; } = new();

    /// <summary>Known sensor names from the library (suggestions).</summary>
    public ObservableCollection<string> KnownSensors { get; } = new();

    /// <summary>Known tags from the library (suggestions).</summary>
    public ObservableCollection<string> KnownTags { get; } = new();

    /// <summary>Known customer names from the library, for the "Zákazník" picker.</summary>
    public ObservableCollection<string> KnownCustomers { get; } = new();

    /// <summary>Known project names from the library, for the "Projekt" picker.</summary>
    public ObservableCollection<string> KnownProjects { get; } = new();

    /// <summary>Profiles available in the shared library, for the "load existing profile to edit" picker.</summary>
    public ObservableCollection<TestProfile> LibraryProfiles { get; } = new();

    private TestProfile? _selectedLibraryProfile;
    /// <summary>Profile picked in <see cref="LibraryProfiles"/> to load for editing (see <see cref="LoadSelectedProfileCommand"/>).</summary>
    public TestProfile? SelectedLibraryProfile
    {
        get => _selectedLibraryProfile;
        set { if (SetProperty(ref _selectedLibraryProfile, value)) LoadSelectedProfileCommand.RaiseCanExecuteChanged(); }
    }

    /// <summary>Loads <see cref="SelectedLibraryProfile"/> into the editor (see <see cref="LoadProfile"/>).</summary>
    public RelayCommand LoadSelectedProfileCommand { get; }

    /// <summary>Refreshes <see cref="LibraryProfiles"/> from the store, keeping the current selection if it still exists.</summary>
    public void RefreshLibraryProfiles()
    {
        Guid? previously = SelectedLibraryProfile?.Id;
        LibraryProfiles.Clear();
        foreach (TestProfile p in _store.LoadAll().OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            LibraryProfiles.Add(p);
        }

        SelectedLibraryProfile = previously is { } id ? LibraryProfiles.FirstOrDefault(p => p.Id == id) : null;
        LoadKnownValues();
    }

    /// <summary>
    /// The segments that will actually be saved, exposed for direct drag-editing in
    /// <see cref="Views.ProfileEditorChart"/>. Regenerated from the sweep/sequence
    /// parameters whenever they change; a manual drag edits an entry here directly
    /// and survives until the next parameter change regenerates the list.
    /// </summary>
    public ObservableCollection<SegmentViewModel> Segments { get; } = new();

    private string _customer = string.Empty;
    public string Customer { get => _customer; set => SetProperty(ref _customer, value); }

    private string _project = string.Empty;
    public string Project { get => _project; set => SetProperty(ref _project, value); }

    /// <summary>(Re)loads the sensor/tag/customer/project suggestion lists from every
    /// profile in the library, so newly saved values show up without a restart.</summary>
    private void LoadKnownValues()
    {
        List<TestProfile> all = _store.LoadAll();

        KnownSensors.Clear();
        foreach (string s in all.SelectMany(p => p.Sensors ?? new List<string>())
                     .Where(s => !string.IsNullOrWhiteSpace(s))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
        {
            KnownSensors.Add(s);
        }

        KnownTags.Clear();
        foreach (string t in all.SelectMany(p => p.Tags ?? new List<string>())
                     .Where(t => !string.IsNullOrWhiteSpace(t))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase))
        {
            KnownTags.Add(t);
        }

        KnownCustomers.Clear();
        foreach (string c in all.Select(p => p.Customer)
                     .Where(c => !string.IsNullOrWhiteSpace(c))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase))
        {
            KnownCustomers.Add(c);
        }

        KnownProjects.Clear();
        foreach (string p in all.Select(x => x.Project)
                     .Where(p => !string.IsNullOrWhiteSpace(p))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase))
        {
            KnownProjects.Add(p);
        }
    }

    /// <summary>
    /// Set by the shell so the "Editovať profil" action can hand the saved profile
    /// over to the standalone profile editor (opens it there, loaded and ready).
    /// </summary>
    public Action<Guid>? OpenInEditorRequested { get; set; }

    /// <summary>True while the name is being set programmatically, so the user-edit
    /// detection in <see cref="ProfileName"/> ignores our own writes.</summary>
    private bool _settingNameInternally;

    /// <summary>
    /// Id of the library profile currently being edited (set by <see cref="LoadProfile"/>
    /// or by the first successful save of a session), so re-saving – even after a rename –
    /// updates that same profile instead of matching by name or creating a duplicate.
    /// </summary>
    private Guid? _editingProfileId;

    private QuickProfileMode _mode = QuickProfileMode.Parametric;
    /// <summary>Which builder is active: the symmetric sweep, or a typed temperature sequence.</summary>
    public QuickProfileMode Mode
    {
        get => _mode;
        set
        {
            QuickProfileMode previous = _mode;
            if (SetProperty(ref _mode, value))
            {
                // Switching from the sweep into the typed sequence: carry over the
                // temperatures the sweep is currently set to, so the sequence starts
                // as an editable copy of what was just configured instead of the
                // (likely stale) default points.
                if (value == QuickProfileMode.Sequence && previous == QuickProfileMode.Parametric)
                {
                    ReplaceSequenceSteps(ComputeSweepSequence().Select(t => (t, PlateauMinutes)));
                }

                OnPropertyChanged(nameof(IsParametricMode));
                OnPropertyChanged(nameof(IsSequenceMode));
                OnPropertyChanged(nameof(HasLeadIn));
                Recalculate();
            }
        }
    }

    public bool IsParametricMode
    {
        get => Mode == QuickProfileMode.Parametric;
        set { if (value) Mode = QuickProfileMode.Parametric; }
    }

    public bool IsSequenceMode
    {
        get => Mode == QuickProfileMode.Sequence;
        set { if (value) Mode = QuickProfileMode.Sequence; }
    }

    /// <summary>
    /// The typed temperature sequence, as individually editable points – each with its
    /// own plateau (hold) length – instead of one raw semicolon-separated string with a
    /// single shared hold time. Ramps between consecutive points still share
    /// <see cref="RampMinutes"/>.
    /// </summary>
    public ObservableCollection<SequenceStepViewModel> SequenceSteps { get; } = new();

    private string _newSequenceTemperatures = string.Empty;
    /// <summary>
    /// Quick-add buffer: one or more temperatures separated by semicolons (comma or dot
    /// decimals), appended as new points via <see cref="AddSequenceStepsFromTextCommand"/>
    /// instead of retyping the whole sequence for every point.
    /// </summary>
    public string NewSequenceTemperatures
    {
        get => _newSequenceTemperatures;
        set => SetProperty(ref _newSequenceTemperatures, value);
    }

    /// <summary>Appends a single blank point (same temperature as the last one) to <see cref="SequenceSteps"/>.</summary>
    public RelayCommand AddSequenceStepCommand { get; }

    /// <summary>Parses <see cref="NewSequenceTemperatures"/> and appends each value as a new point.</summary>
    public RelayCommand AddSequenceStepsFromTextCommand { get; }

    /// <summary>Removes one point from <see cref="SequenceSteps"/>.</summary>
    public RelayCommand<SequenceStepViewModel> RemoveSequenceStepCommand { get; }

    /// <summary>Moves a point one position earlier in <see cref="SequenceSteps"/>.</summary>
    public RelayCommand<SequenceStepViewModel> MoveSequenceStepUpCommand { get; }

    /// <summary>Moves a point one position later in <see cref="SequenceSteps"/>.</summary>
    public RelayCommand<SequenceStepViewModel> MoveSequenceStepDownCommand { get; }

    private string _sequenceParseError = string.Empty;
    /// <summary>Non-empty when <see cref="SequenceSteps"/> does not have enough points, or the
    /// quick-add text could not be parsed (shown next to the sequence editor).</summary>
    public string SequenceParseError
    {
        get => _sequenceParseError;
        private set { if (SetProperty(ref _sequenceParseError, value)) OnPropertyChanged(nameof(HasSequenceParseError)); }
    }

    /// <summary>True when <see cref="SequenceParseError"/> has a message to show.</summary>
    public bool HasSequenceParseError => !string.IsNullOrEmpty(SequenceParseError);

    private void AddSequenceStep()
    {
        double temperature = SequenceSteps.Count > 0 ? SequenceSteps[^1].Temperature : 20;
        AddSequenceStepInternal(temperature, PlateauMinutes);
        Recalculate();
    }

    private void AddSequenceStepsFromText()
    {
        if (!TryParseTemperatureList(NewSequenceTemperatures, out List<double> values, out string error))
        {
            Status = error;
            return;
        }

        foreach (double v in values)
        {
            AddSequenceStepInternal(v, PlateauMinutes);
        }

        NewSequenceTemperatures = string.Empty;
        Recalculate();
    }

    private void RemoveSequenceStep(SequenceStepViewModel? step)
    {
        if (step is null)
        {
            return;
        }

        UnhookSequenceStep(step);
        SequenceSteps.Remove(step);
        Recalculate();
    }

    private void MoveSequenceStep(SequenceStepViewModel? step, int direction)
    {
        if (step is null)
        {
            return;
        }

        int from = SequenceSteps.IndexOf(step);
        int to = from + direction;
        if (from < 0 || to < 0 || to >= SequenceSteps.Count)
        {
            return;
        }

        SequenceSteps.Move(from, to);
        Recalculate();
    }

    private void AddSequenceStepInternal(double temperature, double plateauMinutes)
    {
        var step = new SequenceStepViewModel(temperature, plateauMinutes);
        HookSequenceStep(step);
        SequenceSteps.Add(step);
    }

    private void HookSequenceStep(SequenceStepViewModel step) => step.PropertyChanged += OnSequenceStepChanged;

    private void UnhookSequenceStep(SequenceStepViewModel step) => step.PropertyChanged -= OnSequenceStepChanged;

    private void OnSequenceStepChanged(object? sender, PropertyChangedEventArgs e) => Recalculate();

    /// <summary>Replaces <see cref="SequenceSteps"/> wholesale (unhooking/hooking change
    /// notifications correctly) without triggering a recalculation itself – callers
    /// recalculate afterward alongside whatever else they changed.</summary>
    private void ReplaceSequenceSteps(IEnumerable<(double Temperature, double PlateauMinutes)> steps)
    {
        foreach (SequenceStepViewModel s in SequenceSteps)
        {
            UnhookSequenceStep(s);
        }

        SequenceSteps.Clear();
        foreach ((double temperature, double plateauMinutes) in steps)
        {
            AddSequenceStepInternal(temperature, plateauMinutes);
        }
    }

    private List<(double Temperature, double PlateauMinutes)> DefaultSequenceSteps() =>
        new List<double> { 0, 20, 40, 20, 0 }.Select(t => (t, PlateauMinutes)).ToList();

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
    /// <summary>Base plateau (soak) length at each temperature, before optimisation (parametric mode)
    /// or the shared hold length applied to every step (sequence mode).</summary>
    public double PlateauMinutes { get => _plateauMinutes; set { if (SetProperty(ref _plateauMinutes, Math.Max(0, value))) Recalculate(); } }

    private double _rampMinutes = 20;
    /// <summary>Ramp length between two consecutive temperatures.</summary>
    public double RampMinutes { get => _rampMinutes; set { if (SetProperty(ref _rampMinutes, Math.Max(0, value))) Recalculate(); } }

    private bool _includeDescending = true;
    /// <summary>Also sweep back down from the high temperature to the low one (parametric mode only).</summary>
    public bool IncludeDescending { get => _includeDescending; set { if (SetProperty(ref _includeDescending, value)) Recalculate(); } }

    private bool _doublePeak = true;
    /// <summary>
    /// Split the peak into two highest points with a plateau <see cref="PeakDipCelsius"/>
    /// lower between them, so a temperature change occurs at the top. On by default
    /// (parametric mode only).
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

    /// <summary>The temperature the sweep/sequence itself starts at, regardless of mode.</summary>
    private double FirstTargetTemperature
    {
        get
        {
            if (Mode == QuickProfileMode.Sequence)
            {
                return SequenceSteps.Count > 0 ? SequenceSteps[0].Temperature : StartTemperature;
            }

            return LowTemperature;
        }
    }

    /// <summary>True when an initial lead-in ramp is actually added (enabled, non-zero, and not already at the first setpoint).</summary>
    public bool HasLeadIn => StartFromCurrent && StartRampMinutes > 0 && Math.Abs(StartTemperature - FirstTargetTemperature) > 0.05;

    private double _peakDipCelsius = 10;
    /// <summary>How much lower (°C) the notch between the two peaks is (parametric mode only).</summary>
    public double PeakDipCelsius { get => _peakDipCelsius; set { if (SetProperty(ref _peakDipCelsius, Math.Max(0, value))) Recalculate(); } }

    private double _shortenByHours;
    /// <summary>Reduce the total time by this many hours, spread evenly across the plateaus (parametric mode only).</summary>
    public double ShortenByHours { get => _shortenByHours; set { if (SetProperty(ref _shortenByHours, Math.Max(0, value))) Recalculate(); } }

    private int _cycles = 1;
    /// <summary>How many times the profile (or just its sweep body) repeats.</summary>
    public int Cycles { get => _cycles; set { if (SetProperty(ref _cycles, Math.Max(1, value))) Recalculate(); } }

    private bool _cycleBodyOnly = true;
    /// <summary>When cycling, repeat only the sweep body – the initial lead-in ramp and the
    /// closing ramp/hold to room temperature run once (they are not repeated).</summary>
    public bool CycleBodyOnly { get => _cycleBodyOnly; set { if (SetProperty(ref _cycleBodyOnly, value)) Recalculate(); } }

    private bool _endAtSafeTemperature = true;
    /// <summary>
    /// Safety cool-down: append a final ramp to <see cref="EndTemperature"/> (25&#160;°C by
    /// default) followed by a plateau of at least one hour (<see cref="EndHoldMinutes"/>),
    /// so every profile ends parked near room temperature before the chamber powers off.
    /// On by default.
    /// </summary>
    public bool EndAtSafeTemperature { get => _endAtSafeTemperature; set { if (SetProperty(ref _endAtSafeTemperature, value)) Recalculate(); } }

    private double _endTemperature = 25;
    /// <summary>Temperature (°C) the closing safety plateau holds at (default 25&#160;°C).</summary>
    public double EndTemperature { get => _endTemperature; set { if (SetProperty(ref _endTemperature, value)) Recalculate(); } }

    private double _endHoldMinutes = 60;
    /// <summary>Length (min) of the closing safety plateau. Clamped to a minimum of one hour.</summary>
    public double EndHoldMinutes { get => _endHoldMinutes; set { if (SetProperty(ref _endHoldMinutes, Math.Max(60, value))) Recalculate(); } }

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

    private int _cycleBandStart;
    /// <summary>Zero-based first segment index of the repeated region, for the draggable profile chart.</summary>
    public int CycleBandStart { get => _cycleBandStart; private set => SetProperty(ref _cycleBandStart, value); }

    private int _cycleBandEnd;
    /// <summary>Zero-based last segment index (inclusive) of the repeated region, for the draggable profile chart.</summary>
    public int CycleBandEnd { get => _cycleBandEnd; private set => SetProperty(ref _cycleBandEnd, value); }

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

    /// <summary>
    /// Loads an existing profile back into the editor as an editable temperature
    /// sequence, so a previously created profile (whether built here or in the full
    /// editor) can be tweaked and re-saved instead of only ever creating new ones.
    /// Each point's own plateau length is reconstructed from its actual hold segment
    /// (see <see cref="ExtractSequenceSteps"/>), so a profile with different hold times
    /// per step is reproduced faithfully instead of being flattened to one shared
    /// length; the shared ramp length is taken from the first ramp segment found.
    /// Lead-in and end-safety-cooldown are turned off (their source segments become
    /// part of the plain sequence instead) so nothing is double-added; re-enable them
    /// from the checkboxes if the loaded profile needs them.
    /// </summary>
    public void LoadProfile(TestProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        DisarmDelete();
        _editingProfileId = profile.Id;
        Mode = QuickProfileMode.Sequence;

        StartFromCurrent = false;
        EndAtSafeTemperature = false;

        List<(double Temperature, double PlateauMinutes)> sequence = ExtractSequenceSteps(profile.Segments);
        ReplaceSequenceSteps(sequence.Count > 0 ? sequence : DefaultSequenceSteps());

        ProfileSegment? firstRamp = profile.Segments.FirstOrDefault(s => s.IsRamp);
        if (firstRamp is not null) RampMinutes = Math.Max(0, firstRamp.Duration.TotalMinutes);

        Cycles = Math.Max(1, profile.Cycles);
        CycleBodyOnly = profile.HasCycleRegion;

        Customer = profile.Customer;
        Project = profile.Project;
        EditorSensors.Clear();
        foreach (string s in profile.Sensors) EditorSensors.Add(s);
        EditorTags.Clear();
        foreach (string t in profile.Tags) EditorTags.Add(t);

        _settingNameInternally = true;
        ProfileName = profile.Name;
        _settingNameInternally = false;
        IsAutoName = false;

        IsSaveSuccess = false;
        Status = $"Profil „{profile.Name}“ načítaný na úpravu ({sequence.Count} teplotných bodov, každý s vlastnou dĺžkou plata). " +
            "Nábeh a bezpečnostné ukončenie sú vypnuté – zapni ich znova, ak ich profil potrebuje.";
        Recalculate();
    }

    /// <summary>
    /// Reconstructs sequence points from a segment list: every hold (non-ramp) segment
    /// becomes one point, carrying its own actual duration as that point's plateau
    /// length – so re-loading a profile reproduces its original per-step hold times
    /// instead of flattening them to one shared length. A ramp's target temperature
    /// becomes a (zero-length) point of its own only when nothing follows it at the
    /// same temperature (e.g. a profile with no explicit holds); otherwise the ramp
    /// simply establishes the point that the next hold's duration then folds into.
    /// </summary>
    private static List<(double Temperature, double PlateauMinutes)> ExtractSequenceSteps(IReadOnlyList<ProfileSegment> segments)
    {
        var result = new List<(double Temperature, double PlateauMinutes)>();
        foreach (ProfileSegment s in segments)
        {
            double t = Math.Round(s.TargetTemperature, 1);
            double holdMinutes = s.IsRamp ? 0 : Math.Max(0, s.Duration.TotalMinutes);

            if (result.Count > 0 && Math.Abs(result[^1].Temperature - t) <= 0.05)
            {
                result[^1] = (result[^1].Temperature, result[^1].PlateauMinutes + holdMinutes);
            }
            else
            {
                result.Add((t, holdMinutes));
            }
        }

        return result;
    }

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

    /// <summary>
    /// Total profile length in minutes for a given per-plateau length: lead-in ramp +
    /// all sweep ramps &amp; plateaus + the closing safety hold (ramp + fixed ≥1 h plateau).
    /// The closing hold is a fixed length and is never shortened by the optimisation.
    /// </summary>
    private double TotalMinutes(double plateauMinutes)
    {
        double leadIn = HasLeadIn ? StartRampMinutes : 0;
        double endHold = EndAtSafeTemperature ? RampMinutes + Math.Max(60, EndHoldMinutes) : 0;
        double sweep = RampCount() * RampMinutes + PlateauCount() * plateauMinutes;

        int cyc = Math.Max(1, Cycles);
        if (cyc <= 1)
        {
            return leadIn + sweep + endHold;
        }

        // Body-only cycling repeats just the sweep; otherwise the whole profile repeats.
        return CycleBodyOnly ? leadIn + sweep * cyc + endHold : (leadIn + sweep + endHold) * cyc;
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

    /// <summary>
    /// The sweep's own temperature points, in the same order the parametric builder
    /// visits them – ascending run, optional double-peak dip/high, optional descending
    /// run back down – but without the lead-in or end-safety segments. Used to seed
    /// <see cref="SequenceSteps"/> when switching from "Sweep" to "Postupnosť teplôt".
    /// </summary>
    private List<double> ComputeSweepSequence()
    {
        List<double> up = AscendingTemps();
        var temps = new List<double>(up);

        if (DoublePeak)
        {
            temps.Add(HighTemperature - PeakDipCelsius);
            temps.Add(HighTemperature);
        }

        if (IncludeDescending)
        {
            for (int i = up.Count - 2; i >= 0; i--)
            {
                temps.Add(up[i]);
            }
        }

        return temps;
    }

    /// <summary>Builds the segment list from the active mode's generator.</summary>
    private List<ProfileSegment> BuildSegments() =>
        Mode == QuickProfileMode.Sequence ? BuildSequenceSegments() : BuildParametricSegments();

    private List<ProfileSegment> BuildParametricSegments()
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

        // Safety cool-down: always end the profile parked at a safe temperature
        // (25 °C by default) held for at least an hour, so the chamber is near room
        // temperature by the time the run finishes and the power is cut. This hold
        // is fixed length – the time-shortening optimisation never touches it.
        if (EndAtSafeTemperature)
        {
            segs.Add(Ramp(EndTemperature, RampMinutes));
            segs.Add(Plateau(EndTemperature, Math.Max(60, EndHoldMinutes)));
        }

        return segs;
    }

    /// <summary>
    /// Builds a step-and-hold segment list from <see cref="SequenceSteps"/>: a plateau
    /// at the first point (its own length), then a ramp (shared <see cref="RampMinutes"/>)
    /// + plateau (that point's own length) for every following point. Returns an empty
    /// list while there are fewer than two points.
    /// </summary>
    private List<ProfileSegment> BuildSequenceSegments()
    {
        var segs = new List<ProfileSegment>();
        if (SequenceSteps.Count < 2)
        {
            return segs;
        }

        if (HasLeadIn)
        {
            segs.Add(Ramp(SequenceSteps[0].Temperature, StartRampMinutes));
        }

        segs.Add(Plateau(SequenceSteps[0].Temperature, SequenceSteps[0].PlateauMinutes));
        for (int i = 1; i < SequenceSteps.Count; i++)
        {
            segs.Add(Ramp(SequenceSteps[i].Temperature, RampMinutes));
            segs.Add(Plateau(SequenceSteps[i].Temperature, SequenceSteps[i].PlateauMinutes));
        }

        if (EndAtSafeTemperature)
        {
            segs.Add(Ramp(EndTemperature, RampMinutes));
            segs.Add(Plateau(EndTemperature, Math.Max(60, EndHoldMinutes)));
        }

        return segs;
    }

    /// <summary>
    /// Parses a semicolon-separated list of temperatures, e.g. "20;30,5;60" for the
    /// quick-add buffer. Each token accepts either '.' or ',' as the decimal separator.
    /// Unlike the old whole-sequence parser this only needs one value (it appends to
    /// whatever points already exist).
    /// </summary>
    private static bool TryParseTemperatureList(string? text, out List<double> values, out string error)
    {
        values = new List<double>();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Zadaj aspoň jednu teplotu (voliteľne viac oddelených bodkočiarkou).";
            return false;
        }

        foreach (string raw in text.Split(';'))
        {
            string token = raw.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            if (!TryParseTemperature(token, out double value))
            {
                error = $"Neplatná teplota „{token}“.";
                values.Clear();
                return false;
            }

            values.Add(value);
        }

        if (values.Count == 0)
        {
            error = "Zadaj aspoň jednu teplotu.";
            return false;
        }

        return true;
    }

    private static bool TryParseTemperature(string token, out double value) =>
        double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(token.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

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

    /// <summary>
    /// Builds the profile that will be saved, from the current <see cref="Segments"/>
    /// (which reflects both the generator and any manual chart drag) rather than
    /// regenerating – so a drag edit in the chart is what actually gets persisted.
    /// </summary>
    private TestProfile BuildProfile()
    {
        List<ProfileSegment> segs = Segments.Count > 0
            ? Segments.Select(s => s.ToModel()).ToList()
            : BuildSegments();
        int cyc = Math.Max(1, Cycles);
        (int start, int end) = ResolveCycleRegion(segs.Count);

        return new TestProfile
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(ProfileName) ? "Rýchly profil" : ProfileName.Trim(),
            Kind = ChamberKind.TemperatureOnly,
            Cycles = cyc,
            CycleStartIndex = start,
            CycleEndIndex = end,
            Customer = Customer.Trim(),
            Project = Project.Trim(),
            Sensors = EditorSensors.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Tags = EditorTags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            CreatedAt = DateTimeOffset.Now,
            Segments = segs,
        };
    }

    /// <summary>
    /// Resolves the repeated region as segment indices: with "body only" the lead-in
    /// ramp (1 segment) and the closing ramp+hold (2 segments) run once, and the sweep
    /// in between repeats; otherwise the whole profile is the region.
    /// </summary>
    private (int Start, int End) ResolveCycleRegion(int segmentCount)
    {
        int start = 0, end = Math.Max(0, segmentCount - 1);
        if (Math.Max(1, Cycles) > 1 && CycleBodyOnly)
        {
            start = HasLeadIn ? 1 : 0;
            end = segmentCount - 1 - (EndAtSafeTemperature ? 2 : 0);
            if (end < start)
            {
                start = 0;
                end = Math.Max(0, segmentCount - 1);
            }
        }

        return (start, end);
    }

    /// <summary>Regenerates <see cref="Segments"/> from the active mode's generator, discarding
    /// any manual chart drag made since the last regeneration.</summary>
    private void RefreshSegmentsFromGenerator()
    {
        List<ProfileSegment> segs = BuildSegments();
        Segments.Clear();
        foreach (ProfileSegment s in segs)
        {
            Segments.Add(new SegmentViewModel(s));
        }

        (int start, int end) = ResolveCycleRegion(segs.Count);
        CycleBandStart = start;
        CycleBandEnd = end;
    }

    private void Recalculate()
    {
        RefreshSegmentsFromGenerator();

        if (Mode == QuickProfileMode.Sequence)
        {
            RecalculateSequence();
        }
        else
        {
            RecalculateParametric();
        }

        UpdateAutoName();
    }

    private void RecalculateParametric()
    {
        List<double> up = AscendingTemps();
        StepTemperatures.Clear();
        foreach (double t in up)
        {
            StepTemperatures.Add($"{t:0.#} °C");
        }

        double plateau = EffectivePlateauMinutes();
        double optimized = TotalMinutes(plateau);
        double baseTotal = TotalMinutes(PlateauMinutes);

        BaseTotalText = Format(baseTotal);
        OptimizedTotalText = Format(optimized);
        EffectivePlateauText = $"{plateau:0.#} min / plato";
        SegmentCount = RampCount() + PlateauCount() + (HasLeadIn ? 1 : 0) + (EndAtSafeTemperature ? 2 : 0);

        double delta = up.Count > 1 ? (HighTemperature - LowTemperature) / (up.Count - 1) : 0;
        RampRateText = RampMinutes > 0
            ? $"{Math.Abs(delta) / RampMinutes:0.##} °C/min  ({Math.Abs(delta):0.#} °C / krok)"
            : "skok (0 min)";

        Summary = ComposeSummary();
    }

    private void RecalculateSequence()
    {
        SequenceParseError = SequenceSteps.Count < 2
            ? "Pridaj aspoň dve teploty (body postupnosti)."
            : string.Empty;

        StepTemperatures.Clear();
        foreach (SequenceStepViewModel step in SequenceSteps)
        {
            StepTemperatures.Add($"{step.Temperature:0.#} °C · {step.PlateauMinutes:0.#} min");
        }

        SegmentCount = Segments.Count;
        TestProfile profile = BuildProfile();
        BaseTotalText = Format(profile.TotalDuration.TotalMinutes);
        OptimizedTotalText = BaseTotalText;
        double avgPlateau = SequenceSteps.Count > 0 ? SequenceSteps.Average(s => s.PlateauMinutes) : 0;
        EffectivePlateauText = $"{avgPlateau:0.#} min / plato (priemer)";
        RampRateText = RampMinutes > 0 ? $"{RampMinutes:0.#} min / rampa" : "skok (0 min)";
        Summary = ComposeSequenceSummary();
    }

    /// <summary>
    /// Human-readable description of the sweep shown next to "Náhľad profilu".
    /// Also drives the automatically generated profile name (see <see cref="ComposeAutoName"/>).
    /// </summary>
    private string ComposeSummary() =>
        $"{LowTemperature:0.#} → {HighTemperature:0.#} °C, " +
        (UseTemperatureStep
            ? $"krok {TemperatureStep:0.#} °C ({TemperaturePointCount()} bodov)"
            : $"{IntermediateSteps} medzikrokov") +
        (IncludeDescending ? " a späť dole" : string.Empty) +
        (DoublePeak ? " · 2 vrcholy" : string.Empty) +
        (HasLeadIn ? $" · nábeh z {StartTemperature:0.#} °C ({StartRampMinutes:0} min)" : string.Empty) +
        (EndAtSafeTemperature ? $" · koniec na {EndTemperature:0.#} °C ({Math.Max(60, EndHoldMinutes):0} min)" : string.Empty) +
        (Cycles > 1 ? $" · cyklus ×{Cycles}{(CycleBodyOnly ? " (telo)" : string.Empty)}" : string.Empty);

    /// <summary>Same description as <see cref="ComposeSummary"/>, for a typed sequence.</summary>
    private string ComposeSequenceSummary() =>
        SequenceSteps.Count == 0
            ? SequenceParseError
            : string.Join(" → ", SequenceSteps.Select(s => s.Temperature.ToString("0.#", CultureInfo.CurrentCulture))) + " °C" +
              $" · rampa {RampMinutes:0.#} min" +
              (HasLeadIn ? $" · nábeh z {StartTemperature:0.#} °C ({StartRampMinutes:0} min)" : string.Empty) +
              (EndAtSafeTemperature ? $" · koniec na {EndTemperature:0.#} °C ({Math.Max(60, EndHoldMinutes):0} min)" : string.Empty) +
              (Cycles > 1 ? $" · cyklus ×{Cycles}{(CycleBodyOnly ? " (telo)" : string.Empty)}" : string.Empty);

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
    /// Builds a compact, technical profile name from the active mode's parameters.
    /// Optionally prefixed with <see cref="NamePrefix"/>.
    /// </summary>
    private string ComposeAutoName()
    {
        string core = Mode == QuickProfileMode.Sequence ? ComposeSequenceAutoNameCore() : ComposeParametricAutoNameCore();
        string prefix = NamePrefix?.Trim() ?? string.Empty;
        return prefix.Length > 0 ? $"{prefix} {core}" : core;
    }

    /// <summary>
    /// Compact technical name for the parametric sweep: temperature range, number of
    /// steps, the °C step between them, plateau length and the total profile duration –
    /// e.g. <c>-20→60 °C · 9 krokov · krok 10 °C · ↕ · plato 30m · Σ 1d 2h</c>.
    /// </summary>
    private string ComposeParametricAutoNameCore()
    {
        int points = TemperaturePointCount();
        double step = points > 1 ? Math.Abs(HighTemperature - LowTemperature) / (points - 1) : 0;
        double plateau = EffectivePlateauMinutes();

        return $"{LowTemperature:0.#}→{HighTemperature:0.#} °C · {points} krokov · krok {step:0.#} °C" +
            (IncludeDescending ? " · ↕" : string.Empty) +
            (Cycles > 1 ? $" · ×{Cycles}" : string.Empty) +
            $" · plato {plateau:0.#}m · Σ {FormatDurationCompact(TotalMinutes(plateau))}";
    }

    /// <summary>Compact technical name for a typed sequence, e.g. <c>0-20-40-20-0 °C · Σ 3h 20min</c>.</summary>
    private string ComposeSequenceAutoNameCore()
    {
        if (SequenceSteps.Count < 2)
        {
            return "Rýchly profil (postupnosť)";
        }

        double totalMinutes = Segments.Sum(s => s.DurationMinutes);
        return $"{string.Join("-", SequenceSteps.Select(s => s.Temperature.ToString("0.#", CultureInfo.InvariantCulture)))} °C" +
            (Cycles > 1 ? $" · ×{Cycles}" : string.Empty) +
            $" · Σ {FormatDurationCompact(totalMinutes)}";
    }

    /// <summary>
    /// Upserts the current sweep into the shared library and returns the persisted
    /// profile. Matches the profile being edited by id when one is loaded (see
    /// <see cref="LoadProfile"/> / <see cref="_editingProfileId"/>) so a rename during
    /// editing still updates it in place; otherwise matches by name, as before.
    /// </summary>
    private TestProfile Persist(out bool created)
    {
        TestProfile profile = BuildProfile();
        TestProfile? existing = _editingProfileId is { } id
            ? _store.LoadAll().FirstOrDefault(p => p.Id == id)
            : _store.LoadAll().FirstOrDefault(p => string.Equals(p.Name.Trim(), profile.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        created = existing is null;
        if (existing is not null)
        {
            profile.Id = existing.Id;
        }

        _editingProfileId = profile.Id;
        _store.Save(profile);
        return profile;
    }

    private void SaveToLibrary()
    {
        DisarmDelete();
        try
        {
            TestProfile profile = Persist(out bool created);
            RefreshLibraryProfiles();
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
            RefreshLibraryProfiles();
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
        _editingProfileId = null;
        Mode = QuickProfileMode.Parametric;
        NamePrefix = string.Empty;
        LowTemperature = -20;
        HighTemperature = 60;
        UseTemperatureStep = false;
        IntermediateSteps = 7;
        TemperatureStep = 10;
        PlateauMinutes = 30;
        RampMinutes = 20;
        ReplaceSequenceSteps(DefaultSequenceSteps());
        IncludeDescending = true;
        DoublePeak = true;
        PeakDipCelsius = 10;
        StartFromCurrent = true;
        StartTemperature = 25;
        StartRampMinutes = 60;
        ShortenByHours = 0;
        EndAtSafeTemperature = true;
        EndTemperature = 25;
        EndHoldMinutes = 60;
        Cycles = 1;
        CycleBodyOnly = true;
        Customer = string.Empty;
        Project = string.Empty;
        EditorSensors.Clear();
        EditorTags.Clear();
        EnableAutoName(); // back to an auto-generated name
        IsSaveSuccess = false;
        Status = "Nový rýchly profil – parametre vynulované na predvolené.";
        Recalculate(); // make sure the graph reflects the reset state even if every
                        // individual setter above happened to be a no-op change
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

    /// <summary>Deletes the saved profile whose name matches the current one (with confirmation).</summary>
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

        bool confirmed = Views.ConfirmDialog.Ask(
            $"Naozaj vymazať profil „{existing.Name}“ z knižnice? Túto akciu nie je možné vrátiť.",
            "Vymazať profil",
            "Vymazať");
        if (!confirmed)
        {
            Status = "Mazanie zrušené.";
            return;
        }

        _store.Delete(existing.Id);
        RefreshLibraryProfiles();
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
        if (ts.TotalDays >= 1)
        {
            return $"{(int)ts.TotalDays} d {ts.Hours} h {ts.Minutes} min";
        }

        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours} h {ts.Minutes} min" : $"{ts.Minutes} min";
    }

    /// <summary>Compact duration (for the technical profile name), e.g. <c>1d 2h</c>, <c>3h 20min</c>, <c>45min</c>.</summary>
    private static string FormatDurationCompact(double minutes)
    {
        var ts = TimeSpan.FromMinutes(Math.Max(0, minutes));
        if (ts.TotalDays >= 1)
        {
            return $"{(int)ts.TotalDays}d {ts.Hours}h" + (ts.Minutes > 0 ? $" {ts.Minutes}min" : string.Empty);
        }

        if (ts.TotalHours >= 1)
        {
            return $"{ts.Hours}h" + (ts.Minutes > 0 ? $" {ts.Minutes}min" : string.Empty);
        }

        return $"{(int)Math.Round(ts.TotalMinutes)}min";
    }
}
