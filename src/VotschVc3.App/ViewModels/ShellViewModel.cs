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
    private static readonly HashSet<string> PersistedKeys = new HashSet<string>
    {
        nameof(ChamberViewModel.Name),
        nameof(ChamberViewModel.Host), nameof(ChamberViewModel.Port), nameof(ChamberViewModel.Address),
        nameof(ChamberViewModel.AnalogChannelCount), nameof(ChamberViewModel.StartChannelIndex),
        nameof(ChamberViewModel.SelectedTerminator), nameof(ChamberViewModel.PollIntervalSeconds),
        nameof(ChamberViewModel.AlarmsEnabled), nameof(ChamberViewModel.TempMin), nameof(ChamberViewModel.TempMax),
        nameof(ChamberViewModel.HumMin), nameof(ChamberViewModel.HumMax),
        nameof(ChamberViewModel.AutoStopOnAlarm), nameof(ChamberViewModel.AutoReconnect),
        nameof(ChamberViewModel.QuickPresets),
    }.Concat(ChamberViewModel.NameplatePropertyNames).ToHashSet();

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
        Admin = new AdminViewModel(this);
        QuickProfile = new QuickProfileViewModel(_store);
        Chambers = new ObservableCollection<ChamberViewModel>();

        // Commands must exist before chambers are built (AddChamberInternal uses them).
        OpenChamberCommand = new RelayCommand<ChamberViewModel>(OpenChamber, c => c is not null);
        OpenThermometersCommand = new RelayCommand(() => CurrentView = Thermometers);
        OpenRecordingViewerCommand = new RelayCommand(() => CurrentView = RecordingViewer);
        OpenProfileLibraryCommand = new RelayCommand(() => CurrentView = ProfileLibrary);
        OpenQuickProfileCommand = new RelayCommand(() => CurrentView = QuickProfile);
        OpenAuditCommand = new RelayCommand(() => CurrentView = Audit);
        OpenAppLogCommand = new RelayCommand(() => CurrentView = AppLog);
        OpenChangelogCommand = new RelayCommand(() => CurrentView = Changelog);
        OpenAdminCommand = new RelayCommand(() => CurrentView = Admin, () => CanManage);
        GoHomeCommand = new RelayCommand(GoHome);
        LogoutCommand = new RelayCommand(Logout);
        AddChamberCommand = new RelayCommand(AddChamber, () => CanManage);
        RemoveChamberCommand = new RelayCommand<ChamberViewModel>(RemoveChamber, c => c is not null && Chambers.Count > 1 && CanManage);
        MoveChamberUpCommand = new RelayCommand<ChamberViewModel>(c => MoveChamber(c, -1), c => c is not null);
        MoveChamberDownCommand = new RelayCommand<ChamberViewModel>(c => MoveChamber(c, +1), c => c is not null);
        SaveEmailSettingsCommand = new RelayCommand(SaveEmailSettings);
        TestEmailCommand = new AsyncRelayCommand(TestEmailAsync, onError: ex => EmailStatus = $"Chyba: {ex.Message}");

        // Build chambers from the saved configuration (seed defaults on first run).
        List<ChamberConfig> configs = _configStore.LoadAll();
        bool seeded = configs.Count == 0;

        // One-time reseed to the real lab layout (VT3 7034, VC3 7034, POL-EKO with
        // their fixed IP addresses / ports). Guarded by a marker so a user who later
        // edits IPs or removes a chamber keeps their changes on the next start.
        string reseedMarker = System.IO.Path.Combine(dir, ".chambers_seed_v3");
        bool reseeded = false;
        if (seeded || !System.IO.File.Exists(reseedMarker))
        {
            configs = DefaultConfigs();
            reseeded = true;
        }

        foreach (ChamberConfig config in configs)
        {
            AddChamberInternal(config);
        }

        if (seeded || reseeded)
        {
            SaveConfigs();
        }

        try
        {
            System.IO.Directory.CreateDirectory(dir);
            if (!System.IO.File.Exists(reseedMarker))
            {
                System.IO.File.WriteAllText(reseedMarker, DateTimeOffset.Now.ToString("o"));
            }
        }
        catch
        {
            // A missing marker only means the one-time seed check runs again; harmless.
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

    /// <summary>Quick temperature-sweep profile builder.</summary>
    public QuickProfileViewModel QuickProfile { get; }

    /// <summary>Application diagnostic log viewer.</summary>
    public AppLogViewModel AppLog { get; } = new();

    /// <summary>Embedded changelog viewer.</summary>
    public ChangelogViewModel Changelog { get; } = new();

    /// <summary>Admin-only settings screen (e-mail notifications, chamber management).</summary>
    public AdminViewModel Admin { get; }

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
    public RelayCommand OpenQuickProfileCommand { get; }
    public RelayCommand OpenAuditCommand { get; }
    public RelayCommand OpenAppLogCommand { get; }
    public RelayCommand OpenChangelogCommand { get; }
    public RelayCommand OpenAdminCommand { get; }
    public RelayCommand GoHomeCommand { get; }
    public RelayCommand LogoutCommand { get; }
    public RelayCommand AddChamberCommand { get; }
    public RelayCommand<ChamberViewModel> RemoveChamberCommand { get; }

    /// <summary>Moves a chamber one place earlier (left) in the dashboard order.</summary>
    public RelayCommand<ChamberViewModel> MoveChamberUpCommand { get; }

    /// <summary>Moves a chamber one place later (right) in the dashboard order.</summary>
    public RelayCommand<ChamberViewModel> MoveChamberDownCommand { get; }

    /// <summary>Audit trail view model.</summary>
    public AuditViewModel Audit { get; }

    private void OpenChamber(ChamberViewModel? chamber)
    {
        if (chamber is not null)
        {
            // Pick up any profiles created elsewhere (e.g. the quick builder) so they
            // are available in the chamber's history list.
            chamber.ReloadProfiles();
            CurrentView = chamber;
        }
    }

    private void GoHome()
    {
        // Refresh every chamber's saved-profile list so profiles created in the quick
        // builder / editor show up in the dashboard picker without restarting the app.
        foreach (ChamberViewModel chamber in Chambers)
        {
            chamber.ReloadProfiles();
        }

        CurrentView = this;
    }

    #region Users & permissions

    private User? _currentUser;

    public string CurrentUserName => _currentUser?.Name ?? "—";
    public string CurrentRoleLabel => _currentUser is null ? string.Empty : RoleLabel(_currentUser.Role);
    public bool IsLoggedIn => _currentUser is not null;

    private bool CanControl => _currentUser is { Role: >= UserRole.Supervisor };
    private bool CanManage => _currentUser is { Role: UserRole.Admin };

    /// <summary>True when the signed-in user may open the admin settings screen.</summary>
    public bool IsAdmin => CanManage;

    private void OnLoggedIn(User user)
    {
        _currentUser = user;
        _audit.CurrentUser = user.Name;
        _audit.Log("Systém", "Prihlásenie", $"Rola: {user.Role}");
        ApplyPermissions();
        CurrentView = this;
        RaiseUserChanged();

        // Bring every chamber online automatically once someone is signed in.
        _ = ConnectAllChambersAsync();
    }

    private Task ConnectAllChambersAsync() =>
        Task.WhenAll(Chambers.Select(c => c.ConnectIfPossibleAsync()));

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
            chamber.SetManageAllowed(CanManage);
        }

        AddChamberCommand.RaiseCanExecuteChanged();
        RemoveChamberCommand.RaiseCanExecuteChanged();
        OpenAdminCommand.RaiseCanExecuteChanged();

        // Non-admins must not linger on the admin screen after a role change.
        if (!CanManage && ReferenceEquals(CurrentView, Admin))
        {
            GoHome();
        }
    }

    private void RaiseUserChanged()
    {
        OnPropertyChanged(nameof(CurrentUserName));
        OnPropertyChanged(nameof(CurrentRoleLabel));
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(IsAdmin));
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

    /// <summary>Protocols for the "add chamber" picker (Vötsch ASCII-2, POL-EKO MODBUS).</summary>
    public Array ChamberProtocols { get; } = Enum.GetValues(typeof(ChamberProtocol));

    private string _newChamberName = string.Empty;
    public string NewChamberName { get => _newChamberName; set => SetProperty(ref _newChamberName, value); }

    private ChamberKind _newChamberKind = ChamberKind.TemperatureHumidity;
    public ChamberKind NewChamberKind { get => _newChamberKind; set => SetProperty(ref _newChamberKind, value); }

    private ChamberProtocol _newChamberProtocol = ChamberProtocol.VotschAscii2;
    public ChamberProtocol NewChamberProtocol
    {
        get => _newChamberProtocol;
        set => SetProperty(ref _newChamberProtocol, value);
    }

    private string _newChamberHost = "192.168.0.1";
    public string NewChamberHost { get => _newChamberHost; set => SetProperty(ref _newChamberHost, value); }

    private static List<ChamberConfig> DefaultConfigs() => new()
    {
        // Vötsch VT3 7034 – temperature only. ASCII-2 port 2049 (may change per site).
        new ChamberConfig
        {
            Name = "Komora 1 — Vötsch VT3 7034 (teplota)", Kind = ChamberKind.TemperatureOnly, Host = "10.88.1.175", Port = 2049,
            Nameplate = new ChamberNameplate
            {
                Model = "VT³ 7034", SerialNumber = "58566198240010", OrderNumber = "56619824",
                YearOfConstruction = "2014", Refrigerant1 = "R-404A · 2,5 kg", Refrigerant2 = "R-23 · 0,75 kg",
                SupplyVoltage = "3/N/PE AC 400V±10% 50Hz", NominalPower = "4,9 kW", NominalCurrent = "16 A",
                SystemNumber = "67624022", FirstCalibration = "2014", NextCalibration = "2015",
                Notes = "Made in Germany. Štanddruck 13 bar g.",
            },
        },
        // Vötsch VC3 7034 – temperature + humidity.
        new ChamberConfig
        {
            Name = "Komora 2 — Vötsch VC3 7034 (teplota + vlhkosť)", Kind = ChamberKind.TemperatureHumidity, Host = "10.88.1.180", Port = 2049,
            Nameplate = new ChamberNameplate
            {
                Model = "VC³ 7034", SerialNumber = "58566126860010", OrderNumber = "56612686",
                YearOfConstruction = "2008", Refrigerant1 = "R-404A · 2,5 kg", Refrigerant2 = "R-23 · 0,55 kg",
                SupplyVoltage = "3/N/PE AC 400V±10% 50Hz", NominalPower = "4,9 kW", NominalCurrent = "16 A",
                SystemNumber = "67624021", FirstCalibration = "08-09", NextCalibration = "2009",
                Notes = "Štanddruck 13 bar g.",
            },
        },
        DefaultPolEkoConfig(),
    };

    /// <summary>The pre-configured POL-EKO SLN 115 drying oven (MODBUS TCP).</summary>
    private static ChamberConfig DefaultPolEkoConfig() => new()
    {
        Name = "POL-EKO SLN 115 — sušiareň",
        Kind = ChamberKind.TemperatureOnly,
        Protocol = ChamberProtocol.PolEkoModbus,
        Host = "10.88.5.162",
        Port = 502,
        Address = 1,
        TempMin = 0,
        TempMax = 300, // SLN drying oven range is up to +300 °C
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
        bool polEko = NewChamberProtocol == ChamberProtocol.PolEkoModbus;
        var config = new ChamberConfig
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(NewChamberName) ? $"Komora {Chambers.Count + 1}" : NewChamberName.Trim(),
            // POL-EKO ovens are temperature-only and speak MODBUS TCP on port 502.
            Kind = polEko ? ChamberKind.TemperatureOnly : NewChamberKind,
            Protocol = NewChamberProtocol,
            Port = polEko ? 502 : 1080,
            Host = string.IsNullOrWhiteSpace(NewChamberHost) ? "192.168.0.1" : NewChamberHost.Trim(),
        };

        AddChamberInternal(config);
        SaveConfigs();
        NewChamberName = string.Empty;
    }

    private CancellationTokenSource? _removeArmCts;

    private async void RemoveChamber(ChamberViewModel? chamber)
    {
        if (chamber is null || Chambers.Count <= 1)
        {
            return;
        }

        // Two-step confirmation: the first ✕ click arms the button ("✕ Naozaj?"),
        // a second click within 4 s removes the chamber; otherwise it disarms
        // itself. Removing a chamber deletes its saved configuration, so an
        // accidental single click must never be enough.
        if (!chamber.IsRemoveArmed)
        {
            foreach (ChamberViewModel c in Chambers)
            {
                c.SetRemoveArmed(false);
            }

            chamber.SetRemoveArmed(true);
            _removeArmCts?.Cancel();
            _removeArmCts = new CancellationTokenSource();
            CancellationToken token = _removeArmCts.Token;
            _ = Task.Delay(TimeSpan.FromSeconds(4), token).ContinueWith(
                _ => chamber.SetRemoveArmed(false),
                token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
            return;
        }

        _removeArmCts?.Cancel();
        chamber.SetRemoveArmed(false);

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

    /// <summary>Reorders a chamber by <paramref name="delta"/> places and persists the new order.</summary>
    private void MoveChamber(ChamberViewModel? chamber, int delta)
    {
        if (chamber is null)
        {
            return;
        }

        int i = Chambers.IndexOf(chamber);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= Chambers.Count)
        {
            return;
        }

        Chambers.Move(i, j);
        SaveConfigs();
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
