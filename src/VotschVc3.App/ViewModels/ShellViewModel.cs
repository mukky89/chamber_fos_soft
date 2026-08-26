using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Diagnostics;
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
        nameof(ChamberViewModel.AutoRecoverProfile),
        nameof(ChamberViewModel.QuickPresets), nameof(ChamberViewModel.QuickProfiles),
        nameof(ChamberViewModel.IsLocked), nameof(ChamberViewModel.LockPasswordHash),
    }.Concat(ChamberViewModel.NameplatePropertyNames).ToHashSet();

    private readonly ProfileStore _store;
    private readonly EmailSettingsStore _emailStore;
    private readonly ChamberConfigStore _configStore;
    private readonly ProfileRunCheckpointStore _checkpointStore;
    private readonly UserStore _userStore;
    private readonly AuditLog _audit;
    private readonly LoginViewModel _login;
    private readonly EmailNotifier _notifier = new();
    private readonly UiSettingsStore _uiStore;
    private readonly UiSettings _ui;
    private CancellationTokenSource? _saveCts;
    private readonly System.Windows.Threading.DispatcherTimer _bridgeStatusTimer;

    public ShellViewModel()
    {
        // All app data lives under Documents\Lab Control; profiles in their own
        // folder, settings + markers in the root. Initialize() also migrates the
        // old VotschVc3 layout once (idempotent, so calling it here is safe even
        // if App.OnStartup already ran it).
        AppPaths.Initialize();
        string dir = AppPaths.SettingsDir;
        _store = new ProfileStore(System.IO.Path.Combine(AppPaths.ProfilesDir, "profiles.json"));
        _emailStore = new EmailSettingsStore(System.IO.Path.Combine(dir, "email.json"));
        _configStore = new ChamberConfigStore(System.IO.Path.Combine(dir, "chambers.json"));
        _checkpointStore = new ProfileRunCheckpointStore(AppPaths.ProfileRecoveryDir);
        _userStore = new UserStore(System.IO.Path.Combine(dir, "users.json"));
        _audit = new AuditLog(System.IO.Path.Combine(dir, "audit.csv"));
        _uiStore = new UiSettingsStore(System.IO.Path.Combine(dir, "ui.json"));
        _ui = _uiStore.Load();
        ChamberViewModel.ProfileLogIntervalSeconds = _ui.ProfileLogIntervalSeconds;
        ChamberViewModel.SikaSoakToleranceC = _ui.SikaSoakToleranceC;
        _notifier.Settings = _emailStore.Load();

        Audit = new AuditViewModel(_audit);
        ProfileLibrary = new ProfileLibraryViewModel(_store);
        _login = new LoginViewModel(_userStore, OnLoggedIn);

        Thermometers = new ThermometersViewModel();
        Admin = new AdminViewModel(this);
        QuickProfile = new QuickProfileViewModel(_store);
        // "Editovať profil" in the quick builder saves the profile and jumps to the editor.
        Chambers = new ObservableCollection<ChamberViewModel>();

        // Commands must exist before chambers are built (AddChamberInternal uses them).
        OpenChamberCommand = new RelayCommand<ChamberViewModel>(OpenChamber, c => c is not null);
        OpenThermometersCommand = new RelayCommand(() => CurrentView = Thermometers);
        OpenRecordingViewerCommand = new RelayCommand(() =>
        {
            // Always show logs from profiles that finished since the viewer was last opened.
            RecordingViewer.RefreshRecentLogsCommand.Execute(null);
            CurrentView = RecordingViewer;
        });
        OpenProfileLibraryCommand = new RelayCommand(() =>
        {
            // Always show the latest saved profiles when entering the editor.
            ProfileLibrary.RefreshFromStore();
            CurrentView = ProfileLibrary;
        });
        OpenQuickProfileCommand = new RelayCommand(() =>
        {
            // Always show the latest saved profiles when entering the panel.
            QuickProfile.RefreshLibraryProfiles();

            // Start from the default profile every time the screen is opened – the builder
            // used to come back holding whatever was left half-edited, which then quietly
            // saved over the profile that had been loaded before.
            QuickProfile.StartNewProfile();
            CurrentView = QuickProfile;
        });
        OpenAuditCommand = new RelayCommand(() => CurrentView = Audit);
        OpenAppLogCommand = new RelayCommand(() => CurrentView = AppLog);
        OpenChangelogCommand = new RelayCommand(() => CurrentView = Changelog);
        OpenAdminCommand = new RelayCommand(() => CurrentView = Admin, () => CanManage);
        OpenDataFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.Root));
        OpenProfilesFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.ProfilesDir));
        OpenProfileLogFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.ProfileLogDir));
        OpenAppLogFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.AppLogDir));
        RefreshBridgeStatusCommand = new RelayCommand(RefreshBridgeStatus);
        StartBridgeCommand = new RelayCommand(StartBridge);
        OpenBridgeFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.Root));
        GoHomeCommand = new RelayCommand(GoHome);
        LogoutCommand = new RelayCommand(Logout);
        ToggleTimelineCommand = new RelayCommand(() => ShowTimeline = !ShowTimeline);
        ToggleProfessionalSidebarCommand = new RelayCommand(() => ProfessionalSidebarCollapsed = !ProfessionalSidebarCollapsed);
        AddChamberCommand = new RelayCommand(AddChamber, () => CanManage);
        RemoveChamberCommand = new RelayCommand<ChamberViewModel>(RemoveChamber, c => c is not null && Chambers.Count > 1 && CanManage);
        MoveChamberUpCommand = new RelayCommand<ChamberViewModel>(c => MoveChamber(c, -1), c => c is not null);
        MoveChamberDownCommand = new RelayCommand<ChamberViewModel>(c => MoveChamber(c, +1), c => c is not null);
        SaveEmailSettingsCommand = new RelayCommand(SaveEmailSettings);
        LogEmailDiagnosticsCommand = new RelayCommand(LogEmailDiagnostics);
        TestEmailCommand = new AsyncRelayCommand(TestEmailAsync, onError: ex => EmailStatus = $"Chyba: {ex.Message}");
        AddUserCommand = new RelayCommand(AddUser,
            () => CanManage && !string.IsNullOrWhiteSpace(NewUserName) && !string.IsNullOrEmpty(NewUserPassword));
        DeleteUserCommand = new RelayCommand<User>(DeleteUser, u => CanManage && u is not null);
        SaveUsersCommand = new RelayCommand(SaveUsers, () => CanManage);
        RefreshUsers();

        _bridgeStatusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _bridgeStatusTimer.Tick += (_, _) => RefreshBridgeStatus();
        _bridgeStatusTimer.Start();
        RefreshBridgeStatus();
        EnsureBridgeStarted();

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
        // re-add exactly the two canonical ones – "SIKA Sylex" (10.88.5.226) and
        // "SIKA PolyTech" (10.88.6.28) – with the correct nameplate and range.
        // Guarded by a marker so a later manual rename / IP edit is respected.
        // Marker bumped to v3 so the new Sylex default IP (10.88.5.226) reaches
        // installs that already ran the v2 reset.
        string sikaResetMarker = System.IO.Path.Combine(dir, ".chambers_sika_reset_v3");
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

        // Keep the visible (dashboard/timeline) list in sync with Chambers and the
        // POL-EKO visibility setting: any add / remove / reorder rebuilds it.
        Chambers.CollectionChanged += (_, _) => RebuildVisibleChambers();
        RebuildVisibleChambers();

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

        // Bundled default profiles: import once on first run (marker-guarded).
        SeedDefaultProfiles(dir);

        // Start at the login screen.
        _currentView = _login;
    }

    /// <summary>Metadata for one bundled seed profile (from <c>seed-profiles-manifest.json</c>).</summary>
    private sealed class SeedEntry
    {
        public string File { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
        public string Customer { get; set; } = string.Empty;
        public string Project { get; set; } = string.Empty;
        public List<string> Sensors { get; set; } = new();
        public List<string> Tags { get; set; } = new();
    }

    /// <summary>
    /// Imports the profiles bundled with the build the first time this build version
    /// runs. The raw Weiss/Vötsch BEdit files live in an embedded ZIP and are parsed by
    /// the same importer the app uses interactively; a manifest supplies the corrected
    /// name, the original name (kept for backward compatibility), sensors and tags.
    /// The chamber type is detected from the parsed content (humidity channel present →
    /// temperature+humidity, otherwise temperature-only). Guarded by a versioned marker
    /// so a user's later edits / deletions are respected and nothing is duplicated;
    /// bump the marker version when the bundled set changes.
    /// </summary>
    private void SeedDefaultProfiles(string dir)
    {
        string marker = System.IO.Path.Combine(dir, ".seed_profiles_v5");
        if (System.IO.File.Exists(marker))
        {
            return;
        }

        try
        {
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();

            Dictionary<string, SeedEntry> manifest = new(StringComparer.OrdinalIgnoreCase);
            using (System.IO.Stream? ms = assembly.GetManifestResourceStream("seed-profiles-manifest.json"))
            {
                if (ms is not null)
                {
                    using var reader = new System.IO.StreamReader(ms);
                    var entries = System.Text.Json.JsonSerializer.Deserialize<List<SeedEntry>>(
                        reader.ReadToEnd(),
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    foreach (SeedEntry e in entries ?? new List<SeedEntry>())
                    {
                        manifest[e.File] = e;
                    }
                }
            }

            using System.IO.Stream? zip = assembly.GetManifestResourceStream("seed-profiles.zip");
            if (zip is null)
            {
                return;
            }

            using var archive = new System.IO.Compression.ZipArchive(zip, System.IO.Compression.ZipArchiveMode.Read);
            var seeded = new List<TestProfile>();
            var seededNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (System.IO.Compression.ZipArchiveEntry entry in archive.Entries)
            {
                try
                {
                    using System.IO.Stream es = entry.Open();
                    using var mem = new System.IO.MemoryStream();
                    es.CopyTo(mem);
                    byte[] bytes = mem.ToArray();

                    // Parse with the tested importer, keeping humidity so we can detect the kind.
                    ProfileImportResult result = BEditImporter.Import(bytes, ChamberKind.TemperatureHumidity);
                    TestProfile profile = result.Profile;
                    if (profile.Segments.Count == 0)
                    {
                        continue;
                    }

                    bool hasHumidity = profile.Segments.Any(s => s.TargetHumidity is not null);
                    profile.Kind = hasHumidity ? ChamberKind.TemperatureHumidity : ChamberKind.TemperatureOnly;
                    if (!hasHumidity)
                    {
                        foreach (ProfileSegment s in profile.Segments)
                        {
                            s.TargetHumidity = null;
                        }
                    }

                    manifest.TryGetValue(entry.FullName, out SeedEntry? meta);
                    profile.OriginalName = meta?.OriginalName ?? entry.FullName;
                    profile.Name = string.IsNullOrWhiteSpace(meta?.Name) ? entry.FullName : meta!.Name;
                    profile.Customer = meta?.Customer ?? string.Empty;
                    profile.Project = meta?.Project ?? string.Empty;
                    profile.Sensors = meta?.Sensors is { Count: > 0 } ? new List<string>(meta.Sensors) : new List<string> { "Ostatné" };
                    profile.Tags = meta?.Tags is not null ? new List<string>(meta.Tags) : new List<string>();

                    // Accurate temperature-range tag from the real segments.
                    double min = profile.Segments.Min(s => s.TargetTemperature);
                    double max = profile.Segments.Max(s => s.TargetTemperature);
                    string range = $"{min:0.#}…{max:0.#} °C";
                    if (!profile.Tags.Contains(range))
                    {
                        profile.Tags.Add(range);
                    }

                    profile.Id = StableGuid(profile.Name);

                    if (seededNames.Add(profile.Name.Trim()))
                    {
                        seeded.Add(profile);
                    }
                }
                catch
                {
                    // A single unparseable file must not abort the whole seed.
                }
            }

            // One bulk write; existing profiles (by id or name) are left untouched.
            _store.AddMissing(seeded);

            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(marker, DateTimeOffset.Now.ToString("o"));
        }
        catch
        {
            // Seeding must never crash startup; a missing marker just retries next launch.
        }
    }

    /// <summary>Deterministic GUID from a string, so a bundled profile keeps the same id across builds.</summary>
    private static Guid StableGuid(string text)
    {
        byte[] hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("votsch-seed:" + text));
        return new Guid(hash);
    }

    public ObservableCollection<ChamberViewModel> Chambers { get; }

    /// <summary>
    /// The chambers actually shown on the dashboard and the timeline: every device
    /// except the POL-EKO oven while it is hidden (see <see cref="ShowPolEko"/>).
    /// Rebuilt from <see cref="Chambers"/> so it always reflects the current order.
    /// </summary>
    public ObservableCollection<ChamberViewModel> VisibleChambers { get; } = new();

    private void RebuildVisibleChambers()
    {
        VisibleChambers.Clear();
        foreach (ChamberViewModel chamber in Chambers)
        {
            if (_ui.ShowPolEko || !chamber.IsPolEko)
            {
                VisibleChambers.Add(chamber);
            }
        }
    }

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
                OnPropertyChanged(nameof(DetailView));
            }
        }
    }

    public bool IsHome => ReferenceEquals(CurrentView, this);

    /// <summary>
    /// View shown above the permanently retained home dashboard. Returning null on
    /// the home page prevents the shell itself from being rendered recursively.
    /// </summary>
    public object? DetailView => IsHome ? null : CurrentView;

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

    /// <summary>Opens the root data folder (Documents\Lab Control) in the file explorer.</summary>
    public RelayCommand OpenDataFolderCommand { get; }

    /// <summary>Opens the profiles folder (Documents\Lab Control\Profiles).</summary>
    public RelayCommand OpenProfilesFolderCommand { get; }

    /// <summary>Opens the profile temperature-log folder (Documents\Lab Control\Profilelog).</summary>
    public RelayCommand OpenProfileLogFolderCommand { get; }

    /// <summary>Opens the application-log folder (Documents\Lab Control\App log).</summary>
    public RelayCommand OpenAppLogFolderCommand { get; }
    public RelayCommand RefreshBridgeStatusCommand { get; }
    public RelayCommand StartBridgeCommand { get; }
    public RelayCommand OpenBridgeFolderCommand { get; }

    /// <summary>Creates the folder if needed and opens it in the OS file explorer.</summary>
    private static void OpenFolder(string path)
    {
        try
        {
            System.IO.Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            VotschVc3.Core.Diagnostics.AppLog.Warn("UI", $"Priečinok sa nepodarilo otvoriť ({path}): {ex.Message}");
        }
    }
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
            OnPropertyChanged(nameof(EffectiveCompactMode));
        }
    }

    /// <summary>
    /// Admin toggle (persisted): which dashboard layout operators see —
    /// Administrácia → Vzhľad a ovládanie → Režim ovládania. Defaults to
    /// <see cref="UiControlMode.Classic"/> (the original layout), so existing
    /// installs are never switched to the new one without an admin opting in.
    /// </summary>
    public UiControlMode ControlMode
    {
        get => _ui.ControlMode;
        set
        {
            if (_ui.ControlMode == value)
            {
                return;
            }

            _ui.ControlMode = value;
            SaveUiSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsProfessionalMode));
            OnPropertyChanged(nameof(EffectiveCompactMode));
        }
    }

    /// <summary>True while the Professional dashboard is the active control mode.</summary>
    public bool IsProfessionalMode => ControlMode == UiControlMode.Professional;

    /// <summary>
    /// The compact card scale should apply either because the admin picked the
    /// dedicated "Kompaktný" control mode, or because the separate legacy
    /// <see cref="CompactMode"/> checkbox is on — the two are independent knobs
    /// that both shrink the same Classic card layout.
    /// </summary>
    public bool EffectiveCompactMode => CompactMode || ControlMode == UiControlMode.Compact;

    /// <summary>
    /// Admin toggle (persisted): the Professional dashboard asks for
    /// confirmation before stopping a device or a running profile. Only
    /// affects the Professional layout — the Classic Stop buttons are
    /// unchanged so existing operators keep their familiar one-click Stop.
    /// </summary>
    public bool ConfirmStopAction
    {
        get => _ui.ConfirmStopAction;
        set
        {
            if (_ui.ConfirmStopAction == value)
            {
                return;
            }

            _ui.ConfirmStopAction = value;
            SaveUiSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Admin toggle (persisted): the Professional dashboard asks for
    /// confirmation (range, duration, cycles) before starting a profile.
    /// Only affects the Professional layout.
    /// </summary>
    public bool ConfirmProfileStart
    {
        get => _ui.ConfirmProfileStart;
        set
        {
            if (_ui.ConfirmProfileStart == value)
            {
                return;
            }

            _ui.ConfirmProfileStart = value;
            SaveUiSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Admin toggle (persisted): show the alarm center panel on the
    /// Professional dashboard.
    /// </summary>
    public bool ShowAlarmCenter
    {
        get => _ui.ShowAlarmCenter;
        set
        {
            if (_ui.ShowAlarmCenter == value)
            {
                return;
            }

            _ui.ShowAlarmCenter = value;
            SaveUiSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Persisted: whether the Professional dashboard's sidebar is collapsed to
    /// icons only. Off by default (full labels shown).
    /// </summary>
    public bool ProfessionalSidebarCollapsed
    {
        get => _ui.ProfessionalSidebarCollapsed;
        set
        {
            if (_ui.ProfessionalSidebarCollapsed == value)
            {
                return;
            }

            _ui.ProfessionalSidebarCollapsed = value;
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

    /// <summary>
    /// Persisted setting: how often (seconds) a row is written to the per-profile
    /// temperature log during a run. Default 30 s. Applies to all chambers and takes
    /// effect immediately, even during a running profile. Clamped to 1…3600 s.
    /// </summary>
    public double ProfileLogIntervalSeconds
    {
        get => _ui.ProfileLogIntervalSeconds;
        set
        {
            int seconds = Math.Clamp((int)Math.Round(value), 1, 3600);
            if (_ui.ProfileLogIntervalSeconds == seconds)
            {
                return;
            }

            _ui.ProfileLogIntervalSeconds = seconds;
            ChamberViewModel.ProfileLogIntervalSeconds = seconds;
            SaveUiSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Persisted setting: tolerance (°C) for the guaranteed soak on SIKA thermal
    /// baths. On every hold the bath first reaches the target within this band before
    /// the dwell time starts. Small by default (0.3 °C). Clamped to 0.1…10 °C.
    /// </summary>
    public double SikaSoakToleranceC
    {
        get => _ui.SikaSoakToleranceC;
        set
        {
            double tolerance = Math.Clamp(Math.Round(value, 2), 0.1, 10.0);
            if (Math.Abs(_ui.SikaSoakToleranceC - tolerance) < 0.0001)
            {
                return;
            }

            _ui.SikaSoakToleranceC = tolerance;
            ChamberViewModel.SikaSoakToleranceC = tolerance;
            SaveUiSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Admin toggle (persisted): whether the POL-EKO drying oven (Sušiareň) appears
    /// on the dashboard / timeline and is connected automatically. Off by default –
    /// the lab does not normally use it. Turning it on brings the oven online.
    /// </summary>
    public bool ShowPolEko
    {
        get => _ui.ShowPolEko;
        set
        {
            if (_ui.ShowPolEko == value)
            {
                return;
            }

            _ui.ShowPolEko = value;
            SaveUiSettings();
            RebuildVisibleChambers();
            OnPropertyChanged();

            if (value)
            {
                // Bring the now-visible POL-EKO oven(s) online.
                _ = Task.WhenAll(Chambers.Where(c => c.IsPolEko).Select(c => c.ConnectIfPossibleAsync()));
            }
        }
    }

    /// <summary>Caption for the show/hide-timeline toggle button.</summary>
    public string TimelineToggleText => ShowTimeline ? "▾ Skryť" : "▸ Zobraziť";

    /// <summary>Shows / hides the dashboard timeline.</summary>
    public RelayCommand ToggleTimelineCommand { get; }

    /// <summary>Collapses / expands the Professional dashboard's sidebar.</summary>
    public RelayCommand ToggleProfessionalSidebarCommand { get; }

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
        Task.WhenAll(Chambers
            .Where(c => _ui.ShowPolEko || !c.IsPolEko) // skip the hidden POL-EKO oven
            .Select(c => c.ConnectIfPossibleAsync()));

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

        // Admin-only library management (delete-all), gated by the admin's own password.
        ProfileLibrary.IsAdmin = CanManage;
        ProfileLibrary.VerifyAdminPassword = pwd =>
            _currentUser is { Role: UserRole.Admin } admin && admin.VerifyPassword(pwd);

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

    #region FOS Dashboard bridge

    private string _bridgeStatusTitle = "⚪ Bridge: zisťujem stav…";
    public string BridgeStatusTitle { get => _bridgeStatusTitle; private set => SetProperty(ref _bridgeStatusTitle, value); }

    private string _bridgeStatusDetail = string.Empty;
    public string BridgeStatusDetail { get => _bridgeStatusDetail; private set => SetProperty(ref _bridgeStatusDetail, value); }

    private string _bridgeConfigurationStatus = string.Empty;
    public string BridgeConfigurationStatus { get => _bridgeConfigurationStatus; private set => SetProperty(ref _bridgeConfigurationStatus, value); }

    public string BridgeConfigPath => System.IO.Path.Combine(AppPaths.Root, "bridge.json");
    public string BridgeStatusPath => System.IO.Path.Combine(AppPaths.Root, "bridge-status.json");

    private void RefreshBridgeStatus()
    {
        bool processRunning = Process.GetProcessesByName("VotschVc3.Agent").Length > 0;
        BridgeStatus? status = BridgeStatusFile.Read(BridgeStatusPath);
        TimeSpan age = status is null ? TimeSpan.MaxValue : DateTime.UtcNow - status.UpdatedUtc;
        bool fresh = age < TimeSpan.FromSeconds(30);

        if (status is { Running: true, DashboardReachable: true } && fresh)
        {
            BridgeStatusTitle = "🟢 FOS Dashboard Bridge je online";
            BridgeStatusDetail = $"{status.DashboardUrl} · PC {status.MachineName} · heartbeat {status.UpdatedUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss} · agent v{status.Version}";
        }
        else if (status is { Running: true } && fresh)
        {
            BridgeStatusTitle = "🟠 Bridge beží, ale web nie je dostupný";
            BridgeStatusDetail = string.IsNullOrWhiteSpace(status.LastError)
                ? "Agent sa pripája k FOS Dashboardu."
                : status.LastError;
        }
        else if (processRunning)
        {
            BridgeStatusTitle = "🟠 Proces Bridge beží, heartbeat je neaktuálny";
            BridgeStatusDetail = status?.LastError ?? "Čakám na prvý heartbeat agenta.";
        }
        else
        {
            BridgeStatusTitle = "🔴 FOS Dashboard Bridge nie je spustený";
            BridgeStatusDetail = status is null
                ? "Desktopová aplikácia beží, ale samostatný Bridge Agent ešte nebol spustený."
                : $"Posledný stav {status.UpdatedUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}: {status.LastError}";
        }

        BridgeConfigurationStatus = ReadBridgeConfigurationStatus();
    }

    private string ReadBridgeConfigurationStatus()
    {
        if (!System.IO.File.Exists(BridgeConfigPath))
        {
            return $"Konfigurácia chýba: {BridgeConfigPath}";
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(System.IO.File.ReadAllText(BridgeConfigPath));
            JsonElement root = document.RootElement;
            string url = root.TryGetProperty("dashboardUrl", out JsonElement urlNode) ? urlNode.GetString() ?? "" : "";
            string key = root.TryGetProperty("agentKey", out JsonElement keyNode) ? keyNode.GetString() ?? "" : "";
            bool validUrl = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps && !url.Contains("YOUR-DASHBOARD", StringComparison.OrdinalIgnoreCase);
            bool validKey = key.StartsWith("lab_", StringComparison.Ordinal) && key.Length >= 44;
            return validUrl && validKey
                ? $"Konfigurácia pripravená · {url} · párovací token nastavený"
                : "Konfigurácia nie je dokončená – nastav dashboardUrl a párovací agentKey (lab_…).";
        }
        catch (Exception ex) when (ex is System.IO.IOException or JsonException or UnauthorizedAccessException)
        {
            return $"Konfiguráciu sa nepodarilo načítať: {ex.Message}";
        }
    }

    private void StartBridge()
    {
        EnsureBridgeStarted(showAlreadyRunningStatus: true);
    }

    private void EnsureBridgeStarted(bool showAlreadyRunningStatus = false)
    {
        try
        {
            EnsureBridgeConfigurationExists();
        }
        catch (Exception ex)
        {
            BridgeStatusTitle = "🔴 Konfiguráciu Bridge sa nepodarilo vytvoriť";
            BridgeStatusDetail = ex.Message;
            return;
        }

        if (Process.GetProcessesByName("VotschVc3.Agent").Length > 0)
        {
            if (showAlreadyRunningStatus)
            {
                BridgeStatusTitle = "🟢 FOS Dashboard Bridge už beží";
                BridgeStatusDetail = "Nie je potrebné spúšťať ďalší proces.";
            }
            return;
        }

        try
        {
            using Process? task = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/Run /TN \"Sylex Lab Control Bridge\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            task?.WaitForExit(3000);
            if (task is not null && task.HasExited && task.ExitCode == 0)
            {
                // schtasks reports that the task was accepted, not that its target
                // executable really stayed alive. Briefly verify the actual agent.
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    if (Process.GetProcessesByName("VotschVc3.Agent").Length > 0)
                    {
                        BridgeStatusTitle = "🟠 Bridge sa automaticky spúšťa…";
                        BridgeStatusDetail = "Stav sa automaticky obnoví do niekoľkých sekúnd.";
                        return;
                    }
                    System.Threading.Thread.Sleep(250);
                }
            }

            string? agentPath = FindBridgeAgentExecutable();
            if (agentPath is null)
            {
                throw new System.IO.FileNotFoundException(
                    "Naplánovaná úloha nie je nainštalovaná a VotschVc3.Agent.exe sa nenašiel.");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = agentPath,
                WorkingDirectory = System.IO.Path.GetDirectoryName(agentPath)!,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            BridgeStatusTitle = "🟠 Bridge sa automaticky spúšťa…";
            BridgeStatusDetail = $"Agent spustený priamo z {agentPath}.";
        }
        catch (Exception ex)
        {
            BridgeStatusTitle = "🔴 Bridge sa nepodarilo spustiť";
            BridgeStatusDetail = ex.Message;
        }
    }

    private void EnsureBridgeConfigurationExists()
    {
        if (System.IO.File.Exists(BridgeConfigPath))
        {
            return;
        }

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(BridgeConfigPath)!);
        using System.IO.Stream? source = typeof(ShellViewModel).Assembly.GetManifestResourceStream("bridge.example.json");
        if (source is null)
        {
            throw new InvalidOperationException("V aplikácii chýba zabudovaný vzor bridge.example.json.");
        }

        string temporaryPath = BridgeConfigPath + ".tmp";
        try
        {
            using (var destination = new System.IO.FileStream(
                temporaryPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
            {
                source.CopyTo(destination);
                destination.Flush(flushToDisk: true);
            }
            System.IO.File.Move(temporaryPath, BridgeConfigPath, overwrite: false);
            VotschVc3.Core.Diagnostics.AppLog.Info(
                "Bridge", $"Vytvorená predvolená konfigurácia: {BridgeConfigPath}");
        }
        finally
        {
            if (System.IO.File.Exists(temporaryPath))
            {
                System.IO.File.Delete(temporaryPath);
            }
        }
    }

    private static string? FindBridgeAgentExecutable()
    {
        string baseDir = AppContext.BaseDirectory;
        string configuration = baseDir.Contains($"{System.IO.Path.DirectorySeparatorChar}Debug{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Debug"
            : "Release";
        string[] candidates =
        {
            System.IO.Path.Combine(baseDir, "LabBridge", "VotschVc3.Agent.exe"),
            System.IO.Path.Combine(baseDir, "VotschVc3.Agent.exe"),
            System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "VotschVc3.Agent", "bin", configuration, "net8.0-windows", "VotschVc3.Agent.exe")),
            System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "VotschVc3.Agent", "bin", "Debug", "net8.0-windows", "VotschVc3.Agent.exe")),
            System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "VotschVc3.Agent", "bin", "Release", "net8.0-windows", "VotschVc3.Agent.exe")),
        };
        return candidates.FirstOrDefault(System.IO.File.Exists);
    }

    #endregion

    #region E-mail notifications

    /// <summary>Live e-mail settings, bound directly by the home page.</summary>
    public EmailSettings Email => _notifier.Settings;

    /// <summary>Delivery method choices for the combo box.</summary>
    public Array EmailMethods { get; } = Enum.GetValues(typeof(EmailMethod));

    private string _emailStatus = "Po dokončení odošle HTML súhrn, graf teploty a CSV log (voliteľné).";
    public string EmailStatus { get => _emailStatus; private set => SetProperty(ref _emailStatus, value); }

    /// <summary>
    /// What still has to be filled in before notifications can be sent, spelled out on the
    /// panel – „Poslať test“ only reported it after failing, and only for the first
    /// missing field. Only the fields the chosen delivery method actually uses count: in
    /// BrevoApi mode the SMTP user and password are irrelevant.
    /// </summary>
    public string EmailReadinessText
    {
        get
        {
            string missing = Email.DescribeMissing();
            if (missing.Length > 0)
            {
                return $"⚠ Chýba: {missing}. Bez toho sa notifikácie neodošlú.";
            }

            // Say which values are coming from the environment – otherwise an empty box on
            // the panel reads as "not configured" even though sending works.
            string environment = Email.DescribeEnvironmentSources();
            string source = environment.Length > 0 ? $" · z premenných prostredia: {environment}" : string.Empty;
            return (Email.Enabled
                ? "✔ Nastavené – notifikácie sa odošlú po dokončení profilu."
                : "✔ Nastavené, ale prepínač notifikácií je vypnutý.") + source;
        }
    }

    /// <summary>Where the values may come from, so no secret has to live in the settings file.</summary>
    public string EmailApiKeyHint =>
        "Ktorékoľvek pole sa dá nechať prázdne a nastaviť do systémovej premennej " +
        $"({string.Join(", ", EmailEnvironment.All)}). Premenné prežijú preinštalovanie aj nový build " +
        "a zdieľajú sa s FOS Dashboardom; vyplnené pole má vždy prednosť. Po zmene premennej treba appku reštartovať.";

    public RelayCommand SaveEmailSettingsCommand { get; }
    public AsyncRelayCommand TestEmailCommand { get; }

    /// <summary>Writes the effective e-mail configuration into the application log.</summary>
    public RelayCommand LogEmailDiagnosticsCommand { get; }

    private void LogEmailDiagnostics()
    {
        string description = _notifier.Describe();
        AppLog.Info(EmailNotifier.LogSource, $"Diagnostika nastavení: {description}");
        OnPropertyChanged(nameof(EmailReadinessText));
        EmailStatus = $"Zapísané do App logu · {description}";
    }

    private void SaveEmailSettings()
    {
        try
        {
            _emailStore.Save(_notifier.Settings);
            OnPropertyChanged(nameof(EmailReadinessText));
            string missing = Email.DescribeMissing();
            EmailStatus = missing.Length > 0
                ? $"Nastavenia uložené, ale ešte chýba: {missing}."
                : "Nastavenia e-mailu uložené.";
        }
        catch (Exception ex)
        {
            EmailStatus = $"Uloženie zlyhalo: {ex.Message}";
        }
    }

    private async Task TestEmailAsync()
    {
        OnPropertyChanged(nameof(EmailReadinessText));
        EmailStatus = "Posielam testovací e-mail…";
        EmailResult result = await _notifier.SendTestAsync();
        EmailStatus = result switch
        {
            // The transport detail (status code, message id) is what makes a send that
            // "went through but did not arrive" traceable in the Brevo logs.
            { Sent: true, Detail: { Length: > 0 } detail } =>
                $"✔ Odoslané na {Email.Recipient} · {detail}",
            { Sent: true } => $"✔ Testovací e-mail odoslaný na {Email.Recipient}.",
            { Skipped: true, Error: { } why } => $"Neodoslané: {why}. Podrobnosti v App logu.",
            { Error: { } err } => $"✖ Test zlyhal: {err}",
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

    /// <summary>
    /// Builds a SIKA TP Premium bath config (REST-API, temperature only). Port and
    /// temperature range default to the TP Premium values (8081, -50…165 °C) but can
    /// be overridden per device – e.g. the TP37200E.2 answers on port 80 with a wider
    /// -60…+200 °C range.
    /// </summary>
    private static ChamberConfig SikaBathConfig(
        string name,
        string host,
        ChamberNameplate nameplate,
        int? port = null,
        double tempMin = -50,
        double tempMax = 165) => new()
    {
        Name = name,
        Kind = ChamberKind.TemperatureOnly,
        Protocol = ChamberProtocol.SikaRestApi,
        Host = host,
        Port = port ?? SikaRestApiProtocol.DefaultPort,
        StartChannelIndex = 0,
        TempMin = tempMin,
        TempMax = tempMax,
        Nameplate = nameplate,
    };

    /// <summary>
    /// SIKA Sylex – nameplate + range from the device itself (TP3M165E.2, s/n 2219005).
    /// Operating range -50…+165 °C is the device's absolute limit (getShells:
    /// AbsolutMin/AbsolutMax; getGradientInfo MaxTemp). Ordinary device: IP can be
    /// changed / removed.
    /// </summary>
    private static ChamberConfig SikaSylexConfig() => SikaBathConfig("SIKA Sylex", "10.88.5.226", new ChamberNameplate
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
              + "REST-API ovládanie overené: setpoint cez setRegister, START/STOP cez "
              + "startCurrentTask/stopCurrentTask + System_ReglerOnOff.",
    }, tempMin: -50, tempMax: 165);

    /// <summary>
    /// SIKA PolyTech – nameplate + range from the device's own getInfoReport
    /// (TP37200E.2, s/n 1712380). Answers the REST-API on port 80 and exposes the
    /// combined getGradientInfo status snapshot.
    /// </summary>
    private static ChamberConfig SikaPolytechConfig() => SikaBathConfig("SIKA PolyTech", "10.88.6.28", new ChamberNameplate
    {
        Manufacturer = "SIKA",
        Model = "TP37200E.2",
        SerialNumber = "1712380",
        OrderNumber = "SIKA",
        YearOfConstruction = "2017",
        SystemNumber = "000575", // HardwareSerial
        Notes = "SIKA TP Premium · Device TP37200E.2 · Firmware V 1.14 · ARM Rev. 1 · rozsah -60…+200 °C. "
              + "REST-API na porte 80, čítanie cez getGradientInfo, "
              + "setpoint cez setRegister, START/STOP cez startCurrentTask/stopCurrentTask + System_ReglerOnOff.",
    }, port: 80, tempMin: -60, tempMax: 200);

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
        var chamber = new ChamberViewModel(config, _store, _notifier, Thermometers, _audit, _checkpointStore);
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
        _bridgeStatusTimer.Stop();
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
