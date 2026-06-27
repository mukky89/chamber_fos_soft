using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private static readonly HashSet<string> PersistedKeys = new()
    {
        nameof(ChamberViewModel.Host), nameof(ChamberViewModel.Port), nameof(ChamberViewModel.Address),
        nameof(ChamberViewModel.AnalogChannelCount), nameof(ChamberViewModel.StartChannelIndex),
        nameof(ChamberViewModel.SelectedTerminator), nameof(ChamberViewModel.PollIntervalSeconds),
        nameof(ChamberViewModel.AlarmsEnabled), nameof(ChamberViewModel.TempMin), nameof(ChamberViewModel.TempMax),
        nameof(ChamberViewModel.HumMin), nameof(ChamberViewModel.HumMax),
        nameof(ChamberViewModel.AutoStopOnAlarm), nameof(ChamberViewModel.AutoReconnect),
    };

    private readonly ProfileStore _store;
    private readonly EmailSettingsStore _emailStore;
    private readonly ChamberConfigStore _configStore;
    private readonly EmailNotifier _notifier = new();
    private CancellationTokenSource? _saveCts;

    public ShellViewModel()
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VotschVc3");
        _store = new ProfileStore(System.IO.Path.Combine(dir, "profiles.json"));
        _emailStore = new EmailSettingsStore(System.IO.Path.Combine(dir, "email.json"));
        _configStore = new ChamberConfigStore(System.IO.Path.Combine(dir, "chambers.json"));
        _notifier.Settings = _emailStore.Load();

        Chambers = new ObservableCollection<ChamberViewModel>
        {
            new("Komora 1 — teplota + vlhkosť", ChamberKind.TemperatureHumidity, "192.168.0.1", _store, _notifier),
            new("Komora 2 — teplota", ChamberKind.TemperatureOnly, "192.168.0.2", _store, _notifier),
        };

        // Restore saved per-chamber configuration (matched by kind), then watch
        // for changes to persist them automatically.
        List<ChamberConfig> configs = _configStore.LoadAll();
        foreach (ChamberViewModel chamber in Chambers)
        {
            ChamberConfig? saved = configs.FirstOrDefault(c => c.Kind == chamber.Kind);
            if (saved is not null)
            {
                chamber.ApplyConfig(saved);
            }

            chamber.PropertyChanged += OnChamberPropertyChanged;
        }

        Thermometers = new ThermometersViewModel();

        OpenChamberCommand = new RelayCommand<ChamberViewModel>(OpenChamber, c => c is not null);
        OpenThermometersCommand = new RelayCommand(() => CurrentView = Thermometers);
        GoHomeCommand = new RelayCommand(GoHome);
        SaveEmailSettingsCommand = new RelayCommand(SaveEmailSettings);
        TestEmailCommand = new AsyncRelayCommand(TestEmailAsync, onError: ex => EmailStatus = $"Chyba: {ex.Message}");

        _currentView = this;
    }

    public ObservableCollection<ChamberViewModel> Chambers { get; }

    /// <summary>ASL F100 thermometers manager (USB).</summary>
    public ThermometersViewModel Thermometers { get; }

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

    /// <summary>Application version (e.g. "v1.0.0"), read from the assembly.</summary>
    public string AppVersion { get; } =
        "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

    /// <summary>Window title including the version.</summary>
    public string WindowTitle => $"Vötsch — Riadenie klimatických komôr  ·  {AppVersion}";

    public RelayCommand<ChamberViewModel> OpenChamberCommand { get; }
    public RelayCommand OpenThermometersCommand { get; }
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

    #region Configuration persistence

    private void OnChamberPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null && PersistedKeys.Contains(e.PropertyName))
        {
            DebouncedSaveConfigs();
        }
    }

    private void DebouncedSaveConfigs()
    {
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _saveCts = new CancellationTokenSource();
        CancellationToken token = _saveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(800, token);
                SaveConfigs();
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer change.
            }
        });
    }

    private void SaveConfigs()
    {
        try
        {
            _configStore.SaveAll(Chambers.Select(c => c.ToConfig()));
        }
        catch
        {
            // Persistence failures must never crash the app.
        }
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        SaveConfigs();

        await Thermometers.DisposeAsync();

        foreach (ChamberViewModel chamber in Chambers)
        {
            chamber.PropertyChanged -= OnChamberPropertyChanged;
            await chamber.DisposeAsync();
        }
    }
}
