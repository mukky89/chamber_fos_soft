using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using VotschVc3.App.Charting;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// Read-only browser over the saved profiles ("Zoznam profilov"): the library tree with
/// filters on the left, a preview of the selected profile on the right – the same chart the
/// quick builder draws. Nothing is edited here; "✎ Upraviť" hands the profile to the quick
/// profile builder, which is the single place where profiles are created and changed.
/// </summary>
public sealed class ProfileLibraryViewModel : ObservableObject
{
    private static readonly Brush HumBrush = Freeze(0x4F, 0xB6, 0xFF);

    private readonly ProfileStore _store;

    public ProfileLibraryViewModel(ProfileStore store)
    {
        _store = store;
        Segments = new ObservableCollection<SegmentViewModel>();

        NewProfileCommand = new RelayCommand(() => NewInQuickProfile?.Invoke());
        EditProfileCommand = new RelayCommand(EditProfile, () => SelectedHistoryProfile is not null);
        DeleteFromHistoryCommand = new RelayCommand(DeleteFromHistory, () => SelectedHistoryProfile is not null);
        ArchiveProfileCommand = new RelayCommand<TestProfile>(ArchiveProfile);
        DuplicateProfileCommand = new RelayCommand(DuplicateProfile, () => SelectedHistoryProfile is not null);
        ConvertToSikaCommand = new RelayCommand(ConvertToSika,
            () => SelectedHistoryProfile is { DeviceKind: not ProfileDeviceKind.Sika });
        RefreshHistoryCommand = new RelayCommand(RefreshFromStore);
        DeleteAllProfilesCommand = new RelayCommand(DeleteAllProfiles, () => IsAdmin && History.Count > 0);
        ExpandAllCommand = new RelayCommand(() => SetAllExpanded(true));
        CollapseAllCommand = new RelayCommand(() => SetAllExpanded(false));
        ClearFilterCommand = new RelayCommand(() =>
        {
            FilterText = string.Empty;
            SelectedTag = AllTagsOption;
            DeviceKindFilter = ProfileDeviceKind.Any;
        });

        RefreshHistory();
    }

    /// <summary>Set by the shell: opens the given profile in the quick profile builder.</summary>
    public Action<TestProfile>? OpenInQuickProfile { get; set; }

    /// <summary>Set by the shell: opens the quick profile builder with a fresh profile.</summary>
    public Action? NewInQuickProfile { get; set; }

    /// <summary>The device families used by the tree filter, with the Slovak label shown.</summary>
    public IReadOnlyList<DeviceKindOption> DeviceKinds { get; } = new List<DeviceKindOption>
    {
        new(ProfileDeviceKind.Any, "Univerzálny (všetky zariadenia)"),
        new(ProfileDeviceKind.Votsch, "Vötsch / Weiss (rampy a plata)"),
        new(ProfileDeviceKind.Sika, "SIKA TP (teplota + výdrž)"),
    };

    private ProfileDeviceKind _deviceKindFilter = ProfileDeviceKind.Any;

    /// <summary>Tree filter: <see cref="ProfileDeviceKind.Any"/> shows everything, otherwise
    /// only the profiles of that family (plus the universal ones).</summary>
    public ProfileDeviceKind DeviceKindFilter
    {
        get => _deviceKindFilter;
        set { if (SetProperty(ref _deviceKindFilter, value)) RebuildTree(); }
    }

    // ---- preview of the selected profile -------------------------------------------------

    /// <summary>Segments of the selected profile, fed to the preview chart (never saved).</summary>
    public ObservableCollection<SegmentViewModel> Segments { get; }

    private string _previewName = "Žiadny profil";
    /// <summary>Name of the previewed profile.</summary>
    public string PreviewName { get => _previewName; private set => SetProperty(ref _previewName, value); }

    private string _previewMeta = string.Empty;
    /// <summary>One-line summary: created / chamber kind / device / segment count.</summary>
    public string PreviewMeta { get => _previewMeta; private set => SetProperty(ref _previewMeta, value); }

