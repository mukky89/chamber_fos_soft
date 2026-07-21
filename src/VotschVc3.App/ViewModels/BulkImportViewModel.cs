using System.Collections.ObjectModel;
using System.IO;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// Bulk profile import tool: pick many exported profile files at once, rename them,
/// and normalise every one to the lab standard (a starting ramp to the first
/// temperature and a closing ramp back to room temperature) before saving them into
/// the shared library in a single step.
/// </summary>
public sealed class BulkImportViewModel : ObservableObject
{
    private readonly ProfileStore _store;

    public BulkImportViewModel(ProfileStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        AddFilesCommand = new RelayCommand(AddFiles);
        RemoveItemCommand = new RelayCommand<BulkImportItemViewModel>(RemoveItem, i => i is not null);
        ClearCommand = new RelayCommand(ClearItems, () => Items.Count > 0);
        ImportAllCommand = new RelayCommand(ImportAll, () => Items.Any(i => i.IsIncluded));
        Items.CollectionChanged += (_, _) => RaiseListState();
    }

    public ObservableCollection<BulkImportItemViewModel> Items { get; } = new();

    public Array ChamberKinds { get; } = Enum.GetValues(typeof(ChamberKind));

    private ChamberKind _kind = ChamberKind.TemperatureOnly;
    /// <summary>Target chamber type for every imported profile (humidity is dropped for temperature-only).</summary>
    public ChamberKind Kind
    {
        get => _kind;
        set { if (SetProperty(ref _kind, value)) RefreshPreview(); }
    }

    private string _namePrefix = string.Empty;
    /// <summary>Optional prefix put in front of every profile name (e.g. a project code).</summary>
    public string NamePrefix { get => _namePrefix; set => SetProperty(ref _namePrefix, value); }

    private string _commonSensorName = string.Empty;
    /// <summary>Sensor name applied to every profile in the batch (groups the library tree).</summary>
    public string CommonSensorName { get => _commonSensorName; set => SetProperty(ref _commonSensorName, value); }

    private string _commonTagsText = string.Empty;
    /// <summary>Comma-separated tags applied to every profile in the batch.</summary>
    public string CommonTagsText { get => _commonTagsText; set => SetProperty(ref _commonTagsText, value); }

    private bool _generateStandardName = true;
    /// <summary>When on, each profile's name is generated from the standard; the source name is kept as OriginalName.</summary>
    public bool GenerateStandardName { get => _generateStandardName; set => SetProperty(ref _generateStandardName, value); }

    private bool _applyStandard = true;
    /// <summary>When on, each profile gets the standard starting/closing ramps applied on import.</summary>
    public bool ApplyStandard
    {
        get => _applyStandard;
        set { if (SetProperty(ref _applyStandard, value)) RefreshPreview(); }
    }

    private double _roomTemperature = 25;
    public double RoomTemperature { get => _roomTemperature; set { if (SetProperty(ref _roomTemperature, value)) RefreshPreview(); } }

    private double _initialRampMinutes = 60;
    public double InitialRampMinutes { get => _initialRampMinutes; set { if (SetProperty(ref _initialRampMinutes, Math.Max(0, value))) RefreshPreview(); } }

    private double _finalRampMinutes = 60;
    public double FinalRampMinutes { get => _finalRampMinutes; set { if (SetProperty(ref _finalRampMinutes, Math.Max(0, value))) RefreshPreview(); } }

    private double _finalHoldMinutes = 60;
    public double FinalHoldMinutes { get => _finalHoldMinutes; set { if (SetProperty(ref _finalHoldMinutes, Math.Max(0, value))) RefreshPreview(); } }

    private string _status = "Pridaj súbory profilov (CSV/TXT/JSON/BEdit). Každý sa premenuje a upraví podľa štandardu.";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>True after at least one profile was imported, so the caller refreshes the library on close.</summary>
    public bool ImportedAnything { get; private set; }

    public int IncludedCount => Items.Count(i => i.IsIncluded);
    public bool HasItems => Items.Count > 0;

    public RelayCommand AddFilesCommand { get; }
    public RelayCommand<BulkImportItemViewModel> RemoveItemCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand ImportAllCommand { get; }

    private ProfileStandardizationOptions BuildOptions() => new()
    {
        RoomTemperature = RoomTemperature,
        InitialRampMinutes = InitialRampMinutes,
        FinalRampMinutes = FinalRampMinutes,
        FinalHoldMinutes = FinalHoldMinutes,
    };

