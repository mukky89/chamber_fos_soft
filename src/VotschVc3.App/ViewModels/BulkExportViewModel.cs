using System.Collections.ObjectModel;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.ViewModels;

/// <summary>One selectable profile row in the bulk-export list.</summary>
public sealed class BulkExportItemViewModel : ObservableObject
{
    public BulkExportItemViewModel(TestProfile profile) => Profile = profile;

    public TestProfile Profile { get; }

    public string Name => Profile.Name;

    public string Info =>
        $"{Profile.Segments.Count} segm · " +
        (Profile.Sensors is { Count: > 0 } ? string.Join(", ", Profile.Sensors) : "bez snímača") +
        (Profile.Tags is { Count: > 0 } ? " · " + string.Join(", ", Profile.Tags) : string.Empty);

    private bool _isIncluded = true;
    public bool IsIncluded
    {
        get => _isIncluded;
        set { if (SetProperty(ref _isIncluded, value)) IncludedChanged?.Invoke(); }
    }

    public event Action? IncludedChanged;
}

/// <summary>
/// Bulk profile export tool: pick which library profiles to export and write the
/// selected ones to a single JSON file (importable back / bundleable as seed).
/// </summary>
public sealed class BulkExportViewModel : ObservableObject
{
    private readonly ProfileStore _store;

    public BulkExportViewModel(ProfileStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));

        foreach (TestProfile profile in _store.LoadAll())
        {
            var item = new BulkExportItemViewModel(profile);
            item.IncludedChanged += RaiseState;
            Items.Add(item);
        }

        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => Items.Any(i => i.IsIncluded) && !IsBusy);

        Status = Items.Count == 0
            ? "Knižnica je prázdna – niet čo exportovať."
            : $"{Items.Count} profilov v knižnici. Označ, ktoré exportovať.";
    }

    public ObservableCollection<BulkExportItemViewModel> Items { get; } = new();

    public int IncludedCount => Items.Count(i => i.IsIncluded);
    public bool HasItems => Items.Count > 0;

    private string _status = string.Empty;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private double _progress;
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) ExportCommand.RaiseCanExecuteChanged(); }
    }

    /// <summary>True after a successful export (so the caller can report it).</summary>
    public bool ExportedAnything { get; private set; }

    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public AsyncRelayCommand ExportCommand { get; }

    private void SetAll(bool included)
    {
        foreach (BulkExportItemViewModel item in Items)
        {
            item.IsIncluded = included;
        }
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(IncludedCount));
        ExportCommand.RaiseCanExecuteChanged();
    }

    private async Task ExportAsync()
    {
        List<BulkExportItemViewModel> included = Items.Where(i => i.IsIncluded).ToList();
        if (included.Count == 0)
        {
            Status = "Nie je označený žiadny profil.";
            return;
        }

        // The file dialog must be shown on the UI thread before any awaiting.
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Hromadný export profilov do súboru",
            Filter = "Profily (*.json)|*.json",
            DefaultExt = ".json",
            FileName = $"profily-{DateTime.Now:yyyyMMdd}.json",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        Progress = 0;
        Status = $"Exportujem {included.Count} profilov…";

        var collected = new List<TestProfile>(included.Count);
        int done = 0;
        foreach (BulkExportItemViewModel item in included)
        {
            collected.Add(item.Profile);
            done++;
            Progress = done / (double)included.Count * 100d;
            await Task.Yield(); // keep the progress bar responsive
        }

        try
        {
            ProfileFile.Write(dialog.FileName, collected);
            ExportedAnything = true;
            Status = $"Exportovaných {collected.Count} profilov do {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            Status = $"Export zlyhal: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
