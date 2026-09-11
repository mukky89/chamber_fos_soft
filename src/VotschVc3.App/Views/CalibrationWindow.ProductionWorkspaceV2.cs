using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.Calibration;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.Views;

internal static class CalibrationWindowProductionWorkspaceV2Bootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(CalibrationWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded),
            handledEventsToo: true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is CalibrationWindow window) window.InitializeProductionWorkspaceV2();
    }
}

/// <summary>
/// Production-oriented interaction layer for the FBG calibration window. It intentionally lives
/// outside CalibrationViewModel so measurement logic remains testable while operator ergonomics
/// (focus, autosave, spectrum, progress, data paths and run-time locking) stay in the WPF layer.
/// </summary>
public partial class CalibrationWindow
{
    private bool _productionWorkspaceV2Initialized;
    private readonly HashSet<CalibrationPeakRowViewModel> _productionObservedRows = new();
    private readonly Dictionary<DataGrid, bool> _productionGridReadOnly = new();
    private readonly Dictionary<Control, bool> _productionInputEnabled = new();
    private HashSet<string> _knownPeakIdentities = new(StringComparer.OrdinalIgnoreCase);
    private bool _peakIdentityBaselineReady;
    private HashSet<string>? _pendingTopologyChange;
    private int _pendingTopologyChangeConfirmations;
    private CancellationTokenSource? _rowReconcileCts;
    private CancellationTokenSource? _topologyPollCts;
    private DispatcherTimer? _referenceFiveSecondTimer;
    private DispatcherTimer? _topologyTimer;
    private PeakLoggerExtendedApiClient? _extendedPeakLoggerApi;
    private Border? _productionInfoBanner;
    private TextBlock? _productionInfoText;
    private Border? _productionRunPanel;
    private ProgressBar? _productionRunProgress;
    private TextBlock? _productionRunHeadline;
    private TextBlock? _productionRunDetail;
    private TextBlock? _dataRootText;
    private TextBlock? _currentRunPathText;
    private TabControl? _productionTabs;
    private TabItem? _liveMonitorTab;
    private Guid? _lastProfileSelection;
    private bool _referenceRefreshRequested;

