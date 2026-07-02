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
        ImportProfileCommand = new RelayCommand(ImportProfile);
        ExportProfileCommand = new RelayCommand(ExportProfile, () => Segments.Count > 0);

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

    private int _cycles = 1;
    public int Cycles { get => _cycles; set { if (SetProperty(ref _cycles, Math.Max(1, value))) Recalculate(); } }

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

    private TestProfile? _selectedHistoryProfile;
    public TestProfile? SelectedHistoryProfile
    {
        get => _selectedHistoryProfile;
        set
        {
            if (SetProperty(ref _selectedHistoryProfile, value))
            {
                LoadFromHistoryCommand.RaiseCanExecuteChanged();
                DeleteFromHistoryCommand.RaiseCanExecuteChanged();
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
    public RelayCommand ImportProfileCommand { get; }
    public RelayCommand ExportProfileCommand { get; }

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
        Kind = Kind,
        Cycles = Cycles,
        CreatedAt = DateTimeOffset.Now,
        Segments = Segments.Select(s => s.ToModel()).ToList(),
    };

    private void ApplyProfile(TestProfile profile)
    {
        Kind = profile.Kind;
        ProfileName = profile.Name;
        Cycles = profile.Cycles;
        Segments.Clear();
        foreach (ProfileSegment segment in profile.Segments)
        {
            Segments.Add(new SegmentViewModel(segment));
        }

        SelectedSegment = Segments.FirstOrDefault();
        Recalculate();
    }

    private void NewProfile()
    {
        ProfileName = "Nový profil";
        Cycles = 1;
        SeedDefaultProfile();
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
        profile.Id = Guid.NewGuid();
        _store.Save(profile);
        RefreshHistory();
        SelectedHistoryProfile = History.FirstOrDefault(p => p.Id == profile.Id);
        StatusMessage = $"Profil \"{profile.Name}\" uložený.";
    }

    private void LoadFromHistory()
    {
        if (SelectedHistoryProfile is { } profile)
        {
            ApplyProfile(profile);
            StatusMessage = $"Profil \"{profile.Name}\" načítaný.";
        }
    }

    private void DeleteFromHistory()
    {
        if (SelectedHistoryProfile is { } profile)
        {
            _store.Delete(profile.Id);
            RefreshHistory();
            StatusMessage = $"Profil \"{profile.Name}\" odstránený.";
        }
    }

    private void RefreshHistory()
    {
        History.Clear();
        foreach (TestProfile profile in _store.LoadAll())
        {
            History.Add(profile);
        }
    }

    private void ImportProfile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importovať Vötsch / SIMPATI profil",
            Filter = "Profily (*.csv;*.txt;*.dat;*.prg;*.json;*.b0*)|*.csv;*.txt;*.dat;*.prg;*.json;*.b0*|Všetky súbory (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ProfileImportResult result = ProfileImporter.ImportFile(dialog.FileName, Kind);
            ApplyProfile(result.Profile);
            StatusMessage = $"Importované ({result.FormatDescription}), {result.Profile.Segments.Count} segmentov.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import zlyhal: {ex.Message}";
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
        double minutes = Segments.Sum(s => s.DurationMinutes) * Math.Max(1, Cycles);
        var total = TimeSpan.FromMinutes(minutes);
        ProfileDurationText = total.TotalMinutes < 1
            ? "< 1 min"
            : $"{(int)total.TotalHours} h {total.Minutes} min";

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