    private void AddFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Vybrať profily na hromadný import",
            Multiselect = true,
            Filter = "Profily (*.csv;*.txt;*.dat;*.prg;*.json;*.b??)|*.csv;*.txt;*.dat;*.prg;*.json;*.b?;*.b??|Všetky súbory (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        int added = 0, failed = 0;
        foreach (string path in dialog.FileNames)
        {
            if (Items.Any(i => string.Equals(i.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue; // already in the list
            }

            try
            {
                // Import keeping humidity; the target Kind is applied at save time.
                ProfileImportResult result = ProfileImporter.ImportFile(path, ChamberKind.TemperatureHumidity);
                var item = new BulkImportItemViewModel(path, result.Profile, result.FormatDescription, result.Warnings);
                item.PropertyChanged += OnItemPropertyChanged;
                Items.Add(item);
                added++;
            }
            catch (Exception ex)
            {
                failed++;
                Status = $"Súbor „{Path.GetFileName(path)}“ sa nepodarilo načítať: {ex.Message}";
            }
        }

        RefreshPreview();
        if (added > 0)
        {
            Status = failed == 0
                ? $"Pridaných {added} profilov. Skontroluj názvy a nastavenia, potom „Importovať vybrané“."
                : $"Pridaných {added} profilov, {failed} zlyhalo (pozri poslednú hlášku).";
        }
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BulkImportItemViewModel.IsIncluded))
        {
            RaiseListState();
        }
    }

    private void RemoveItem(BulkImportItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.PropertyChanged -= OnItemPropertyChanged;
        Items.Remove(item);
    }

    private void ClearItems()
    {
        foreach (BulkImportItemViewModel item in Items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        Items.Clear();
        Status = "Zoznam vyčistený.";
    }

    /// <summary>Recomputes each item's projected segment count / notes for the current options.</summary>
    private void RefreshPreview()
    {
        foreach (BulkImportItemViewModel item in Items)
        {
            item.UpdatePreview(Kind, ApplyStandard, BuildOptions());
        }
    }

    private void ImportAll()
    {
        List<BulkImportItemViewModel> included = Items.Where(i => i.IsIncluded).ToList();
        if (included.Count == 0)
        {
            Status = "Nie je vybraný žiadny profil.";
            return;
        }

        // Guard against two rows resolving to the same final name (they would upsert
        // onto each other). Names are matched case-insensitively.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int saved = 0, failed = 0, skipped = 0;

        foreach (BulkImportItemViewModel item in included)
        {
            try
            {
                TestProfile profile = item.Raw.Clone();
                profile.Kind = Kind;
                profile.Sensors = SplitValues(CommonSensorName);
                profile.Tags = SplitValues(CommonTagsText);
                profile.OriginalName = item.Raw.Name; // keep the source name as the "old name"
                if (Kind == ChamberKind.TemperatureOnly)
                {
                    foreach (ProfileSegment segment in profile.Segments)
                    {
                        segment.TargetHumidity = null;
                    }
                }

                if (ApplyStandard)
                {
                    ProfileStandardizer.Standardize(profile, BuildOptions());
                }

                // Name: generated from the profile (standard) or the edited/prefixed name.
                string baseName = GenerateStandardName
                    ? ProfileNaming.StandardName(profile)
                    : ComposeName(item.Name);
                if (string.IsNullOrWhiteSpace(baseName))
                {
                    item.StatusText = "⚠ prázdny názov – preskočené";
                    skipped++;
                    continue;
                }

                string finalName = UniqueName(baseName, seen);
                seen.Add(finalName);
                profile.Name = finalName;

                // Upsert by name so re-importing updates instead of duplicating.
                TestProfile? existing = _store.LoadAll()
                    .FirstOrDefault(p => string.Equals(p.Name.Trim(), finalName, StringComparison.OrdinalIgnoreCase));
                profile.Id = existing?.Id ?? Guid.NewGuid();
                profile.CreatedAt = DateTimeOffset.Now;
                _store.Save(profile);

                item.StatusText = existing is null
                    ? $"✔ uložený „{finalName}“ ({profile.Segments.Count} segm.)"
                    : $"✔ aktualizovaný „{finalName}“ ({profile.Segments.Count} segm.)";
                saved++;
            }
            catch (Exception ex)
            {
                item.StatusText = $"✕ zlyhalo: {ex.Message}";
                failed++;
            }
        }

        if (saved > 0)
        {
            ImportedAnything = true;
        }

        Status = $"Hotovo: uložených {saved}" +
            (failed > 0 ? $", zlyhalo {failed}" : string.Empty) +
            (skipped > 0 ? $", preskočených {skipped}" : string.Empty) + ".";
    }

    /// <summary>Returns <paramref name="baseName"/>, suffixed " #2", " #3"… if already used in this batch.</summary>
    private static string UniqueName(string baseName, HashSet<string> used)
    {
        if (!used.Contains(baseName))
        {
            return baseName;
        }

        int n = 2;
        string candidate;
        do
        {
            candidate = $"{baseName} #{n}";
            n++;
        }
        while (used.Contains(candidate));

        return candidate;
    }

    private static List<string> SplitValues(string text) => (text ?? string.Empty)
        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private string ComposeName(string name)
    {
        string prefix = NamePrefix?.Trim() ?? string.Empty;
        string core = name?.Trim() ?? string.Empty;
        return prefix.Length > 0 ? $"{prefix} {core}".Trim() : core;
    }

    private void RaiseListState()
    {
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(HasItems));
        ClearCommand.RaiseCanExecuteChanged();
        ImportAllCommand.RaiseCanExecuteChanged();
    }
}
