using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.Calibration;
using VotschVc3.App.Notifications;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.Views;

internal static class CalibrationWindowWiringModesV9Bootstrap
{
    [ModuleInitializer]
    internal static void Initialize() => EventManager.RegisterClassHandler(
        typeof(CalibrationWindow), FrameworkElement.LoadedEvent,
        new RoutedEventHandler((sender, _) => ((CalibrationWindow)sender).InitializeWiringModesV9()), true);
}

public partial class CalibrationWindow
{
    private SensorPairingPanel? _pairingPanel;
    private TextBlock? _pairingSteps;
    private TextBlock? _pairingResult;
    private bool _pairingApiVerified;
    private bool _wiringModesV9Initialized;

    private void ResetWiring_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsRunning) return;
        if (!ConfirmDialog.Ask("Vymazať uložené zapojenie, SN a výber peakov pre tento profil? Pripojené peaky sa pri obnovení načítajú znova bez priradení. Výsledky kalibrácií zostanú zachované.",
            "Začať zapojenie odznova", "Vymazať zapojenie")) return;
        _wiringGrid?.CancelEdit(DataGridEditingUnit.Cell);
        _wiringGrid?.CancelEdit(DataGridEditingUnit.Row);
        CloseSequentialWiringV9();
        try { _viewModel.ResetWiring(); }
        catch (InvalidOperationException ex) { ShowProductionInfo(ex.Message); }
    }
    private bool _wiringEntryModeSequential;
    private Window? _sequentialWiringWindow;
    private TextBox? _sequentialSnBox;
    private TextBlock? _sequentialStatus;
    private Button? _sequentialArmButton;
    private string? _sequentialPendingSn;
    private HashSet<string> _sequentialBaseline = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _sequentialLookupCts;

    internal void InitializeWiringModesV9()
    {
        if (_wiringModesV9Initialized) return;
        _wiringModesV9Initialized = true;
        InitializeWiringGridUxV6();
        Closed += (_, _) => CloseSequentialWiringV9();
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ConfigureWiringModesV9));
    }

    private void ConfigureWiringModesV9()
    {
        if (_wiringGrid is null) return;
        _wiringEntryModeSequential = false;
        _wiringGrid.PreviewKeyDown -= WiringGridPreviewKeyDownV9;
        _wiringGrid.PreviewKeyDown += WiringGridPreviewKeyDownV9;
        FocusFirstEmptySerialV9();
    }

    private void WiringGridPreviewKeyDownV9(object sender, KeyEventArgs e)
    {
        if (_wiringEntryModeSequential || e.Key != Key.Enter || _wiringGrid is null || _viewModel.IsRunning) return;
        DataGridColumn? column = _wiringGrid.CurrentColumn;
        if (column is null || !string.Equals(HeaderText(column.Header), "FBG sensor SN (kanál)", StringComparison.OrdinalIgnoreCase)) return;
        if (_wiringGrid.CurrentItem is not CalibrationPeakRowViewModel current) return;

        e.Handled = true;
        _wiringGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        _wiringGrid.CommitEdit(DataGridEditingUnit.Row, true);
        int index = _viewModel.Peaks.IndexOf(current);
        CalibrationPeakRowViewModel? next = index >= 0 && index + 1 < _viewModel.Peaks.Count ? _viewModel.Peaks[index + 1] : null;
        if (next is not null)
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => FocusSerialCellV9(next, column)));
    }

    private void FocusFirstEmptySerialV9()
    {
        if (_wiringEntryModeSequential || _viewModel.IsRunning) return;
        CalibrationPeakRowViewModel? row = _viewModel.Peaks.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.ChannelSerialNumber));
        DataGridColumn? column = _wiringGrid?.Columns.FirstOrDefault(x => HeaderText(x.Header) == "FBG sensor SN (kanál)");
        if (row is not null && column is not null)
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => FocusSerialCellV9(row, column)));
    }

    private void FocusSerialCellV9(CalibrationPeakRowViewModel row, DataGridColumn column)
    {
        if (_wiringGrid is null || _viewModel.IsRunning || _wiringEntryModeSequential) return;
        _wiringGrid.ScrollIntoView(row, column);
        _wiringGrid.CurrentCell = new DataGridCellInfo(row, column);
        _wiringGrid.Focus();
        _wiringGrid.BeginEdit();
    }

    private void OpenSerialPairing_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsRunning) return;
        if (_wiringGrid is not null &&
            (!_wiringGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
             !_wiringGrid.CommitEdit(DataGridEditingUnit.Row, true))) return;
        _wiringEntryModeSequential = true;
        try { OpenSequentialWiringV9(); }
        finally { _wiringEntryModeSequential = false; }
    }

    private void OpenSequentialWiringV9()
    {
        if (_sequentialWiringWindow is not null) { _sequentialWiringWindow.Activate(); return; }
        Brush background = TryFindResource("BackgroundBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(24, 26, 38));
        Brush surface = TryFindResource("SurfaceBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(34, 36, 58));
        Brush border = TryFindResource("BorderBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(58, 61, 92));
        Brush text = TryFindResource("TextBrush") as Brush ?? Brushes.White;
        Brush muted = TryFindResource("MutedBrush") as Brush ?? Brushes.LightGray;

        var panel = new SensorPairingPanel();
        _pairingPanel = panel;
        _sequentialSnBox = panel.SerialInput;
        _sequentialStatus = panel.Status;
        _sequentialArmButton = panel.Prepare;
        _pairingSteps = null;
        _pairingResult = null;
        var pairingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        pairingTimer.Tick += (_, _) => RefreshPairingCandidates();
        panel.Loaded += (_, _) => pairingTimer.Start();
        panel.Unloaded += (_, _) => pairingTimer.Stop();
        panel.ConfirmPeaks.Click += (_, _) => ConfirmSequentialPeaks();
        panel.Prepare.Click += async (_, _) => await ArmSequentialSerialV9Async();
        panel.SerialInput.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && panel.Prepare.IsEnabled)
            { e.Handled = true; await ArmSequentialSerialV9Async(); }
        };
        panel.CloseAction.Click += (_, _) => _sequentialWiringWindow?.Close();
        panel.ChangeSerial.Click += (_, _) =>
        {
            _sequentialPendingSn = null;
            _sequentialLookupCts?.Cancel();
            panel.SerialInput.IsEnabled = true;
            panel.Prepare.IsEnabled = true;
            panel.SetStage(1);
            panel.Status.Text = "Po potvrdení SN pripoj snímač do voľného kanála.";
            panel.SerialInput.Focus();
            panel.SerialInput.SelectAll();
        };
        var card = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = SystemParameters.WorkArea.Height - 100 };        _sequentialWiringWindow = new Window
        {
            Owner = this, Title = "Priradenie FBG SN", Content = card,
            SizeToContent = SizeToContent.Height, Width = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = background, Foreground = text,
        };
        _sequentialWiringWindow.SourceInitialized += (_, _) => EnableDarkTitleBarV9(_sequentialWiringWindow);
        _sequentialWiringWindow.Closed += (_, _) =>
        {
            _sequentialWiringWindow = null; _sequentialSnBox = null; _sequentialStatus = null; _sequentialArmButton = null;
            _sequentialPendingSn = null; _sequentialLookupCts?.Cancel();
        };
        _sequentialWiringWindow.Loaded += (_, _) => _sequentialSnBox?.Focus();
        _sequentialWiringWindow.ShowDialog();
    }

    private static void EnableDarkTitleBarV9(Window? window)
    {
        if (window is null) return;
        IntPtr handle = new WindowInteropHelper(window).Handle;
        int enabled = 1;
        if (DwmSetWindowAttributeV9(handle, 20, ref enabled, sizeof(int)) != 0)
            _ = DwmSetWindowAttributeV9(handle, 19, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttributeV9(IntPtr window, int attribute, ref int value, int valueSize);

    private async Task ArmSequentialSerialV9Async()
    {
        if (_sequentialSnBox is null || _sequentialStatus is null || _sequentialArmButton is null || _viewModel.IsRunning) return;
        string sn = SylexFosRowMetadataStore.ParseSerialNumber(_sequentialSnBox.Text);
        if (string.IsNullOrWhiteSpace(sn)) { _sequentialStatus.Text = "Zadaj SN snímača."; _sequentialSnBox.Focus(); return; }
        _sequentialLookupCts?.Cancel();
        var lookup = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        _sequentialLookupCts = lookup;
        // Arm before the optional lookup so a slow/offline API cannot block pairing.
        if (_viewModel.Peaks.Any(p => string.Equals(p.ChannelSerialNumber, sn, StringComparison.OrdinalIgnoreCase)))
        {
            _sequentialStatus.Text = "Toto SN už je priradené. Skontroluj zapojenie alebo zadaj iné SN.";
            return;
        }
        _pairingApiVerified = false;
        _pairingMetadata = null;
        if (_pairingSteps is not null) _pairingSteps.Text = "✓ 1 SN pripravené     ● 2 Pripoj snímač     ○ 3 Priradenie";
        _pendingNote = _pairingPanel?.NoteInput.Text ?? string.Empty;
        _candidateSignature = string.Empty;
        _candidateSince = DateTime.UtcNow;
        _sequentialPendingSn = sn;
        _pairingPanel?.SetStage(2, sn);
        _sequentialBaseline = CurrentPeakIdentities();
        _sequentialSnBox.IsEnabled = false;
        _sequentialArmButton.IsEnabled = false;
        _sequentialStatus.Text = $"SN {sn} je pripravené. Pripoj snímač.\nÚdaje z API sa načítavajú na pozadí…";
        bool IsCurrent() => _sequentialWiringWindow is not null &&
            ReferenceEquals(_sequentialLookupCts, lookup) && _sequentialPendingSn == sn;
        try
        {
            ProductionMetadata? metadata = _sylexFosIntegration is null ? null :
                await _sylexFosIntegration.PreviewAsync(sn, lookup.Token);
            if (!IsCurrent()) return;
            _pairingApiVerified = metadata is not null;
            _pairingMetadata = metadata;
            RefreshPairingCandidates();
            _sequentialStatus!.Text = metadata is null
                ? $"SN {sn} je pripravené. Pripoj snímač.\nBez overenia API – produkčné údaje zatiaľ nie sú dostupné."
                : $"SN {sn} je pripravené. Pripoj snímač.\nAPI overené · {metadata.SensorName}\n{metadata.ProductDescription}";
        }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            _sequentialStatus!.Text = $"SN {sn} je pripravené. Pripoj snímač.\nAPI neodpovedá – párovanie pokračuje bez overenia.";
            AppLog.Warn("FBG zapojenie", $"Voliteľné API overenie SN zlyhalo: {ex.Message}");
        }
    }
    private ProductionMetadata? _pairingMetadata;
    private string _pendingNote = string.Empty;
    private string _candidateSignature = string.Empty;
    private DateTime _candidateSince;
    private List<CalibrationPeakRowViewModel> PendingCandidates() => _viewModel.Peaks.Where(p =>
        !p.IsDisconnected && !_sequentialBaseline.Contains(PeakIdentity(p)) && string.IsNullOrWhiteSpace(p.ChannelSerialNumber)).ToList();
    private bool TryPairSequentialPeak(IEnumerable<string> addedIdentities) => _sequentialPendingSn is not null;
    private void RefreshPairingCandidates()
    {
        if (_pairingPanel is null || _sequentialPendingSn is null) return;
        var rows = PendingCandidates();
        _pairingPanel.ShowWavelengthComparison(FbgWavelengthComparison.Evaluate(
            rows.Select(p => ($"{p.Channel} / {p.PeakId}", p.CurrentWavelengthNm)), _pairingMetadata?.Fbg));
        string signature = string.Join(";", rows.Select(PeakIdentity).OrderBy(x => x));
        if (signature != _candidateSignature) { _candidateSignature = signature; _candidateSince = DateTime.UtcNow; }
        bool missing = _viewModel.Peaks.Any(p => _sequentialBaseline.Contains(PeakIdentity(p)) && p.IsDisconnected);
        bool singleChannel = rows.Select(p => $"{p.PeakLoggerDeviceSerialNumber}|{p.Channel}").Distinct().Count() == 1;
        _pairingPanel.ConfirmPeaks.IsEnabled = rows.Count > 0 && singleChannel && !missing && (DateTime.UtcNow - _candidateSince).TotalSeconds >= 3;
        _pairingPanel.Candidates.Text = missing ? "Pôvodné peaky chýbajú. Skontroluj pripojenie pred potvrdením." :
            rows.Count == 0 ? "Čakám na nové peaky…" :
            $"Nové peaky: {rows.Count}\n" + string.Join(", ", rows.Select(p => $"{p.Channel} / {p.PeakId} · {p.CurrentWavelengthNm:F3} nm")) +
            (singleChannel ? "\nSkontroluj počet peakov snímača a potvrď priradenie." : "\nPribudlo viac kanálov. Pripájaj iba jeden snímač naraz.");
    }
    private void ConfirmSequentialPeaks()
    {
        RefreshPairingCandidates();
        if (_pairingPanel?.ConfirmPeaks.IsEnabled != true || _sequentialPendingSn is null) return;
        var rows = PendingCandidates();
        var row = rows[0];
        string sn = _sequentialPendingSn;
        foreach (var peak in rows) { peak.ChannelSerialNumber = sn; peak.Notes = _pendingNote; }
        _pairingPanel.NoteInput.Clear();        _sequentialPendingSn = null;
        _sequentialLookupCts?.Cancel();
        _pairingPanel?.ShowResult(sn, row.Channel, _pairingApiVerified);
        if (_pairingResult is not null) _pairingResult.Text = $"✓ Priradenie dokončené: {sn} → kanál {row.Channel}\n" +
            (_pairingApiVerified ? "API overené. Pripravené na ďalší snímač." : "SN priradené; API zatiaľ neoverené. Pripravené na ďalší snímač.");
        if (_pairingSteps is not null) _pairingSteps.Text = "● 1 Zadaj ďalšie SN     ○ 2 Pripoj snímač     ○ 3 Priradenie";
        ShowProductionInfo($"SN {sn} bolo priradené ku kanálu {row.Channel}. Pripravené na ďalší snímač.");
        if (_sequentialStatus is not null) _sequentialStatus.Text = $"Priradené: {sn} → kanál {row.Channel}. Zadaj ďalšie SN.";
        if (_sequentialSnBox is not null)
        {
            _sequentialSnBox.Text = string.Empty; _sequentialSnBox.IsEnabled = true; _sequentialArmButton!.IsEnabled = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => _sequentialSnBox?.Focus()));
        }
        AppLog.Info("FBG zapojenie", $"Poradovo priradené SN {sn} ku kanálu {row.Channel}.");
    }

    private void CloseSequentialWiringV9()
    {
        _sequentialLookupCts?.Cancel();
        if (_sequentialWiringWindow is not null) { Window window = _sequentialWiringWindow; _sequentialWiringWindow = null; window.Close(); }
    }
}
