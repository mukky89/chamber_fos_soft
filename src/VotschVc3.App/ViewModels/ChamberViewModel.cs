using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using VotschVc3.App.Charting;
using VotschVc3.App.Mvvm;
using VotschVc3.App.Notifications;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Communication.PolEko;
using VotschVc3.Core.Communication.Sika;
using VotschVc3.Core.Diagnostics;
using VotschVc3.Core.Notifications;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;
using VotschVc3.Core.Recording;
using VotschVc3.Core.Security;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// View model for a single chamber. Each instance owns its own connection,
/// polling loop and profile runner, so two chambers can be operated
/// simultaneously and independently.
/// </summary>
public sealed class ChamberViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxTerminalLines = 1000;

    private readonly IChamberDevice _client;
    private readonly ProfileStore _store;
    private readonly ProfileRunStateStore _runStateStore;
    private readonly EmailNotifier _email;
    private readonly ThermometersViewModel _thermometers;
    private readonly AuditLog _audit;
    private CancellationTokenSource? _pollingCts;
    private CancellationTokenSource? _profileCts;
    private bool _powerOffOnProfileCancel;
    private CsvRecorder? _recorder;
    private ProfileTemperatureLog? _profileTempLog;
    private DateTime? _profileActualStart;
    private DateTime? _profileEstimatedEnd;
    private System.Windows.Threading.DispatcherTimer? _countdownTimer;
    private ProfileRunner? _activeRunner;

    public ChamberViewModel(ChamberConfig config, ProfileStore store, ProfileRunStateStore runStateStore, EmailNotifier email, ThermometersViewModel thermometers, AuditLog audit)
    {
        ArgumentNullException.ThrowIfNull(config);
        Id = config.Id;
        Name = config.Name;
        Kind = config.Kind;
        Protocol = config.Protocol;
        _host = config.Host;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _runStateStore = runStateStore ?? throw new ArgumentNullException(nameof(runStateStore));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _thermometers = thermometers ?? throw new ArgumentNullException(nameof(thermometers));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));

        _client = Protocol switch
        {
            ChamberProtocol.PolEkoModbus => new PolEkoClient(),
            ChamberProtocol.SikaRestApi => new SikaTpClient(),
            _ => new ChamberClient(),
        };
        _client.FrameExchanged += OnFrameExchanged;

        Segments = new ObservableCollection<SegmentViewModel>();
        Segments.CollectionChanged += OnSegmentsChanged;

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected, ReportError);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected, ReportError);
        ReadOnceCommand = new AsyncRelayCommand(ReadOnceAsync, () => IsConnected, ReportError);
        ApplySetpointCommand = new AsyncRelayCommand(ApplySetpointAsync, () => IsConnected && IsOperable, ReportError);
        StopChamberCommand = new AsyncRelayCommand(StopChamberAsync, () => IsConnected && IsOperable, ReportError);
        QuickSetTemperatureCommand = new AsyncRelayCommand<double?>(QuickSetTemperatureAsync, _ => IsConnected && IsOperable, ReportError);
        ToggleEditPresetsCommand = new RelayCommand(() => IsEditingPresets = !IsEditingPresets, () => IsManageAllowed);
        ToggleEditQuickProfilesCommand = new RelayCommand(() => IsEditingQuickProfiles = !IsEditingQuickProfiles, () => IsManageAllowed);
        AddQuickProfileCommand = new RelayCommand(AddQuickProfile, () => IsManageAllowed && ProfileToPin is not null);
        RemoveQuickProfileCommand = new RelayCommand<string>(RemoveQuickProfile, n => IsManageAllowed && !string.IsNullOrEmpty(n));
        ToggleEditNameCommand = new RelayCommand(() => IsEditingName = !IsEditingName);
        ToggleLockCommand = new RelayCommand(ToggleLock);
        CancelUnlockCommand = new RelayCommand(() => IsUnlockPromptOpen = false);
        ClearLockPasswordCommand = new RelayCommand(() => SetLockPassword(null), () => IsManageAllowed);

        StartProfileCommand = new AsyncRelayCommand(StartProfileAsync, () => IsConnected && IsOperable && !IsProfileRunning && Segments.Count > 0, ReportError);
        StartSelectedProfileCommand = new AsyncRelayCommand(StartSelectedProfileAsync, CanStartSelectedProfile, ReportError);
        QuickStartProfileCommand = new AsyncRelayCommand<TestProfile?>(QuickStartProfileAsync,
            p => p is not null && IsConnected && IsOperable && !IsProfileRunning, ReportError);
        PauseResumeProfileCommand = new RelayCommand(PauseResumeProfile, () => IsProfileRunning && IsUnlocked);
        StopProfileCommand = new RelayCommand(StopProfile, () => IsProfileRunning && IsUnlocked);
        ResumeInterruptedRunCommand = new AsyncRelayCommand(ResumeInterruptedRunAsync,
            () => HasInterruptedRun && IsConnected && IsOperable && !IsProfileRunning, ReportError);
        DiscardInterruptedRunCommand = new RelayCommand(DiscardInterruptedRun,
            () => HasInterruptedRun && !IsProfileRunning && IsUnlocked);
        CancelProfileCommand = new RelayCommand(CancelProfile, CanCancelProfile);
        StartQueueCommand = new AsyncRelayCommand(StartQueueAsync, () => IsConnected && IsOperable && !IsProfileRunning && Queue.Count > 0, ReportError);
        AddToQueueCommand = new RelayCommand(AddToQueue, () => Segments.Count > 0);
        RemoveFromQueueCommand = new RelayCommand(RemoveFromQueue, () => SelectedQueueItem is not null);
        ClearQueueCommand = new RelayCommand(() => { Queue.Clear(); RefreshQueueCommands(); });
        AddToChainCommand = new RelayCommand(AddToChain,
            () => SelectedHistoryProfile is not null && ProfileChain.Count < MaxChainedProfiles);
        RemoveFromChainCommand = new RelayCommand<TestProfile>(RemoveFromChain, p => p is not null);
        ClearChainCommand = new RelayCommand(() => ProfileChain.Clear(), () => ProfileChain.Count > 0);
        StartChainCommand = new AsyncRelayCommand(StartChainAsync,
            () => IsConnected && IsOperable && !IsProfileRunning && ProfileChain.Count > 0, ReportError);
        ProfileChain.CollectionChanged += (_, _) => OnChainChanged();
        AddSegmentCommand = new RelayCommand(AddSegment);
        AddSegmentBeforeCommand = new RelayCommand(() => InsertSegment(0), () => SelectedSegment is not null);
        AddSegmentAfterCommand = new RelayCommand(() => InsertSegment(1), () => SelectedSegment is not null);
        RemoveSegmentCommand = new RelayCommand(RemoveSegment, () => SelectedSegment is not null);
        MoveSegmentUpCommand = new RelayCommand(() => MoveSegment(-1), () => SelectedSegment is not null);
        MoveSegmentDownCommand = new RelayCommand(() => MoveSegment(+1), () => SelectedSegment is not null);
        ToggleSegmentsExpandCommand = new RelayCommand(() => IsSegmentsExpanded = !IsSegmentsExpanded);

        SaveToHistoryCommand = new RelayCommand(SaveToHistory, () => Segments.Count > 0);
        LoadFromHistoryCommand = new RelayCommand(LoadFromHistory, () => SelectedHistoryProfile is not null);
        DeleteFromHistoryCommand = new RelayCommand(DeleteFromHistory, () => SelectedHistoryProfile is not null);
        ImportProfileCommand = new RelayCommand(ImportProfile);
        ExportProfileCommand = new RelayCommand(ExportProfile, () => Segments.Count > 0);

        StartRecordingCommand = new RelayCommand(StartRecording, () => !IsRecording);
        StopRecordingCommand = new RelayCommand(StopRecording, () => IsRecording);
        BrowseRecordingPathCommand = new RelayCommand(BrowseRecordingPath);

        SendTerminalCommand = new AsyncRelayCommand(SendTerminalAsync, () => IsConnected && !string.IsNullOrWhiteSpace(TerminalInput), ReportError);
        ClearTerminalCommand = new RelayCommand(() =>
        {
            TerminalLines.Clear();
            DiagResult = string.Empty;
        });
        CopyTerminalCommand = new RelayCommand(CopyTerminalToClipboard);

        InsertReadCommandCommand = new RelayCommand(() => TerminalInput = TestReadFrame);
        InsertWriteCommandCommand = new RelayCommand(() => TerminalInput = BuildWriteFrame(DiagTestTemperature, true));
        InsertStopCommandCommand = new RelayCommand(() => TerminalInput = BuildWriteFrame(DiagTestTemperature, false));
        RunSetpointDiagnosticCommand = new AsyncRelayCommand(RunSetpointDiagnosticAsync, () => IsConnected && IsAsciiProtocol, ReportError);
        ReadDigitalCommand = new AsyncRelayCommand(ReadDigitalAsync, () => IsConnected && IsAsciiProtocol, ReportError);
        SimservProbeCommand = new AsyncRelayCommand(SimservProbeAsync, () => IsConnected && IsAsciiProtocol, ReportError);
        ReadProgramInfoCommand = new AsyncRelayCommand(ReadProgramInfoAsync, () => IsConnected && IsAsciiProtocol, ReportError);
        ModbusScanCommand = new AsyncRelayCommand(ModbusScanAsync, () => IsConnected && IsPolEko, ReportError);
        SikaInfoReportCommand = new AsyncRelayCommand(SikaInfoReportAsync, () => IsConnected && IsSika, ReportError);
        SikaCalibrationStatusCommand = new AsyncRelayCommand(SikaCalibrationStatusAsync, () => IsConnected && IsSika, ReportError);
        InsertSimservSetpointCommand = new RelayCommand(
            () => TerminalInput = SimservProtocol.BuildSetNominalValue(Address, 1, DiagTestTemperature).TrimEnd('\r'));
        InsertSimservStartCommand = new RelayCommand(
            () => TerminalInput = SimservProtocol.BuildSetDigitalOut(Address, 1, on: true).TrimEnd('\r'));

        ApplyConfig(config);
        if (IsPolEko)
        {
            // A sensible first command for the MODBUS terminal (read input register 0).
            _terminalInput = "04 0000 0001";
        }
        else if (IsSika)
        {
            // A sensible first command for the REST-API terminal (measured reference temperature).
            _terminalInput = "getRegister?register=TRset_TR";
        }

        SeedDefaultProfile();
        RefreshHistory();
        RecalculateTiming();
        LoadInterruptedRun();
    }

    /// <summary>Stable identity (used as a key in the shell and for persistence).</summary>
    public Guid Id { get; }

    private string _name = string.Empty;
    /// <summary>Human readable chamber name shown on the home page and header (editable).</summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? _name : value.Trim());
    }

    /// <summary>Chamber capabilities.</summary>
    public ChamberKind Kind { get; }

    /// <summary>Wire protocol used to talk to this chamber / oven.</summary>
    public ChamberProtocol Protocol { get; }

    /// <summary><c>true</c> for a POL-EKO MODBUS oven (temperature only).</summary>
    public bool IsPolEko => Protocol == ChamberProtocol.PolEkoModbus;

    /// <summary><c>true</c> for a SIKA TP Premium bath / dry block (REST-API, temperature only).</summary>
    public bool IsSika => Protocol == ChamberProtocol.SikaRestApi;

    /// <summary><c>true</c> for the original Vötsch / Weiss ASCII-2 protocol.</summary>
    public bool IsAsciiProtocol => Protocol == ChamberProtocol.VotschAscii2;

    /// <summary><c>true</c> when the chamber supports humidity control.</summary>
    public bool SupportsHumidity => Kind == ChamberKind.TemperatureHumidity && Protocol == ChamberProtocol.VotschAscii2;

    /// <summary>Short capability label for the UI.</summary>
    public string KindLabel => IsPolEko
        ? "POL-EKO · Teplota"
        : IsSika
            ? "SIKA TP · Teplota (kúpeľ)"
            : SupportsHumidity ? "Teplota + vlhkosť" : "Teplota";

    #region Nameplate (type-plate details)

    private ChamberNameplate _nameplate = new();

    private void SetPlate(Action<string> assign, string current, string? value,
        [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        value ??= string.Empty;
        if (current != value)
        {
            assign(value);
            OnPropertyChanged(name);
        }
    }

    public string Manufacturer { get => _nameplate.Manufacturer; set => SetPlate(v => _nameplate.Manufacturer = v, _nameplate.Manufacturer, value); }
    public string DeviceModel { get => _nameplate.Model; set => SetPlate(v => _nameplate.Model = v, _nameplate.Model, value); }
    public string SerialNumber { get => _nameplate.SerialNumber; set => SetPlate(v => _nameplate.SerialNumber = v, _nameplate.SerialNumber, value); }
    public string OrderNumber { get => _nameplate.OrderNumber; set => SetPlate(v => _nameplate.OrderNumber = v, _nameplate.OrderNumber, value); }
    public string YearOfConstruction { get => _nameplate.YearOfConstruction; set => SetPlate(v => _nameplate.YearOfConstruction = v, _nameplate.YearOfConstruction, value); }
    public string Refrigerant1 { get => _nameplate.Refrigerant1; set => SetPlate(v => _nameplate.Refrigerant1 = v, _nameplate.Refrigerant1, value); }
    public string Refrigerant2 { get => _nameplate.Refrigerant2; set => SetPlate(v => _nameplate.Refrigerant2 = v, _nameplate.Refrigerant2, value); }
    public string SupplyVoltage { get => _nameplate.SupplyVoltage; set => SetPlate(v => _nameplate.SupplyVoltage = v, _nameplate.SupplyVoltage, value); }
    public string NominalPower { get => _nameplate.NominalPower; set => SetPlate(v => _nameplate.NominalPower = v, _nameplate.NominalPower, value); }
    public string NominalCurrent { get => _nameplate.NominalCurrent; set => SetPlate(v => _nameplate.NominalCurrent = v, _nameplate.NominalCurrent, value); }
    public string SystemNumber { get => _nameplate.SystemNumber; set => SetPlate(v => _nameplate.SystemNumber = v, _nameplate.SystemNumber, value); }
    public string FirstCalibration { get => _nameplate.FirstCalibration; set => SetPlate(v => _nameplate.FirstCalibration = v, _nameplate.FirstCalibration, value); }
    public string NextCalibration { get => _nameplate.NextCalibration; set => SetPlate(v => _nameplate.NextCalibration = v, _nameplate.NextCalibration, value); }
    public string DeviceNotes { get => _nameplate.Notes; set => SetPlate(v => _nameplate.Notes = v, _nameplate.Notes, value); }

    /// <summary>Names of the nameplate properties, so the shell can persist edits to them.</summary>
    public static readonly string[] NameplatePropertyNames =
    {
        nameof(Manufacturer), nameof(DeviceModel), nameof(SerialNumber), nameof(OrderNumber),
        nameof(YearOfConstruction), nameof(Refrigerant1), nameof(Refrigerant2), nameof(SupplyVoltage),
        nameof(NominalPower), nameof(NominalCurrent), nameof(SystemNumber),
        nameof(FirstCalibration), nameof(NextCalibration), nameof(DeviceNotes),
    };

    private void RaiseNameplate()
    {
        foreach (string p in NameplatePropertyNames)
        {
            OnPropertyChanged(p);
        }
    }

    #endregion

    private bool _isControlAllowed = true;
    /// <summary><c>false</c> for view-only users (Operator role); gates control commands.</summary>
    public bool IsControlAllowed
    {
        get => _isControlAllowed;
        private set
        {
            if (SetProperty(ref _isControlAllowed, value))
            {
                OnPropertyChanged(nameof(IsOperable));
                RefreshCommands();
                AppLog.Info(Name, value
                    ? "Ovládanie povolené (rola Supervisor/Admin)."
                    : "Ovládanie zakázané pre aktuálnu rolu – ovládacie tlačidlá (Nastaviť/Stop/Štart) sú neaktívne.");
            }
        }
    }

    /// <summary>Sets whether the current user may operate this chamber.</summary>
    public void SetControlAllowed(bool allowed) => IsControlAllowed = allowed;

    #region Device lock (safety)

    private bool _isLocked;
    /// <summary>
    /// When <c>true</c> the device is locked: every control command is disabled and the
    /// dashboard control panels are greyed out, so a running profile / temperature can't
    /// be changed by an accidental button press. Persisted per device.
    /// </summary>
    public bool IsLocked
    {
        get => _isLocked;
        private set
        {
            if (SetProperty(ref _isLocked, value))
            {
                OnPropertyChanged(nameof(IsUnlocked));
                OnPropertyChanged(nameof(IsOperable));
                OnPropertyChanged(nameof(LockGlyph));
                OnPropertyChanged(nameof(LockButtonText));
                OnPropertyChanged(nameof(LockStateLabel));
                RefreshCommands();
                AppLog.Info(Name, value ? "Zariadenie zamknuté (ovládanie zablokované)." : "Zariadenie odomknuté.");
            }
        }
    }

    /// <summary><c>true</c> when the device is not locked (used to enable the control panels).</summary>
    public bool IsUnlocked => !IsLocked;

    /// <summary>True when the user may actually operate the device: allowed by role AND not locked.</summary>
    public bool IsOperable => IsControlAllowed && !IsLocked;

    private string? _lockPasswordHash;
    /// <summary>SHA-256 hash of the optional unlock password (persisted); empty = no password.</summary>
    public string? LockPasswordHash
    {
        get => _lockPasswordHash;
        private set
        {
            if (SetProperty(ref _lockPasswordHash, value))
            {
                OnPropertyChanged(nameof(HasLockPassword));
            }
        }
    }

    /// <summary><c>true</c> when releasing the lock requires a password.</summary>
    public bool HasLockPassword => !string.IsNullOrEmpty(_lockPasswordHash);

    private bool _isUnlockPromptOpen;
    /// <summary>True while the inline "enter password to unlock" prompt is shown on the card.</summary>
    public bool IsUnlockPromptOpen
    {
        get => _isUnlockPromptOpen;
        private set => SetProperty(ref _isUnlockPromptOpen, value);
    }

    /// <summary>🔒 while locked, 🔓 while unlocked – for the lock toggle button.</summary>
    public string LockGlyph => IsLocked ? "🔒" : "🔓";

    /// <summary>Caption for the lock toggle button.</summary>
    public string LockButtonText => IsLocked ? "Odomknúť" : "Zamknúť";

    /// <summary>Short badge text shown while the device is locked.</summary>
    public string LockStateLabel => HasLockPassword ? "🔒 Zamknuté (heslo)" : "🔒 Zamknuté";

    /// <summary>Toggles the lock: locks immediately, or starts the unlock (password prompt if set).</summary>
    public RelayCommand ToggleLockCommand { get; }

    /// <summary>Closes the inline unlock password prompt without unlocking.</summary>
    public RelayCommand CancelUnlockCommand { get; }

    /// <summary>Removes the unlock password (admin, in device settings).</summary>
    public RelayCommand ClearLockPasswordCommand { get; }

    private void ToggleLock()
    {
        if (!IsLocked)
        {
            IsLocked = true;
            IsUnlockPromptOpen = false;
            ShowActionInfo(HasLockPassword
                ? "🔒 Zariadenie zamknuté (odomknutie vyžaduje heslo)."
                : "🔒 Zariadenie zamknuté.");
            return;
        }

        // Locked: a password releases it via the inline prompt; otherwise unlock now.
        if (HasLockPassword)
        {
            IsUnlockPromptOpen = true;
        }
        else
        {
            Unlock();
        }
    }

    /// <summary>Verifies the entered password (if any) and releases the lock. Called from the view.</summary>
    public void TryUnlock(string? password)
    {
        if (HasLockPassword &&
            !string.Equals(_lockPasswordHash, User.Hash(password ?? string.Empty), StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Nesprávne heslo – zariadenie zostáva zamknuté.";
            return;
        }

        Unlock();
    }

    private void Unlock()
    {
        IsUnlockPromptOpen = false;
        IsLocked = false;
        ShowActionInfo("🔓 Zariadenie odomknuté.");
    }

    /// <summary>
    /// Locks the device automatically when a run starts (test profile, quick profile or
    /// manual set point), so a running test / temperature can't be changed by an accidental
    /// button press. No-op when the user can't control the device or it is already locked.
    /// </summary>
    private void AutoLockOnRun(string reason)
    {
        if (!IsControlAllowed || IsLocked)
        {
            return;
        }

        IsLocked = true;
        IsUnlockPromptOpen = false;
        ShowActionInfo($"🔒 Zariadenie automaticky zamknuté ({reason}). Pred zmenou alebo zastavením ho odomkni.");
    }

    /// <summary>
    /// Sets or clears the unlock password (admin, in device settings). An empty value
    /// removes the password so the lock can be released with a single click.
    /// </summary>
    public void SetLockPassword(string? password)
    {
        LockPasswordHash = string.IsNullOrWhiteSpace(password) ? null : User.Hash(password);
        OnPropertyChanged(nameof(LockStateLabel));
        StatusMessage = HasLockPassword ? "Heslo zámku nastavené." : "Heslo zámku odstránené.";
    }

    #endregion

    private bool _isManageAllowed;
    /// <summary><c>true</c> only for admins; gates per-device configuration (e.g. quick presets).</summary>
    public bool IsManageAllowed
    {
        get => _isManageAllowed;
        private set
        {
            if (SetProperty(ref _isManageAllowed, value))
            {
                if (!value)
                {
                    IsEditingPresets = false;
                    IsEditingQuickProfiles = false;
                }

                ToggleEditPresetsCommand.RaiseCanExecuteChanged();
                ToggleEditQuickProfilesCommand.RaiseCanExecuteChanged();
                AddQuickProfileCommand.RaiseCanExecuteChanged();
                RemoveQuickProfileCommand.RaiseCanExecuteChanged();
                ClearLockPasswordCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Sets whether the current user may configure this chamber (admin only).</summary>
    public void SetManageAllowed(bool allowed) => IsManageAllowed = allowed;

    private bool _isEditingName;
    /// <summary>True while the dashboard name field is in edit mode (via the ✎ button).</summary>
    public bool IsEditingName { get => _isEditingName; private set => SetProperty(ref _isEditingName, value); }

    public RelayCommand ToggleEditNameCommand { get; }

    private bool _isRemoveArmed;
    /// <summary>
    /// Two-step removal state driven by the shell: the dashboard ✕ arms first,
    /// a second click within a few seconds actually removes the chamber.
    /// </summary>
    public bool IsRemoveArmed
    {
        get => _isRemoveArmed;
        private set { if (SetProperty(ref _isRemoveArmed, value)) OnPropertyChanged(nameof(RemoveButtonText)); }
    }

    /// <summary>Caption of the dashboard remove button (reflects the armed state).</summary>
    public string RemoveButtonText => IsRemoveArmed ? "✕ Naozaj?" : "✕";

    /// <summary>Arms / disarms the two-step remove confirmation (called by the shell).</summary>
    public void SetRemoveArmed(bool value) => IsRemoveArmed = value;

    #region Connection

    private string _host;
    public string Host { get => _host; set { if (SetProperty(ref _host, value)) OnPropertyChanged(nameof(Endpoint)); } }

    private int _port = 1080;
    public int Port { get => _port; set { if (SetProperty(ref _port, value)) OnPropertyChanged(nameof(Endpoint)); } }

    public string Endpoint => $"{Host}:{Port}";

    private int _address = 1;
    public int Address { get => _address; set => SetProperty(ref _address, value); }

    private int _analogChannelCount = Ascii2Protocol.DefaultAnalogChannelCount;
    public int AnalogChannelCount { get => _analogChannelCount; set => SetProperty(ref _analogChannelCount, Math.Max(1, value)); }

    private int _startChannelIndex;
    public int StartChannelIndex { get => _startChannelIndex; set => SetProperty(ref _startChannelIndex, Math.Clamp(value, 0, DigitalChannels.Count - 1)); }

    public IReadOnlyList<string> TerminatorOptions { get; } = new[] { "CR (\\r)", "CR LF (\\r\\n)", "LF (\\n)" };

    private string _selectedTerminator = "CR (\\r)";
    public string SelectedTerminator { get => _selectedTerminator; set => SetProperty(ref _selectedTerminator, value); }

    private string TerminatorValue => SelectedTerminator switch
    {
        "CR LF (\\r\\n)" => "\r\n",
        "LF (\\n)" => "\n",
        _ => "\r",
    };

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(ConnectionState));
                RefreshCommands();
            }
        }
    }

    public string ConnectionState => IsConnected ? $"Pripojené · {Endpoint}" : "Odpojené";

    private string _statusMessage = "Pripravené.";
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    private string _actionInfo = string.Empty;
    /// <summary>Transient "what just happened" banner shown after an operator action.</summary>
    public string ActionInfo
    {
        get => _actionInfo;
        private set { if (SetProperty(ref _actionInfo, value)) OnPropertyChanged(nameof(HasActionInfo)); }
    }

    /// <summary>True while an <see cref="ActionInfo"/> banner should be visible.</summary>
    public bool HasActionInfo => !string.IsNullOrEmpty(ActionInfo);

    private System.Windows.Threading.DispatcherTimer? _actionInfoTimer;

    /// <summary>
    /// Pops a short confirmation banner ("Nastavené 30 °C · štart ZAPNUTÝ", …) so the
    /// operator always sees what an action did and what is now switched on. It also
    /// mirrors to <see cref="StatusMessage"/> and auto-clears after a few seconds.
    /// </summary>
    private void ShowActionInfo(string message)
    {
        ActionInfo = message;
        StatusMessage = message;
        if (_actionInfoTimer is null)
        {
            _actionInfoTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4.5),
            };
            _actionInfoTimer.Tick += (_, _) =>
            {
                _actionInfoTimer!.Stop();
                ActionInfo = string.Empty;
            };
        }

        _actionInfoTimer.Stop();
        _actionInfoTimer.Start();
    }

    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }

    private ChamberConnectionSettings BuildSettings() => new()
    {
        Host = Host,
        Port = Port,
        Address = Address,
        Terminator = TerminatorValue,
        AnalogChannelCount = AnalogChannelCount,
        StartChannelIndex = StartChannelIndex,
    };

    private async Task ConnectAsync()
    {
        StopReconnect();
        _connectionLostHandled = false;
        _pollFailureCount = 0;
        _bannerWarned = false;
        ClearAlarm("link");

        StatusMessage = $"Pripájam sa na {Endpoint}…";
        await _client.ConnectAsync(BuildSettings());
        IsConnected = true;
        ShowActionInfo($"🔌 Pripojené na {Endpoint}");
        _audit.Log(Name, "Pripojenie", Endpoint);
        AppLog.Info(Name, $"Pripojené na {Endpoint}.");

        if (PollingEnabled)
        {
            StartPolling();
        }

        // A freshly interrupted run (app crash / restart) continues automatically.
        TryAutoResumeAfterConnect();
    }

    private async Task DisconnectAsync()
    {
        StopReconnect();
        StopPolling();
        // Cancel a running profile without the Stop-button power-off (we are
        // disconnecting; a StopAsync on the closing connection would just error).
        _profileCts?.Cancel();
        await _client.DisconnectAsync();
        IsConnected = false;
        _connectionLostHandled = false;
        SetManualStarted(false);
        SetReadRunning(null);
        ClearAllAlarms();
        ShowActionInfo("🔌 Odpojené");
        _audit.Log(Name, "Odpojenie", Endpoint);
    }

    /// <summary>
    /// Best-effort connect used to bring the chamber online automatically (e.g.
    /// right after a user logs in). Never throws: on failure it reports the error
    /// and, when <see cref="AutoReconnect"/> is enabled, keeps retrying quietly in
    /// the background instead of surfacing an alarm.
    /// </summary>
    public async Task ConnectIfPossibleAsync()
    {
        if (IsConnected)
        {
            return;
        }

        try
        {
            await ConnectAsync();
        }
        catch (Exception ex)
        {
            ReportError(ex);
            if (AutoReconnect)
            {
                StartReconnect();
            }
        }
    }

    #endregion

    #region Live monitoring

    private bool _pollingEnabled = true;
    public bool PollingEnabled
    {
        get => _pollingEnabled;
        set
        {
            if (SetProperty(ref _pollingEnabled, value) && IsConnected)
            {
                if (value) StartPolling(); else StopPolling();
            }
        }
    }

    private double _pollIntervalSeconds = 2;
    public double PollIntervalSeconds { get => _pollIntervalSeconds; set => SetProperty(ref _pollIntervalSeconds, Math.Max(0.5, value)); }

    private double? _measuredTemperature;
    public double? MeasuredTemperature { get => _measuredTemperature; private set => SetProperty(ref _measuredTemperature, value); }

    private double? _measuredTemperatureSetpoint;
    public double? MeasuredTemperatureSetpoint { get => _measuredTemperatureSetpoint; private set => SetProperty(ref _measuredTemperatureSetpoint, value); }

    private double? _measuredHumidity;
    public double? MeasuredHumidity { get => _measuredHumidity; private set => SetProperty(ref _measuredHumidity, value); }

    private double? _measuredHumiditySetpoint;
    public double? MeasuredHumiditySetpoint { get => _measuredHumiditySetpoint; private set => SetProperty(ref _measuredHumiditySetpoint, value); }

    private string _lastRaw = string.Empty;
    public string LastRaw { get => _lastRaw; private set => SetProperty(ref _lastRaw, value); }

    private DateTimeOffset? _lastUpdate;
    public DateTimeOffset? LastUpdate { get => _lastUpdate; private set => SetProperty(ref _lastUpdate, value); }

    public AsyncRelayCommand ReadOnceCommand { get; }

    private async Task ReadOnceAsync() => ApplyReading(await _client.ReadAsync());

    private void StartPolling()
    {
        StopPolling();
        _firstReadLogged = false;
        _pollingCts = new CancellationTokenSource();
        _ = PollLoopAsync(_pollingCts.Token);
    }

    private void StopPolling()
    {
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        _pollingCts = null;
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                ChamberReading reading = await _client.ReadAsync(token);
                if (LooksLikeControllerBanner(reading.Raw))
                {
                    WarnControllerBannerOnce(reading.Raw);
                    throw new InvalidOperationException(
                        "Odpoveď je uvítací banner riadiacej jednotky, nie ASCII-2 dáta (skontroluj port).");
                }

                _pollFailureCount = 0;
                ApplyReading(reading);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (await OnPollFailureAsync(ex))
                {
                    break;
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ApplyReading(ChamberReading reading)
    {
        MeasuredTemperature = reading.Temperature;
        MeasuredTemperatureSetpoint = reading.TemperatureSetpoint;
        if (SupportsHumidity)
        {
            MeasuredHumidity = reading.Humidity;
            MeasuredHumiditySetpoint = reading.HumiditySetpoint;
        }

        // Determine the chamber's real running state from the reported digital
        // "start / system on" channel, so the dashboard reflects the actual
        // chamber and not just what this app happened to send. Only trust it when
        // the response actually carried a digital block.
        bool hasDigital = RawHasDigitalBlock(reading.Raw);
        SetReadRunning(hasDigital ? reading.DigitalChannels.Start : null);

        // Log the first reading of each connection so the exact frame layout
        // (digital block, start channel, values) can be mapped for the
        // running/idle detection.
        if (!_firstReadLogged)
        {
            _firstReadLogged = true;
            AppLog.Info(Name,
                $"Prvé čítanie (RAW): \"{reading.Raw}\" · digitálny blok={(hasDigital ? "áno" : "NIE")} " +
                $"'{reading.DigitalChannels.ToProtocolString()}' · štart[{StartChannelIndex}]={reading.DigitalChannels.Start} " +
                $"· hodnoty=[{string.Join(", ", reading.AnalogValues.Select(v => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))}]");
        }

        LastRaw = reading.Raw;
        LastUpdate = reading.Timestamp;
        OnPropertyChanged(nameof(ReferenceDeviation));
        RecordLive(reading);
        CheckAlarms(reading);

        if (IsRecording)
        {
            try
            {
                _recorder?.Record(reading, ReferenceTemperature);
                RecordedRows = _recorder?.RowCount ?? 0;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba záznamu: {ex.Message}";
                StopRecording();
            }
        }
    }

    #endregion

    #region Reference thermometer

    /// <summary>Thermometers available as an external reference (live collection).</summary>
    public ObservableCollection<ThermometerDeviceViewModel> AvailableThermometers => _thermometers.Devices;

    private ThermometerDeviceViewModel? _selectedReferenceThermometer;
    /// <summary>External ASL F100 used as a reference next to the chamber's own probe.</summary>
    public ThermometerDeviceViewModel? SelectedReferenceThermometer
    {
        get => _selectedReferenceThermometer;
        set
        {
            ThermometerDeviceViewModel? old = _selectedReferenceThermometer;
            if (SetProperty(ref _selectedReferenceThermometer, value))
            {
                if (old is not null) old.PropertyChanged -= OnReferenceChanged;
                if (value is not null)
                {
                    value.PropertyChanged += OnReferenceChanged;

                    // Connect the assigned thermometer so its temperature starts
                    // updating (it polls itself every ~2 s once connected).
                    if (!value.IsConnected && value.ConnectCommand.CanExecute(null))
                    {
                        value.ConnectCommand.Execute(null);
                    }
                }

                RaiseReference();
            }
        }
    }

    /// <summary>The reference thermometer's current temperature, if any.</summary>
    public double? ReferenceTemperature => SelectedReferenceThermometer?.Temperature;

    /// <summary><c>true</c> when a reference thermometer is selected.</summary>
    public bool HasReference => SelectedReferenceThermometer is not null;

    /// <summary>Chamber measured temperature minus reference temperature.</summary>
    public double? ReferenceDeviation =>
        MeasuredTemperature is { } m && ReferenceTemperature is { } r ? m - r : null;

    private void OnReferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ThermometerDeviceViewModel.Temperature))
        {
            RaiseReference();
        }
    }

    private void RaiseReference()
    {
        OnPropertyChanged(nameof(ReferenceTemperature));
        OnPropertyChanged(nameof(ReferenceDeviation));
        OnPropertyChanged(nameof(HasReference));
    }

    #endregion

    #region Manual set point

    private double _manualTemperature = 25;
    public double ManualTemperature { get => _manualTemperature; set { if (SetProperty(ref _manualTemperature, value)) RaiseActivity(); } }

    private bool _controlHumidity;
    public bool ControlHumidity { get => _controlHumidity; set => SetProperty(ref _controlHumidity, value); }

    private double _manualHumidity = 50;
    public double ManualHumidity { get => _manualHumidity; set => SetProperty(ref _manualHumidity, value); }

    private string _digitalChannelsText = new('0', DigitalChannels.Count);
    public string DigitalChannelsText { get => _digitalChannelsText; set => SetProperty(ref _digitalChannelsText, value); }

    public AsyncRelayCommand ApplySetpointCommand { get; }
    public AsyncRelayCommand StopChamberCommand { get; }

    /// <summary>
    /// Guards a manual / quick temperature set point against the device's allowed
    /// range [<see cref="TempMin"/>, <see cref="TempMax"/>]. Returns <c>false</c> and
    /// shows a clear message when the value is out of range, so the app never sends a
    /// set point the device would reject or that is unsafe.
    /// </summary>
    private bool IsTemperatureInRange(double celsius)
    {
        if (celsius < TempMin || celsius > TempMax)
        {
            string msg = $"⚠ {celsius:0.#} °C je mimo povoleného rozsahu zariadenia [{TempMin:0.#}…{TempMax:0.#} °C].";
            ShowActionInfo(msg);
            StatusMessage = msg;
            AppLog.Warn(Name, msg);
            DesktopNotifier.Notify($"Teplota mimo rozsahu · {Name}", msg, DesktopNotificationKind.Warning);
            return false;
        }

        return true;
    }

    private async Task ApplySetpointAsync()
    {
        // Enforce the device's allowed temperature range before anything is sent.
        if (!IsTemperatureInRange(ManualTemperature))
        {
            return;
        }

        DigitalChannels digital = ParseDigitalText();
        digital.StartChannelIndex = StartChannelIndex;
        digital.Start = true;

        bool humidity = SupportsHumidity && ControlHumidity;
        if (humidity && (ManualHumidity < HumMin || ManualHumidity > HumMax))
        {
            string msg = $"⚠ {ManualHumidity:0.#} % je mimo povoleného rozsahu vlhkosti [{HumMin:0.#}…{HumMax:0.#} %].";
            ShowActionInfo(msg);
            StatusMessage = msg;
            AppLog.Warn(Name, msg);
            DesktopNotifier.Notify($"Vlhkosť mimo rozsahu · {Name}", msg, DesktopNotificationKind.Warning);
            return;
        }

        // Only send the humidity control variable to humidity chambers; a temp-only
        // Simpac answers SET NOMINAL on channel 2 with -8 (variable not found).
        var setpoints = humidity
            ? new List<double> { ManualTemperature, ManualHumidity }
            : new List<double> { ManualTemperature };
        string summary = $"{ManualTemperature:0.0} °C" + (humidity ? $", {ManualHumidity:0.0} %" : string.Empty);
        AppLog.Info(Name, $"Zápis setpointu: {summary} · adresa {Address} · štart kanál #{StartChannelIndex + 1} = ON · " +
            $"analóg. kanálov {AnalogChannelCount} · digitálne '{DigitalChannelsText}'.");
        await _client.WriteSetpointsAsync(setpoints, digital);
        SetManualStarted(true);
        ShowActionInfo($"✔ Nastavené {summary} · štart ZAPNUTÝ");
        // Safety: lock the device after a manual set point so it can't be changed by accident.
        AutoLockOnRun("manuálne ovládanie");
        _audit.Log(Name, "Setpoint", summary);
        AppLog.Info(Name, $"Setpoint zapísaný: {summary}. Skontroluj v logu odpoveď regulátora (RX) na príkaz TX $..E…");
    }

    private double _quickTemperature = 25;
    /// <summary>Temperature (°C) entered in the dashboard quick-set field.</summary>
    public double QuickTemperature { get => _quickTemperature; set => SetProperty(ref _quickTemperature, value); }

    private List<double> _quickPresets = new();

    /// <summary>
    /// One-click temperature presets on the dashboard card. Per-device and
    /// persisted; defaults depend on the device (an oven cannot go below ambient).
    /// </summary>
    public IReadOnlyList<double> QuickPresets => _quickPresets;

    /// <summary>The presets as editable text ("60, 105, 150, 250") for the admin editor.</summary>
    public string QuickPresetsText
    {
        get => string.Join(", ", _quickPresets.Select(p => p.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)));
        set
        {
            List<double> parsed = ParsePresets(value);
            if (parsed.Count > 0)
            {
                _quickPresets = parsed;
                OnPropertyChanged(nameof(QuickPresets));
                StatusMessage = $"Predvoľby rýchleho ovládania uložené: {QuickPresetsText} °C.";
            }

            OnPropertyChanged(); // normalise (or revert) the text box content
        }
    }

    private bool _isEditingPresets;
    /// <summary>True while the admin preset editor row is visible on the card.</summary>
    public bool IsEditingPresets { get => _isEditingPresets; set => SetProperty(ref _isEditingPresets, value); }

    public RelayCommand ToggleEditPresetsCommand { get; }

    private List<double> DefaultQuickPresets() => IsPolEko
        ? new List<double> { 0, 25, 50, 60, 80, 120, 150, 250 }
        : IsSika
            ? new List<double> { 0, 25, 60, 100, 150 }
            : new List<double> { -20, 0, 25, 60 };

    /// <summary>
    /// The original 4-value POL-EKO preset default. A dryer still carrying exactly
    /// this set is transparently upgraded to the fuller default on load.
    /// </summary>
    private static readonly List<double> LegacyPolEkoPresets = new() { 60, 105, 150, 250 };

    /// <summary>Parses "60, 105.5, 150" → presets; invalid tokens are skipped, 1–8 values.</summary>
    private static List<double> ParsePresets(string? text)
    {
        var result = new List<double>();
        foreach (string token in (text ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)
                && v is >= -100 and <= 350 && !result.Contains(v))
            {
                result.Add(v);
                if (result.Count == 8)
                {
                    break;
                }
            }
        }

        return result;
    }

    public AsyncRelayCommand<double?> QuickSetTemperatureCommand { get; }

    /// <summary>Applies a temperature set point directly (used by the presets and the quick field).</summary>
    private async Task QuickSetTemperatureAsync(double? celsius)
    {
        ManualTemperature = celsius ?? QuickTemperature;
        await ApplySetpointAsync();
    }

    private async Task StopChamberAsync()
    {
        AppLog.Info(Name, $"Stop komory: adresa {Address} · úplné vypnutie výkonu (stop programu + štart kanál OFF).");
        await _client.StopAsync();
        SetManualStarted(false);
        ShowActionInfo("⏹ Stop – výkon komory VYPNUTÝ");
        _audit.Log(Name, "Stop komory", string.Empty);
        AppLog.Info(Name, "Komora zastavená – výkon vypnutý.");
    }

    private DigitalChannels ParseDigitalText() => DigitalChannels.Parse(DigitalChannelsText, StartChannelIndex);

    #endregion

    #region Profile editor

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

    private string _profileName = "Profil 1";
    public string ProfileName { get => _profileName; set { if (SetProperty(ref _profileName, value)) RaiseActivity(); } }

    private int _cycles = 1;
    public int Cycles
    {
        get => _cycles;
        set { if (SetProperty(ref _cycles, Math.Max(1, value))) RecalculateTiming(); }
    }

    private double _profileUpdateIntervalSeconds = 5;
    public double ProfileUpdateIntervalSeconds { get => _profileUpdateIntervalSeconds; set => SetProperty(ref _profileUpdateIntervalSeconds, Math.Max(1, value)); }

    private bool _useDelayedStart;
    /// <summary>When set, the profile waits until <see cref="ScheduledStart"/> before running.</summary>
    public bool UseDelayedStart
    {
        get => _useDelayedStart;
        set { if (SetProperty(ref _useDelayedStart, value)) RecalculateTiming(); }
    }

    private DateTime _scheduledStartDate = DateTime.Today;
    public DateTime ScheduledStartDate
    {
        get => _scheduledStartDate;
        set
        {
            if (SetProperty(ref _scheduledStartDate, value))
            {
                OnPropertyChanged(nameof(ScheduledStartDateText));
                RecalculateTiming();
            }
        }
    }

    /// <summary>The scheduled date as editable text (dd.MM.yyyy), to avoid the OS DatePicker.</summary>
    public string ScheduledStartDateText
    {
        get => ScheduledStartDate.ToString("dd.MM.yyyy");
        set
        {
            if (DateTime.TryParse(value, out DateTime parsed))
            {
                ScheduledStartDate = parsed.Date;
            }

            OnPropertyChanged();
        }
    }

    private string _scheduledStartTime = DateTime.Now.AddMinutes(5).ToString("HH:mm");
    public string ScheduledStartTime
    {
        get => _scheduledStartTime;
        set
        {
            if (SetProperty(ref _scheduledStartTime, value))
            {
                OnPropertyChanged(nameof(ScheduledStartHour));
                OnPropertyChanged(nameof(ScheduledStartMinute));
                RecalculateTiming();
            }
        }
    }

    /// <summary>Scheduled-start hour (0–23), for the time stepper on the dashboard.</summary>
    public int ScheduledStartHour
    {
        get => TimeSpan.TryParse(ScheduledStartTime, out TimeSpan t) ? t.Hours : 0;
        set => ScheduledStartTime = $"{Math.Clamp(value, 0, 23):00}:{ScheduledStartMinute:00}";
    }

    /// <summary>Scheduled-start minute (0–59), for the time stepper on the dashboard.</summary>
    public int ScheduledStartMinute
    {
        get => TimeSpan.TryParse(ScheduledStartTime, out TimeSpan t) ? t.Minutes : 0;
        set => ScheduledStartTime = $"{ScheduledStartHour:00}:{Math.Clamp(value, 0, 59):00}";
    }

    /// <summary>The resolved scheduled start moment (date + parsed time).</summary>
    public DateTime ScheduledStart
    {
        get
        {
            TimeSpan time = TimeSpan.TryParse(ScheduledStartTime, out TimeSpan t) ? t : TimeSpan.Zero;
            return ScheduledStartDate.Date + time;
        }
    }

    private bool _isProfileRunning;
    public bool IsProfileRunning
    {
        get => _isProfileRunning;
        private set
        {
            if (SetProperty(ref _isProfileRunning, value))
            {
                if (!value)
                {
                    IsProfilePaused = false;
                }

                RefreshCommands();
                RaiseActivity();
                OnPropertyChanged(nameof(HasProfilePreview));
                BuildProfilePreview();
            }
        }
    }

    private bool _isProfilePaused;
    /// <summary><c>true</c> while a running profile is paused (test time frozen).</summary>
    public bool IsProfilePaused
    {
        get => _isProfilePaused;
        private set { if (SetProperty(ref _isProfilePaused, value)) OnPropertyChanged(nameof(PauseResumeGlyph)); }
    }

    /// <summary>Glyph for the single pause/resume button (▶ when paused, ⏸ while running).</summary>
    public string PauseResumeGlyph => IsProfilePaused ? "▶" : "⏸";

    private bool _manualStarted;

    // The chamber's own reported "system on" state from the last reading:
    // true/false when the response carried a digital block, null when unknown.
    private bool? _readRunning;
    private bool _firstReadLogged;

    /// <summary><c>true</c> while the chamber is actively conditioning. Prefers the
    /// chamber's own reported state (digital start channel); falls back to what
    /// this app started only while the real state is unknown.</summary>
    public bool IsActive => IsProfileRunning || (_readRunning ?? _manualStarted);

    /// <summary>One-word device state for the dashboard: "Aktívna" while a set point
    /// is being driven (profile or manual), "Neaktívna" otherwise.</summary>
    public string StateLabel => IsActive ? "Aktívna" : "Neaktívna";

    /// <summary>Detail line under the state: what exactly is running.</summary>
    public string ActivityLabel
    {
        get
        {
            if (IsProfileRunning)
            {
                return $"Profil: {ProfileName}";
            }

            double setpoint = MeasuredTemperatureSetpoint ?? ManualTemperature;
            return (_readRunning ?? _manualStarted)
                ? $"Beží · setpoint {setpoint:0.0} °C"
                : "Žiadna nastavená teplota";
        }
    }

    /// <summary>Temperature/humidity operating range for the dashboard (from alarm limits).</summary>
    public string RangeLabel
    {
        get
        {
            string temp = $"{TempMin:0.#}…{TempMax:0.#} °C";
            return SupportsHumidity ? $"{temp}  ·  {HumMin:0.#}…{HumMax:0.#} %rv" : temp;
        }
    }

    private void SetManualStarted(bool value)
    {
        if (_manualStarted != value)
        {
            _manualStarted = value;
            RaiseActivity();
        }
    }

    private void SetReadRunning(bool? value)
    {
        if (_readRunning != value)
        {
            _readRunning = value;
            RaiseActivity();
        }
    }

    /// <summary>True when the raw frame contains a digital-channel block (a run of 8+ 0/1).</summary>
    private static bool RawHasDigitalBlock(string raw)
    {
        int run = 0;
        foreach (char c in raw)
        {
            if (c is '0' or '1')
            {
                if (++run >= 8)
                {
                    return true;
                }
            }
            else
            {
                run = 0;
            }
        }

        return false;
    }

    private void RaiseActivity()
    {
        // Track when the device became active, so the dashboard timeline (Gantt)
        // can anchor the start of a manual-run bar.
        if (IsActive)
        {
            _activeSince ??= DateTime.Now;
        }
        else
        {
            _activeSince = null;
        }

        OnPropertyChanged(nameof(ActiveSince));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(ActivityLabel));
        OnPropertyChanged(nameof(ControlModeBadge));
        OnPropertyChanged(nameof(HasControlMode));
    }

    private string _profileStatus = "Nečinné.";
    public string ProfileStatus { get => _profileStatus; private set => SetProperty(ref _profileStatus, value); }

    private string _profileTimeRemaining = string.Empty;
    /// <summary>Live "time left" countdown of the running profile (updates every second).</summary>
    public string ProfileTimeRemaining { get => _profileTimeRemaining; private set => SetProperty(ref _profileTimeRemaining, value); }

    private string _profileCompletionText = string.Empty;
    /// <summary>
    /// Compact completion line for the card header (under the profile name): the day
    /// name, date and time the profile finishes plus the live remaining countdown,
    /// e.g. "🏁 Koniec: Ut 21.07.2026 09:20 · zostáva 38:38:55".
    /// </summary>
    public string ProfileCompletionText { get => _profileCompletionText; private set { if (SetProperty(ref _profileCompletionText, value)) OnPropertyChanged(nameof(HasProfileCompletion)); } }

    /// <summary>True while a completion line is available (a profile is running with a known end).</summary>
    public bool HasProfileCompletion => !string.IsNullOrEmpty(ProfileCompletionText);

    /// <summary>Slovak full day-of-week names, indexed by <see cref="DayOfWeek"/> (Sunday = 0).</summary>
    private static readonly string[] SkDayNamesFull =
        { "Nedeľa", "Pondelok", "Utorok", "Streda", "Štvrtok", "Piatok", "Sobota" };

    /// <summary>Slovak short day-of-week abbreviations, indexed by <see cref="DayOfWeek"/> (Sunday = 0).</summary>
    private static readonly string[] SkDayNamesShort = { "Ne", "Po", "Ut", "St", "Št", "Pi", "So" };

    private static string DayFull(DateTime d) => SkDayNamesFull[(int)d.DayOfWeek];

    private static string DayShort(DateTime d) => SkDayNamesShort[(int)d.DayOfWeek];

    /// <summary>Prominent control-mode badge: "PROFIL", "MANUÁL" or empty when idle.</summary>
    public string ControlModeBadge =>
        IsProfileRunning ? "PROFIL" : ((_readRunning ?? _manualStarted) ? "MANUÁL" : string.Empty);

    /// <summary>True while a control mode (profile or manual) is active — drives the badge visibility.</summary>
    public bool HasControlMode => !string.IsNullOrEmpty(ControlModeBadge);

    // ---- Dashboard timeline (Gantt) anchors ----

    private DateTime? _activeSince;

    /// <summary>When the app first observed the device active (profile or manual).
    /// Start of a manual-run bar on the dashboard timeline; <c>null</c> while idle.</summary>
    public DateTime? ActiveSince => _activeSince;

    /// <summary>Actual start of the running profile / profile chain (dashboard timeline).</summary>
    public DateTime? ProfileRunStart => _profileActualStart;

    /// <summary>Estimated end of the running profile / profile chain (dashboard timeline).</summary>
    public DateTime? ProfileRunEnd => _profileEstimatedEnd;

    /// <summary>Planned duration of the editor profile incl. cycles — anchors the
    /// timeline bar while a delayed start is still waiting for its scheduled time.</summary>
    public TimeSpan PlannedProfileDuration =>
        TimeSpan.FromMinutes(Segments.Sum(s => s.DurationMinutes) * Math.Max(1, Cycles));

    private void RaiseGanttTimes()
    {
        OnPropertyChanged(nameof(ProfileRunStart));
        OnPropertyChanged(nameof(ProfileRunEnd));
    }

    /// <summary>Folder under the user's Documents where per-profile temperature logs are written.</summary>
    private static readonly string ProfileLogDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VotschVc3", "profil-logy");

    /// <summary>Opens a fresh per-profile temperature log for the run that is starting.</summary>
    private void OpenProfileTemperatureLog(string profileName)
    {
        try
        {
            _profileTempLog?.Dispose();
            _profileTempLog = new ProfileTemperatureLog(
                ProfileLogDirectory, profileName, Name, SupportsHumidity, DateTime.Now);
            AppLog.Info(Name, $"Log teplôt profilu: {_profileTempLog.FilePath}");
        }
        catch (Exception ex)
        {
            _profileTempLog = null;
            AppLog.Warn(Name, $"Nepodarilo sa otvoriť log teplôt profilu: {ex.Message}");
        }
    }

    /// <summary>Closes the per-profile temperature log at the end of a run.</summary>
    private void CloseProfileTemperatureLog()
    {
        try
        {
            if (_profileTempLog is { } log)
            {
                AppLog.Info(Name, $"Log teplôt profilu uložený ({log.RowCount} riadkov): {log.FilePath}");
            }

            _profileTempLog?.Dispose();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _profileTempLog = null;
        }
    }

    private void StartCountdown()
    {
        if (_countdownTimer is null)
        {
            _countdownTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += (_, _) => UpdateCountdown();
        }

        UpdateCountdown();
        _countdownTimer.Start();
    }

    private void StopCountdown()
    {
        _countdownTimer?.Stop();
        ProfileTimeRemaining = string.Empty;
        ProfileCompletionText = string.Empty;
    }

    private void UpdateCountdown()
    {
        if (!IsProfileRunning || _profileEstimatedEnd is not { } end)
        {
            ProfileTimeRemaining = string.Empty;
            ProfileCompletionText = string.Empty;
            return;
        }

        TimeSpan left = end - DateTime.Now;
        if (left < TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
        }

        string hms = left.TotalHours >= 1
            ? $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}"
            : $"{left.Minutes:00}:{left.Seconds:00}";
        ProfileTimeRemaining = $"Zostáva {hms}";

        // Compact completion line for the card header, incl. the day name of completion.
        ProfileCompletionText = $"🏁 Koniec: {DayShort(end)} {end:dd.MM.yyyy} {end:HH:mm} · zostáva {hms}";
    }

    private double _profileProgress;
    public double ProfileProgress { get => _profileProgress; private set => SetProperty(ref _profileProgress, value); }

    private string _profileDurationText = "—";
    /// <summary>Total duration of the profile including cycles.</summary>
    public string ProfileDurationText { get => _profileDurationText; private set => SetProperty(ref _profileDurationText, value); }

    private string _profileScheduleText = string.Empty;
    /// <summary>Computed start / end schedule of the profile.</summary>
    public string ProfileScheduleText { get => _profileScheduleText; private set => SetProperty(ref _profileScheduleText, value); }

    private string _profileWarnings = string.Empty;
    /// <summary>"Smart check" validation warnings for the current profile.</summary>
    public string ProfileWarnings { get => _profileWarnings; private set { if (SetProperty(ref _profileWarnings, value)) OnPropertyChanged(nameof(HasProfileWarnings)); } }

    public bool HasProfileWarnings => !string.IsNullOrEmpty(ProfileWarnings);

    /// <summary>Device temperature envelope used by the profile "smart check".</summary>
    private (double Min, double Max) DeviceTempRange => IsPolEko
        ? (0, 300)    // SLN drying oven: ambient .. +300 °C, no sub-zero capability
        : IsSika
            ? (-50, 165) // SIKA TP Premium bath / dry block (documented MinTemp/MaxTemp example)
            : (-80, 200); // Vötsch climate chamber

    private void ValidateProfile()
    {
        (double tMin, double tMax) = DeviceTempRange;
        var issues = new List<string>();
        for (int i = 0; i < Segments.Count; i++)
        {
            SegmentViewModel s = Segments[i];
            if (s.DurationMinutes <= 0)
            {
                issues.Add($"Segment {i + 1}: trvanie ≤ 0");
            }

            if (s.TargetTemperature < tMin || s.TargetTemperature > tMax)
            {
                issues.Add($"Segment {i + 1}: teplota mimo rozsahu zariadenia [{tMin:0}; {tMax:0}] °C");
            }

            if (SupportsHumidity && s.TargetHumidity is { } hh && (hh < 0 || hh > 100))
            {
                issues.Add($"Segment {i + 1}: vlhkosť mimo 0–100 %");
            }
        }

        ProfileWarnings = issues.Count == 0 ? string.Empty : "⚠ " + string.Join(" · ", issues);
    }

    public AsyncRelayCommand StartProfileCommand { get; }

    /// <summary>Starts the profile selected in the history list (▶ on the dashboard card).</summary>
    public AsyncRelayCommand StartSelectedProfileCommand { get; }

    /// <summary>One-click launch of a specific saved profile (quick profile buttons on the card).</summary>
    public AsyncRelayCommand<TestProfile?> QuickStartProfileCommand { get; }

    /// <summary>Pauses or resumes the running profile (⏸ / ▶ on the dashboard card).</summary>
    public RelayCommand PauseResumeProfileCommand { get; }

    public RelayCommand StopProfileCommand { get; }

    /// <summary>Resumes an interrupted run (app crash / connection loss) from its checkpoint.</summary>
    public AsyncRelayCommand ResumeInterruptedRunCommand { get; }

    /// <summary>Discards the saved checkpoint of an interrupted run.</summary>
    public RelayCommand DiscardInterruptedRunCommand { get; }

    /// <summary>Cancels a quick-started (or loaded) profile and clears it off the card.</summary>
    public RelayCommand CancelProfileCommand { get; }
    public RelayCommand AddSegmentCommand { get; }
    public RelayCommand AddSegmentBeforeCommand { get; }
    public RelayCommand AddSegmentAfterCommand { get; }
    public RelayCommand RemoveSegmentCommand { get; }
    public RelayCommand MoveSegmentUpCommand { get; }
    public RelayCommand MoveSegmentDownCommand { get; }
    public RelayCommand ToggleSegmentsExpandCommand { get; }

    private bool _isSegmentsExpanded;
    /// <summary>When set, the segment grid is expanded (preview/run panels collapsed).</summary>
    public bool IsSegmentsExpanded { get => _isSegmentsExpanded; set => SetProperty(ref _isSegmentsExpanded, value); }

    // --- Test queue ---
    public ObservableCollection<QueuedProfileViewModel> Queue { get; } = new();

    private QueuedProfileViewModel? _selectedQueueItem;
    public QueuedProfileViewModel? SelectedQueueItem
    {
        get => _selectedQueueItem;
        set { if (SetProperty(ref _selectedQueueItem, value)) RemoveFromQueueCommand.RaiseCanExecuteChanged(); }
    }

    public AsyncRelayCommand StartQueueCommand { get; }
    public RelayCommand AddToQueueCommand { get; }
    public RelayCommand RemoveFromQueueCommand { get; }
    public RelayCommand ClearQueueCommand { get; }

    private void AddToQueue()
    {
        Queue.Add(new QueuedProfileViewModel(BuildProfile()));
        RefreshQueueCommands();
    }

    private void RemoveFromQueue()
    {
        if (SelectedQueueItem is { } item)
        {
            Queue.Remove(item);
            RefreshQueueCommands();
        }
    }

    private void RefreshQueueCommands()
    {
        StartQueueCommand.RaiseCanExecuteChanged();
        RemoveFromQueueCommand.RaiseCanExecuteChanged();
    }

    // ---- Rad profilov na dashboarde: max 3 uložené profily spustené za sebou ----

    /// <summary>Maximum profiles that can be chained back-to-back from the dashboard.</summary>
    public const int MaxChainedProfiles = 3;

    /// <summary>Saved profiles queued on the dashboard card to run back-to-back, in order.</summary>
    public ObservableCollection<TestProfile> ProfileChain { get; } = new();

    /// <summary>True when at least one profile is in the dashboard chain.</summary>
    public bool HasChainedProfiles => ProfileChain.Count > 0;

    /// <summary>"1/3" fill indicator of the dashboard chain.</summary>
    public string ChainCountText => $"{ProfileChain.Count}/{MaxChainedProfiles}";

    /// <summary>Total duration of all chained profiles (incl. cycles).</summary>
    public string ChainDurationText =>
        "Spolu " + FormatDuration(TimeSpan.FromTicks(ProfileChain.Sum(p => p.TotalDuration.Ticks)));

    public RelayCommand AddToChainCommand { get; }
    public RelayCommand<TestProfile> RemoveFromChainCommand { get; }
    public RelayCommand ClearChainCommand { get; }
    public AsyncRelayCommand StartChainCommand { get; }

    private void AddToChain()
    {
        if (SelectedHistoryProfile is not { } profile)
        {
            return;
        }

        if (ProfileChain.Count >= MaxChainedProfiles)
        {
            ShowActionInfo($"Rad je plný – maximálne {MaxChainedProfiles} profily za sebou.");
            return;
        }

        ProfileChain.Add(profile);
    }

    private void RemoveFromChain(TestProfile? profile)
    {
        if (profile is not null)
        {
            ProfileChain.Remove(profile);
        }
    }

    private void OnChainChanged()
    {
        OnPropertyChanged(nameof(HasChainedProfiles));
        OnPropertyChanged(nameof(ChainCountText));
        OnPropertyChanged(nameof(ChainDurationText));
        AddToChainCommand.RaiseCanExecuteChanged();
        ClearChainCommand.RaiseCanExecuteChanged();
        StartChainCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Runs the dashboard chain (1–3 profiles back-to-back) and empties it afterwards.</summary>
    private async Task StartChainAsync()
    {
        List<TestProfile> profiles = ProfileChain.ToList();
        await RunSequenceAsync(profiles);
        ProfileChain.Clear();
    }

    private void SeedDefaultProfile()
    {
        if (SupportsHumidity)
        {
            Segments.Add(new SegmentViewModel(new ProfileSegment { Name = "Ohrev", TargetTemperature = 60, TargetHumidity = 80, Duration = TimeSpan.FromMinutes(30), IsRamp = true }));
            Segments.Add(new SegmentViewModel(new ProfileSegment { Name = "Plato", TargetTemperature = 60, TargetHumidity = 80, Duration = TimeSpan.FromMinutes(60), IsRamp = false }));
            Segments.Add(new SegmentViewModel(new ProfileSegment { Name = "Chladenie", TargetTemperature = 25, TargetHumidity = 50, Duration = TimeSpan.FromMinutes(30), IsRamp = true }));
        }
        else
        {
            Segments.Add(new SegmentViewModel(new ProfileSegment { Name = "Ohrev", TargetTemperature = 85, Duration = TimeSpan.FromMinutes(30), IsRamp = true }));
            Segments.Add(new SegmentViewModel(new ProfileSegment { Name = "Plato", TargetTemperature = 85, Duration = TimeSpan.FromMinutes(60), IsRamp = false }));
            Segments.Add(new SegmentViewModel(new ProfileSegment { Name = "Chladenie", TargetTemperature = -40, Duration = TimeSpan.FromMinutes(30), IsRamp = true }));
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

    private Task StartProfileAsync() => RunSequenceAsync(new List<TestProfile> { BuildProfile() });

    private Task StartQueueAsync() => RunSequenceAsync(Queue.Select(q => q.Profile).ToList());

    private bool CanStartSelectedProfile() =>
        IsConnected && IsOperable && !IsProfileRunning &&
        (SelectedHistoryProfile is not null || Segments.Count > 0);

    /// <summary>
    /// Dashboard ▶: loads the profile picked in the list (if any) into the editor and
    /// starts it, so a chamber can be launched from a saved profile without opening it.
    /// </summary>
    private Task StartSelectedProfileAsync()
    {
        if (SelectedHistoryProfile is { } profile)
        {
            ApplyProfile(profile);
        }

        return StartProfileAsync();
    }

    /// <summary>
    /// Quick profile button: loads the given saved profile into the editor and starts
    /// it in one click, without picking it from the dropdown first.
    /// </summary>
    private Task QuickStartProfileAsync(TestProfile? profile)
    {
        if (profile is null)
        {
            return Task.CompletedTask;
        }

        ApplyProfile(profile);
        return StartProfileAsync();
    }

    /// <summary>Dashboard ⏸ / ▶: toggles pause on the running profile.</summary>
    private void PauseResumeProfile()
    {
        if (_activeRunner is not { } runner)
        {
            // The runner exists only once segments actually execute; during a
            // delayed-start countdown there is nothing to pause yet.
            StatusMessage = "Profil ešte nebeží (čaká na naplánovaný štart) – pozastaviť sa dá až po štarte.";
            return;
        }

        if (runner.IsPaused)
        {
            runner.Resume();
            IsProfilePaused = false;
            StatusMessage = $"Profil \"{ProfileName}\" pokračuje.";
            _audit.Log(Name, "Profil pokračuje", ProfileName);
        }
        else
        {
            runner.Pause();
            IsProfilePaused = true;
            ProfileStatus = $"⏸ Pozastavené · \"{ProfileName}\"";
            StatusMessage = ProfileStatus;
            _audit.Log(Name, "Profil pozastavený", ProfileName);
        }
    }

    /// <summary>
    /// Runs one or more profiles back-to-back (the test queue). When
    /// <paramref name="resume"/> is set, the sequence continues from the saved
    /// checkpoint instead of the beginning (crash / connection-loss recovery).
    /// </summary>
    private async Task RunSequenceAsync(IReadOnlyList<TestProfile> profiles, ProfileRunState? resume = null)
    {
        if (profiles.Count == 0)
        {
            return;
        }

        _profileCts = new CancellationTokenSource();
        CancellationToken token = _profileCts.Token;
        IsProfileRunning = true;
        ProfileProgress = 0;
        _profileNowFraction = 0; // fresh run: the preview "now" marker starts at the beginning
        _runSequence = profiles;
        _lastCheckpointKey = (-1, -1, -1);
        _checkpointWarned = false;
        ClearInterruptedRunOffer();

        // Safety: lock the device the moment a test/quick profile starts.
        AutoLockOnRun(profiles.Count > 1 ? "spustená fronta profilov" : "spustený profil");

        try
        {
            if (resume is null && UseDelayedStart && ScheduledStart > DateTime.Now)
            {
                await WaitForScheduledStartAsync(token);
            }

            // On resume the start/end markers are back-dated by the already
            // completed test time, so the countdown / Gantt show the true remainder.
            TimeSpan alreadyDone = resume?.CompletedDuration() ?? TimeSpan.Zero;
            _profileActualStart = DateTime.Now - alreadyDone;
            _profileEstimatedEnd = _profileActualStart + TimeSpan.FromTicks(profiles.Sum(p => p.TotalDuration.Ticks));
            RaiseGanttTimes();
            StartCountdown();
            RecalculateTiming();
            OpenProfileTemperatureLog(profiles.Count > 1 ? $"Fronta ({profiles.Count} profilov)" : profiles[0].Name);

            int firstProfile = resume is null ? 0 : Math.Clamp(resume.ProfileIndex, 0, profiles.Count - 1);
            for (int i = firstProfile; i < profiles.Count; i++)
            {
                TestProfile profile = profiles[i];
                if (profiles.Count > 1)
                {
                    // Load the chained profile into the editor so the preview chart,
                    // name and "now" marker follow the profile that actually runs.
                    ApplyProfile(profile);
                }

                ProfileRunPosition? position = resume is not null && i == firstProfile ? resume.ToPosition() : null;
                if (position is null)
                {
                    ShowActionInfo($"▶ Spustený profil „{profile.Name}\" ({i + 1}/{profiles.Count})");
                    _audit.Log(Name, "Štart profilu", $"{profile.Name} ({i + 1}/{profiles.Count}, {profile.Cycles} cyklov)");
                    AppLog.Info(Name, $"Štart profilu \"{profile.Name}\" ({i + 1}/{profiles.Count}, {profile.Cycles} cyklov, {profile.Segments.Count} segmentov).");
                }
                else
                {
                    string place = $"cyklus {position.Cycle + 1}/{Math.Max(1, profile.Cycles)} · segment {position.SegmentIndex + 1}/{profile.Segments.Count}";
                    ShowActionInfo($"▶ Profil „{profile.Name}\" pokračuje od miesta prerušenia ({place})");
                    _audit.Log(Name, "Profil pokračuje po prerušení", $"{profile.Name} · {place}");
                    AppLog.Info(Name, $"Profil \"{profile.Name}\" pokračuje od miesta prerušenia: {place}, {position.ElapsedInSegment:hh\\:mm\\:ss} v segmente.");
                }

                await RunProfileCoreAsync(profile, i, profiles.Count, position, token);
            }

            DeleteRunCheckpoint();
            ProfileProgress = 100;
            ProfileStatus = profiles.Count > 1 ? "Fronta dokončená." : "Profil dokončený.";
            StatusMessage = ProfileStatus;
            _audit.Log(Name, "Profil dokončený", profiles.Count > 1 ? $"Fronta {profiles.Count} profilov" : profiles[0].Name);
            AppLog.Info(Name, ProfileStatus);
            DesktopNotifier.Notify(
                $"{ProfileStatus.TrimEnd('.')} · {Name}",
                profiles.Count > 1
                    ? $"Dokončených {profiles.Count} profilov ({DateTime.Now:HH:mm})."
                    : $"\"{profiles[0].Name}\" dokončený o {DateTime.Now:HH:mm}.",
                DesktopNotificationKind.Success);
            await NotifyCompletionAsync();
        }
        catch (OperationCanceledException)
        {
            // A deliberate stop must not leave a resume checkpoint behind.
            DeleteRunCheckpoint();
            ProfileStatus = "Profil zrušený.";
            _audit.Log(Name, "Profil zrušený", string.Empty);
            AppLog.Warn(Name, "Profil zrušený používateľom.");
            if (_powerOffOnProfileCancel)
            {
                try
                {
                    await _client.StopAsync();
                    SetManualStarted(false);
                    ShowActionInfo("⏹ Profil zastavený – výkon komory VYPNUTÝ");
                }
                catch (Exception ex)
                {
                    AppLog.Warn(Name, $"Vypnutie výkonu po zastavení profilu zlyhalo: {ex.Message}");
                }
            }
            else
            {
                StatusMessage = "Profil zrušený.";
            }
        }
        catch (Exception ex)
        {
            // The run died on an error (typically a lost connection). The last
            // checkpoint stays on disk, so once the link is back the run can be
            // resumed – automatically after a reconnect, or via the banner.
            ProfileStatus = $"⚠ Profil prerušený chybou: {ex.Message}";
            StatusMessage = ProfileStatus;
            _audit.Log(Name, "Profil prerušený chybou", ex.Message);
            AppLog.Error(Name, ex);
            DesktopNotifier.Notify(
                $"Profil prerušený · {Name}",
                $"{ex.Message} – beh sa dá obnoviť od miesta prerušenia.",
                DesktopNotificationKind.Warning);
        }
        finally
        {
            _powerOffOnProfileCancel = false;
            IsProfileRunning = false;
            _activeRunner = null;
            _runSequence = null;
            _profileActualStart = null;
            _profileEstimatedEnd = null;
            RaiseGanttTimes();
            StopCountdown();
            CloseProfileTemperatureLog();
            _profileCts?.Dispose();
            _profileCts = null;
            RecalculateTiming();
            LoadInterruptedRun();
        }
    }

    private async Task RunProfileCoreAsync(
        TestProfile profile, int indexInQueue, int queueCount, ProfileRunPosition? resumeFrom, CancellationToken token)
    {
        double startTemp = MeasuredTemperature ?? profile.Segments[0].TargetTemperature;
        double? startHum = SupportsHumidity ? MeasuredHumidity : null;

        var runner = new ProfileRunner(_client, TimeSpan.FromSeconds(ProfileUpdateIntervalSeconds));
        _activeRunner = runner;
        double totalSeconds = Math.Max(1, profile.TotalDuration.TotalSeconds);
        double singlePassSeconds = Math.Max(1, profile.SinglePassDuration.TotalSeconds);

        runner.Progress += (_, e) =>
        {
            // Checkpoint on the worker thread (file I/O must not block the UI).
            SaveRunCheckpoint(indexInQueue, e);

            RunOnUi(() =>
            {
                double completedBeforeSegment = ElapsedBeforeSegment(profile, e.SegmentIndex);
                double doneThisPass = completedBeforeSegment + e.Segment.Duration.TotalSeconds * e.Fraction;
                double overallSeconds = e.Cycle * singlePassSeconds + doneThisPass;
                double profileFraction = Math.Clamp(overallSeconds / totalSeconds, 0d, 1d);
                ProfileProgress = Math.Clamp((indexInQueue + profileFraction) / queueCount * 100d, 0, 100);
                ProfileStatus =
                    (e.IsSoaking ? "⏳ Soak · " : string.Empty) +
                    (queueCount > 1 ? $"[{indexInQueue + 1}/{queueCount}] " : string.Empty) +
                    $"\"{profile.Name}\" · cyklus {e.Cycle + 1}/{profile.Cycles} · segment {e.SegmentIndex + 1}/{profile.Segments.Count} " +
                    $"· {e.TemperatureSetpoint:0.0} °C" +
                    (e.HumiditySetpoint is { } h ? $", {h:0.0} %" : string.Empty);

                // Advance the "now" marker on the profile preview.
                _profileNowFraction = Math.Clamp(doneThisPass / singlePassSeconds, 0d, 1d);
                BuildProfilePreview();

                // Per-profile temperature record (set point vs measured chamber temperature).
                _profileTempLog?.Log(DateTime.Now, e.TemperatureSetpoint, MeasuredTemperature,
                    e.HumiditySetpoint, SupportsHumidity ? MeasuredHumidity : null);
            });
        };

        await runner.RunAsync(profile, startTemp, startHum, resumeFrom, token);
    }

    private async Task WaitForScheduledStartAsync(CancellationToken token)
    {
        while (DateTime.Now < ScheduledStart)
        {
            token.ThrowIfCancellationRequested();
            TimeSpan remaining = ScheduledStart - DateTime.Now;
            ProfileStatus = $"Naplánované na {ScheduledStart:dd.MM HH:mm} · štart o {FormatCountdown(remaining)}";
            StatusMessage = ProfileStatus;
            TimeSpan tick = remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
            if (tick > TimeSpan.Zero)
            {
                await Task.Delay(tick, token);
            }
        }
    }

    private async Task NotifyCompletionAsync()
    {
        if (!_email.CanSend)
        {
            return;
        }

        string subject = $"Profil dokončený: {ProfileName} ({Name})";
        string body =
            $"Testovací profil \"{ProfileName}\" na komore \"{Name}\" bol dokončený " +
            $"{DateTime.Now:dd.MM.yyyy HH:mm:ss}.\r\nCelkové trvanie: {ProfileDurationText}.";

        EmailResult result = await _email.SendAsync(subject, body);
        if (result.Sent)
        {
            StatusMessage = "Profil dokončený · e-mail odoslaný.";
        }
        else if (result.Error is not null)
        {
            StatusMessage = $"Profil dokončený · e-mail zlyhal: {result.Error}";
        }
    }

    private static string FormatCountdown(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
        if (t.TotalMinutes >= 1) return $"{t.Minutes} min {t.Seconds} s";
        return $"{t.Seconds} s";
    }

    private static double ElapsedBeforeSegment(TestProfile profile, int segmentIndex)
    {
        double seconds = 0;
        for (int i = 0; i < segmentIndex && i < profile.Segments.Count; i++)
        {
            seconds += profile.Segments[i].Duration.TotalSeconds;
        }

        return seconds;
    }

    private void StopProfile()
    {
        // Pressing Stop on a profile should also cut the chamber output, not just
        // end the schedule. The power-off runs after the runner unwinds (in the
        // cancellation handler of RunSequenceAsync) so it can't race a pending write.
        _powerOffOnProfileCancel = true;
        _profileCts?.Cancel();
    }

    private bool CanCancelProfile() =>
        IsUnlocked && (IsProfileRunning || Segments.Count > 0 || SelectedHistoryProfile is not null);

    /// <summary>
    /// Cancels a quick-started (or otherwise loaded) profile: stops the run if any,
    /// then clears the profile out of the editor / preview so the card stops showing
    /// it as the active "testovací profil". The running profile executes from a
    /// snapshot (see <see cref="BuildProfile"/>), so clearing the editor is safe.
    /// </summary>
    private void CancelProfile()
    {
        bool wasRunning = IsProfileRunning;
        if (wasRunning)
        {
            StopProfile();
        }

        Segments.Clear();
        SelectedSegment = null;
        ProfileName = "Profil 1";
        SelectedHistoryProfile = null;
        RecalculateTiming();
        OnPropertyChanged(nameof(HasProfilePreview));
        BuildProfilePreview();
        RefreshCommands();
        ShowActionInfo(wasRunning
            ? "✕ Profil zastavený a odobraný z karty."
            : "✕ Profil odobraný z karty.");
    }

    #region Crash recovery (resume an interrupted run)

    /// <summary>Checkpoints are written at most this often (except on segment changes).</summary>
    private const double CheckpointMinIntervalSeconds = 5;

    /// <summary>
    /// An interruption younger than this resumes automatically after (re)connecting –
    /// the crash-and-restart / short-outage case. Older checkpoints only show the
    /// banner, because after a long unmanaged gap the operator should decide.
    /// </summary>
    private static readonly TimeSpan AutoResumeMaxAge = TimeSpan.FromMinutes(30);

    private IReadOnlyList<TestProfile>? _runSequence;
    private ProfileRunState? _pendingResume;
    private DateTime _lastCheckpointUtc;
    private (int Profile, int Cycle, int Segment) _lastCheckpointKey = (-1, -1, -1);
    private bool _checkpointWarned;

    /// <summary>True when a saved checkpoint of an interrupted run is waiting to be resumed.</summary>
    public bool HasInterruptedRun => _pendingResume is not null;

    private string _interruptedRunInfo = string.Empty;
    /// <summary>Banner text describing the interrupted run (profile, position, when saved).</summary>
    public string InterruptedRunInfo { get => _interruptedRunInfo; private set => SetProperty(ref _interruptedRunInfo, value); }

    /// <summary>
    /// Loads the saved checkpoint for this chamber (start-up and after every run)
    /// and shows / hides the resume banner accordingly.
    /// </summary>
    private void LoadInterruptedRun()
    {
        if (IsProfileRunning)
        {
            return;
        }

        ProfileRunState? state = null;
        try
        {
            state = _runStateStore.Load(Id);
        }
        catch (Exception ex)
        {
            AppLog.Warn(Name, $"Checkpoint prerušeného profilu sa nepodarilo načítať: {ex.Message}");
        }

        if (state?.CurrentProfile is { } profile && profile.Segments.Count > 0)
        {
            _pendingResume = state;
            string queue = state.Profiles.Count > 1 ? $" [{state.ProfileIndex + 1}/{state.Profiles.Count}]" : string.Empty;
            InterruptedRunInfo =
                $"⚠ Prerušený beh: „{profile.Name}“{queue} · cyklus {state.Cycle + 1}/{Math.Max(1, profile.Cycles)} " +
                $"· segment {state.SegmentIndex + 1}/{profile.Segments.Count} · prerušené {state.SavedAt:dd.MM. HH:mm:ss}";
            AppLog.Info(Name, $"Nájdený prerušený profil \"{profile.Name}\" ({state.SavedAt:dd.MM.yyyy HH:mm:ss}) – možno pokračovať od miesta prerušenia.");
        }
        else
        {
            _pendingResume = null;
            InterruptedRunInfo = string.Empty;
        }

        OnPropertyChanged(nameof(HasInterruptedRun));
        ResumeInterruptedRunCommand.RaiseCanExecuteChanged();
        DiscardInterruptedRunCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Hides the resume banner (without touching the file on disk).</summary>
    private void ClearInterruptedRunOffer()
    {
        _pendingResume = null;
        InterruptedRunInfo = string.Empty;
        OnPropertyChanged(nameof(HasInterruptedRun));
        ResumeInterruptedRunCommand.RaiseCanExecuteChanged();
        DiscardInterruptedRunCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Banner ▶: continues the interrupted run from its saved checkpoint.</summary>
    private async Task ResumeInterruptedRunAsync()
    {
        if (_pendingResume is not { } state || state.CurrentProfile is not { } profile)
        {
            return;
        }

        // Show the resumed profile in the editor / preview.
        ApplyProfile(profile);
        await RunSequenceAsync(state.Profiles, state);
    }

    /// <summary>Banner ✕: throws the checkpoint away (the run stays finished-as-interrupted).</summary>
    private void DiscardInterruptedRun()
    {
        try
        {
            _runStateStore.Delete(Id);
        }
        catch (Exception ex)
        {
            AppLog.Warn(Name, $"Checkpoint sa nepodarilo zmazať: {ex.Message}");
        }

        _audit.Log(Name, "Prerušený profil zahodený", _pendingResume?.CurrentProfile?.Name ?? string.Empty);
        StatusMessage = "Prerušený profil zahodený – beh nebude pokračovať.";
        ClearInterruptedRunOffer();
    }

    /// <summary>
    /// Called right after a successful (re)connect: a fresh checkpoint (typically an
    /// app crash + restart, or a short network outage during a run) continues
    /// automatically; an older one only offers the banner so the operator decides.
    /// </summary>
    private void TryAutoResumeAfterConnect()
    {
        if (_pendingResume is not { } state || IsProfileRunning)
        {
            return;
        }

        if (DateTimeOffset.Now - state.SavedAt > AutoResumeMaxAge)
        {
            StatusMessage = "Nájdený prerušený profil – pokračovanie potvrď tlačidlom „Pokračovať v profile“.";
            return;
        }

        RunOnUi(() =>
        {
            if (_pendingResume is null || IsProfileRunning)
            {
                return;
            }

            _audit.Log(Name, "Automatické pokračovanie profilu", state.CurrentProfile?.Name ?? string.Empty);
            AppLog.Info(Name, "Čerstvý prerušený profil – po pripojení pokračujem automaticky od miesta prerušenia.");
            _ = SafeResumeInterruptedRunAsync();
        });
    }

    private async Task SafeResumeInterruptedRunAsync()
    {
        try
        {
            await ResumeInterruptedRunAsync();
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    /// <summary>
    /// Persists the current run position. Runs on the runner's worker thread on
    /// every set point write, throttled so the file is written at most every
    /// <see cref="CheckpointMinIntervalSeconds"/> (segment / cycle changes always
    /// write, so a resume never replays a finished segment).
    /// </summary>
    private void SaveRunCheckpoint(int indexInQueue, ProfileProgressEventArgs e)
    {
        if (_runSequence is not { } sequence)
        {
            return;
        }

        (int, int, int) key = (indexInQueue, e.Cycle, e.SegmentIndex);
        DateTime now = DateTime.UtcNow;
        if (key == _lastCheckpointKey && (now - _lastCheckpointUtc).TotalSeconds < CheckpointMinIntervalSeconds)
        {
            return;
        }

        try
        {
            _runStateStore.Save(new ProfileRunState
            {
                ChamberId = Id,
                SavedAt = DateTimeOffset.Now,
                Profiles = sequence.ToList(),
                ProfileIndex = indexInQueue,
                Cycle = e.Cycle,
                SegmentIndex = e.SegmentIndex,
                ElapsedInSegmentSeconds = e.ElapsedInSegment.TotalSeconds,
                SegmentStartTemperature = e.SegmentStartTemperature,
                SegmentStartHumidity = e.SegmentStartHumidity,
            });
            _lastCheckpointKey = key;
            _lastCheckpointUtc = now;
        }
        catch (Exception ex)
        {
            if (!_checkpointWarned)
            {
                // Warn once per run; a full disk must not spam the log every 5 s.
                _checkpointWarned = true;
                AppLog.Warn(Name, $"Checkpoint behu sa nepodarilo uložiť (obnova po páde nebude dostupná): {ex.Message}");
            }
        }
    }

    /// <summary>Removes the checkpoint after a completed or deliberately stopped run.</summary>
    private void DeleteRunCheckpoint()
    {
        _lastCheckpointKey = (-1, -1, -1);
        try
        {
            _runStateStore.Delete(Id);
        }
        catch (Exception ex)
        {
            AppLog.Warn(Name, $"Checkpoint sa nepodarilo zmazať: {ex.Message}");
        }
    }

    #endregion

    private void AddSegment()
    {
        var segment = new SegmentViewModel(new ProfileSegment
        {
            Name = $"Segment {Segments.Count + 1}",
            TargetTemperature = MeasuredTemperature ?? 25,
            TargetHumidity = SupportsHumidity ? (MeasuredHumidity ?? 50) : null,
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
            TargetTemperature = MeasuredTemperature ?? 25,
            TargetHumidity = SupportsHumidity ? (MeasuredHumidity ?? 50) : null,
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

        StartProfileCommand.RaiseCanExecuteChanged();
        SaveToHistoryCommand.RaiseCanExecuteChanged();
    }

    private void MoveSegment(int delta)
    {
        if (SelectedSegment is not { } segment)
        {
            return;
        }

        int index = Segments.IndexOf(segment);
        int target = index + delta;
        if (target < 0 || target >= Segments.Count)
        {
            return;
        }

        Segments.Move(index, target);
        SelectedSegment = segment;
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

        StartProfileCommand.RaiseCanExecuteChanged();
        StartSelectedProfileCommand.RaiseCanExecuteChanged();
        SaveToHistoryCommand.RaiseCanExecuteChanged();
        ExportProfileCommand.RaiseCanExecuteChanged();
        CancelProfileCommand.RaiseCanExecuteChanged();
        RecalculateTiming();
    }

    private void OnSegmentEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SegmentViewModel.DurationMinutes)
            or nameof(SegmentViewModel.TargetTemperature)
            or nameof(SegmentViewModel.TargetHumidity)
            or nameof(SegmentViewModel.IsRamp))
        {
            RecalculateTiming();
        }
    }

    private void RecalculateTiming()
    {
        double minutes = Segments.Sum(s => s.DurationMinutes) * Math.Max(1, Cycles);
        var total = TimeSpan.FromMinutes(minutes);
        ProfileDurationText = FormatDuration(total);

        if (IsProfileRunning && _profileActualStart is { } start)
        {
            DateTime end = start + total;
            ProfileScheduleText = $"Spustené {start:HH:mm:ss} · koniec ~ {DayFull(end)} {end:dd.MM.yyyy} {end:HH:mm:ss}";
        }
        else if (UseDelayedStart)
        {
            DateTime begin = ScheduledStart;
            DateTime end = begin + total;
            ProfileScheduleText = $"Štart {DayShort(begin)} {begin:dd.MM HH:mm} · koniec ~ {DayFull(end)} {end:dd.MM.yyyy} {end:HH:mm}";
        }
        else
        {
            DateTime end = DateTime.Now + total;
            ProfileScheduleText = $"Ak spustíš teraz, koniec ~ {DayFull(end)} {end:dd.MM.yyyy} {end:HH:mm}";
        }

        BuildPreviewCharts();
        BuildProfilePreview();
        ValidateProfile();
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalMinutes < 1) return "< 1 min";
        int days = (int)t.TotalDays;
        string result = days > 0 ? $"{days} d " : string.Empty;
        return result + $"{t.Hours} h {t.Minutes} min";
    }

    #endregion

    #region Profile history

    public ObservableCollection<TestProfile> History { get; } = new();

    /// <summary>True when there is at least one saved profile.</summary>
    public bool HasProfiles => History.Count > 0;

    // ---- Quick-launch profile buttons (curated favourites) ----

    private List<string> _pinnedQuickProfiles = new();

    /// <summary>
    /// Saved profiles shown as one-click quick-launch buttons on the card. Only the
    /// profiles the admin explicitly pinned (in pinned order) are shown – nothing is
    /// shown until at least one is selected, so the card stays uncluttered.
    /// </summary>
    public IReadOnlyList<TestProfile> QuickProfiles =>
        _pinnedQuickProfiles
            .Select(n => History.FirstOrDefault(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

    /// <summary>True when no quick-launch profile is pinned yet (drives the "configure" hint).</summary>
    public bool HasNoQuickProfiles => QuickProfiles.Count == 0;

    /// <summary>True when at least one quick-launch profile button is shown.</summary>
    public bool HasQuickProfiles => QuickProfiles.Count > 0;

    /// <summary>The pinned profile names (admin editor list, with remove buttons).</summary>
    public IReadOnlyList<string> PinnedProfileNames => _pinnedQuickProfiles;

    /// <summary>True when the admin has pinned at least one profile (vs. the show-all default).</summary>
    public bool HasPinnedProfiles => _pinnedQuickProfiles.Count > 0;

    private bool _isEditingQuickProfiles;
    /// <summary>True while the admin quick-profile editor row is visible on the card.</summary>
    public bool IsEditingQuickProfiles { get => _isEditingQuickProfiles; set => SetProperty(ref _isEditingQuickProfiles, value); }

    private TestProfile? _profileToPin;
    /// <summary>Profile selected in the editor combo, ready to be pinned as a quick button.</summary>
    public TestProfile? ProfileToPin
    {
        get => _profileToPin;
        set { if (SetProperty(ref _profileToPin, value)) AddQuickProfileCommand.RaiseCanExecuteChanged(); }
    }

    public RelayCommand ToggleEditQuickProfilesCommand { get; }
    public RelayCommand AddQuickProfileCommand { get; }
    public RelayCommand<string> RemoveQuickProfileCommand { get; }

    private void AddQuickProfile()
    {
        if (ProfileToPin is not { } profile)
        {
            return;
        }

        if (!_pinnedQuickProfiles.Any(n => string.Equals(n, profile.Name, StringComparison.OrdinalIgnoreCase)))
        {
            _pinnedQuickProfiles.Add(profile.Name);
            RaiseQuickProfilesChanged();
            StatusMessage = $"Profil \"{profile.Name}\" pridaný medzi rýchle spustenie.";
        }
    }

    private void RemoveQuickProfile(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (_pinnedQuickProfiles.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            RaiseQuickProfilesChanged();
        }
    }

    /// <summary>Notifies the quick-profile bindings and triggers a config save (via QuickProfiles).</summary>
    private void RaiseQuickProfilesChanged()
    {
        OnPropertyChanged(nameof(QuickProfiles));
        OnPropertyChanged(nameof(HasQuickProfiles));
        OnPropertyChanged(nameof(HasNoQuickProfiles));
        OnPropertyChanged(nameof(PinnedProfileNames));
        OnPropertyChanged(nameof(HasPinnedProfiles));
    }

    private TestProfile? _selectedHistoryProfile;
    public TestProfile? SelectedHistoryProfile
    {
        get => _selectedHistoryProfile;
        set
        {
            if (SetProperty(ref _selectedHistoryProfile, value))
            {
                DisarmDelete(); // a different profile is selected – confirmation no longer applies
                LoadFromHistoryCommand.RaiseCanExecuteChanged();
                DeleteFromHistoryCommand.RaiseCanExecuteChanged();
                StartSelectedProfileCommand.RaiseCanExecuteChanged();
                AddToChainCommand.RaiseCanExecuteChanged();
                CancelProfileCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(HasProfilePreview));
                BuildProfilePreview();
            }
        }
    }

    public RelayCommand SaveToHistoryCommand { get; }
    public RelayCommand LoadFromHistoryCommand { get; }
    public RelayCommand DeleteFromHistoryCommand { get; }
    public RelayCommand ImportProfileCommand { get; }
    public RelayCommand ExportProfileCommand { get; }

    /// <summary>
    /// Reloads the saved-profile list from the shared store. Public so the shell can
    /// refresh it after the user creates a profile elsewhere (e.g. the quick builder).
    /// </summary>
    public void ReloadProfiles()
    {
        TestProfile? previouslySelected = SelectedHistoryProfile;
        RefreshHistory();
        if (previouslySelected is not null)
        {
            SelectedHistoryProfile = History.FirstOrDefault(p => p.Id == previouslySelected.Id);
        }
    }

    private void RefreshHistory()
    {
        History.Clear();

        // A humidity chamber can also run temperature-only profiles (e.g. those made
        // by the quick builder), so include those; hide humidity profiles on a
        // temperature-only chamber since it cannot honour the humidity channel.
        foreach (TestProfile profile in _store.LoadAll()
                     .Where(p => p.Kind == Kind || p.Kind == ChamberKind.TemperatureOnly))
        {
            History.Add(profile);
        }

        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(QuickProfiles));
        OnPropertyChanged(nameof(HasQuickProfiles));
        StartSelectedProfileCommand.RaiseCanExecuteChanged();
        QuickStartProfileCommand.RaiseCanExecuteChanged();
    }

    private void SaveToHistory()
    {
        TestProfile profile = BuildProfile();

        // Upsert by name: saving a profile with an existing name updates it instead
        // of piling up duplicates in the shared library.
        TestProfile? existing = _store.LoadAll()
            .FirstOrDefault(p => string.Equals(p.Name.Trim(), profile.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        profile.Id = existing?.Id ?? Guid.NewGuid();

        _store.Save(profile);
        RefreshHistory();
        SelectedHistoryProfile = History.FirstOrDefault(p => p.Id == profile.Id);
        StatusMessage = existing is null
            ? $"Profil \"{profile.Name}\" uložený do histórie."
            : $"Profil \"{profile.Name}\" aktualizovaný (prepísaná staršia verzia).";
    }

    private void LoadFromHistory()
    {
        if (SelectedHistoryProfile is { } profile)
        {
            ApplyProfile(profile);
            StatusMessage = $"Profil \"{profile.Name}\" načítaný z histórie.";
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

            string warnings = result.Warnings.Count > 0
                ? $" · {result.Warnings.Count} upozornení: {string.Join(" ", result.Warnings.Take(2))}"
                : string.Empty;
            StatusMessage = $"Importované ({result.FormatDescription}), {result.Profile.Segments.Count} segmentov{warnings}";
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

    private static string Sanitize(string name)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "profil" : name;
    }

    private void ApplyProfile(TestProfile profile)
    {
        ProfileName = profile.Name;
        Cycles = profile.Cycles;

        Segments.Clear();
        foreach (ProfileSegment segment in profile.Segments)
        {
            Segments.Add(new SegmentViewModel(segment));
        }

        SelectedSegment = Segments.FirstOrDefault();
        RecalculateTiming();
    }

    private CancellationTokenSource? _deleteArmCts;

    private bool _isDeleteArmed;
    /// <summary>Two-step delete: the first click arms the button, the second (within 3 s) deletes.</summary>
    public bool IsDeleteArmed
    {
        get => _isDeleteArmed;
        private set { if (SetProperty(ref _isDeleteArmed, value)) OnPropertyChanged(nameof(DeleteButtonText)); }
    }

    /// <summary>Delete button caption reflecting the confirmation state.</summary>
    public string DeleteButtonText => IsDeleteArmed ? "Naozaj zmazať?" : "Zmazať";

    private void DisarmDelete()
    {
        _deleteArmCts?.Cancel();
        _deleteArmCts = null;
        IsDeleteArmed = false;
    }

    private void DeleteFromHistory()
    {
        if (SelectedHistoryProfile is not { } profile)
        {
            return;
        }

        if (!IsDeleteArmed)
        {
            // First click only arms the confirmation; it auto-reverts after 3 s so a
            // forgotten armed button cannot delete something much later.
            IsDeleteArmed = true;
            _deleteArmCts?.Cancel();
            _deleteArmCts = new CancellationTokenSource();
            CancellationToken token = _deleteArmCts.Token;
            _ = Task.Delay(TimeSpan.FromSeconds(3), token).ContinueWith(
                _ => IsDeleteArmed = false,
                token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
            StatusMessage = $"Zmazať profil \"{profile.Name}\"? Potvrď druhým klikom do 3 sekúnd.";
            return;
        }

        DisarmDelete();
        _store.Delete(profile.Id);
        RefreshHistory();
        StatusMessage = $"Profil \"{profile.Name}\" odstránený z histórie.";
    }

    #endregion

    #region Recording

    private string _recordingPath =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            $"vc3_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    public string RecordingPath { get => _recordingPath; set => SetProperty(ref _recordingPath, value); }

    private bool _isRecording;
    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (SetProperty(ref _isRecording, value))
            {
                StartRecordingCommand.RaiseCanExecuteChanged();
                StopRecordingCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private long _recordedRows;
    public long RecordedRows { get => _recordedRows; private set => SetProperty(ref _recordedRows, value); }

    public RelayCommand StartRecordingCommand { get; }
    public RelayCommand StopRecordingCommand { get; }
    public RelayCommand BrowseRecordingPathCommand { get; }

    private void StartRecording()
    {
        try
        {
            _recorder = new CsvRecorder(RecordingPath);
            RecordedRows = _recorder.RowCount;
            IsRecording = true;
            StatusMessage = $"Zaznamenávam do {_recorder.FilePath}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Nedá sa spustiť záznam: {ex.Message}";
        }
    }

    private void StopRecording()
    {
        _recorder?.Dispose();
        _recorder = null;
        IsRecording = false;
        StatusMessage = "Záznam zastavený.";
    }

    private void BrowseRecordingPath()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Súbor záznamu",
            Filter = "CSV súbory (*.csv)|*.csv|Všetky súbory (*.*)|*.*",
            DefaultExt = ".csv",
            FileName = System.IO.Path.GetFileName(RecordingPath),
            InitialDirectory = System.IO.Path.GetDirectoryName(RecordingPath) ?? string.Empty,
        };

        if (dialog.ShowDialog() == true)
        {
            RecordingPath = dialog.FileName;
        }
    }

    #endregion

    #region Terminal

    public ObservableCollection<string> TerminalLines { get; } = new();

    private string _terminalInput = "$01I";
    public string TerminalInput
    {
        get => _terminalInput;
        set { if (SetProperty(ref _terminalInput, value)) SendTerminalCommand.RaiseCanExecuteChanged(); }
    }

    public AsyncRelayCommand SendTerminalCommand { get; }
    public RelayCommand ClearTerminalCommand { get; }

    /// <summary>Copies the whole visible TX/RX log to the clipboard (one frame per line).</summary>
    public RelayCommand CopyTerminalCommand { get; }

    private async Task SendTerminalAsync() => await _client.SendRawAsync(TerminalInput);

    private void CopyTerminalToClipboard()
    {
        if (TerminalLines.Count == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, TerminalLines));
            ShowActionInfo("✔ Terminál skopírovaný do schránky.");
        }
        catch
        {
            // The clipboard can be momentarily locked by another process; ignore.
        }
    }

    #region Set-point diagnostics (Vötsch ASCII-2)

    private double _diagTestTemperature = 30;
    /// <summary>Temperature used by the set-point test commands.</summary>
    public double DiagTestTemperature { get => _diagTestTemperature; set => SetProperty(ref _diagTestTemperature, value); }

    private string _diagResult = "Spusti test alebo pošli ukážkový príkaz nižšie.";
    /// <summary>Human-readable result / diagnosis of the last set-point test.</summary>
    public string DiagResult { get => _diagResult; private set => SetProperty(ref _diagResult, value); }

    public RelayCommand InsertReadCommandCommand { get; }
    public RelayCommand InsertWriteCommandCommand { get; }
    public RelayCommand InsertStopCommandCommand { get; }
    public AsyncRelayCommand RunSetpointDiagnosticCommand { get; }
    public AsyncRelayCommand ReadDigitalCommand { get; }
    public AsyncRelayCommand SimservProbeCommand { get; }
    public AsyncRelayCommand ReadProgramInfoCommand { get; }
    public AsyncRelayCommand ModbusScanCommand { get; }
    public AsyncRelayCommand SikaInfoReportCommand { get; }
    public AsyncRelayCommand SikaCalibrationStatusCommand { get; }

    /// <summary>
    /// Dumps a block of POL-EKO MODBUS registers so an undocumented value (e.g. the
    /// running program number) can be found empirically by comparing a scan taken
    /// while a program runs with one taken while idle.
    /// </summary>
    private async Task ModbusScanAsync()
    {
        if (_client is not VotschVc3.Core.Communication.PolEko.PolEkoClient poleko)
        {
            DiagResult = "MODBUS sken je len pre POL-EKO.";
            return;
        }

        DiagResult = "MODBUS sken prebieha… (môže trvať pár sekúnd)";
        DiagResult = await poleko.ScanRegistersAsync(64);
        AppLog.Info(Name, "[MODBUS] Sken registrov dokončený.");
    }

    /// <summary>Reads and shows the SIKA <c>getInfoReport</c> (device details, calibration dates, temp range).</summary>
    private async Task SikaInfoReportAsync()
    {
        if (_client is not VotschVc3.Core.Communication.Sika.SikaTpClient sika)
        {
            DiagResult = "getInfoReport je len pre SIKA REST-API.";
            return;
        }

        DiagResult = "Čítam getInfoReport…";
        DiagResult = await sika.SendRawAsync("getInfoReport");
        AppLog.Info(Name, "[SIKA] getInfoReport dokončený.");
    }

    /// <summary>Reads and shows the SIKA <c>getCalibrationStatus</c> (current calibration run, if any).</summary>
    private async Task SikaCalibrationStatusAsync()
    {
        if (_client is not VotschVc3.Core.Communication.Sika.SikaTpClient sika)
        {
            DiagResult = "getCalibrationStatus je len pre SIKA REST-API.";
            return;
        }

        DiagResult = "Čítam getCalibrationStatus…";
        DiagResult = await sika.SendRawAsync("getCalibrationStatus");
        AppLog.Info(Name, "[SIKA] getCalibrationStatus dokončený.");
    }

    public RelayCommand InsertSimservSetpointCommand { get; }
    public RelayCommand InsertSimservStartCommand { get; }

    /// <summary>
    /// Reads the controller's live program / operating state over SIMSERV, so we can
    /// see what is running even when the chamber is driven by another app or a
    /// program was started on the chamber's own panel. Some controllers do not
    /// implement every command (they answer with a negative error code, e.g. -5).
    /// </summary>
    private async Task ReadProgramInfoAsync()
    {
        if (!IsAsciiProtocol)
        {
            DiagResult = "Program info je pre Vötsch/Simpac (SIMSERV); iné protokoly ho nepodporujú.";
            return;
        }

        DiagResult = "Čítam stav programu (SIMSERV)…";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Stav komory / programu (SIMSERV) – živý stav regulátora bez ohľadu na to, kto ho spustil:");
        sb.AppendLine();

        async Task Probe(string label, string frame)
        {
            string sent = frame.TrimEnd('\r', '\n');
            try
            {
                string resp = await _client.SendRawAsync(sent);
                sb.AppendLine($"→ {label}: {sent}");
                sb.AppendLine($"← {(string.IsNullOrEmpty(resp) ? "(bez odpovede)" : resp)}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"→ {label}: {sent}");
                sb.AppendLine($"← CHYBA: {ex.Message}");
            }

            sb.AppendLine();
        }

        int id = Address;
        await Probe("Prevádzkový režim (10010: 0x02=manuál, 0x04=program)", SimservProtocol.BuildGetOperatingMode(id));
        await Probe("Prevádzkový stav (10012: 0x2=beží)", SimservProtocol.BuildGetOperatingStatus(id));
        await Probe("Beží program? (19062: 1/0)", SimservProtocol.BuildGetProgramStatus(id));
        await Probe("Názov programu (19031)", SimservProtocol.BuildGetProgramName(id));
        await Probe("Detaily programu (19064: názov, cykly, štart)", SimservProtocol.BuildGetProgramStart(id));

        sb.AppendLine("Poznámka: ak niektorý príkaz vráti záporné číslo (napr. -5 = neznámy príkaz), " +
            "tento regulátor danú funkciu nepodporuje. Podľa toho, čo vráti, viem doplniť čítanie " +
            "bežiaceho programu do hlavného okna.");
        DiagResult = sb.ToString();
        AppLog.Info(Name, "[SIMSERV] Program info dokončené.");
    }

    /// <summary>
    /// Sends a handful of SIMSERV function commands over the live connection and
    /// shows the raw replies, so we can tell whether this Simpac controller speaks
    /// SIMSERV on the current port. If it does, set point and start can be written
    /// with SET NOMINAL VALUE (11001) / SET DIGITALOUT (14001) instead of the
    /// ASCII-2 <c>$ddE</c> frame the controller ignores.
    /// </summary>
    private async Task SimservProbeAsync()
    {
        if (!IsAsciiProtocol)
        {
            DiagResult = "SIMSERV je pre Vötsch/Simpac; iné protokoly ho nepodporujú.";
            return;
        }

        DiagResult = "SIMSERV test prebieha… (môže trvať pár sekúnd)";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("SIMSERV test (Simpac) – posielam funkčné príkazy a čítam odpoveď:");
        sb.AppendLine();

        async Task Probe(string label, string frame)
        {
            string sent = frame.TrimEnd('\r', '\n');
            try
            {
                string resp = await _client.SendRawAsync(sent);
                sb.AppendLine($"→ {label}: {sent}");
                sb.AppendLine($"← {(string.IsNullOrEmpty(resp) ? "(bez odpovede)" : resp)}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"→ {label}: {sent}");
                sb.AppendLine($"← CHYBA: {ex.Message}");
            }

            sb.AppendLine();
        }

        int id = Address;
        await Probe("Typ regulátora (10017)", SimservProtocol.BuildGetChamberType(id));
        await Probe("Nameraná teplota (11004, idx 1)", SimservProtocol.BuildGetActualValue(id, 1));
        await Probe("Setpoint teplota (11002, idx 1)", SimservProtocol.BuildGetNominalValue(id, 1));
        await Probe("Prevádzkový režim (10010)", SimservProtocol.BuildGetOperatingMode(id));

        sb.AppendLine("Ak prišli zmysluplné odpovede (napr. „1¶44444\" = Simpac, „1¶24.5\" = teplota), " +
            "komora hovorí SIMSERV a viem cez to zapisovať setpoint aj štart. Ak je všade " +
            "„(bez odpovede)\" alebo timeout, komora na tomto porte SIMSERV nepodporuje.");
        DiagResult = sb.ToString();
        AppLog.Info(Name, "[SIMSERV] Probe dokončený.");
    }

    /// <summary>The read frame (no terminator) for the current address, e.g. "$01I".</summary>
    public string TestReadFrame => Ascii2Protocol.BuildReadCommand(Address, TerminatorValue).TrimEnd('\r', '\n');

    /// <summary>Builds the exact write frame (no terminator) this app would send.</summary>
    private string BuildWriteFrame(double temperature, bool start)
    {
        DigitalChannels digital = ParseDigitalText();
        digital.StartChannelIndex = StartChannelIndex;
        digital.Start = start;
        double humidity = SupportsHumidity && ControlHumidity ? ManualHumidity : 0d;
        var setpoints = new List<double> { temperature, humidity };
        return Ascii2Protocol.BuildWriteCommand(Address, setpoints, digital, AnalogChannelCount, TerminatorValue)
            .TrimEnd('\r', '\n');
    }

    /// <summary>
    /// Writes a test set point and reads it back, so we can tell whether the chamber
    /// actually accepts the write and, if not, point at the most likely cause.
    /// </summary>
    private async Task RunSetpointDiagnosticAsync()
    {
        if (!IsAsciiProtocol)
        {
            DiagResult = "Diagnostika je pre Vötsch ASCII-2; použi terminál daný protokolom zariadenia.";
            return;
        }

        DiagResult = "Prebieha test…";
        ChamberReading before = await _client.ReadAsync();
        double? spBefore = before.TemperatureSetpoint;
        AppLog.Info(Name, $"[DIAG] Pred zápisom · RAW=\"{before.Raw}\" · nameraná={before.Temperature:0.0} · setpoint={spBefore:0.0}");

        var digital = ParseDigitalText();
        digital.StartChannelIndex = StartChannelIndex;
        digital.Start = true;
        var setpoints = new List<double> { DiagTestTemperature };
        AppLog.Info(Name, $"[DIAG] Zapisujem setpoint {DiagTestTemperature:0.0} °C, štart kanál #{StartChannelIndex}=ON, adresa {Address}.");
        await _client.WriteSetpointsAsync(setpoints, digital);

        await Task.Delay(1500);
        ChamberReading after = await _client.ReadAsync();
        double? spAfter = after.TemperatureSetpoint;
        AppLog.Info(Name, $"[DIAG] Po zápise · RAW=\"{after.Raw}\" · nameraná={after.Temperature:0.0} · setpoint={spAfter:0.0}");

        bool accepted = spAfter is { } a && Math.Abs(a - DiagTestTemperature) < 1.0;
        if (accepted)
        {
            DiagResult = $"✅ Zápis funguje: setpoint sa zmenil na {spAfter:0.0} °C. " +
                "Ak sa teplota napriek tomu nemení, komora možno nie je spustená (štart kanál) alebo beží interný program.";
        }
        else
        {
            DiagResult =
                $"❌ Setpoint sa NEZMENIL (pred: {spBefore:0.0}, po: {spAfter:0.0} °C). Najpravdepodobnejšie príčiny:\n" +
                "1) Komora nie je v režime diaľkového/PC ovládania – na regulátore prepni na 'externé / SIMPATI / PC' ovládanie (čítanie ide vždy, zápis len v tomto režime).\n" +
                $"2) Nesprávny štart / 'condition on' kanál (teraz #{StartChannelIndex + 1}) – skús iný index v záložke Pripojenie.\n" +
                $"3) Nesprávna adresa (teraz {Address}) alebo počet analógových kanálov (teraz {AnalogChannelCount}).\n" +
                "4) Iný terminátor rámca (skús CR LF). Pozri App log pre presné RAW rámce (TX/RX).";
        }

        StatusMessage = accepted ? "Diagnostika: zápis setpointu OK." : "Diagnostika: zápis setpointu zlyhal – pozri výsledok v záložke.";
    }

    /// <summary>
    /// Reads the chamber's digital-channel block and lists which bits are set, so the
    /// operator can identify the real "start / condition on" channel: start the chamber
    /// manually on its panel, read here, and the bit that is 1 is the start channel.
    /// </summary>
    private async Task ReadDigitalAsync()
    {
        if (!IsAsciiProtocol)
        {
            return;
        }

        ChamberReading r = await _client.ReadAsync();
        bool[] bits = r.DigitalChannels.ToArray();
        var set = new List<int>();
        for (int i = 0; i < bits.Length; i++)
        {
            if (bits[i])
            {
                set.Add(i);
            }
        }

        string setList = set.Count > 0 ? string.Join(", ", set) : "žiadny";
        string block = r.DigitalChannels.ToProtocolString();
        DiagResult =
            $"Digitálne kanály z odpovede komory:\n{block}\n" +
            $"Nastavené bity (0-based index): {setList}.\n" +
            $"Teraz zapisujeme štart na index #{StartChannelIndex}.\n\n" +
            "Ako nájsť správny štart kanál: spusti komoru RUČNE na paneli (nech beží na výkon), " +
            "potom klikni „Prečítať digitálne“. Bit, ktorý sa zmení na '1', je štart / 'condition on' " +
            "kanál – zadaj jeho index do poľa „Štart kanál index“ v záložke Pripojenie a ulož.";
        AppLog.Info(Name, $"[DIAG] Digitálne: {block} · nastavené={setList} · RAW=\"{r.Raw}\"");
    }

    #endregion

    private void OnFrameExchanged(object? sender, FrameExchangedEventArgs e)
    {
        string tx = Visualise(e.Request);
        string rx = Visualise(e.Response);

        // Mirror control traffic (set points, stop, vendor commands) into the
        // diagnostic app log so a "connects but won't control" problem is
        // visible, including the controller's reply. The routine "$xxI" polling
        // reads are skipped so the log is not flooded.
        string req = e.Request;
        bool isRoutineRead = req.Length > 3 && req[0] == Ascii2Protocol.StartDelimiter
            && char.ToUpperInvariant(req[3]) == Ascii2Protocol.ReadCommand;
        if (!isRoutineRead)
        {
            AppLog.Info(Name, $"Príkaz TX: {tx}  →  RX: {(string.IsNullOrEmpty(rx) ? "(bez odpovede)" : rx)}");
        }

        RunOnUi(() =>
        {
            AppendTerminal($"{e.Timestamp:HH:mm:ss.fff}  TX  {tx}");
            AppendTerminal($"{e.Timestamp:HH:mm:ss.fff}  RX  {rx}");
        });
    }

    private void AppendTerminal(string line)
    {
        TerminalLines.Add(line);
        while (TerminalLines.Count > MaxTerminalLines)
        {
            TerminalLines.RemoveAt(0);
        }
    }

    private static string Visualise(string frame) => frame.Replace("\r", "<CR>").Replace("\n", "<LF>");

    #endregion

    #region Safety / watchdog

    private const int MaxPollFailures = 3;
    private readonly Dictionary<string, string> _alarms = new();
    private int _pollFailureCount;
    private bool _connectionLostHandled;
    private bool _bannerWarned;
    private CancellationTokenSource? _reconnectCts;

    /// <summary>
    /// True when a response is the controller runtime's version banner
    /// (e.g. "100 OK: Portable IEC 61131-3 RT Scheduler for Windows CE …")
    /// rather than an ASCII-2 measured-value frame. This is what a S!MPAC
    /// controller returns when you connect to the CoDeSys console port instead
    /// of the ASCII-2 data port.
    /// </summary>
    private static bool LooksLikeControllerBanner(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string u = raw.ToUpperInvariant();
        return u.Contains("OK:") &&
            (u.Contains("SCHEDULER") || u.Contains("IEC 61131") || u.Contains("REVISION") || u.Contains("WINDOWS CE"));
    }

    /// <summary>Logs the "wrong port" hint once per connection so it does not flood the log.</summary>
    private void WarnControllerBannerOnce(string raw)
    {
        if (_bannerWarned)
        {
            return;
        }

        _bannerWarned = true;
        AppLog.Warn(Name,
            $"Riadiaca jednotka odpovedala uvítacím bannerom (\"{raw.Trim()}\"), nie ASCII-2 dátami. " +
            $"Pravdepodobne nesprávny port – teraz {Port}. ASCII-2 rozhranie S!MPAC býva na porte 2051 " +
            $"(ASCII-1 2050, SIMSERV 2049; staršie riadiace jednotky ASCII na 2049). " +
            "Zmeň port v nastaveniach komory (Pripojenie a live).");
    }

    private bool _alarmsEnabled;
    public bool AlarmsEnabled
    {
        get => _alarmsEnabled;
        set
        {
            if (SetProperty(ref _alarmsEnabled, value) && !value)
            {
                ClearAlarm("temp");
                ClearAlarm("hum");
            }
        }
    }

    private double _tempMin = -45;
    public double TempMin { get => _tempMin; set { if (SetProperty(ref _tempMin, value)) OnPropertyChanged(nameof(RangeLabel)); } }

    private double _tempMax = 190;
    public double TempMax { get => _tempMax; set { if (SetProperty(ref _tempMax, value)) OnPropertyChanged(nameof(RangeLabel)); } }

    private double _humMin;
    public double HumMin { get => _humMin; set { if (SetProperty(ref _humMin, value)) OnPropertyChanged(nameof(RangeLabel)); } }

    private double _humMax = 100;
    public double HumMax { get => _humMax; set { if (SetProperty(ref _humMax, value)) OnPropertyChanged(nameof(RangeLabel)); } }

    private bool _autoStopOnAlarm = true;
    /// <summary>Stop a running profile when an alarm or connection loss occurs.</summary>
    public bool AutoStopOnAlarm { get => _autoStopOnAlarm; set => SetProperty(ref _autoStopOnAlarm, value); }

    private bool _autoReconnect = true;
    /// <summary>Automatically try to reconnect after a connection loss.</summary>
    public bool AutoReconnect { get => _autoReconnect; set => SetProperty(ref _autoReconnect, value); }

    private bool _isAlarm;
    public bool IsAlarm { get => _isAlarm; private set => SetProperty(ref _isAlarm, value); }

    private string _alarmMessage = "Bez alarmu.";
    public string AlarmMessage { get => _alarmMessage; private set => SetProperty(ref _alarmMessage, value); }

    private void CheckAlarms(ChamberReading reading)
    {
        if (!AlarmsEnabled)
        {
            return;
        }

        if (reading.Temperature is { } t)
        {
            if (t < TempMin || t > TempMax)
            {
                RaiseAlarm("temp", $"Teplota {t:0.0} °C mimo limitu [{TempMin:0.#}; {TempMax:0.#}]");
            }
            else
            {
                ClearAlarm("temp");
            }
        }

        if (SupportsHumidity && reading.Humidity is { } h)
        {
            if (h < HumMin || h > HumMax)
            {
                RaiseAlarm("hum", $"Vlhkosť {h:0.0} % mimo limitu [{HumMin:0.#}; {HumMax:0.#}]");
            }
            else
            {
                ClearAlarm("hum");
            }
        }
    }

    private void RaiseAlarm(string key, string message)
    {
        bool isNew = !_alarms.ContainsKey(key);
        _alarms[key] = message;
        UpdateAlarmState();

        if (isNew)
        {
            StatusMessage = $"⚠ ALARM: {message}";
            _audit.Log(Name, "ALARM", message);
            AppLog.Warn(Name, $"ALARM: {message}");
            DesktopNotifier.Notify($"⚠ ALARM · {Name}", message, DesktopNotificationKind.Alarm);
            _ = SendAlarmEmailAsync(message);
            if (AutoStopOnAlarm && IsProfileRunning)
            {
                StopProfile();
            }
        }
    }

    private void ClearAlarm(string key)
    {
        if (_alarms.Remove(key))
        {
            UpdateAlarmState();
        }
    }

    private void ClearAllAlarms()
    {
        if (_alarms.Count > 0)
        {
            _alarms.Clear();
            UpdateAlarmState();
        }
    }

    private void UpdateAlarmState()
    {
        IsAlarm = _alarms.Count > 0;
        AlarmMessage = _alarms.Count > 0 ? string.Join(" · ", _alarms.Values) : "Bez alarmu.";
    }

    private async Task<bool> OnPollFailureAsync(Exception ex)
    {
        _pollFailureCount++;
        StatusMessage = $"Chyba pollingu ({_pollFailureCount}/{MaxPollFailures}): {ex.Message}";
        if (_pollFailureCount >= MaxPollFailures)
        {
            await HandleConnectionLostAsync(ex.Message);
            return true;
        }

        return false;
    }

    private async Task HandleConnectionLostAsync(string reason)
    {
        if (_connectionLostHandled)
        {
            return;
        }

        _connectionLostHandled = true;
        StopPolling();
        await _client.DisconnectAsync();
        IsConnected = false;
        SetReadRunning(null);

        RaiseAlarm("link", $"Strata spojenia: {reason}");

        if (AutoReconnect)
        {
            StartReconnect();
        }
    }

    private void StartReconnect()
    {
        StopReconnect();
        _reconnectCts = new CancellationTokenSource();
        _ = ReconnectLoopAsync(_reconnectCts.Token);
    }

    private void StopReconnect()
    {
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;
    }

    private async Task ReconnectLoopAsync(CancellationToken token)
    {
        int delaySeconds = 2;
        while (!token.IsCancellationRequested)
        {
            StatusMessage = $"Pokus o opätovné pripojenie o {delaySeconds} s…";
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                await _client.ConnectAsync(BuildSettings(), token);
                // A TCP socket can open even when the controller never answers (or
                // only replies with a version banner on the wrong port), so confirm
                // the link with a real read before declaring success. This stops the
                // connect / drop flapping and the alarm-log spam.
                ChamberReading probe = await _client.ReadAsync(token);
                if (LooksLikeControllerBanner(probe.Raw))
                {
                    WarnControllerBannerOnce(probe.Raw);
                    throw new InvalidOperationException(
                        "Odpoveď je uvítací banner riadiacej jednotky, nie ASCII-2 dáta (skontroluj port).");
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                await _client.DisconnectAsync();
                StatusMessage = $"Opätovné pripojenie zlyhalo: {ex.Message}";
                delaySeconds = Math.Min(delaySeconds * 2, 30);
                continue;
            }

            _connectionLostHandled = false;
            _pollFailureCount = 0;
            IsConnected = true;
            ClearAlarm("link");
            StatusMessage = "Opätovne pripojené.";
            if (PollingEnabled)
            {
                StartPolling();
            }

            // A run that died on the connection loss continues from its checkpoint.
            TryAutoResumeAfterConnect();
            return;
        }
    }

    private Task SendAlarmEmailAsync(string message)
    {
        if (!_email.CanSend)
        {
            return Task.CompletedTask;
        }

        return _email.SendAsync(
            $"⚠ ALARM – {Name}",
            $"Komora \"{Name}\": {message}\r\nČas: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
    }

    #endregion

    #region Charting

    private const int LiveWindow = 600;
    private readonly List<LiveSample> _live = new();

    private static readonly Brush TempBrush = Freeze(0xFF, 0x8A, 0x5C);
    private static readonly Brush TempSpBrush = Freeze(0xFF, 0xC2, 0xA8);
    private static readonly Brush HumBrush = Freeze(0x4F, 0xB6, 0xFF);
    private static readonly Brush HumSpBrush = Freeze(0xA9, 0xDC, 0xFF);

    private IReadOnlyList<ChartSeries> _liveTempSeries = Array.Empty<ChartSeries>();
    public IReadOnlyList<ChartSeries> LiveTempSeries { get => _liveTempSeries; private set => SetProperty(ref _liveTempSeries, value); }

    private IReadOnlyList<ChartSeries> _liveHumSeries = Array.Empty<ChartSeries>();
    public IReadOnlyList<ChartSeries> LiveHumSeries { get => _liveHumSeries; private set => SetProperty(ref _liveHumSeries, value); }

    private IReadOnlyList<ChartSeries> _previewTempSeries = Array.Empty<ChartSeries>();
    public IReadOnlyList<ChartSeries> PreviewTempSeries { get => _previewTempSeries; private set => SetProperty(ref _previewTempSeries, value); }

    private IReadOnlyList<ChartSeries> _previewHumSeries = Array.Empty<ChartSeries>();
    public IReadOnlyList<ChartSeries> PreviewHumSeries { get => _previewHumSeries; private set => SetProperty(ref _previewHumSeries, value); }

    private IReadOnlyList<ChartSeries> _profilePreview = Array.Empty<ChartSeries>();
    /// <summary>
    /// Temperature curve of the profile picked on the dashboard (or the running one),
    /// with a dashed vertical "now" marker showing how far the test has progressed.
    /// </summary>
    public IReadOnlyList<ChartSeries> ProfilePreview { get => _profilePreview; private set => SetProperty(ref _profilePreview, value); }

    /// <summary>0..1 position of the "now" marker within a single pass of the profile.</summary>
    private double _profileNowFraction;

    /// <summary><c>true</c> when there is a profile to preview (selected or running).</summary>
    public bool HasProfilePreview => IsProfileRunning || SelectedHistoryProfile is not null;

    private void BuildProfilePreview()
    {
        List<ProfileSegment> segs =
            IsProfileRunning ? Segments.Select(s => s.ToModel()).ToList()
            : SelectedHistoryProfile?.Segments ?? Segments.Select(s => s.ToModel()).ToList();

        if (segs.Count == 0)
        {
            ProfilePreview = Array.Empty<ChartSeries>();
            return;
        }

        var pts = new List<Point>();
        double t = 0;
        double start = MeasuredTemperature ?? segs[0].TargetTemperature;
        pts.Add(new Point(0, start));
        foreach (ProfileSegment s in segs)
        {
            double dur = s.Duration.TotalMinutes;
            if (s.IsRamp)
            {
                t += dur;
                pts.Add(new Point(t, s.TargetTemperature));
            }
            else
            {
                pts.Add(new Point(t, s.TargetTemperature));
                t += dur;
                pts.Add(new Point(t, s.TargetTemperature));
            }
        }

        var series = new List<ChartSeries> { new("Profil", TempBrush, pts) };

        // Vertical "now" line at the current position, so the operator can see the
        // stage and temperature directly on the profile while it runs.
        if (IsProfileRunning && t > 0)
        {
            double nowX = Math.Clamp(_profileNowFraction, 0, 1) * t;
            double minY = pts.Min(p => p.Y);
            double maxY = pts.Max(p => p.Y);
            series.Add(new ChartSeries("Teraz", TempSpBrush, new List<Point> { new(nowX, minY), new(nowX, maxY) }, dashed: true));
        }

        ProfilePreview = series;
    }

    private void RecordLive(ChamberReading reading)
    {
        _live.Add(new LiveSample(reading.Timestamp, reading.Temperature, reading.TemperatureSetpoint,
            SupportsHumidity ? reading.Humidity : null, SupportsHumidity ? reading.HumiditySetpoint : null));
        if (_live.Count > LiveWindow)
        {
            _live.RemoveRange(0, _live.Count - LiveWindow);
        }

        BuildLiveCharts();
    }

    private void BuildLiveCharts()
    {
        if (_live.Count == 0)
        {
            LiveTempSeries = Array.Empty<ChartSeries>();
            LiveHumSeries = Array.Empty<ChartSeries>();
            return;
        }

        DateTimeOffset t0 = _live[0].Time;
        double X(LiveSample s) => (s.Time - t0).TotalMinutes;

        var temp = new List<ChartSeries>();
        AddIf(temp, Line("Teplota", TempBrush, false, _live.Select(s => (X(s), s.Temp))));
        AddIf(temp, Line("Setpoint", TempSpBrush, true, _live.Select(s => (X(s), s.TempSp))));
        LiveTempSeries = temp;

        if (SupportsHumidity)
        {
            var hum = new List<ChartSeries>();
            AddIf(hum, Line("Vlhkosť", HumBrush, false, _live.Select(s => (X(s), s.Hum))));
            AddIf(hum, Line("Setpoint", HumSpBrush, true, _live.Select(s => (X(s), s.HumSp))));
            LiveHumSeries = hum;
        }
    }

    private void BuildPreviewCharts()
    {
        if (Segments.Count == 0)
        {
            PreviewTempSeries = Array.Empty<ChartSeries>();
            PreviewHumSeries = Array.Empty<ChartSeries>();
            return;
        }

        var tempPts = new List<Point>();
        var humPts = new List<Point>();
        double prevT = MeasuredTemperature ?? Segments[0].TargetTemperature;
        double prevH = MeasuredHumidity ?? (Segments[0].TargetHumidity ?? 50);
        double t = 0;

        tempPts.Add(new Point(0, prevT));
        if (SupportsHumidity) humPts.Add(new Point(0, prevH));

        int cycles = Math.Max(1, Cycles);
        for (int c = 0; c < cycles; c++)
        {
            foreach (SegmentViewModel s in Segments)
            {
                double dur = Math.Max(0, s.DurationMinutes);
                double targetT = s.TargetTemperature;
                double targetH = s.TargetHumidity ?? prevH;

                if (s.IsRamp)
                {
                    t += dur;
                    tempPts.Add(new Point(t, targetT));
                    if (SupportsHumidity) humPts.Add(new Point(t, targetH));
                }
                else
                {
                    tempPts.Add(new Point(t, targetT));
                    if (SupportsHumidity) humPts.Add(new Point(t, targetH));
                    t += dur;
                    tempPts.Add(new Point(t, targetT));
                    if (SupportsHumidity) humPts.Add(new Point(t, targetH));
                }

                prevT = targetT;
                prevH = targetH;
            }
        }

        PreviewTempSeries = new[] { new ChartSeries("Profil teplota", TempBrush, tempPts) };
        PreviewHumSeries = SupportsHumidity
            ? new[] { new ChartSeries("Profil vlhkosť", HumBrush, humPts) }
            : Array.Empty<ChartSeries>();
    }

    private static ChartSeries? Line(string name, Brush brush, bool dashed, IEnumerable<(double x, double? y)> points)
    {
        var pts = points.Where(p => p.y.HasValue).Select(p => new Point(p.x, p.y!.Value)).ToList();
        return pts.Count > 0 ? new ChartSeries(name, brush, pts, dashed) : null;
    }

    private static void AddIf(List<ChartSeries> list, ChartSeries? series)
    {
        if (series is not null)
        {
            list.Add(series);
        }
    }

    private static Brush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private readonly record struct LiveSample(DateTimeOffset Time, double? Temp, double? TempSp, double? Hum, double? HumSp);

    #endregion

    #region Persistence

    /// <summary>Applies a saved configuration (connection, mapping, alarm limits).</summary>
    public void ApplyConfig(ChamberConfig c)
    {
        ArgumentNullException.ThrowIfNull(c);
        Host = c.Host;
        Port = c.Port;
        Address = c.Address;
        AnalogChannelCount = c.AnalogChannelCount;
        StartChannelIndex = c.StartChannelIndex;
        SelectedTerminator = c.Terminator;
        PollIntervalSeconds = c.PollIntervalSeconds;
        AlarmsEnabled = c.AlarmsEnabled;
        TempMin = c.TempMin;
        TempMax = c.TempMax;
        HumMin = c.HumMin;
        HumMax = c.HumMax;
        AutoStopOnAlarm = c.AutoStopOnAlarm;
        AutoReconnect = c.AutoReconnect;

        List<double> presets = c.QuickPresets is { Count: > 0 } ? new List<double>(c.QuickPresets) : DefaultQuickPresets();
        // One-time upgrade: a dryer still carrying the old 4-value default gets the
        // fuller preset set (0…250 °C) without touching a user's custom presets.
        if (IsPolEko && presets.SequenceEqual(LegacyPolEkoPresets))
        {
            presets = DefaultQuickPresets();
        }

        _quickPresets = presets;
        OnPropertyChanged(nameof(QuickPresets));
        OnPropertyChanged(nameof(QuickPresetsText));

        _pinnedQuickProfiles = c.QuickProfiles is { Count: > 0 } ? new List<string>(c.QuickProfiles) : new List<string>();
        RaiseQuickProfilesChanged();

        _nameplate = c.Nameplate?.Clone() ?? new ChamberNameplate();
        RaiseNameplate();

        LockPasswordHash = c.LockPasswordHash;
        IsLocked = c.IsLocked;
    }

    /// <summary>Captures the current configuration for persistence.</summary>
    public ChamberConfig ToConfig() => new()
    {
        Id = Id,
        Name = Name,
        Kind = Kind,
        Protocol = Protocol,
        Host = Host,
        Port = Port,
        Address = Address,
        AnalogChannelCount = AnalogChannelCount,
        StartChannelIndex = StartChannelIndex,
        Terminator = SelectedTerminator,
        PollIntervalSeconds = PollIntervalSeconds,
        AlarmsEnabled = AlarmsEnabled,
        TempMin = TempMin,
        TempMax = TempMax,
        HumMin = HumMin,
        HumMax = HumMax,
        AutoStopOnAlarm = AutoStopOnAlarm,
        AutoReconnect = AutoReconnect,
        QuickPresets = new List<double>(_quickPresets),
        QuickProfiles = new List<string>(_pinnedQuickProfiles),
        Nameplate = _nameplate.Clone(),
        IsLocked = IsLocked,
        LockPasswordHash = LockPasswordHash,
    };

    #endregion

    #region Infrastructure

    private void RefreshCommands()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        ReadOnceCommand.RaiseCanExecuteChanged();
        ApplySetpointCommand.RaiseCanExecuteChanged();
        StopChamberCommand.RaiseCanExecuteChanged();
        QuickSetTemperatureCommand.RaiseCanExecuteChanged();
        StartProfileCommand.RaiseCanExecuteChanged();
        StartSelectedProfileCommand.RaiseCanExecuteChanged();
        QuickStartProfileCommand.RaiseCanExecuteChanged();
        PauseResumeProfileCommand.RaiseCanExecuteChanged();
        StopProfileCommand.RaiseCanExecuteChanged();
        ResumeInterruptedRunCommand.RaiseCanExecuteChanged();
        DiscardInterruptedRunCommand.RaiseCanExecuteChanged();
        CancelProfileCommand.RaiseCanExecuteChanged();
        StartQueueCommand.RaiseCanExecuteChanged();
        StartChainCommand.RaiseCanExecuteChanged();
        SendTerminalCommand.RaiseCanExecuteChanged();
        RunSetpointDiagnosticCommand.RaiseCanExecuteChanged();
        ReadDigitalCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(IsConnected));
    }

    private void ReportError(Exception ex)
    {
        StatusMessage = $"Chyba: {ex.Message}";
        AppLog.Error(Name, ex);
        DesktopNotifier.Notify($"Chyba · {Name}", ex.Message, DesktopNotificationKind.Warning);
    }

    private static void RunOnUi(Action action)
    {
        Application? app = Application.Current;
        if (app?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    public async ValueTask DisposeAsync()
    {
        StopReconnect();
        StopPolling();
        StopProfile();
        StopRecording();
        CloseProfileTemperatureLog();
        if (_selectedReferenceThermometer is not null)
        {
            _selectedReferenceThermometer.PropertyChanged -= OnReferenceChanged;
        }

        _client.FrameExchanged -= OnFrameExchanged;
        await _client.DisposeAsync();
    }

    #endregion
}
