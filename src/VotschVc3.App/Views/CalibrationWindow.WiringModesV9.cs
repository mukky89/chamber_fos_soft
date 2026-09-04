using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.Calibration;
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
        if (_wiringGrid?.Parent is not Grid root || root.Children.OfType<FrameworkElement>().Any(x => Equals(x.Tag, "WIRING_MODES_V9"))) return;
        if (root.Children.OfType<DockPanel>().FirstOrDefault(x => Grid.GetRow(x) == 0) is not DockPanel header) return;

        var table = new RadioButton
        {
            Content = "Tabuľka SN",
            GroupName = "WiringEntryModeV9",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 10, 0),
            ToolTip = "Enter uloží SN a presunie kurzor na ďalší riadok v rovnakom stĺpci.",
        };
        var sequential = new RadioButton
        {
            Content = "Poradové párovanie",
            GroupName = "WiringEntryModeV9",
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Najprv zadaj SN, potom pripoj snímač. Nový kanál sa priradí automaticky.",
        };
        table.Checked += (_, _) =>
        {
            _wiringEntryModeSequential = false;
            _sequentialPendingSn = null;
            CloseSequentialWiringV9();
            FocusFirstEmptySerialV9();
        };
        sequential.Checked += (_, _) =>
        {
            _wiringEntryModeSequential = true;
            OpenSequentialWiringV9();
        };
        var modes = new StackPanel { Tag = "WIRING_MODES_V9", Orientation = Orientation.Horizontal };
        modes.Children.Add(table);
        modes.Children.Add(sequential);
        DockPanel.SetDock(modes, Dock.Right);
        header.Children.Insert(0, modes);

        // Poradové párovanie je bezpečnejší výrobný postup: operátor najprv
        // pripraví overené SN a až potom sa novo pripojený kanál priradí.
        sequential.IsChecked = true;

        _wiringGrid.PreviewKeyDown -= WiringGridPreviewKeyDownV9;
        _wiringGrid.PreviewKeyDown += WiringGridPreviewKeyDownV9;
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
            MinWidth = 420, MinHeight = 44, FontSize = 18,
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
            Content = "Načítať z API a pripraviť", Padding = new Thickness(18, 9, 18, 9),
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = TryFindResource("AccentButton") as Style,
        };
        _sequentialArmButton.Click += async (_, _) => await ArmSequentialSerialV9Async();
        _sequentialSnBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await ArmSequentialSerialV9Async(); } };
        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock { Text = "Poradové párovanie snímačov", FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = text });
        stack.Children.Add(new TextBlock
        {
            Text = "1  Zadaj SN     2  Pripoj snímač     3  Nový kanál sa priradí automaticky",
            Foreground = muted, Margin = new Thickness(0, 6, 0, 12),
        });
        stack.Children.Add(_sequentialSnBox);
        stack.Children.Add(_sequentialStatus);
        stack.Children.Add(_sequentialArmButton);
        var card = new Border
        {
            Background = surface, BorderBrush = border, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Margin = new Thickness(18), Child = stack,
        };
        _sequentialWiringWindow = new Window
        {
            Owner = this, Title = "Priradenie FBG SN", Content = card,
            SizeToContent = SizeToContent.WidthAndHeight, MinWidth = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = background, Foreground = text,
        };
        _sequentialWiringWindow.SourceInitialized += (_, _) => EnableDarkTitleBarV9(_sequentialWiringWindow);
        _sequentialWiringWindow.Closed += (_, _) =>
        {
            _sequentialWiringWindow = null; _sequentialSnBox = null; _sequentialStatus = null; _sequentialArmButton = null;
            _sequentialPendingSn = null; _sequentialLookupCts?.Cancel();
        };
        _sequentialWiringWindow.Show();
        _sequentialSnBox.Focus();
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
        if (_sequentialSnBox is null || _sequentialStatus is null || _sequentialArmButton is null || _sylexFosIntegration is null) return;
        string sn = SylexFosRowMetadataStore.ParseSerialNumber(_sequentialSnBox.Text);
        if (string.IsNullOrWhiteSpace(sn)) { _sequentialStatus.Text = "Zadaj SN snímača."; _sequentialSnBox.Focus(); return; }
        _sequentialLookupCts?.Cancel();
        _sequentialLookupCts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        _sequentialSnBox.IsEnabled = false; _sequentialArmButton.IsEnabled = false;
        _sequentialStatus.Text = $"Načítavam {sn} zo Sylex FOS API…";
        try
        {
            ProductionMetadata? metadata = await _sylexFosIntegration.PreviewAsync(sn, _sequentialLookupCts.Token);
            if (metadata is null)
            {
                _sequentialStatus.Text = $"SN {sn} sa v API nenašlo. Skontroluj ho a skús znova.";
                _sequentialSnBox.IsEnabled = true; _sequentialArmButton.IsEnabled = true; _sequentialSnBox.Focus(); _sequentialSnBox.SelectAll();
                return;
            }
            _sequentialPendingSn = sn;
            _sequentialBaseline = CurrentPeakIdentities();
            _sequentialStatus.Text = $"{sn} · {metadata.SensorName}\n{metadata.ProductDescription}\nPripoj snímač – čakám na nový peak/kanál.";
            ShowProductionInfo($"Poradové párovanie: SN {sn} je pripravené. Pripoj snímač.");
        }
        catch (OperationCanceledException)
        {
            _sequentialStatus.Text = "API neodpovedalo včas. Skús SN načítať znova.";
            _sequentialSnBox.IsEnabled = true; _sequentialArmButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _sequentialStatus.Text = $"API chyba: {ex.Message}";
            _sequentialSnBox.IsEnabled = true; _sequentialArmButton.IsEnabled = true;
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
        ShowProductionInfo($"SN {sn} bolo priradené ku kanálu {row.Channel}. Pripravené na ďalší snímač.");
        if (_sequentialStatus is not null) _sequentialStatus.Text = $"Priradené: {sn} → kanál {row.Channel}. Zadaj ďalšie SN.";
        if (_sequentialSnBox is not null)
        {
            _sequentialSnBox.Text = string.Empty; _sequentialSnBox.IsEnabled = true; _sequentialArmButton!.IsEnabled = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => _sequentialSnBox.Focus()));
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
