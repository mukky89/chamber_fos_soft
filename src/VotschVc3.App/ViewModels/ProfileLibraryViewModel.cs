using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using VotschVc3.App.Charting;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// Standalone profile editor / library – create, edit, import/export and store
/// test profiles without being connected to a chamber (like the SIMPATI program
/// editor). Profiles are saved to the same history used by the chambers.
/// </summary>
public sealed class ProfileLibraryViewModel : ObservableObject
{
    private static readonly Brush HumBrush = Freeze(0x4F, 0xB6, 0xFF);

    private readonly ProfileStore _store;

    public ProfileLibraryViewModel(ProfileStore store)
    {
        _store = store;
        Segments = new ObservableCollection<SegmentViewModel>();
        Segments.CollectionChanged += OnSegmentsChanged;

        AddSegmentCommand = new RelayCommand(AddSegment);
        AddSegmentBeforeCommand = new RelayCommand(() => InsertSegment(0), () => SelectedSegment is not null);
        AddSegmentAfterCommand = new RelayCommand(() => InsertSegment(1), () => SelectedSegment is not null);
        RemoveSegmentCommand = new RelayCommand(RemoveSegment, () => SelectedSegment is not null);
        MoveSegmentUpCommand = new RelayCommand(() => MoveSegment(-1), () => SelectedSegment is not null);
        MoveSegmentDownCommand = new RelayCommand(() => MoveSegment(+1), () => SelectedSegment is not null);
        ToggleSegmentsExpandCommand = new RelayCommand(() => IsSegmentsExpanded = !IsSegmentsExpanded);
        NewProfileCommand = new RelayCommand(NewProfile);
        SaveToHistoryCommand = new RelayCommand(SaveToHistory, () => Segments.Count > 0);
        LoadFromHistoryCommand = new RelayCommand(LoadFromHistory, () => SelectedHistoryProfile is not null);
        DeleteFromHistoryCommand = new RelayCommand(DeleteFromHistory, () => SelectedHistoryProfile is not null);
        DuplicateProfileCommand = new RelayCommand(DuplicateProfile, () => SelectedHistoryProfile is not null);
        RefreshHistoryCommand = new RelayCommand(RefreshFromStore);
        ImportProfileCommand = new RelayCommand(ImportProfile);
        BulkImportCommand = new RelayCommand(BulkImport);
        ExportProfileCommand = new RelayCommand(ExportProfile, () => Segments.Count > 0);
        ExportLibraryCommand = new RelayCommand(ExportLibrary);
        GenerateStandardNameCommand = new RelayCommand(GenerateStandardName, () => Segments.Count > 0);
        ExpandAllCommand = new RelayCommand(() => SetAllExpanded(true));
        CollapseAllCommand = new RelayCommand(() => SetAllExpanded(false));
        ClearFilterCommand = new RelayCommand(() => { FilterText = string.Empty; SelectedTag = AllTagsOption; });

        SeedDefaultProfile();
        RefreshHistory();
        Recalculate();
    }

    public Array ChamberKinds { get; } = Enum.GetValues(typeof(ChamberKind));

    private ChamberKind _kind = ChamberKind.TemperatureHumidity;
    public ChamberKind Kind
    {
        get => _kind;
        set
        {
            if (SetProperty(ref _kind, value))
            {
                OnPropertyChanged(nameof(SupportsHumidity));
                Recalculate();
            }
        }
    }

    public bool SupportsHumidity => Kind == ChamberKind.TemperatureHumidity;

    public ObservableCollection<SegmentViewModel> Segments { get; }

