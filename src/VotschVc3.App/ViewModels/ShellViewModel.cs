using System.Collections.ObjectModel;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Notifications;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// Root view model. Hosts the two chambers, the home page (chamber picker plus
/// global e-mail settings) and navigation between the home page and a chamber's
/// detail view.
/// </summary>
public sealed class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ProfileStore _store;
    private readonly EmailSettingsStore _emailStore;
    private readonly EmailNotifier _notifier = new();

    public ShellViewModel()
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VotschVc3");
        _store = new ProfileStore(System.IO.Path.Combine(dir, "profiles.json"));
        _emailStore = new EmailSettingsStore(System.IO.Path.Combine(dir, "email.json"));
        _notifier.Settings = _emailStore.Load();

        Chambers = new ObservableCollection<ChamberViewModel>
        {
            new("Komora 1 — teplota + vlhkosť", ChamberKind.TemperatureHumidity, "192.168.0.1", _store, _notifier),
            new("Komora 2 — teplota", ChamberKind.TemperatureOnly, "192.168.0.2", _store, _notifier),
        };

        OpenChamberCommand = new RelayCommand<ChamberViewModel>(OpenChamber, c => c is not null);
        GoHomeCommand = new RelayCommand(GoHome);
        SaveEmailSettingsCommand = new RelayCommand(SaveEmailSettings);
        TestEmailCommand = new AsyncRelayCommand(TestEmailAsync, onError: ex => EmailStatus = $"Chyba: {ex.Message}");

        _currentView = this;
    }

    public ObservableCollection<ChamberViewModel> Chambers { get; }

    private object _currentView;
    /// <summary>Either this shell (home page) or the selected chamber.</summary>
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

    #region E-mail notifications

    /// <summary>Live e-mail settings, bound directly by the home page.</summary>
    public EmailSettings Email => _notifier.Settings;

    /// <summary>Delivery method choices for the combo box.</summary>
    public Array EmailMethods { get; } = Enum.GetValues(typeof(EmailMethod));

    private string _emailStatus = "E-mail upozornenie po dokončení profilu (voliteľné).";
    public string EmailStatus { get => _emailStatus; private set => SetProperty(ref _emailStatus, value); }

    public RelayCommand SaveEmailSettingsCommand { get; }
    public AsyncRelayCommand TestEmailCommand { get; }

    private void SaveEmailSettings()
    {
        try
        {
            _emailStore.Save(_notifier.Settings);
            EmailStatus = "Nastavenia e-mailu uložené.";
        }
        catch (Exception ex)
        {
            EmailStatus = $"Uloženie zlyhalo: {ex.Message}";
        }
    }

    private async Task TestEmailAsync()
    {
        EmailStatus = "Posielam testovací e-mail…";
        EmailResult result = await _notifier.SendTestAsync();
        EmailStatus = result switch
        {
            { Sent: true } => $"Testovací e-mail odoslaný na {Email.Recipient}.",
            { Error: { } err } => $"Test zlyhal: {err}",
            _ => "Zadaj adresáta pre test.",
        };
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        foreach (ChamberViewModel chamber in Chambers)
        {
            await chamber.DisposeAsync();
        }
    }
}