    private string _previewOwner = string.Empty;
    /// <summary>Customer · project of the previewed profile (empty when neither is set).</summary>
    public string PreviewOwner
    {
        get => _previewOwner;
        private set { if (SetProperty(ref _previewOwner, value)) OnPropertyChanged(nameof(HasPreviewOwner)); }
    }

    public bool HasPreviewOwner => !string.IsNullOrWhiteSpace(PreviewOwner);

    private string _previousName = string.Empty;
    /// <summary>The imported ("old") name of the previewed profile, when it has one.</summary>
    public string OriginalName
    {
        get => _previousName;
        private set { if (SetProperty(ref _previousName, value)) OnPropertyChanged(nameof(HasOriginalName)); }
    }

    public bool HasOriginalName => !string.IsNullOrWhiteSpace(OriginalName);

    private string _previewNotes = string.Empty;
    /// <summary>Optional operator note stored with the previewed profile.</summary>
    public string PreviewNotes
    {
        get => _previewNotes;
        private set { if (SetProperty(ref _previewNotes, value)) OnPropertyChanged(nameof(HasPreviewNotes)); }
    }

    public bool HasPreviewNotes => !string.IsNullOrWhiteSpace(PreviewNotes);

    private ProfileValidationStatus _previewValidationStatus = ProfileValidationStatus.TBT;
    public ProfileValidationStatus PreviewValidationStatus
    {
        get => _previewValidationStatus;
        private set => SetProperty(ref _previewValidationStatus, value);
    }

    private string _previewWarning = string.Empty;
    public string PreviewWarning
    {
        get => _previewWarning;
        private set { if (SetProperty(ref _previewWarning, value)) OnPropertyChanged(nameof(HasPreviewWarning)); }
    }

    public bool HasPreviewWarning => !string.IsNullOrWhiteSpace(PreviewWarning);

    /// <summary>Sensors of the previewed profile (chips).</summary>
    public ObservableCollection<string> PreviewSensors { get; } = new();

    /// <summary>Tags of the previewed profile (chips).</summary>
    public ObservableCollection<string> PreviewTags { get; } = new();

    private bool _hasSelection;
    /// <summary>Whether a profile is selected – the preview panel is empty otherwise.</summary>
    public bool HasSelection
    {
        get => _hasSelection;
        private set { if (SetProperty(ref _hasSelection, value)) OnPropertyChanged(nameof(HasNoSelection)); }
    }

    public bool HasNoSelection => !HasSelection;

    private ChamberKind _kind = ChamberKind.TemperatureHumidity;
    /// <summary>Chamber kind of the previewed profile (drives the humidity preview).</summary>
    public ChamberKind Kind
    {
        get => _kind;
        private set
        {
            if (SetProperty(ref _kind, value))
            {
                OnPropertyChanged(nameof(SupportsHumidity));
            }
        }
    }

    public bool SupportsHumidity => Kind == ChamberKind.TemperatureHumidity;

    private int _cycles = 1;
    /// <summary>Cycle count of the previewed profile (the chart repeats the body).</summary>
    public int Cycles { get => _cycles; private set => SetProperty(ref _cycles, value); }

    private int _cycleBandStart;
    /// <summary>Zero-based region start for the chart band.</summary>
    public int CycleBandStart { get => _cycleBandStart; private set => SetProperty(ref _cycleBandStart, value); }

    private int _cycleBandEnd;
    /// <summary>Zero-based region end for the chart band.</summary>
    public int CycleBandEnd { get => _cycleBandEnd; private set => SetProperty(ref _cycleBandEnd, value); }

    private string _cycleRegionText = string.Empty;
    /// <summary>Human-readable description of what repeats and how many times.</summary>
    public string CycleRegionText { get => _cycleRegionText; private set => SetProperty(ref _cycleRegionText, value); }