    private SegmentViewModel? _selectedSegment;
    public SegmentViewModel? SelectedSegment
    {
        get => _selectedSegment;
        set
        {
            if (SetProperty(ref _selectedSegment, value))
            {
                RemoveSegmentCommand.RaiseCanExecuteChanged();
                MoveSegmentUpCommand.RaiseCanExecuteChanged();
                MoveSegmentDownCommand.RaiseCanExecuteChanged();
                AddSegmentBeforeCommand.RaiseCanExecuteChanged();
                AddSegmentAfterCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _profileName = "Nový profil";
    public string ProfileName { get => _profileName; set => SetProperty(ref _profileName, value); }

    private string _originalName = string.Empty;
    /// <summary>Original (imported) name, preserved when the app generates a standardized name.</summary>
    public string OriginalName
    {
        get => _originalName;
        set { if (SetProperty(ref _originalName, value)) OnPropertyChanged(nameof(HasOriginalName)); }
    }

    public bool HasOriginalName => !string.IsNullOrWhiteSpace(OriginalName);

    /// <summary>Sensors the edited profile is for (chips; one profile can serve several).</summary>
    public ObservableCollection<string> EditorSensors { get; } = new();

    /// <summary>Tags of the edited profile (chips).</summary>
    public ObservableCollection<string> EditorTags { get; } = new();

    /// <summary>Distinct sensor names across the library, offered as suggestions in the editor.</summary>
    public ObservableCollection<string> KnownSensors { get; } = new();

    /// <summary>Distinct tags across the library, offered as suggestions in the editor.</summary>
    public ObservableCollection<string> KnownTags { get; } = new();

    private int _cycles = 1;
    public int Cycles { get => _cycles; set { if (SetProperty(ref _cycles, Math.Max(1, value))) Recalculate(); } }

    private int _cycleFromSegment = 1;
    /// <summary>First segment (1-based) of the repeated region.</summary>
    public int CycleFromSegment
    {
        get => _cycleFromSegment;
        set { if (SetProperty(ref _cycleFromSegment, ClampSegment(value))) RaiseCycleRegion(); }
    }

    private int _cycleToSegment = 1;
    /// <summary>Last segment (1-based, inclusive) of the repeated region.</summary>
    public int CycleToSegment
    {
        get => _cycleToSegment;
        set { if (SetProperty(ref _cycleToSegment, ClampSegment(value))) RaiseCycleRegion(); }
    }

    /// <summary>Zero-based region start for the chart band.</summary>
    public int CycleBandStart => Math.Min(CycleFromSegment, CycleToSegment) - 1;

    /// <summary>Zero-based region end for the chart band.</summary>
    public int CycleBandEnd => Math.Max(CycleFromSegment, CycleToSegment) - 1;

    /// <summary>Human-readable description of what is cycled and how many times.</summary>
    public string CycleRegionText
    {
        get
        {
            if (Cycles <= 1)
            {
                return "Cyklovanie vypnuté (1×). Zvýš počet cyklov a označ rozsah segmentov.";
            }

            int from = Math.Min(CycleFromSegment, CycleToSegment);
            int to = Math.Max(CycleFromSegment, CycleToSegment);
            bool whole = from <= 1 && to >= Segments.Count;
            return whole
                ? $"Cykluje sa celý profil ×{Cycles}."
                : $"Cyklujú sa segmenty {from}–{to} ×{Cycles} · okolité segmenty (nábeh/koniec) prebehnú raz.";
        }
    }

    private int ClampSegment(int value) => Math.Clamp(value, 1, Math.Max(1, Segments.Count));

    private void RaiseCycleRegion()
    {
        OnPropertyChanged(nameof(CycleBandStart));
        OnPropertyChanged(nameof(CycleBandEnd));
        OnPropertyChanged(nameof(CycleRegionText));
    }

    private string _profileDurationText = "—";
    public string ProfileDurationText { get => _profileDurationText; private set => SetProperty(ref _profileDurationText, value); }

    private string _profileWarnings = string.Empty;
    public string ProfileWarnings { get => _profileWarnings; private set { if (SetProperty(ref _profileWarnings, value)) OnPropertyChanged(nameof(HasProfileWarnings)); } }
    public bool HasProfileWarnings => !string.IsNullOrEmpty(ProfileWarnings);

    private IReadOnlyList<ChartSeries> _humPreview = Array.Empty<ChartSeries>();
    public IReadOnlyList<ChartSeries> HumPreview { get => _humPreview; private set => SetProperty(ref _humPreview, value); }

    private string _statusMessage = "Vytvor alebo načítaj profil. Pripojenie ku komore nie je potrebné.";
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public ObservableCollection<TestProfile> History { get; } = new();

    /// <summary>Sensor-grouped, filtered tree shown in the library panel.</summary>
    public ObservableCollection<ProfileTreeGroupViewModel> ProfileTree { get; } = new();

    /// <summary>Sentinel item meaning "no tag filter".</summary>
    public const string AllTagsOption = "— všetky tagy —";

    /// <summary>Distinct tags across the library, plus the "all" sentinel, for the tag filter.</summary>
    public ObservableCollection<string> AvailableTags { get; } = new() { AllTagsOption };

    private string _filterText = string.Empty;
    /// <summary>Free-text filter over profile name, sensor and tags.</summary>
    public string FilterText
    {
        get => _filterText;
        set { if (SetProperty(ref _filterText, value)) RebuildTree(); }
    }

    private string _selectedTag = AllTagsOption;
    /// <summary>Selected tag filter (or <see cref="AllTagsOption"/> for no tag filter).</summary>
    public string SelectedTag
    {
        get => _selectedTag;
        set { if (SetProperty(ref _selectedTag, value ?? AllTagsOption)) RebuildTree(); }
    }

    private string _treeSummary = string.Empty;
    /// <summary>Caption under the tree, e.g. "12 profilov · 4 snímače".</summary>
    public string TreeSummary { get => _treeSummary; private set => SetProperty(ref _treeSummary, value); }

    private bool _suppressAutoLoad;

    private TestProfile? _selectedHistoryProfile;
    public TestProfile? SelectedHistoryProfile
    {
        get => _selectedHistoryProfile;
        set
        {
            if (SetProperty(ref _selectedHistoryProfile, value))
            {
                DisarmDelete(); // a different profile is selected – confirmation no longer applies
                LoadFromHistoryCommand.RaiseCanExecuteChanged();
                DeleteFromHistoryCommand.RaiseCanExecuteChanged();
                DuplicateProfileCommand.RaiseCanExecuteChanged();

                // Single click = load into the editor (standard list behaviour), unless the
                // selection was set programmatically (e.g. during a refresh).
                if (!_suppressAutoLoad && value is not null)
                {
                    ApplyProfile(value);
                    StatusMessage = $"Profil \"{value.Name}\" načítaný.";
                }
            }
        }
    }

    public RelayCommand AddSegmentCommand { get; }
    public RelayCommand AddSegmentBeforeCommand { get; }
    public RelayCommand AddSegmentAfterCommand { get; }
    public RelayCommand RemoveSegmentCommand { get; }
    public RelayCommand MoveSegmentUpCommand { get; }
    public RelayCommand MoveSegmentDownCommand { get; }
    public RelayCommand ToggleSegmentsExpandCommand { get; }
    public RelayCommand NewProfileCommand { get; }

    private bool _isSegmentsExpanded;
    public bool IsSegmentsExpanded { get => _isSegmentsExpanded; set => SetProperty(ref _isSegmentsExpanded, value); }
    public RelayCommand SaveToHistoryCommand { get; }
    public RelayCommand LoadFromHistoryCommand { get; }
    public RelayCommand DeleteFromHistoryCommand { get; }

    /// <summary>Duplicates the selected saved profile (name suffixed with " COPY").</summary>
    public RelayCommand DuplicateProfileCommand { get; }

    /// <summary>Reloads the saved-profile list from disk (also used on entering the editor).</summary>
    public RelayCommand RefreshHistoryCommand { get; }
    public RelayCommand ImportProfileCommand { get; }

    /// <summary>Opens the bulk-import tool (many files at once, renamed + standardised).</summary>
    public RelayCommand BulkImportCommand { get; }
    public RelayCommand ExportProfileCommand { get; }

    /// <summary>Exports the whole library to one JSON file (importable / bundleable as seed profiles).</summary>
    public RelayCommand ExportLibraryCommand { get; }

    /// <summary>Moves the current name to "old name" and generates a standardized name from the profile.</summary>
    public RelayCommand GenerateStandardNameCommand { get; }

    /// <summary>Expands every sensor group in the library tree.</summary>
    public RelayCommand ExpandAllCommand { get; }

    /// <summary>Collapses every sensor group in the library tree.</summary>
    public RelayCommand CollapseAllCommand { get; }

    /// <summary>Clears the text and tag filters.</summary>
    public RelayCommand ClearFilterCommand { get; }

    private void SeedDefaultProfile()
    {
        Segments.Clear();
        if (SupportsHumidity)
        {
            Segments.Add(new SegmentViewModel(new ProfileSegment { Name = "Ohrev", TargetTemperature = 60, TargetHumidity = 80, Duration = TimeSpan.FromMinutes(30), IsRamp = true }));
            Segments.Add(new SegmentViewModel(new ProfileSegment { Name = "Plato", TargetTemperature = 60, TargetHumidity = 80, Duration = TimeSpan.FromMinutes(60), IsRamp = false }));
        }
        else
        {
            Segments.Add(new SegmentViewModel(new ProfileSegment { Name = "Ohrev", TargetTemperature = 85, Duration = TimeSpan.FromMinutes(30), IsRamp = true }));
            Segments.Add(new SegmentViewModel(new ProfileSegment { Name = "Plato", TargetTemperature = 85, Duration = TimeSpan.FromMinutes(60), IsRamp = false }));
        }
    }

    private TestProfile BuildProfile() => new()
    {
        Name = ProfileName,
        OriginalName = OriginalName,
        Kind = Kind,
        Cycles = Cycles,
        CycleStartIndex = CycleBandStart,
        CycleEndIndex = CycleBandEnd,
        Sensors = EditorSensors.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        Tags = EditorTags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        CreatedAt = DateTimeOffset.Now,
        Segments = Segments.Select(s => s.ToModel()).ToList(),
    };

    private static void ReplaceAll(ObservableCollection<string> target, IEnumerable<string>? values)
    {
        target.Clear();
        foreach (string v in values ?? Enumerable.Empty<string>())
        {
            target.Add(v);
        }
    }

    private void ApplyProfile(TestProfile profile)
    {
        Kind = profile.Kind;
        ProfileName = profile.Name;
        OriginalName = profile.OriginalName ?? string.Empty;
        ReplaceAll(EditorSensors, profile.Sensors);
        ReplaceAll(EditorTags, profile.Tags);
        Cycles = profile.Cycles;
        Segments.Clear();
        // (segments are added just below; set the cycle region after they exist)
        foreach (ProfileSegment segment in profile.Segments)
        {
            Segments.Add(new SegmentViewModel(segment));
        }

        // Cycle region (1-based) – now that segments exist so the clamps use the right count.
        _cycleFromSegment = ClampSegment(profile.ResolvedCycleStart + 1);
        _cycleToSegment = ClampSegment(profile.ResolvedCycleEnd + 1);
        OnPropertyChanged(nameof(CycleFromSegment));
        OnPropertyChanged(nameof(CycleToSegment));
        RaiseCycleRegion();

        SelectedSegment = Segments.FirstOrDefault();
        Recalculate();
    }

    private void GenerateStandardName()
    {
        TestProfile profile = BuildProfile();
        if (string.IsNullOrWhiteSpace(OriginalName))
        {
            OriginalName = ProfileName; // preserve the current name as the "old name"
        }

        ProfileName = ProfileNaming.StandardName(profile);
        StatusMessage = $"Názov vygenerovaný podľa štandardu (pôvodný uložený ako „Starý názov“).";
    }

    private void NewProfile()
    {
        ProfileName = "Nový profil";
        OriginalName = string.Empty;
        EditorSensors.Clear();
        EditorTags.Clear();
        Cycles = 1;
        SeedDefaultProfile();
        _cycleFromSegment = 1;
        _cycleToSegment = Math.Max(1, Segments.Count);
        OnPropertyChanged(nameof(CycleFromSegment));
        OnPropertyChanged(nameof(CycleToSegment));
        RaiseCycleRegion();
        SelectedSegment = Segments.FirstOrDefault();
        Recalculate();
        StatusMessage = "Nový profil pripravený.";
    }

    private void AddSegment()
    {
        var segment = new SegmentViewModel(new ProfileSegment
        {
            Name = $"Segment {Segments.Count + 1}",
            TargetTemperature = 25,
            TargetHumidity = SupportsHumidity ? 50 : null,
            Duration = TimeSpan.FromMinutes(10),
            IsRamp = true,
        });
        Segments.Add(segment);
        SelectedSegment = segment;
    }

    private void InsertSegment(int offset)
    {
        int index = SelectedSegment is null ? Segments.Count : Segments.IndexOf(SelectedSegment) + offset;
        index = Math.Clamp(index, 0, Segments.Count);
        var segment = new SegmentViewModel(new ProfileSegment
        {
            Name = $"Segment {Segments.Count + 1}",
            TargetTemperature = 25,
            TargetHumidity = SupportsHumidity ? 50 : null,
            Duration = TimeSpan.FromMinutes(10),
            IsRamp = true,
        });
        Segments.Insert(index, segment);
        SelectedSegment = segment;
    }

    private void RemoveSegment()
    {
        if (SelectedSegment is { } segment)
        {
            int index = Segments.IndexOf(segment);
            Segments.Remove(segment);
            if (Segments.Count > 0)
            {
                SelectedSegment = Segments[Math.Clamp(index, 0, Segments.Count - 1)];
            }
        }
    }

    private void MoveSegment(int delta)
    {
        if (SelectedSegment is not { } segment)
        {
            return;
        }

        int index = Segments.IndexOf(segment);
        int target = index + delta;
        if (target >= 0 && target < Segments.Count)
        {
            Segments.Move(index, target);
            SelectedSegment = segment;
        }
    }

    private void SaveToHistory()
    {
        TestProfile profile = BuildProfile();

        // Upsert by name: saving a profile with an existing name updates it instead
        // of piling up duplicates in the shared library.
        TestProfile? existing = _store.LoadAll()
            .FirstOrDefault(p => string.Equals(p.Name.Trim(), profile.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        profile.Id = existing?.Id ?? Guid.NewGuid();

        _store.Save(profile);
        RefreshHistory();
        SelectedHistoryProfile = History.FirstOrDefault(p => p.Id == profile.Id);
        StatusMessage = existing is null
            ? $"Profil \"{profile.Name}\" uložený."
            : $"Profil \"{profile.Name}\" aktualizovaný (prepísaná staršia verzia).";
    }

    private void LoadFromHistory()
    {
        if (SelectedHistoryProfile is { } profile)
        {
            ApplyProfile(profile);
            StatusMessage = $"Profil \"{profile.Name}\" načítaný.";
        }
    }

    private CancellationTokenSource? _deleteArmCts;

    private bool _isDeleteArmed;
    /// <summary>Two-step delete: the first click arms the button, the second (within 3 s) deletes.</summary>
    public bool IsDeleteArmed
    {
        get => _isDeleteArmed;
        private set { if (SetProperty(ref _isDeleteArmed, value)) OnPropertyChanged(nameof(DeleteButtonText)); }
    }

    /// <summary>Delete button caption reflecting the confirmation state.</summary>
    public string DeleteButtonText => IsDeleteArmed ? "Naozaj zmazať?" : "Zmazať";

    private void DisarmDelete()
    {
        _deleteArmCts?.Cancel();
        _deleteArmCts = null;
        IsDeleteArmed = false;
    }

    private void DeleteFromHistory()
    {
        if (SelectedHistoryProfile is not { } profile)
        {
            return;
        }

        bool confirmed = Views.ConfirmDialog.Ask(
            $"Naozaj vymazať profil „{profile.Name}“ z knižnice? Túto akciu nie je možné vrátiť.",
            "Vymazať profil",
            "Vymazať");
        if (!confirmed)
        {
            StatusMessage = "Mazanie zrušené.";
            return;
        }

        _store.Delete(profile.Id);
        RefreshHistory();
        StatusMessage = $"Profil „{profile.Name}“ odstránený.";
    }

    private void RefreshHistory()
    {
        Guid? selectedId = SelectedHistoryProfile?.Id;
        _suppressAutoLoad = true;
        History.Clear();
        foreach (TestProfile profile in _store.LoadAll())
        {
            History.Add(profile);
        }

        // Keep the previous selection pointed at the reloaded instance (so refreshing
        // doesn't clear the list selection or reload the editor).
        SelectedHistoryProfile = selectedId is { } id ? History.FirstOrDefault(p => p.Id == id) : null;
        _suppressAutoLoad = false;

        RefreshKnownValues();
        RebuildTree();
    }

    /// <summary>Rebuilds the tag filter list and the editor suggestion lists (sensors + tags).</summary>
    private void RefreshKnownValues()
    {
        List<string> tags = History
            .SelectMany(p => p.Tags ?? new List<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        List<string> sensors = History
            .SelectMany(p => p.Sensors ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        string previous = SelectedTag;
        AvailableTags.Clear();
        AvailableTags.Add(AllTagsOption);
        foreach (string tag in tags)
        {
            AvailableTags.Add(tag);
        }

        _selectedTag = AvailableTags.Contains(previous) ? previous : AllTagsOption;
        OnPropertyChanged(nameof(SelectedTag));

        SyncCollection(KnownTags, tags);
        SyncCollection(KnownSensors, sensors);
    }

    private static void SyncCollection(ObservableCollection<string> target, List<string> values)
    {
        target.Clear();
        foreach (string v in values)
        {
            target.Add(v);
        }
    }

    /// <summary>Rebuilds the sensor-grouped, filtered tree from <see cref="History"/>.
    /// A profile with several sensors appears under each of them.</summary>
    private void RebuildTree()
    {
        var expanded = ProfileTree.ToDictionary(g => g.Header, g => g.IsExpanded);

        string needle = FilterText?.Trim() ?? string.Empty;
        bool tagFilter = SelectedTag != AllTagsOption;

        IEnumerable<TestProfile> matches = History.Where(p => Matches(p, needle, tagFilter ? SelectedTag : null));

        // Expand each profile into (sensor, profile) pairs so multi-sensor profiles
        // land in every matching group.
        var groups = matches
            .SelectMany(p =>
            {
                List<string> sensors = (p.Sensors ?? new List<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return sensors.Count == 0
                    ? new[] { (Sensor: "Bez snímača", Profile: p) }
                    : sensors.Select(s => (Sensor: s.Trim(), Profile: p)).ToArray();
            })
            .GroupBy(x => x.Sensor)
            .OrderBy(g => g.Key == "Bez snímača" ? "￿" : g.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        int distinctProfiles = matches.Count();

        ProfileTree.Clear();
        foreach (var group in groups)
        {
            var vm = new ProfileTreeGroupViewModel(group.Key, group.Select(x => x.Profile).OrderByDescending(p => p.CreatedAt))
            {
                // Keep a group's expansion state across rebuilds; expand while actively filtering.
                IsExpanded = needle.Length > 0 || tagFilter || !expanded.TryGetValue(group.Key, out bool wasOpen) || wasOpen,
            };
            ProfileTree.Add(vm);
        }

        TreeSummary = groups.Count == 0
            ? "Žiadny profil nevyhovuje filtru."
            : $"{distinctProfiles} {ProfileWord(distinctProfiles)} · {groups.Count} {SensorWord(groups.Count)}";
    }

    private static bool Matches(TestProfile p, string needle, string? tag)
    {
        if (tag is not null && !(p.Tags ?? new List<string>()).Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (needle.Length == 0)
        {
            return true;
        }

        bool InText(string? s) => s is not null && s.Contains(needle, StringComparison.CurrentCultureIgnoreCase);
        return InText(p.Name)
            || (p.Sensors ?? new List<string>()).Any(InText)
            || (p.Tags ?? new List<string>()).Any(InText);
    }

    private void SetAllExpanded(bool expanded)
    {
        foreach (ProfileTreeGroupViewModel group in ProfileTree)
        {
            group.IsExpanded = expanded;
        }
    }

    private static string ProfileWord(int n) => n == 1 ? "profil" : (n >= 2 && n <= 4 ? "profily" : "profilov");

    private static string SensorWord(int n) => n == 1 ? "snímač" : (n >= 2 && n <= 4 ? "snímače" : "snímačov");

    /// <summary>Reloads the saved profiles from disk. Called on entering the editor and by the ↻ button.</summary>
    public void RefreshFromStore()
    {
        RefreshHistory();
        StatusMessage = $"Profily obnovené zo súboru ({History.Count}).";
    }

    /// <summary>Reloads the library and opens the profile with the given id in the editor (used by the quick builder).</summary>
    public void OpenForEditing(Guid id)
    {
        RefreshHistory();
        TestProfile? match = History.FirstOrDefault(p => p.Id == id);
        if (match is not null)
        {
            SelectedHistoryProfile = match; // auto-loads it into the editor
            StatusMessage = $"Profil \"{match.Name}\" otvorený z rýchleho vytvárača.";
        }
    }

    private void DuplicateProfile()
    {
        if (SelectedHistoryProfile is not { } source)
        {
            return;
        }

        TestProfile copy = source.Clone();
        copy.Id = Guid.NewGuid();
        copy.Name = $"{source.Name} COPY";
        copy.CreatedAt = DateTimeOffset.Now;

        _store.Save(copy);
        RefreshHistory();
        SelectedHistoryProfile = History.FirstOrDefault(p => p.Id == copy.Id); // loads the copy into the editor
        StatusMessage = $"Profil \"{source.Name}\" duplikovaný ako \"{copy.Name}\".";
    }

    private void ImportProfile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importovať Vötsch / SIMPATI profil",
            Filter = "Profily (*.csv;*.txt;*.dat;*.prg;*.json;*.b??)|*.csv;*.txt;*.dat;*.prg;*.json;*.b?;*.b??|Všetky súbory (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ProfileImportResult result = ProfileImporter.ImportFile(dialog.FileName, Kind);

            // Keep the source name as the "old name" and generate a standardized name.
            result.Profile.OriginalName = result.Profile.Name;
            result.Profile.Name = ProfileNaming.StandardName(result.Profile);
            ApplyProfile(result.Profile);
            StatusMessage = $"Importované ({result.FormatDescription}), {result.Profile.Segments.Count} segmentov · " +
                $"názov vygenerovaný, pôvodný „{result.Profile.OriginalName}“ uložený ako Starý názov.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import zlyhal: {ex.Message}";
        }
    }

    private void BulkImport()
    {
        bool imported = Views.BulkImportWindow.Show(_store);
        if (imported)
        {
            RefreshFromStore();
            StatusMessage = "Hromadný import dokončený – knižnica obnovená.";
        }
    }

    private void ExportLibrary()
    {
        bool exported = Views.BulkExportWindow.Show(_store);
        if (exported)
        {
            StatusMessage = "Hromadný export dokončený.";
        }
    }

    private void ExportProfile()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportovať profil",
            Filter = "CSV (pre Vötsch/Excel) (*.csv)|*.csv|JSON (*.json)|*.json",
            DefaultExt = ".csv",
            FileName = $"{Sanitize(ProfileName)}.csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ProfileExporter.ExportFile(BuildProfile(), dialog.FileName);
            StatusMessage = $"Profil exportovaný do {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export zlyhal: {ex.Message}";
        }
    }

    private void OnSegmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (SegmentViewModel s in e.OldItems)
            {
                s.PropertyChanged -= OnSegmentEdited;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (SegmentViewModel s in e.NewItems)
            {
                s.PropertyChanged += OnSegmentEdited;
            }
        }

        SaveToHistoryCommand.RaiseCanExecuteChanged();
        ExportProfileCommand.RaiseCanExecuteChanged();
        Recalculate();
    }

    private void OnSegmentEdited(object? sender, PropertyChangedEventArgs e) => Recalculate();

    private void Recalculate()
    {
        // Keep the cycle region within the current segment count (segments may have changed).
        int clampedFrom = ClampSegment(_cycleFromSegment);
        int clampedTo = ClampSegment(_cycleToSegment);
        if (clampedFrom != _cycleFromSegment) { _cycleFromSegment = clampedFrom; OnPropertyChanged(nameof(CycleFromSegment)); }
        if (clampedTo != _cycleToSegment) { _cycleToSegment = clampedTo; OnPropertyChanged(nameof(CycleToSegment)); }

        // Region-aware total: intro once + body × cycles + outro once.
        int cycles = Math.Max(1, Cycles);
        int start = CycleBandStart, end = CycleBandEnd;
        double introMin = 0, bodyMin = 0, outroMin = 0;
        for (int i = 0; i < Segments.Count; i++)
        {
            double dur = Math.Max(0, Segments[i].DurationMinutes);
            if (i < start) introMin += dur;
            else if (i <= end) bodyMin += dur;
            else outroMin += dur;
        }

        var total = TimeSpan.FromMinutes(introMin + bodyMin * cycles + outroMin);
        ProfileDurationText = total.TotalMinutes < 1
            ? "< 1 min"
            : total.TotalDays >= 1
                ? $"{(int)total.TotalDays} d {total.Hours} h {total.Minutes} min"
                : $"{(int)total.TotalHours} h {total.Minutes} min";

        RaiseCycleRegion();
        ValidateProfile();
        BuildHumPreview();
    }

    private void ValidateProfile()
    {
        var issues = new List<string>();
        for (int i = 0; i < Segments.Count; i++)
        {
            SegmentViewModel s = Segments[i];
            if (s.DurationMinutes <= 0)
            {
                issues.Add($"Segment {i + 1}: trvanie ≤ 0");
            }

            if (s.TargetTemperature is < -80 or > 200)
            {
                issues.Add($"Segment {i + 1}: teplota mimo rozsahu");
            }

            if (SupportsHumidity && s.TargetHumidity is { } hh && (hh < 0 || hh > 100))
            {
                issues.Add($"Segment {i + 1}: vlhkosť mimo 0–100 %");
            }
        }

        ProfileWarnings = issues.Count == 0 ? string.Empty : "⚠ " + string.Join(" · ", issues);
    }

    private void BuildHumPreview()
    {
        if (!SupportsHumidity || Segments.Count == 0)
        {
            HumPreview = Array.Empty<ChartSeries>();
            return;
        }

        var points = new List<Point>();
        double prevH = Segments[0].TargetHumidity ?? 50;
        double t = 0;
        points.Add(new Point(0, prevH));

        int cycles = Math.Max(1, Cycles);
        for (int c = 0; c < cycles; c++)
        {
            foreach (SegmentViewModel s in Segments)
            {
                double dur = Math.Max(0, s.DurationMinutes);
                double targetH = s.TargetHumidity ?? prevH;
                if (s.IsRamp)
                {
                    t += dur;
                    points.Add(new Point(t, targetH));
                }
                else
                {
                    points.Add(new Point(t, targetH));
                    t += dur;
                    points.Add(new Point(t, targetH));
                }

                prevH = targetH;
            }
        }

        HumPreview = new[] { new ChartSeries("Profil vlhkosť", HumBrush, points) };
    }

    private static string Sanitize(string name)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "profil" : name;
    }

    private static Brush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
