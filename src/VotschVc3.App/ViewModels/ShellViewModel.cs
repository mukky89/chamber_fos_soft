using System.Collections.ObjectModel;
using System.ComponentModel;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Notifications;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;
using VotschVc3.Core.Security;
using VotschVc3.Core.Settings;

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
        nameof(ChamberViewModel.QuickPresets), nameof(ChamberViewModel.QuickProfiles),
        nameof(ChamberViewModel.IsLocked), nameof(ChamberViewModel.LockPasswordHash),
    }.Concat(ChamberViewModel.NameplatePropertyNames).ToHashSet();

    private readonly ProfileStore _store;
    private readonly EmailSettingsStore _emailStore;
    private readonly ChamberConfigStore _configStore;
    private readonly UserStore _userStore;
    private readonly AuditLog _audit;
    private readonly LoginViewModel _login;
    private readonly EmailNotifier _notifier = new();
    private readonly UiSettingsStore _uiStore;
    private readonly UiSettings _ui;
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
        _uiStore = new UiSettingsStore(System.IO.Path.Combine(dir, "ui.json"));
        _ui = _uiStore.Load();
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
        ToggleTimelineCommand = new RelayCommand(() => ShowTimeline = !ShowTimeline);
        AddChamberCommand = new RelayCommand(AddChamber, () => CanManage);
        RemoveChamberCommand = new RelayCommand<ChamberViewModel>(RemoveChamber, c => c is not null && Chambers.Count > 1 && CanManage);
        MoveChamberUpCommand = new RelayCommand<ChamberViewModel>(c => MoveChamber(c, -1), c => c is not null);
        MoveChamberDownCommand = new RelayCommand<ChamberViewModel>(c => MoveChamber(c, +1), c => c is not null);
        SaveEmailSettingsCommand = new RelayCommand(SaveEmailSettings);
        TestEmailCommand = new AsyncRelayCommand(TestEmailAsync, onError: ex => EmailStatus = $"Chyba: {ex.Message}");
        AddUserCommand = new RelayCommand(AddUser,
            () => CanManage && !string.IsNullOrWhiteSpace(NewUserName) && !string.IsNullOrEmpty(NewUserPassword));
        DeleteUserCommand = new RelayCommand<User>(DeleteUser, u => CanManage && u is not null);
        SaveUsersCommand = new RelayCommand(SaveUsers, () => CanManage);
        RefreshUsers();

        // Build chambers from the saved configuration (seed defaults on first run).
        List<ChamberConfig> configs = _configStore.LoadAll();
        bool seeded = configs.Count == 0;

        // One-time reseed to the real lab layout (VT3 7034, VC3 7034, POL-EKO with
        // their fixed IP addresses / ports). Guarded by a marker so a user who later
        // edits IPs or removes a chamber keeps their changes on the next start.
        string reseedMarker = System.IO.Path.Combine(dir, ".chambers_seed_v6");
        bool reseeded = false;
        if (seeded || !System.IO.File.Exists(reseedMarker))
        {
            configs = DefaultConfigs();
            reseeded = true;
        }

        // One-time: force the canonical device names (keyed by each device's fixed
        // lab IP) so existing installs pick up the renamed devices. Guarded by its own
        // marker; after it runs once an admin can freely rename a device and keep it.
        string namesMarker = System.IO.Path.Combine(dir, ".device_names_v1");
        bool renamed = false;
        if (!reseeded && !System.IO.File.Exists(namesMarker))
        {
            foreach (ChamberConfig config in configs)
            {
                string? canonical = CanonicalDeviceName(config.Host);
                if (canonical is not null && config.Name != canonical)
                {
                    config.Name = canonical;
                    renamed = true;
                }
            }
        }

        // One-time: add the new Komora 3 — FOI climate chamber for labs that already
        // have a saved config (so a fresh reseed doesn't wipe their existing IP
        // edits). Guarded by its own marker and matched by Host so a user who later
        // removes it doesn't get it silently re-added.
        string extraChambersMarker = System.IO.Path.Combine(dir, ".chambers_add_polytech_foi_v1");
        bool addedExtras = false;
        if (!seeded && !reseeded && !System.IO.File.Exists(extraChambersMarker))
        {
            if (!configs.Any(c => c.Host == "10.88.5.233"))
            {
                configs.Add(DefaultKomora3FoiConfig());
                addedExtras = true;
            }
        }

        // One-time clean-up of the SIKA baths (earlier builds left duplicated /
        // inconsistently named entries). Remove every SIKA REST-API chamber and
        // re-add exactly the two canonical ones – "SIKA Sylex" (10.88.5.81) and
        // "SIKA PolyTech" (10.88.6.28) – with the correct nameplate and range.
        // Guarded by a marker so a later manual rename / IP edit is respected.
        string sikaResetMarker = System.IO.Path.Combine(dir, ".chambers_sika_reset_v2");
        bool sikaReset = false;
        if (!seeded && !reseeded && !System.IO.File.Exists(sikaResetMarker))
        {
            configs.RemoveAll(c => c.Protocol == ChamberProtocol.SikaRestApi);
            configs.AddRange(DefaultSikaConfigs());
            sikaReset = true;
        }

        // Every start: force the known lab devices into a fixed display order
        // (Komora 1/2/3, Sušiareň). Also "na tvrdo", so it wins over any manual
        // reordering after a restart. SIKA baths are no longer forced defaults –
        // they are ordinary devices an admin adds / removes manually.
        bool reordered = ApplyForcedOrder(configs);

        foreach (ChamberConfig config in configs)
        {
            AddChamberInternal(config);
        }

        if (seeded || reseeded || renamed || addedExtras || sikaReset || reordered)
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

            if (!System.IO.File.Exists(namesMarker))
            {
                System.IO.File.WriteAllText(namesMarker, DateTimeOffset.Now.ToString("o"));
            }

            if (!System.IO.File.Exists(extraChambersMarker))
            {
                System.IO.File.WriteAllText(extraChambersMarker, DateTimeOffset.Now.ToString("o"));
            }

            if (!System.IO.File.Exists(sikaResetMarker))
            {
                System.IO.File.WriteAllText(sikaResetMarker, DateTimeOffset.Now.ToString("o"));
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
    public string WindowTitle => $"Riadenie laboratórnych zariadení  ·  {AppVersion}";

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

    /// <summary>
    /// Admin toggle (persisted): when on, the dashboard cards expose the reorder
    /// arrows so chambers can be dragged into a new order. Off by default.
    /// </summary>
    public bool AllowChamberReorder
    {
        get => _ui.AllowChamberReorder;
        set
        {
            if (_ui.AllowChamberReorder == value)
            {
                return;
            }

            _ui.AllowChamberReorder = value;
            SaveUiSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsReorderAllowed));
        }
    }

    /// <summary>
    /// Whether the reorder arrows should be visible right now: only for admins
    /// and only when the admin has explicitly enabled reordering.
    /// </summary>
    public bool IsReorderAllowed => CanManage && _ui.AllowChamberReorder;

    /// <summary>
    /// Admin toggle (persisted): compact dashboard layout. When on, the cards,
    /// device graphics and text shrink so more devices fit on one screen; the
    /// original layout returns when it is switched off. Off by default.
    /// </summary>
    public bool CompactMode
    {
        get => _ui.CompactMode;
        set
        {
            if (_ui.CompactMode == value)
            {
                return;
            }

            _ui.CompactMode = value;
            SaveUiSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Persisted toggle: whether the fleet timeline (Gantt) is shown on the
    /// dashboard. On by default; can be hidden to free up vertical space.
    /// </summary>
    public bool ShowTimeline
    {
        get => _ui.ShowTimeline;
        set
        {
            if (_ui.ShowTimeline == value)
            {
                return;
            }

            _ui.ShowTimeline = value;
            SaveUiSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimelineToggleText));
        }
    }

    /// <summary>Caption for the show/hide-timeline toggle button.</summary>
    public string TimelineToggleText => ShowTimeline ? "▾ Skryť" : "▸ Zobraziť";

    /// <summary>Shows / hides the dashboard timeline.</summary>
    public RelayCommand ToggleTimelineCommand { get; }

    private void SaveUiSettings()
    {
        try
        {
            _uiStore.Save(_ui);
        }
        catch
        {
            // A failed preference write must never crash the app.
        }
    }

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
        _login.RefreshUsers();
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
        AddUserCommand.RaiseCanExecuteChanged();
        DeleteUserCommand.RaiseCanExecuteChanged();
        SaveUsersCommand.RaiseCanExecuteChanged();

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
        OnPropertyChanged(nameof(IsReorderAllowed));
    }

    private static string RoleLabel(UserRole role) => role switch
    {
        UserRole.Admin => "Admin",
        UserRole.Supervisor => "Supervisor",
        _ => "Operátor",
    };

    #endregion

    #region User management (admin)

    /// <summary>All application users (admin management screen).</summary>
    public ObservableCollection<User> Users { get; } = new();

    /// <summary>Available roles for the role pickers.</summary>
    public Array UserRoles { get; } = Enum.GetValues(typeof(UserRole));

    private string _newUserName = string.Empty;
    public string NewUserName
    {
        get => _newUserName;
        set { if (SetProperty(ref _newUserName, value)) AddUserCommand.RaiseCanExecuteChanged(); }
    }

    private string _newUserPassword = string.Empty;
    public string NewUserPassword
    {
        get => _newUserPassword;
        set { if (SetProperty(ref _newUserPassword, value)) AddUserCommand.RaiseCanExecuteChanged(); }
    }

    private UserRole _newUserRole = UserRole.Operator;
    public UserRole NewUserRole { get => _newUserRole; set => SetProperty(ref _newUserRole, value); }

    private string _userStatus = "Vytvor používateľov a priraď im rolu (mení sa aj rola existujúcich).";
    public string UserStatus { get => _userStatus; private set => SetProperty(ref _userStatus, value); }

    public RelayCommand AddUserCommand { get; }
    public RelayCommand<User> DeleteUserCommand { get; }
    public RelayCommand SaveUsersCommand { get; }

    private void RefreshUsers()
    {
        Users.Clear();
        foreach (User user in _userStore.LoadAll())
        {
            Users.Add(user);
        }
    }

    private void AddUser()
    {
        string name = NewUserName.Trim();
        if (Users.Any(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            UserStatus = $"Používateľ „{name}“ už existuje.";
            return;
        }

        Users.Add(new User { Name = name, Role = NewUserRole, PasswordHash = User.Hash(NewUserPassword) });
        PersistUsers();
        _audit.Log("Systém", "Nový používateľ", $"{name} · {NewUserRole}");
        UserStatus = $"Používateľ „{name}“ ({RoleLabel(NewUserRole)}) vytvorený.";
        NewUserName = string.Empty;
        NewUserPassword = string.Empty;
        NewUserRole = UserRole.Operator;
    }

    private void DeleteUser(User? user)
    {
        if (user is null)
        {
            return;
        }

        if (string.Equals(user.Name, _currentUser?.Name, StringComparison.OrdinalIgnoreCase))
        {
            UserStatus = "Nemôžeš odstrániť práve prihláseného používateľa.";
            return;
        }

        if (user.Role == UserRole.Admin && Users.Count(u => u.Role == UserRole.Admin) <= 1)
        {
            UserStatus = "Musí ostať aspoň jeden admin.";
            return;
        }

        Users.Remove(user);
        PersistUsers();
        _audit.Log("Systém", "Odstránený používateľ", user.Name);
        UserStatus = $"Používateľ „{user.Name}“ odstránený.";
    }

    private void SaveUsers()
    {
        // Role changes are edited in place on the list; block saving away the last admin.
        if (!Users.Any(u => u.Role == UserRole.Admin))
        {
            UserStatus = "Musí ostať aspoň jeden admin – zmeny neuložené.";
            RefreshUsers();
            return;
        }

        PersistUsers();
        UserStatus = "Zmeny používateľov uložené.";
    }

    private void PersistUsers()
    {
        try
        {
            _userStore.SaveAll(Users);
            _login.RefreshUsers();
        }
        catch (Exception ex)
        {
            UserStatus = $"Uloženie zlyhalo: {ex.Message}";
        }
    }

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

    /// <summary>Canonical device name for a fixed lab IP (used by the one-time name migration).</summary>
    private static string? CanonicalDeviceName(string host) => host switch
    {
        "10.88.5.175" => "Komora 1 — Vötsch VT3 7034 (teplota)",
        "10.88.5.180" => "Komora 2 — Vötsch VC3 7034 (teplota + vlhkosť)",
        "10.88.5.162" => "Sušiareň — POL-EKO SLN 115 (teplota)",
        _ => null,
    };

    private static List<ChamberConfig> DefaultConfigs() => new List<ChamberConfig>()
    {
        // Vötsch VT3 7034 – temperature only. ASCII-2 port 2049 (may change per site).
        new ChamberConfig
        {
            Name = "Komora 1 — Vötsch VT3 7034 (teplota)", Kind = ChamberKind.TemperatureOnly, Host = "10.88.5.175", Port = 2049,
            StartChannelIndex = 1,
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
            Name = "Komora 2 — Vötsch VC3 7034 (teplota + vlhkosť)", Kind = ChamberKind.TemperatureHumidity, Host = "10.88.5.180", Port = 2049,
            StartChannelIndex = 1,
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
        DefaultKomora3FoiConfig(),
    }.Concat(DefaultSikaConfigs()).ToList();

    /// <summary>The two lab SIKA TP Premium calibration baths (REST-API), in display order.</summary>
    private static List<ChamberConfig> DefaultSikaConfigs() => new()
    {
        SikaSylexConfig(),
        SikaPolytechConfig(),
    };

    /// <summary>Builds a SIKA TP Premium bath config (REST-API, temperature only, -50…165 °C).</summary>
    private static ChamberConfig SikaBathConfig(string name, string host, ChamberNameplate nameplate) => new()
    {
        Name = name,
        Kind = ChamberKind.TemperatureOnly,
        Protocol = ChamberProtocol.SikaRestApi,
        Host = host,
        Port = SikaRestApiProtocol.DefaultPort,
        StartChannelIndex = 0,
        TempMin = -50,
        TempMax = 165,
        Nameplate = nameplate,
    };

    /// <summary>
    /// SIKA Sylex – nameplate + range from the device's system information
    /// (TP3M165E.2, s/n 2219005). Ordinary device: IP can be changed / removed.
    /// </summary>
    private static ChamberConfig SikaSylexConfig() => SikaBathConfig("SIKA Sylex", "10.88.5.81", new ChamberNameplate
    {
        Manufacturer = "SIKA",
        Model = "TP3M165E.2",
        SerialNumber = "2219005",
        OrderNumber = "SIKA",
        YearOfConstruction = "2022",
        SystemNumber = "001927", // HardwareSerial
        FirstCalibration = "2022-05-09",
        NextCalibration = "2025-05-09",
        Notes = "SIKA TP Premium · SW 28.17 · Firmware V 1.15 · ARM Rev. 1 · rozsah -50…+165 °C. "
              + "Pozn.: REST-API ovládanie (setSP) vyžaduje TP software > 30.35.",
    });

    /// <summary>SIKA PolyTech – SIKA TP Premium bath at its fixed lab IP.</summary>
    private static ChamberConfig SikaPolytechConfig() => SikaBathConfig("SIKA PolyTech", "10.88.6.28", new ChamberNameplate
    {
        Manufacturer = "SIKA",
        Model = "TP Premium",
        OrderNumber = "SIKA",
        Notes = "SIKA TP Premium · rozsah -50…+165 °C. "
              + "Pozn.: REST-API ovládanie (setSP) vyžaduje TP software > 30.35.",
    });

    /// <summary>The pre-configured POL-EKO SLN 115 drying oven (MODBUS TCP).</summary>
    private static ChamberConfig DefaultPolEkoConfig() => new()
    {
        Name = "Sušiareň — POL-EKO SLN 115 (teplota)",
        Kind = ChamberKind.TemperatureOnly,
        Protocol = ChamberProtocol.PolEkoModbus,
        Host = "10.88.5.162",
        Port = 502,
        Address = 1,
        TempMin = 0,
        TempMax = 300, // SLN drying oven range is up to +300 °C
    };

    /// <summary>
    /// Komora 3 — FOI: another temperature + humidity climate chamber (different
    /// model than Komora 1/2, but the same Vötsch ASCII-2 communication protocol).
    /// </summary>
    private static ChamberConfig DefaultKomora3FoiConfig() => new()
    {
        Name = "Komora 3 - FOI",
        Kind = ChamberKind.TemperatureHumidity,
        Protocol = ChamberProtocol.VotschAscii2,
        Host = "10.88.5.233",
        Port = 2049,
        StartChannelIndex = 1,
    };

    /// <summary>
    /// Fixed "na tvrdo" display order of the known lab devices. Configs whose name
    /// starts with one of these prefixes are ordered accordingly; any other device
    /// keeps its relative position at the end.
    /// </summary>
    private static readonly string[] ForcedChamberOrder =
    {
        "Komora 1", "Komora 2", "Komora 3", "Sušiareň", "SIKA Sylex", "SIKA PolyTech",
    };

    private static int ForcedOrderRank(ChamberConfig c)
    {
        for (int i = 0; i < ForcedChamberOrder.Length; i++)
        {
            if (c.Name.StartsWith(ForcedChamberOrder[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return ForcedChamberOrder.Length;
    }

    /// <summary>
    /// Reorders <paramref name="configs"/> in place to <see cref="ForcedChamberOrder"/>.
    /// Stable within a rank (LINQ OrderBy), so unknown devices keep their order.
    /// Returns <c>true</c> if the order actually changed (so the caller re-saves).
    /// </summary>
    private static bool ApplyForcedOrder(List<ChamberConfig> configs)
    {
        List<ChamberConfig> ordered = configs.OrderBy(ForcedOrderRank).ToList();
        if (ordered.SequenceEqual(configs))
        {
            return false;
        }

        configs.Clear();
        configs.AddRange(ordered);
        return true;
    }

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
        bool sika = NewChamberProtocol == ChamberProtocol.SikaRestApi;
        var config = new ChamberConfig
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(NewChamberName) ? $"Komora {Chambers.Count + 1}" : NewChamberName.Trim(),
            // POL-EKO ovens and SIKA baths are temperature-only.
            Kind = (polEko || sika) ? ChamberKind.TemperatureOnly : NewChamberKind,
            Protocol = NewChamberProtocol,
            // POL-EKO speaks MODBUS TCP on port 502, SIKA's REST-API is fixed on port 8081.
            Port = polEko ? 502 : sika ? SikaRestApiProtocol.DefaultPort : 1080,
            Host = string.IsNullOrWhiteSpace(NewChamberHost) ? "192.168.0.1" : NewChamberHost.Trim(),
            // Vötsch start channel is digital channel 1 (verified running bit);
            // POL-EKO (MODBUS) and SIKA (REST-API) do not use this field.
            StartChannelIndex = (polEko || sika) ? 0 : 1,
            // Allowed temperature range: POL-EKO ovens up to +300 °C, SIKA TP baths
            // -50…+165 °C; Vötsch keeps the ChamberConfig default (editable per device).
            TempMin = sika ? -50 : polEko ? 0 : -45,
            TempMax = sika ? 165 : polEko ? 300 : 190,
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
