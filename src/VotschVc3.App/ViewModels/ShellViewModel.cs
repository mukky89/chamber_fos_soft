using System.Collections.ObjectModel;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// Root view model. Hosts the two chambers, the home page (chamber picker) and
/// navigation between the home page and a chamber's detail view.
/// </summary>
public sealed class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ProfileStore _store;

    public ShellViewModel()
    {
        string profilePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "VotschVc3", "profiles.json");
        _store = new ProfileStore(profilePath);

        Chambers = new ObservableCollection<ChamberViewModel>
        {
            new("Komora 1 — teplota + vlhkosť", ChamberKind.TemperatureHumidity, "192.168.0.1", _store),
            new("Komora 2 — teplota", ChamberKind.TemperatureOnly, "192.168.0.2", _store),
        };

        OpenChamberCommand = new RelayCommand<ChamberViewModel>(OpenChamber, c => c is not null);
        GoHomeCommand = new RelayCommand(GoHome);

        _currentView = this;
    }

    /// <summary>The configured chambers.</summary>
    public ObservableCollection<ChamberViewModel> Chambers { get; }

    private object _currentView;
    /// <summary>
    /// Either this shell (home page) or the selected <see cref="ChamberViewModel"/>.
    /// Bound to a <c>ContentControl</c> whose <c>DataTemplate</c>s select the view.
    /// </summary>
    public object CurrentView
    {
        get => _currentView;
        private set
        {
            if (SetProperty(ref _currentView, value))
            {
                OnPropertyChanged(nameof(IsHome));
            }
        }
    }

    /// <summary><c>true</c> while the home page is shown.</summary>
    public bool IsHome => ReferenceEquals(CurrentView, this);

    public RelayCommand<ChamberViewModel> OpenChamberCommand { get; }
    public RelayCommand GoHomeCommand { get; }

    private void OpenChamber(ChamberViewModel? chamber)
    {
        if (chamber is not null)
        {
            CurrentView = chamber;
        }
    }

    private void GoHome() => CurrentView = this;

    public async ValueTask DisposeAsync()
    {
        foreach (ChamberViewModel chamber in Chambers)
        {
            await chamber.DisposeAsync();
        }
    }
}
