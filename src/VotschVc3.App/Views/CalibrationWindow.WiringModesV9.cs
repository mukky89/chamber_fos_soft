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

    private void RemoveMissingPeaks_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsRunning) { ShowProductionInfo("Počas kalibrácie nemožno odstraňovať peaky."); return; }
        if (IsWiringGridEditingV3()) { ShowProductionInfo("Najprv dokonči editáciu bunky klávesom Enter."); return; }
        try
        {
            int removed = _viewModel.RemoveMissingUnassignedPeaks();
            _knownPeakIdentities = CurrentPeakIdentities();
            _sequentialBaseline.IntersectWith(_knownPeakIdentities);
            ShowProductionInfo(removed > 0
                ? $"Odstránené neprítomné nepriradené peaky: {removed}."
                : "Nie sú tu neprítomné nepriradené peaky na odstránenie. Priradené a vybrané peaky sa zachovávajú.");
        }
        catch (Exception ex)
        {
            ShowProductionInfo($"Zmenu zapojenia sa nepodarilo uložiť: {ex.Message}. Zopakuj uloženie ikonou.");
        }
    }

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
        panel.CloseHeader.Click += (_, _) => _sequentialWiringWindow?.Close();
        panel.ChangeSerial.Click += (_, _) =>
        {
            _sequentialPendingSn = null;
            _sequentialLookupCts?.Cancel();
            _sequentialLookupCts = null;
            panel.SerialInput.IsEnabled = panel.Prepare.IsEnabled = true;
            panel.SetStage(1);
            panel.Status.Text = "Po potvrdení SN pripoj snímač do voľného kanála.";
            panel.SerialInput.Focus();
            panel.SerialInput.SelectAll();
        };
        panel.CancelLookup.Click += (_, _) =>
        {
            _sequentialLookupCts?.Cancel();
            _sequentialLookupCts = null;
            panel.ShowMetadata(null);
            panel.Status.Text = "Pokračuješ bez API. Pripoj snímač a skontroluj peaky; typy zatiaľ nie sú overené.";
        };
        panel.ShowDuplicate.Click += (_, _) =>
        {
            string serial = SylexFosRowMetadataStore.ParseSerialNumber(panel.SerialInput.Text);
            var duplicate = FindAssignedSerial(serial);
            if (duplicate is null) { panel.ClearIssue(); return; }
            _sequentialWiringWindow?.Close();
            _viewModel.PeakSearchText = string.Empty;
            _viewModel.PeakFilterAll = true;
            if (_wiringGrid is not null)
            {
                _wiringGrid.SelectedItem = duplicate;
                _wiringGrid.ScrollIntoView(duplicate);
                _wiringGrid.Focus();
            }
        };
        var card = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = SystemParameters.WorkArea.Height - 100 };        _sequentialWiringWindow = new Window
        {
            Owner = this, Title = "Priradenie FBG SN", Content = card,
            SizeToContent = SizeToContent.Height, Width = Math.Min(940, SystemParameters.WorkArea.Width - 60),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = background, Foreground = text,
        };
        _sequentialWiringWindow.SourceInitialized += (_, _) => EnableDarkTitleBarV9(_sequentialWiringWindow);
        _sequentialWiringWindow.Closed += (_, _) =>
        {
            _sequentialWiringWindow = null; _sequentialSnBox = null; _sequentialStatus = null; _sequentialArmButton = null;
            _sequentialPendingSn = null; _sequentialLookupCts?.Cancel(); _sequentialLookupCts = null; _pairingPanel = null;
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

    private CalibrationPeakRowViewModel? FindAssignedSerial(string sn) => _viewModel.Peaks.FirstOrDefault(p =>
        string.Equals(p.SerialNumber, sn, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(p.ChannelSerialNumber, sn, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(p.ChainSerialNumber, sn, StringComparison.OrdinalIgnoreCase));

    private async Task ArmSequentialSerialV9Async()
    {
        var panel = _pairingPanel;
        if (panel is null || !panel.Prepare.IsEnabled || _viewModel.IsRunning) return;
        string sn = SylexFosRowMetadataStore.ParseSerialNumber(panel.SerialInput.Text);
        panel.ClearIssue();
        if (!FbgSerialParser.IsProductionSerial(sn))
        {
            panel.ShowIssue("Neplatný formát SN", "Použi číslice vo formáte 291875/0002 alebo naskenuj výrobný kód. Skontroluj zámeny O/0 a I/1.");
            panel.SerialInput.Focus();
            return;
        }
        panel.SerialInput.Text = sn;
        var duplicate = FindAssignedSerial(sn);
        if (duplicate is not null)
        {
            panel.ShowIssue("Tento snímač už je priradený", $"SN {sn} sa už nachádza v zapojení.\nKanál {duplicate.Channel} · zdroj {duplicate.PeakLoggerDeviceSerialNumber}", duplicate: true);
            panel.Status.Text = "Zobraz existujúci riadok alebo zadaj iné SN.";
            return;
        }
        _sequentialLookupCts?.Cancel();
        using var lookup = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        _sequentialLookupCts = lookup;
        _pairingApiVerified = false;
        _pairingMetadata = null;
        _pendingNote = panel.NoteInput.Text;
        _candidateSignature = string.Empty;
        _candidateSince = DateTime.UtcNow;
        _sequentialPendingSn = sn;
        _sequentialBaseline = _viewModel.Peaks.Where(p => !p.IsDisconnected).Select(PeakIdentity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        panel.SetStage(2, sn);
        panel.SetLoading(true);
        panel.SerialInput.IsEnabled = panel.Prepare.IsEnabled = false;
        panel.Status.Text = "Údaje načítavam na pozadí. Snímač môžeš pripojiť už teraz.";
        RefreshPairingCandidates();
        bool IsCurrent() => ReferenceEquals(_pairingPanel, panel) &&
            ReferenceEquals(_sequentialLookupCts, lookup) && _sequentialPendingSn == sn;
        try
        {
            ProductionMetadata? metadata = _sylexFosIntegration is null ? null :
                await _sylexFosIntegration.PreviewAsync(sn, lookup.Token);
            if (!IsCurrent()) return;
            _pairingApiVerified = metadata is not null;
            _pairingMetadata = metadata;
            panel.ShowMetadata(metadata);
            RefreshPairingCandidates();
            panel.Status.Text = metadata is null
                ? "API nenašlo údaje snímača. Skontroluj SN; párovanie je možné aj bez API."
                : "Údaje API sú načítané. Skontroluj snímač, počet peakov a výber kalibrácie.";
        }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            panel.ShowMetadata(null);
            panel.Status.Text = "API neodpovedá – párovanie pokračuje bez overenia. Údaje možno doplniť neskôr.";
            AppLog.Warn("FBG zapojenie", $"Voliteľné API overenie SN zlyhalo: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_sequentialLookupCts, lookup)) _sequentialLookupCts = null;
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
        _pairingPanel.UpdateCandidates(rows, _pairingMetadata);
        _pairingPanel.ShowWavelengthComparison(FbgWavelengthComparison.Evaluate(
            rows.Select(p => ($"{p.Channel} / {p.PeakId}", p.CurrentWavelengthNm)), _pairingMetadata?.Fbg));
        string signature = string.Join(";", rows.Select(PeakIdentity).OrderBy(x => x));
        if (signature != _candidateSignature) { _candidateSignature = signature; _candidateSince = DateTime.UtcNow; }
        string[] candidateChannels = rows.Select(p => $"{p.PeakLoggerDeviceSerialNumber}|{p.Channel}")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        bool missing = _viewModel.Peaks.Any(p => CalibrationPeakTopologyPolicy.BlocksPairing(
            _sequentialBaseline.Contains(PeakIdentity(p)), p.RequiresReconnect,
            $"{p.PeakLoggerDeviceSerialNumber}|{p.Channel}", candidateChannels));
        bool singleChannel = candidateChannels.Length == 1;
        _pairingPanel.ConfirmPeaks.IsEnabled = rows.Count > 0 && singleChannel && !missing && (DateTime.UtcNow - _candidateSince).TotalSeconds >= 3;
        _pairingPanel.Candidates.Text = missing ? "Na párovanom kanáli chýbajú pôvodné priradené peaky. Skontroluj jeho pripojenie." :
            rows.Count == 0 ? "Čakám na nové peaky…" :
             $"Nové peaky: {rows.Count}" +
            (singleChannel ? "\nSkontroluj počet peakov snímača a potvrď priradenie." : "\nPribudlo viac kanálov. Pripájaj iba jeden snímač naraz.");
    }
    private void ConfirmSequentialPeaks()
    {
        RefreshPairingCandidates();
        if (_pairingPanel?.ConfirmPeaks.IsEnabled != true || _sequentialPendingSn is null) return;
        var rows = PendingCandidates();
        var row = rows[0];
        string sn = _sequentialPendingSn;
        if (_viewModel.IsRunning || FindAssignedSerial(sn) is not null) { _pairingPanel.Status.Text = "SN už je priradené alebo sa začala kalibrácia. Zmeň SN / skontroluj zapojenie."; return; }
        foreach (var peak in rows) { peak.ChannelSerialNumber = sn; peak.Notes = _pendingNote; _pairingPanel.ApplyPreviewSelection(peak); }
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