    internal void InitializeProductionWorkspaceV2()
    {
        if (_productionWorkspaceV2Initialized) return;
        _productionWorkspaceV2Initialized = true;
        _extendedPeakLoggerApi = new PeakLoggerExtendedApiClient();

        _viewModel.PropertyChanged += OnProductionWorkspacePropertyChanged;
        _viewModel.Peaks.CollectionChanged += OnProductionPeaksCollectionChanged;
        foreach (CalibrationPeakRowViewModel row in _viewModel.Peaks) AttachProductionRow(row);
        _lastProfileSelection = _viewModel.SelectedProfile?.Id;

        Closed += OnProductionWorkspaceClosed;

        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            ConfigureProductionWiringGridV2();
            ConfigureProductionTabs();
            RenameReferenceStabilityLabels();
            CaptureRunLockedControls();
            _knownPeakIdentities = CurrentPeakIdentities();
            _peakIdentityBaselineReady = true;
            ApplyRunInputLock(_viewModel.IsRunning);
            UpdateProductionRunPanel();
            RefreshDataPathPanel();
        }));

        var storageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        storageTimer.Tick += (_, _) =>
        {
            if (_dataRootText is not null) _dataRootText.Text = _viewModel.StorageDirectories + "\n" + _viewModel.StorageStatus;
        };
        Closed += (_, _) => storageTimer.Stop();
        storageTimer.Start();
        StartReferenceFiveSecondRefresh();
        StartPeakLoggerTopologyWatch();
    }

    private void ConfigureProductionWiringGridV2()
    {
        _wiringGrid ??= FindProductionDescendants<DataGrid>(this)
            .FirstOrDefault(grid => grid.Columns.Any(c => HeaderText(c.Header) == "FBG sensor SN (kanál)"));
        if (_wiringGrid is null) return;

        // Keep sixteen production lines visible at once. Additional rows remain scrollable.
        _wiringGrid.ClearValue(FrameworkElement.MinHeightProperty);
        _wiringGrid.MaxHeight = (16 * 31) + 36 + 16;
        _wiringGrid.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        _wiringGrid.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);

        DataGridColumn? sensorColumn = _wiringGrid.Columns.FirstOrDefault(c => HeaderText(c.Header) == "Snímač");
        if (sensorColumn is not null) _wiringGrid.Columns.Remove(sensorColumn);

        DataGridColumn? fbgTypeColumn = _wiringGrid.Columns.FirstOrDefault(c => HeaderText(c.Header) == "Typ FBG");
        if (fbgTypeColumn is DataGridBoundColumn fbgBound)
        {
            fbgBound.Binding = new Binding("ApiMetadata.FbgType") { Mode = BindingMode.OneWay };
            fbgBound.IsReadOnly = true;
            fbgBound.MinWidth = 120;
            if (fbgBound is DataGridTextColumn textColumn)
            {
                var typeStyle = new Style(typeof(TextBlock), textColumn.ElementStyle);
                typeStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding("ApiMetadata.FbgTypeDetail")));
                textColumn.ElementStyle = typeStyle;
            }
        }

        if (!_wiringGrid.Columns.Any(c => HeaderText(c.Header) == "Vyhodnotenie SN/WL"))
        {
            var evaluationStyle = new Style(typeof(TextBlock));
            evaluationStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding("ApiMetadata.WavelengthEvaluationDetail")));
            _wiringGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Vyhodnotenie SN/WL", Binding = new Binding("ApiMetadata.WavelengthEvaluation"),
                IsReadOnly = true, Width = 155, MinWidth = 130, ElementStyle = evaluationStyle
            });
        }
        if (!_wiringGrid.Columns.Any(c => HeaderText(c.Header) == "Sylex SN"))
        {
            var sylexColumn = new DataGridTextColumn
            {
                Header = "Sylex SN",
                Binding = new Binding("ApiMetadata.SylexSerialNumber") { Mode = BindingMode.OneWay },
                IsReadOnly = true,
                Width = new DataGridLength(118),
                MinWidth = 105,
            };
            int inputSnIndex = _wiringGrid.Columns.IndexOf(
                _wiringGrid.Columns.First(c => HeaderText(c.Header) == "FBG sensor SN (kanál)"));
            _wiringGrid.Columns.Insert(Math.Max(0, inputSnIndex), sylexColumn);
        }

        // Keep one deterministic left-to-right table composition on every window size.
        // The check box is followed by 14 data fields; production metadata never moves
        // into a secondary panel or changes position when runtime enrichments load.
        AddSensorLinksColumn();
        string[] columnOrder =
        {
            "Kalibrovať", "Kanál", "Peak ID", "FBG index", "Aktuálna λ [nm]", "Intenzita",
            "Typ FBG", "Vyhodnotenie SN/WL", "Sylex SN", "Otvoriť", "FBG sensor SN (kanál)", "FBG sensor SN CHAIN",
            "Zákazka", "Názov snímača", "Popis výrobku", "Poznámky",
        };
        for (int displayIndex = 0; displayIndex < columnOrder.Length; displayIndex++)
        {
            DataGridColumn? column = _wiringGrid.Columns.FirstOrDefault(c => HeaderText(c.Header) == columnOrder[displayIndex]);
            if (column is not null) column.DisplayIndex = displayIndex;
        }

        if (_wiringGrid.ContextMenu is null)
        {
            var menu = new ContextMenu();
            var spectrum = new MenuItem { Header = "Zobraziť spektrum kanála" };
            spectrum.Click += ShowSelectedSpectrum_Click;
            menu.Items.Add(spectrum);
            _wiringGrid.ContextMenu = menu;
        }
        _wiringGrid.PreviewMouseRightButtonDown -= WiringGrid_PreviewMouseRightButtonDown;
        _wiringGrid.PreviewMouseRightButtonDown += WiringGrid_PreviewMouseRightButtonDown;

        EnsureProductionInfoBanner();
    }

    private void EnsureProductionInfoBanner()
    {
        if (_productionInfoBanner is not null || _wiringGrid?.Parent is not Grid grid) return;
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
        };
        var banner = new Border
        {
            Background = TryFindResource("SurfaceAltBrush") as Brush ?? Brushes.Transparent,
            BorderBrush = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 6, 9, 6),
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = text,
        };
        Grid.SetRow(banner, 2);
        grid.Children.Add(banner);
        var dismiss = new Button
        {
            Content = "×", ToolTip = "Zavrieť hlásenie", Padding = new Thickness(8, 0, 8, 0),
            Style = TryFindResource("GhostButton") as Style,
        };
        dismiss.Click += (_, _) => banner.Visibility = Visibility.Collapsed;
        banner.Child = null;
        var content = new DockPanel();
        DockPanel.SetDock(dismiss, Dock.Right);
        content.Children.Add(dismiss);
        content.Children.Add(text);
        banner.Child = content;
        _productionInfoBanner = banner;
        _productionInfoText = text;
    }

    private void ConfigureProductionTabs()
    {
        _productionTabs = FindProductionDescendants<TabControl>(this)
            .FirstOrDefault(tab => tab.Items.OfType<TabItem>().Any(item => HeaderText(item.Header) == "Zapojenie"));
        if (_productionTabs is null) return;

        _liveMonitorTab = _productionTabs.Items.OfType<TabItem>()
            .FirstOrDefault(item => HeaderText(item.Header) == "Live monitor");
        if (_liveMonitorTab?.Content is UIElement existing && _productionRunPanel is null)
        {
            _liveMonitorTab.Content = null;
            var panel = new DockPanel();
            _productionRunPanel = BuildProductionRunPanel();
            DockPanel.SetDock(_productionRunPanel, Dock.Top);
            panel.Children.Add(_productionRunPanel);
            panel.Children.Add(existing);
            _liveMonitorTab.Content = panel;
        }

        if (!_productionTabs.Items.OfType<TabItem>().Any(item => HeaderText(item.Header) == "Dáta"))
            _productionTabs.Items.Add(BuildDataTab());
    }

    private Border BuildProductionRunPanel()
    {
        _productionRunHeadline = new TextBlock
        {
            Text = "Kalibrácia pripravená",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
        };
        _productionRunDetail = new TextBlock
        {
            Text = "Po spustení tu bude aktuálne plato, senzor, stav referencie a wavelength samples.",
            Margin = new Thickness(0, 4, 0, 7),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        };
        _productionRunProgress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 10 };
        var stack = new StackPanel();
        stack.Children.Add(_productionRunHeadline);
        stack.Children.Add(_productionRunDetail);
        stack.Children.Add(_productionRunProgress);
        return new Border
        {
            Background = TryFindResource("SurfaceAltBrush") as Brush ?? Brushes.Transparent,
            BorderBrush = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(8, 8, 8, 4),
            Child = stack,
        };
    }

    private TabItem BuildDataTab()
    {
        _dataRootText = new TextBlock { Text = _viewModel.StorageDirectories + "\n" + _viewModel.StorageStatus, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas") };
        _currentRunPathText = new TextBlock { Text = "—", TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas") };

        var rootButton = new Button { Content = "Otvoriť koreň kalibrácií", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 8, 8, 0) };
        rootButton.Click += (_, _) => OpenFolder(CalibrationStorage.Settings.Load().LocalDirectory);
        var runButton = new Button { Content = "Otvoriť aktuálnu / poslednú kalibráciu", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 8, 0, 0) };
        runButton.Click += (_, _) =>
        {
            string? run = FindLatestRunDirectory();
            if (run is not null) OpenFolder(run);
        };

        var buttons = new WrapPanel();
        buttons.Children.Add(rootButton);
        buttons.Children.Add(runButton);

        var stack = new StackPanel { Margin = new Thickness(14), MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(new TextBlock { Text = "Ukladanie kalibrácie", FontSize = 17, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = "Behy sa ukladajú podľa dátumu spustenia: rok → mesiac (napr. 2026 / 09_September) → run. Raw samples, wavelength trace, CSV, summary a reporty zostávajú spolu aj pri prechode do ďalšieho mesiaca. Lokálny zápis pokračuje aj pri výpadku siete; čakajúca sieťová kópia sa doplní automaticky. Nastavenia sú v Administrácii.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 12),
        });
        stack.Children.Add(new TextBlock { Text = "Úložiská a synchronizácia", FontWeight = FontWeights.SemiBold });
        stack.Children.Add(_dataRootText);
        stack.Children.Add(new TextBlock { Text = "Aktuálny / posledný run", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        stack.Children.Add(_currentRunPathText);
        stack.Children.Add(new TextBlock { Text = $"Operátor: {Environment.UserName}", Margin = new Thickness(0, 10, 0, 0) });
        stack.Children.Add(buttons);
        return new TabItem { Header = "Dáta", Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
    }

    private void RenameReferenceStabilityLabels()
    {
        foreach (TextBlock text in FindProductionDescendants<TextBlock>(this))
        {
            if (string.Equals(text.Text, "Stabilita komory", StringComparison.Ordinal))
                text.Text = "Stabilita referencie WIKA";
            else if (string.Equals(text.Text, "Default sensor timeout [min]", StringComparison.Ordinal))
            {
                text.Text = "Minimálny základ FBG limitu [min]";
                text.ToolTip = "Automatický čas podľa vzoriek a intervalu + 20 % rezerva. Predĺženie len pri pokroku; pevný strop 90 min od začiatku FBG fázy.";
                text.ToolTip = "Globálny maximálny čas čakania na stabilitu každého peaku; riadok môže mať vlastný override.";
            }
        }
    }

    private void CaptureRunLockedControls()
    {
        foreach (Control control in FindProductionDescendants<Control>(this))
        {
            if (control is TextBox or ComboBox or CheckBox && control.Name != "OperatorDecisionReasonInput")
                _productionInputEnabled.TryAdd(control, control.IsEnabled);
        }
        foreach (DataGrid grid in FindProductionDescendants<DataGrid>(this))
            _productionGridReadOnly.TryAdd(grid, grid.IsReadOnly);
    }

    private void ApplyRunInputLock(bool running)
    {
        foreach ((Control control, bool original) in _productionInputEnabled.ToArray())
        {
            if (!control.IsLoaded || control.Name == "OperatorDecisionReasonInput") continue;
            control.IsEnabled = running ? false : original;
        }
        foreach ((DataGrid grid, bool original) in _productionGridReadOnly.ToArray())
        {
            if (!grid.IsLoaded) continue;
            grid.IsReadOnly = running || original;
        }
    }

    private void OnProductionWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalibrationViewModel.IsRunning))
        {
            ApplyRunInputLock(_viewModel.IsRunning);
            if (_viewModel.IsRunning && _liveMonitorTab is not null && _productionTabs is not null)
                _productionTabs.SelectedItem = _overviewTab ?? _liveMonitorTab;
            RefreshDataPathPanel();
        }
        else if (e.PropertyName == nameof(CalibrationViewModel.SelectedProfile))
        {
            Guid? now = _viewModel.SelectedProfile?.Id;
            if (now != _lastProfileSelection)
            {
                _lastProfileSelection = now;
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (!_viewModel.IsRunning && _viewModel.MarkAllPlateausCommand.CanExecute(null))
                    {
                        _viewModel.MarkAllPlateausCommand.Execute(null);
                        if (_viewModel.SaveSetupCommand.CanExecute(null)) _viewModel.SaveSetupCommand.Execute(null);
                    }
                }));
            }
        }

        if (e.PropertyName is nameof(CalibrationViewModel.RunState)
            or nameof(CalibrationViewModel.PlateauLabel)
            or nameof(CalibrationViewModel.TemperatureLabel)
            or nameof(CalibrationViewModel.ReferenceTemperatureLabel)
            or nameof(CalibrationViewModel.StableLabel)
            or nameof(CalibrationViewModel.StatusMessage)
            or nameof(CalibrationViewModel.IsRunning))
        {
            UpdateProductionRunPanel();
        }
    }

    private void UpdateProductionRunPanel()
    {
        if (_productionRunHeadline is null || _productionRunDetail is null || _productionRunProgress is null) return;
        CalibrationWorkspaceStatusSnapshot snapshot = CalibrationStatusViewModel.Instance.GetWorkspace(_chamberId);
        CalibrationTargetProgressViewModel? active = _viewModel.TargetProgress.FirstOrDefault(target =>
            target.State is not CalibrationTargetState.Stable and not CalibrationTargetState.Overridden)
            ?? _viewModel.TargetProgress.FirstOrDefault();

        _productionRunHeadline.Text = _viewModel.IsRunning
            ? $"{_viewModel.RunState} · {_viewModel.PlateauLabel}"
            : "Kalibrácia pripravená";

        string sensor = active is null
            ? "snímač: —"
            : $"snímač: {active.SerialNumber} · kanál {active.Channel} · peak {active.PeakId} · samples {active.SamplesLabel}";
        _productionRunDetail.Text = _viewModel.IsRunning
            ? $"{sensor}\nWIKA: {_viewModel.ReferenceTemperatureLabel} · komora: {_viewModel.TemperatureLabel} (informatívne) · {_viewModel.StatusMessage}"
            : "Po spustení sa zobrazí aktuálne plato, senzor, činnosť, WIKA referencia a wavelength samples.";
        _productionRunProgress.Value = _viewModel.IsRunning ? snapshot.ProgressPercent : 0;
    }

    private void OnProductionPeaksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (CalibrationPeakRowViewModel row in e.OldItems) DetachProductionRow(row);
        if (e.NewItems is not null)
            foreach (CalibrationPeakRowViewModel row in e.NewItems) AttachProductionRow(row);
        SchedulePeakIdentityReconcile();
    }

    private void AttachProductionRow(CalibrationPeakRowViewModel row)
    {
        if (!_productionObservedRows.Add(row)) return;
        row.PropertyChanged += OnProductionRowPropertyChanged;
        SylexFosRowMetadataStore.SetParsedSerial(row, row.SerialNumber);
    }

    private void DetachProductionRow(CalibrationPeakRowViewModel row)
    {
        if (!_productionObservedRows.Remove(row)) return;
        row.PropertyChanged -= OnProductionRowPropertyChanged;
    }

    private void OnProductionRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CalibrationPeakRowViewModel row) return;
        if (e.PropertyName is nameof(CalibrationPeakRowViewModel.ChannelSerialNumber)
            or nameof(CalibrationPeakRowViewModel.ChainSerialNumber)
            or nameof(CalibrationPeakRowViewModel.SerialNumber))
        {
            SylexFosRowMetadataStore.SetParsedSerial(row, row.SerialNumber);
            // Metadata bindings update individual cells without rebuilding row containers.
            // Existing VM has a 350 ms debounce; this explicit save adds a second safety net so
            // every completed SN field is already on disk before the operator moves to the next row.
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!_viewModel.IsRunning && _viewModel.SaveSetupCommand.CanExecute(null))
                    _viewModel.SaveSetupCommand.Execute(null);
            }));
        }
    }

    private void SchedulePeakIdentityReconcile()
    {
        _rowReconcileCts?.Cancel();
        _rowReconcileCts?.Dispose();
        _rowReconcileCts = new CancellationTokenSource();
        CancellationToken token = _rowReconcileCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(ReconcilePeakIdentityUi, DispatcherPriority.Background, token);
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void ReconcilePeakIdentityUi()
    {
        HashSet<string> current = CurrentPeakIdentities();
        if (!_peakIdentityBaselineReady)
        {
            _knownPeakIdentities = current;
            _peakIdentityBaselineReady = true;
            return;
        }

        if (PeakTopologyComparer.AreEquivalent(current, _knownPeakIdentities))
        {
            // A normal reconnect/refresh rebuilds the observable collection. Its API-side source
            // identifier may be temporarily blank or refreshed, but the same channel + Peak ID
            // is not a newly connected row and must not focus the editor or show a false banner.
            _knownPeakIdentities = current;
            return;
        }

        string[] added = current.Except(_knownPeakIdentities, StringComparer.OrdinalIgnoreCase).ToArray();
        string[] removed = _knownPeakIdentities.Except(current, StringComparer.OrdinalIgnoreCase).ToArray();
        _knownPeakIdentities = current;

        if (removed.Length > 0)
            ShowProductionInfo($"PeakLogger: odstránených {removed.Length} riadkov / peakov. Tabuľka zapojenia bola aktualizovaná.");

        if (added.Length > 0)
        {
            if (_wiringEntryModeSequential && TryPairSequentialPeak(added)) return;

            CalibrationPeakRowViewModel? row = _viewModel.Peaks.FirstOrDefault(p => added.Contains(PeakIdentity(p), StringComparer.OrdinalIgnoreCase));
            if (row is not null)
            {
                ShowProductionInfo($"PeakLogger: pribudol nový riadok {row.Channel} / {row.PeakId}. Zadaj FBG sensor SN.");
                // A newly detected peak must not steal the operator's existing row selection.
                if (_wiringGrid?.SelectedItem is null) FocusNewPeakRow(row);
            }
        }
    }

    private void FocusNewPeakRow(CalibrationPeakRowViewModel row)
    {
        if (_wiringGrid is null || _viewModel.IsRunning) return;
        DataGridColumn? serialColumn = _wiringGrid.Columns.FirstOrDefault(c => HeaderText(c.Header) == "FBG sensor SN (kanál)");
        if (serialColumn is null) return;

        _wiringGrid.SelectedItem = row;
        _wiringGrid.ScrollIntoView(row, serialColumn);
        _wiringGrid.CurrentCell = new DataGridCellInfo(row, serialColumn);
        _wiringGrid.Focus();
        _wiringGrid.BeginEdit();

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (_wiringGrid.ItemContainerGenerator.ContainerFromItem(row) is not DataGridRow dataRow) return;
            dataRow.BorderBrush = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
            dataRow.BorderThickness = new Thickness(3);
            DataGridCell? cell = FindCell(dataRow, serialColumn.DisplayIndex);
            cell?.Focus();
            if (FindProductionDescendants<TextBox>((DependencyObject?)cell ?? dataRow).FirstOrDefault() is TextBox editor)
            {
                editor.Focus();
                Keyboard.Focus(editor);
                editor.SelectAll();
            }
        }));
    }

    private static DataGridCell? FindCell(DataGridRow row, int displayIndex)
    {
        DataGridCellsPresenter? presenter = FindProductionDescendants<DataGridCellsPresenter>(row).FirstOrDefault();
        if (presenter is null) return null;
        return presenter.ItemContainerGenerator.ContainerFromIndex(displayIndex) as DataGridCell;
    }

    private void ShowProductionInfo(string message)
    {
        if (_productionInfoBanner is null || _productionInfoText is null) return;
        _productionInfoText.Text = message;
        _productionInfoBanner.Visibility = Visibility.Visible;
        AppLog.Info("FBG zapojenie", message);
    }

    private void StartReferenceFiveSecondRefresh()
    {
        _referenceFiveSecondTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _referenceFiveSecondTimer.Tick += ReferenceFiveSecondTimer_Tick;
        _referenceFiveSecondTimer.Start();
    }

    private void ReferenceFiveSecondTimer_Tick(object? sender, EventArgs e)
    {
        if (_referenceRefreshRequested || _viewModel.IsRunning || _viewModel.SelectedF100 is null) return;
        if (!_viewModel.CheckF100Command.CanExecute(null)) return;
        _referenceRefreshRequested = true;
        _viewModel.CheckF100Command.Execute(null);
        _ = Task.Run(async () =>
        {
            await Task.Delay(4200).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() => _referenceRefreshRequested = false);
        });
    }

    private void StartPeakLoggerTopologyWatch()
    {
        _topologyTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3),
        };
        _topologyTimer.Tick += TopologyTimer_Tick;
        _topologyTimer.Start();
    }

    private async void TopologyTimer_Tick(object? sender, EventArgs e)
    {
        if (_viewModel.IsRunning || !_viewModel.PeakLoggerConnected || _viewModel.UseSimulator || _extendedPeakLoggerApi is null) return;
        if (_topologyPollCts is not null) return;
        _topologyPollCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            IReadOnlySet<string>? topology = await _extendedPeakLoggerApi.ReadTopologyAsync(
                _viewModel.PeakLoggerHost,
                _viewModel.PeakLoggerPort,
                _topologyPollCts.Token);
            if (topology is null) return;

            HashSet<string> live = topology.Where(identity => !_viewModel.IsPeakLoggerSourceIgnored(identity)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!_peakIdentityBaselineReady || _knownPeakIdentities.Count == 0)
            {
                // Only trigger a refresh if the API reports topology that is genuinely different
                // from the UI. The row reconciliation will then focus the new item.
                if (PeakTopologyComparer.AreEquivalent(live, CurrentPeakIdentities())) return;
            }
            if (!ConfirmTopologyChange(live, CurrentPeakIdentities())) return;

            if (_viewModel.SaveSetupCommand.CanExecute(null)) _viewModel.SaveSetupCommand.Execute(null);
            ShowProductionInfo("PeakLogger hlási zmenu zapojenia – aktualizujem tabuľku…");
            await _viewModel.RefreshSensorsInBackgroundAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Warn("PeakLogger topology", ex.Message);
        }
        finally
        {
            _topologyPollCts.Dispose();
            _topologyPollCts = null;
        }
    }

    private void WiringGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node is not null && node is not DataGridRow) node = VisualTreeHelper.GetParent(node);
        if (node is DataGridRow row && _wiringGrid is not null) _wiringGrid.SelectedItem = row.Item;
    }

    private async void ShowSelectedSpectrum_Click(object sender, RoutedEventArgs e)
    {
        if (_wiringGrid?.SelectedItem is not CalibrationPeakRowViewModel row || _extendedPeakLoggerApi is null) return;
        try
        {
            ShowProductionInfo($"Načítavam spektrum kanála {row.Channel}…");
            IReadOnlyList<PeakLoggerSpectrumPoint> points = await _extendedPeakLoggerApi.ReadSpectrumAsync(
                _viewModel.PeakLoggerHost,
                _viewModel.PeakLoggerPort,
                row.Channel,
                row.PeakLoggerDeviceSerialNumber);
            var window = new PeakLoggerSpectrumWindow(row.Channel, row.PeakLoggerDeviceSerialNumber, points) { Owner = this };
            window.Show();
            ShowProductionInfo($"Spektrum kanála {row.Channel}: načítaných {points.Count} bodov.");
        }
        catch (Exception ex)
        {
            ShowProductionInfo($"Spektrum kanála {row.Channel} sa nepodarilo načítať: {ex.Message}");
        }
    }

    private void RefreshDataPathPanel()
    {
        if (_dataRootText is not null) _dataRootText.Text = _viewModel.StorageDirectories + "\n" + _viewModel.StorageStatus;
        if (_currentRunPathText is null) return;
        string? run = FindLatestRunDirectory();
        if (run is null)
        {
            _currentRunPathText.Text = "Zatiaľ neexistuje žiadny run adresár.";
            return;
        }
        _currentRunPathText.Text = BuildRunPathDescription(run);
    }

    private static string BuildRunPathDescription(string directory)
    {
        try
        {
            string summary = Path.Combine(directory, "summary.json");
            if (File.Exists(summary))
            {
                CalibrationRunRecord? run = JsonSerializer.Deserialize<CalibrationRunRecord>(File.ReadAllText(summary), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (run is not null)
                    return $"{directory}\nRunId: {run.RunId}\nZačiatok: {run.StartedAt:yyyy-MM-dd HH:mm:ss}\nOperátor: {run.Operator}";
            }
        }
        catch { }
        return directory;
    }

    private string? FindLatestRunDirectory() => _viewModel.CurrentRunDirectory;

    private static void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private HashSet<string> CurrentPeakIdentities() =>
        _viewModel.Peaks.Select(PeakIdentity).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private bool ConfirmTopologyChange(HashSet<string> live, HashSet<string> displayed)
    {
        if (PeakTopologyComparer.AreEquivalent(live, displayed))
        {
            _pendingTopologyChange = null;
            _pendingTopologyChangeConfirmations = 0;
            return false;
        }

        if (_pendingTopologyChange is null || !_pendingTopologyChange.SetEquals(live))
        {
            _pendingTopologyChange = live.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _pendingTopologyChangeConfirmations = 1;
            return false;
        }

        return ++_pendingTopologyChangeConfirmations >= 2;
    }

    private static string PeakIdentity(CalibrationPeakRowViewModel row) =>
        $"{row.PeakLoggerDeviceSerialNumber}|{row.Channel}|{row.PeakId}";

    private static string HeaderText(object? header) => header switch
    {
        TextBlock text => text.Text ?? string.Empty,
        _ => header?.ToString() ?? string.Empty,
    };

    private static IEnumerable<T> FindProductionDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (T nested in FindProductionDescendants<T>(child)) yield return nested;
        }
    }

    private void OnProductionWorkspaceClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnProductionWorkspacePropertyChanged;
        _viewModel.Peaks.CollectionChanged -= OnProductionPeaksCollectionChanged;
        foreach (CalibrationPeakRowViewModel row in _productionObservedRows.ToArray()) DetachProductionRow(row);
        _referenceFiveSecondTimer?.Stop();
        _topologyTimer?.Stop();
        _rowReconcileCts?.Cancel();
        _rowReconcileCts?.Dispose();
        _topologyPollCts?.Cancel();
        _topologyPollCts?.Dispose();
        _extendedPeakLoggerApi?.Dispose();
        Closed -= OnProductionWorkspaceClosed;
    }
}
