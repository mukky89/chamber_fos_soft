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
    private bool _wiringModesV9Initialized;
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

        _sequentialSnBox = new TextBox
        {
            MinHeight = 44, FontSize = 20, VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8),
        };
        _sequentialStatus = new TextBlock
        {
            Text = "Zadaj alebo naskenuj sériové číslo.",
            Foreground = muted, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        };
        _sequentialArmButton = new Button
        {
            Content = "Pripraviť SN  ↵", Padding = new Thickness(18, 9, 18, 9),
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = TryFindResource("AccentButton") as Style,
        };
        _sequentialArmButton.Click += async (_, _) => await ArmSequentialSerialV9Async();
        _sequentialSnBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await ArmSequentialSerialV9Async(); } };
        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock { Text = "Priradiť sériové číslo", FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = text });
        stack.Children.Add(new TextBlock
        {
            Text = "Zadaj SN → Pripoj snímač → Automatické priradenie kanálu",
            Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 20),
        });
        stack.Children.Add(new TextBlock { Text = "Sériové číslo (SN)", Foreground = text, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(_sequentialSnBox);
        stack.Children.Add(_sequentialStatus);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(_sequentialArmButton);
        var reset = new Button { Content = "Zmeniť SN", Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(18, 9, 18, 9), Style = TryFindResource("GhostButton") as Style };
        reset.Click += (_, _) =>
        {
            _sequentialPendingSn = null;
            _sequentialLookupCts?.Cancel();
            _sequentialSnBox!.IsEnabled = true;
            _sequentialArmButton!.IsEnabled = true;
            _sequentialStatus!.Text = "Zadaj alebo naskenuj sériové číslo.";
            _sequentialSnBox.Focus();
            _sequentialSnBox.SelectAll();
        };
        actions.Children.Add(reset);
        stack.Children.Add(actions);
        stack.Children.Add(new TextBlock { Text = "API dopĺňa údaje na pozadí. SN môžeš priradiť aj bez pripojenia k API.",
            Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) });
        var card = new Border
        {
            Background = surface, BorderBrush = border, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Margin = new Thickness(18), Child = stack,
        };
        _sequentialWiringWindow = new Window
        {
            Owner = this, Title = "Priradenie FBG SN", Content = card,
            SizeToContent = SizeToContent.Height, Width = 640,
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
        _sequentialPendingSn = sn;
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
    private bool TryPairSequentialPeak(IEnumerable<string> addedIdentities)
    {
        if (string.IsNullOrWhiteSpace(_sequentialPendingSn)) return false;
        CalibrationPeakRowViewModel? row = _viewModel.Peaks.FirstOrDefault(x =>
            addedIdentities.Contains(PeakIdentity(x), StringComparer.OrdinalIgnoreCase) && !_sequentialBaseline.Contains(PeakIdentity(x)));
        if (row is null) return false;
        string sn = _sequentialPendingSn;
        foreach (CalibrationPeakRowViewModel channelRow in _viewModel.Peaks.Where(x =>
                     string.Equals(x.PeakLoggerDeviceSerialNumber, row.PeakLoggerDeviceSerialNumber, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(x.Channel, row.Channel, StringComparison.OrdinalIgnoreCase)))
            channelRow.ChannelSerialNumber = sn;
        _sequentialPendingSn = null;
        _sequentialLookupCts?.Cancel();
        ShowProductionInfo($"SN {sn} bolo priradené ku kanálu {row.Channel}. Pripravené na ďalší snímač.");
        if (_sequentialStatus is not null) _sequentialStatus.Text = $"Priradené: {sn} → kanál {row.Channel}. Zadaj ďalšie SN.";
        if (_sequentialSnBox is not null)
        {
            _sequentialSnBox.Text = string.Empty; _sequentialSnBox.IsEnabled = true; _sequentialArmButton!.IsEnabled = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => _sequentialSnBox?.Focus()));
        }
        AppLog.Info("FBG zapojenie", $"Poradovo priradené SN {sn} ku kanálu {row.Channel}.");
        return true;
    }

    private void CloseSequentialWiringV9()
    {
        _sequentialLookupCts?.Cancel();
        if (_sequentialWiringWindow is not null) { Window window = _sequentialWiringWindow; _sequentialWiringWindow = null; window.Close(); }
    }
}