    private string _profileDurationText = "—";
    public string ProfileDurationText { get => _profileDurationText; private set => SetProperty(ref _profileDurationText, value); }

    private IReadOnlyList<ChartSeries> _humPreview = Array.Empty<ChartSeries>();
    public IReadOnlyList<ChartSeries> HumPreview { get => _humPreview; private set => SetProperty(ref _humPreview, value); }

    private string _statusMessage = "Vyber profil zo zoznamu – vpravo uvidíš jeho priebeh. Upravuje sa v rýchlom profile.";
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

    private TestProfile? _selectedHistoryProfile;
    public TestProfile? SelectedHistoryProfile
    {
        get => _selectedHistoryProfile;
        set
        {
            if (SetProperty(ref _selectedHistoryProfile, value))
            {
                EditProfileCommand.RaiseCanExecuteChanged();
                DeleteFromHistoryCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(ArchiveButtonText));
                DuplicateProfileCommand.RaiseCanExecuteChanged();
                ConvertToSikaCommand.RaiseCanExecuteChanged();
                ShowPreview(value);
            }
        }
    }

    /// <summary>Opens the quick profile builder with a brand-new profile.</summary>
    public RelayCommand NewProfileCommand { get; }

    /// <summary>Hands the selected profile to the quick profile builder for editing.</summary>
    public RelayCommand EditProfileCommand { get; }

    public RelayCommand DeleteFromHistoryCommand { get; }
    public RelayCommand<TestProfile> ArchiveProfileCommand { get; }

    public string ArchiveButtonText => SelectedHistoryProfile?.IsArchived == true ? "↩ Obnoviť z archívu" : "Archivovať profil";

    private bool _showArchived;
    public bool ShowArchived
    {
        get => _showArchived;
        set { if (SetProperty(ref _showArchived, value)) { SelectedHistoryProfile = null; RebuildTree(); } }
    }

    /// <summary>Duplicates the selected saved profile (name suffixed with " COPY").</summary>
    public RelayCommand DuplicateProfileCommand { get; }

    /// <summary>Saves a SIKA version of the selected profile (holds only, no ramps).</summary>
    public RelayCommand ConvertToSikaCommand { get; }

    /// <summary>Reloads the saved-profile list from disk (also used on entering the screen).</summary>
    public RelayCommand RefreshHistoryCommand { get; }

    /// <summary>Admin-only: deletes every profile in the library (password protected).</summary>
    public RelayCommand DeleteAllProfilesCommand { get; }

    private bool _isAdmin;
    /// <summary>Set by the shell: whether the signed-in user may manage (delete-all) profiles.</summary>
    public bool IsAdmin
    {
        get => _isAdmin;
        set { if (SetProperty(ref _isAdmin, value)) DeleteAllProfilesCommand.RaiseCanExecuteChanged(); }
    }

    /// <summary>Set by the shell: verifies an admin password (returns true when it matches).</summary>
    public Func<string, bool>? VerifyAdminPassword { get; set; }

    /// <summary>Expands every sensor group in the library tree.</summary>
    public RelayCommand ExpandAllCommand { get; }

    /// <summary>Collapses every sensor group in the library tree.</summary>
    public RelayCommand CollapseAllCommand { get; }

    /// <summary>Clears the text, tag and device filters.</summary>
    public RelayCommand ClearFilterCommand { get; }

    private static void ReplaceAll(ObservableCollection<string> target, IEnumerable<string>? values)
    {
        target.Clear();
        foreach (string v in (values ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            target.Add(v);
        }
    }

    /// <summary>Fills the right-hand preview (chart + facts) from the selected profile.</summary>
    private void ShowPreview(TestProfile? profile)
    {
        Segments.Clear();
        if (profile is null)
        {
            HasSelection = false;
            PreviewName = "Žiadny profil";
            PreviewMeta = string.Empty;
            PreviewOwner = string.Empty;
            OriginalName = string.Empty;
            PreviewNotes = string.Empty;
            PreviewValidationStatus = ProfileValidationStatus.TBT;
            PreviewWarning = string.Empty;
            PreviewSensors.Clear();
            PreviewTags.Clear();
            ProfileDurationText = "—";
            CycleRegionText = string.Empty;
            HumPreview = Array.Empty<ChartSeries>();
            return;
        }

        foreach (ProfileSegment segment in profile.Segments)
        {
            Segments.Add(new SegmentViewModel(segment));
        }

        Kind = profile.Kind;
        Cycles = Math.Max(1, profile.Cycles);
        CycleBandStart = profile.ResolvedCycleStart;
        CycleBandEnd = profile.ResolvedCycleEnd;
        PreviewName = profile.CodeAndName;
        OriginalName = profile.OriginalName ?? string.Empty;
        PreviewNotes = profile.Notes?.Trim() ?? string.Empty;
        PreviewValidationStatus = profile.ValidationStatus;
        PreviewWarning = profile.Warning?.Trim() ?? string.Empty;
        PreviewOwner = string.Join(" · ", new[] { profile.Customer, profile.Project }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        ReplaceAll(PreviewSensors, profile.Sensors);
        ReplaceAll(PreviewTags, profile.Tags);

        string kindText = profile.Kind == ChamberKind.TemperatureHumidity ? "teplota + vlhkosť" : "iba teplota";
        string code = profile.HasCode ? $"Kód {profile.Code} · " : string.Empty;
        PreviewMeta = $"{code}{profile.CreatedAt:dd.MM.yyyy HH:mm} · {profile.DeviceKind.Label()} · {kindText} · " +
            $"{profile.Segments.Count} {SegmentWord(profile.Segments.Count)}";

        CycleRegionText = DescribeCycles(profile);

        // A SIKA profile has no ramps: the bath drives itself to each set point and the dwell
        // starts only once it is there, so the run is longer than the dwell sum by that much.
        TimeSpan dwell = TotalDuration(profile);
        TimeSpan settling = profile.DeviceKind == ProfileDeviceKind.Sika
            ? ChamberViewModel.SikaSettling.ForProfile(profile)
            : TimeSpan.Zero;
        ProfileDurationText = settling > TimeSpan.Zero
            ? $"{FormatDuration(dwell + settling)} (výdrž {FormatDuration(dwell)} + ustálenie ~{FormatDuration(settling)})"
            : FormatDuration(dwell);
        BuildHumPreview();
        HasSelection = true;
    }

    /// <summary>Total run time: intro once + the cycled body ×N + outro once.</summary>
    private static TimeSpan TotalDuration(TestProfile profile)
    {
        int cycles = Math.Max(1, profile.Cycles);
        int start = profile.ResolvedCycleStart, end = profile.ResolvedCycleEnd;
        double intro = 0, body = 0, outro = 0;
        for (int i = 0; i < profile.Segments.Count; i++)
        {
            double dur = Math.Max(0, profile.Segments[i].Duration.TotalMinutes);
            if (i < start) intro += dur;
            else if (i <= end) body += dur;
            else outro += dur;
        }

        return TimeSpan.FromMinutes(intro + body * cycles + outro);
    }

    private static string FormatDuration(TimeSpan total) => total.TotalMinutes < 1
        ? "< 1 min"
        : total.TotalDays >= 1
            ? $"{(int)total.TotalDays} d {total.Hours} h {total.Minutes} min"
            : $"{(int)total.TotalHours} h {total.Minutes} min";

    private static string DescribeCycles(TestProfile profile)
    {
        int cycles = Math.Max(1, profile.Cycles);
        if (cycles <= 1)
        {
            return "Bez cyklovania (1×).";
        }

        int from = profile.ResolvedCycleStart + 1;
        int to = profile.ResolvedCycleEnd + 1;
        bool whole = from <= 1 && to >= profile.Segments.Count;
        return whole
            ? $"Cykluje sa celý profil ×{cycles}."
            : $"Cyklujú sa segmenty {from}–{to} ×{cycles} · okolité segmenty (nábeh/koniec) prebehnú raz.";
    }

    private void EditProfile()
    {
        if (SelectedHistoryProfile is { } profile)
        {
            OpenInQuickProfile?.Invoke(profile);
        }
    }

    private void DeleteAllProfiles()
    {
        if (!IsAdmin)
        {
            return;
        }

        int count = History.Count;
        if (count == 0)
        {
            StatusMessage = "Knižnica je prázdna.";
            return;
        }

        bool ok = Views.PasswordDialog.Ask(
            $"Naozaj vymazať VŠETKY profily z knižnice ({count})? Túto akciu nie je možné vrátiť. " +
            "Zadaj heslo admina na potvrdenie.",
            pwd => VerifyAdminPassword?.Invoke(pwd) ?? false,
            "Vymazať všetky profily",
            "Vymazať všetko");
        if (!ok)
        {
            StatusMessage = "Hromadné mazanie zrušené.";
            return;
        }

        int removed = _store.Clear();
        RefreshHistory();
        StatusMessage = $"Vymazaných {removed} profilov z knižnice.";
    }

    private void DeleteFromHistory()
    {
        if (SelectedHistoryProfile is not { } profile)
        {
            return;
        }

        ArchiveProfile(profile);
    }

    private void ArchiveProfile(TestProfile? profile)
    {
        if (profile is null) return;

        bool restoring = profile.IsArchived;
        bool confirmed = Views.ConfirmDialog.Ask(
            restoring
                ? $"Obnoviť profil „{profile.Name}“ späť do aktívnych zoznamov?"
                : $"Archivovať profil „{profile.Name}“? Prestane sa zobrazovať v bežných zoznamoch, ale zostane uložený.",
            restoring ? "Obnoviť profil" : "Archivovať profil",
            restoring ? "Obnoviť" : "Archivovať");
        if (!confirmed)
        {
            StatusMessage = restoring ? "Obnovenie zrušené." : "Archivácia zrušená.";
            return;
        }

        _store.SetArchived(profile, !restoring);
        RefreshHistory();
        StatusMessage = restoring
            ? $"Profil „{profile.Name}“ obnovený z archívu."
            : $"Profil „{profile.Name}“ presunutý do archívu.";
    }

    private void RefreshHistory()
    {
        Guid? selectedId = SelectedHistoryProfile?.Id;
        History.Clear();
        foreach (TestProfile profile in _store.LoadAll())
        {
            History.Add(profile);
        }

        // Keep the previous selection pointed at the reloaded instance so refreshing
        // doesn't clear the preview.
        SelectedHistoryProfile = selectedId is { } id ? History.FirstOrDefault(p => p.Id == id) : null;

        RefreshKnownValues();
        RebuildTree();
        DeleteAllProfilesCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Rebuilds the tag filter list from the saved profiles.</summary>
    private void RefreshKnownValues()
    {
        List<string> tags = History
            .SelectMany(p => p.Tags ?? new List<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
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
    }

    /// <summary>Rebuilds the sensor-grouped, filtered tree from <see cref="History"/>.
    /// A profile with several sensors appears under each of them.</summary>
    private void RebuildTree()
    {
        var expanded = ProfileTree.ToDictionary(g => g.Header, g => g.IsExpanded);

        string needle = FilterText?.Trim() ?? string.Empty;
        bool tagFilter = SelectedTag != AllTagsOption;

        IEnumerable<TestProfile> matches = History
            .Where(p => p.IsArchived == ShowArchived)
            .Where(p => p.DeviceKind.CanRunOn(DeviceKindFilter))
            .Where(p => Matches(p, needle, tagFilter ? SelectedTag : null));

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

        string kindNote = DeviceKindFilter == ProfileDeviceKind.Any
            ? string.Empty
            : $" · filter: {DeviceKindFilter.Label()}";
        string archiveNote = ShowArchived ? " · archív" : string.Empty;
        TreeSummary = groups.Count == 0
            ? (ShowArchived ? "Archív je prázdny alebo nič nevyhovuje filtru." : "Žiadny profil nevyhovuje filtru.") + kindNote
            : $"{distinctProfiles} {ProfileWord(distinctProfiles)} · {groups.Count} {SensorWord(groups.Count)}{kindNote}{archiveNote}";
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
        return InText(p.Code)
            || InText(p.Name)
            || InText(p.Customer)
            || InText(p.Project)
            || InText(p.Notes)
            || InText(p.Warning)
            || InText(p.ValidationStatus.ToString())
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

    private static string SegmentWord(int n) => n == 1 ? "segment" : (n >= 2 && n <= 4 ? "segmenty" : "segmentov");

    /// <summary>Reloads the saved profiles from disk. Called on entering the screen and by the ↻ button.</summary>
    public void RefreshFromStore()
    {
        RefreshHistory();
        StatusMessage = $"Profily obnovené zo súboru ({History.Count}).";
    }

    private void DuplicateProfile()
    {
        if (SelectedHistoryProfile is not { } source)
        {
            return;
        }

        TestProfile copy = source.Clone();
        copy.Id = Guid.NewGuid();
        copy.Code = string.Empty; // the duplicate is its own library entry, with its own code
        copy.IsArchived = false;
        copy.Name = $"{source.Name} COPY";
        copy.CreatedAt = DateTimeOffset.Now;

        _store.Save(copy);
        RefreshHistory();
        SelectedHistoryProfile = History.FirstOrDefault(p => p.Id == copy.Id);
        StatusMessage = $"Profil \"{source.Name}\" duplikovaný ako \"{copy.Name}\".";
    }

    /// <summary>
    /// Converts the selected Vötsch profile into the SIKA format – the setpoints and their
    /// dwell times are kept, the ramps between them are dropped because the bath drives to
    /// a set point on its own. Saved as a new profile so the original stays untouched.
    /// </summary>
    private void ConvertToSika()
    {
        if (SelectedHistoryProfile is not { } source)
        {
            return;
        }

        TestProfile converted = ProfileDeviceConverter.ToSika(source);
        int droppedRamps = source.Segments.Count(s => s.IsRamp);

        if (!Views.ConfirmDialog.Ask(
                $"Previesť profil „{source.Name}“ do SIKA formátu?\n\n" +
                $"Vynechá sa {droppedRamps} rámp – SIKA kúpeľ si na setpoint nabehne sám. " +
                $"Zostane {converted.Segments.Count} teplôt s dobou výdrže " +
                $"({QuickProfileNaming.Duration(converted.SinglePassDuration.TotalMinutes)} namiesto " +
                $"{QuickProfileNaming.Duration(source.SinglePassDuration.TotalMinutes)}).\n\n" +
                $"Uloží sa ako nový profil „{converted.Name}“; pôvodný zostane nezmenený.",
                "Previesť na SIKA profil",
                confirmText: "Previesť",
                danger: false))
        {
            StatusMessage = "Prevod zrušený.";
            return;
        }

        _store.Save(converted);
        RefreshHistory();
        SelectedHistoryProfile = History.FirstOrDefault(p => p.Id == converted.Id);
        StatusMessage =
            $"Profil \"{source.Name}\" prevedený na SIKA formát ako \"{converted.Name}\" " +
            $"({converted.Segments.Count} teplôt, {droppedRamps} rámp vynechaných).";
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

    private static Brush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>One entry of the "device family" pickers in the profile library.</summary>
/// <param name="Value">The stored <see cref="ProfileDeviceKind"/>.</param>
/// <param name="Label">The Slovak text shown in the combo box.</param>
public sealed record DeviceKindOption(ProfileDeviceKind Value, string Label);
