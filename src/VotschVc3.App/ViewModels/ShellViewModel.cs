using System.Collections.ObjectModel;
using System.ComponentModel;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Notifications;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Security;

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
    private readonly UserStore _userStore;
    private readonly AuditLog _audit;
    private readonly LoginViewModel _login;
    private readonly EmailNotifier _notifier = new();
    private CancellationTokenSource? _saveCts;

    public ShellViewModel()
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VotschVc3");
        _store = new ProfileStore(System.IO.Path.Combine(dir, "profiles.json"));
        _emailStore = new EmailSettingsStore(System.IO.Path.Combine(dir, "email.json"));
        _configStore = new ChamberConfigStore(System.IO.Path.Combine(dir, "chambers.json"));
        _userStore = new UserStore(System.IO.Path.Combine(dir, "users.json"));
        _audit = new AuditLog(System.IO.Path.Combine(dir, "audit.csv"));
        _notifier.Settings = _emailStore.Load();

        Audit = new AuditViewModel(_audit);
        ProfileLibrary = new ProfileLibraryViewModel(_store);
        _login = new LoginViewModel(_userStore, OnLoggedIn);

        Thermometers = new ThermometersViewModel();
        Chambers = new ObservableCollection<ChamberViewModel>();

        // Commands must exist before chambers are built (AddChamberInternal uses them).
        OpenChamberCommand = new RelayCommand<ChamberViewModel>(OpenChamber, c => c is not null);
        OpenThermometersCommand = new RelayCommand(() => CurrentView = Thermometers);
        OpenRecordingViewerCommand = new RelayCommand(() => CurrentView = RecordingViewer);
        OpenProfileLibraryCommand = new RelayCommand(() => CurrentView = ProfileLibrary);
        OpenAuditCommand = new RelayCommand(() => CurrentView = Audit);
        GoHomeCommand = new RelayCommand(GoHome);
        LogoutCommand = new RelayCommand(Logout);
        AddChamberCommand = new RelayCommand(AddChamber, () => CanManage);
        RemoveChamberCommand = new RelayCommand<ChamberViewModel>(RemoveChamber, c => c is not null && Chambers.Count > 1 && CanManage);
        SaveEmailSettingsCommand = new RelayCommand(SaveEmailSettings);
        TestEmailCommand = new AsyncRelayCommand(TestEmailAsync, onError: ex => EmailStatus = $"Chyba: {ex.Message}");

        // Build chambers from the saved configuration (seed defaults on first run).
        List<ChamberConfig> configs = _configStore.LoadAll();
        bool seeded = configs.Count == 0;
        if (seeded)
        {
            configs = DefaultConfigs();
        }

        foreach (ChamberConfig config in configs)
        {
            AddChamberInternal(config);
        }

        if (seeded)
        {
            SaveConfigs();
        }

        // Start at the login screen.
        _currentView = _login;
    }

    public ObservableCollection<ChamberViewModel> Chambers { get; }

    /// <summary>ASL F100 thermometers manager (USB).</summary>
    public ThermometersViewModel Thermometers { get; }

    /// <summary>Viewer for saved CSV recordings (analysis).</summary>
    public RecordingViewerViewModel RecordingViewer { get; } = new();

    /// <summary>Standalone profile editor / library (no chamber connection needed).</summary>
    public ProfileLibraryViewModel ProfileLibrary { get; }

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
    public RelayCommand OpenRecordingViewerCommand { get; }
    public RelayCommand OpenProfileLibraryCommand { get; }
    public RelayCommand OpenAuditCommand { get; }
    public RelayCommand GoHomeCommand { get; }
    public RelayCommand LogoutCommand { get; }
    public RelayCommand AddChamberCommand { get; }
    public RelayCommand<ChamberViewModel> RemoveChamberCommand { get; }

    /// <summary>Audit trail view model.</summary>
    public AuditViewModel Audit { get; }

    private void OpenChamber(ChamberViewModel? chamber)
    {
        if (chamber is not null)
        {
            CurrentView = chamber;
        }
    }

    private void GoHome() => CurrentView = this;

    #region Users & permissions

    private User? _currentUser;

    public string CurrentUserName => _currentUser?.Name ?? "—";
    public string CurrentRoleLabel => _currentUser is null ? string.Empty : RoleLabel(_currentUser.Role);
    public bool IsLoggedIn => _currentUser is not null;

    private bool CanControl => _currentUser is { Role: >= UserRole.Supervisor };
    private bool CanManage => _currentUser is { Role: UserRole.Admin };

    private void OnLoggedIn(User user)
    {
        _currentUser = user;
        _audit.CurrentUser = user.Name;
        _audit.Log("Systém", "Prihlásenie", $"Rola: {user.Role}");
        ApplyPermissions();
        CurrentView = this;
        RaiseUserChanged();
    }

    private void Logout()
    {
        if (_currentUser is not null)
        {
            _audit.Log("Systém", "Odhlásenie", _currentUser.Name);
        }

        _currentUser = null;
        _audit.CurrentUser = "—";
        ApplyPermissions();
        CurrentView = _login;
        RaiseUserChanged();
    }

    private void ApplyPermissions()
    {
        foreach (ChamberViewModel chamber in Chambers)
        {
            chamber.SetControlAllowed(CanControl);
        }

        AddChamberCommand.RaiseCanExecuteChanged();
        RemoveChamberCommand.RaiseCanExecuteChanged();
    }

    private void RaiseUserChanged()
    {
        OnPropertyChanged(nameof(CurrentUserName));
        OnPropertyChanged(nameof(CurrentRoleLabel));
        OnPropertyChanged(nameof(IsLoggedIn));
    }

    private static string RoleLabel(UserRole role) => role switch
    {
        UserRole.Admin => "Admin",
        UserRole.Supervisor => "Supervisor",
        _ => "Operátor",
    };

    #endregion

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

    #region Chamber management

    /// <summary>Chamber types for the "add chamber" picker.</summary>
    public Array ChamberKinds { get; } = Enum.GetValues(typeof(ChamberKind));

    private string _newChamberName = string.Empty;
    public string NewChamberName { get => _newChamberName; set => SetProperty(ref _newChamberName, value); }

    private ChamberKind _newChamberKind = ChamberKind.TemperatureHumidity;
    public ChamberKind NewChamberKind { get => _newChamberKind; set => SetProperty(ref _newChamberKind, value); }

    private string _newChamberHost = "192.168.0.1";
    public string NewChamberHost { get => _newChamberHost; set => SetProperty(ref _newChamberHost, value); }

    private static List<ChamberConfig> DefaultConfigs() => new()
    {
        new ChamberConfig { Name = "Komora 1 — teplota + vlhkosť", Kind = ChamberKind.TemperatureHumidity, Host = "192.168.0.1" },
        new ChamberConfig { Name = "Komora 2 — teplota", Kind = ChamberKind.TemperatureOnly, Host = "192.168.0.2" },
    };

    private void AddChamberInternal(ChamberConfig config)
    {
        var chamber = new ChamberViewModel(config, _store, _notifier, Thermometers, _audit);
        chamber.SetControlAllowed(CanControl);
        chamber.PropertyChanged += OnChamberPropertyChanged;
        Chambers.Add(chamber);
        RemoveChamberCommand.RaiseCanExecuteChanged();
    }

    private void AddChamber()
    {
        var config = new ChamberConfig
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(NewChamberName) ? $"Komora {Chambers.Count + 1}" : NewChamberName.Trim(),
            Kind = NewChamberKind,
            Host = string.IsNullOrWhiteSpace(NewChamberHost) ? "192.168.0.1" : NewChamberHost.Trim(),
        };

        AddChamberInternal(config);
        SaveConfigs();
        NewChamberName = string.Empty;
    }

    private async void RemoveChamber(ChamberViewModel? chamber)
    {
        if (chamber is null || Chambers.Count <= 1)
        {
            return;
        }

        if (ReferenceEquals(CurrentView, chamber))
        {
            GoHome();
        }

        Chambers.Remove(chamber);
        chamber.PropertyChanged -= OnChamberPropertyChanged;
        RemoveChamberCommand.RaiseCanExecuteChanged();
        SaveConfigs();
        await chamber.DisposeAsync();
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
