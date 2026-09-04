using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.Calibration;
using VotschVc3.App.Charting;
using VotschVc3.App.Notifications;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

/// <summary>
/// Live calibration traces + operator validation alerts. Wavelength and temperature intentionally
/// use two aligned charts because nm and °C must never share one numerical Y scale.
/// </summary>
internal static class CalibrationWindowProductionWorkspaceV4Bootstrap
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
        if (sender is CalibrationWindow window) window.InitializeProductionWorkspaceV4();
    }
}

public partial class CalibrationWindow
{
    private const int MaxLiveTracePoints = 3000;
    private bool _productionWorkspaceV4Initialized;
    private readonly Dictionary<CalibrationPeakRowViewModel, List<(DateTimeOffset Time, double Wavelength)>> _fbgLiveTrace = new();
    private readonly HashSet<CalibrationPeakRowViewModel> _fbgLiveObservedRows = new();
    private readonly Dictionary<CalibrationPeakRowViewModel, bool> _lastSnWarningState = new();
    private readonly Dictionary<string, bool> _fbgTraceVisibilityByKey = new(StringComparer.Ordinal);
    private System.Windows.Controls.Primitives.UniformGrid? _fbgPeakChartsPanel;
    private readonly Dictionary<CalibrationPeakRowViewModel, ChartView> _peakCharts = new();
    private ChartView? _fbgReferenceTraceChart;
    private ChartView? _chamberTraceChart;
    private ComboBox? _peakDisplayMode;
    private readonly List<(DateTimeOffset Time, double Temperature)> _chamberTrace = new();
    private string _lastChamberSnapshot = "";
    private TextBlock? _fbgTraceSummary;
    private WrapPanel? _fbgTraceFilterPanel;
    private TextBlock? _fbgTraceFilterSummary;
    private DateTimeOffset _liveTraceOrigin = DateTimeOffset.Now;
    private bool _wasRunningV4;
    private CancellationTokenSource? _primeReferenceCts;

    private static readonly Brush[] FbgTracePalette =
    {
        Brushes.DeepSkyBlue, Brushes.Orange, Brushes.MediumSeaGreen, Brushes.Violet,
        Brushes.Gold, Brushes.Coral, Brushes.CornflowerBlue, Brushes.LightGreen,
        Brushes.HotPink, Brushes.Turquoise, Brushes.Khaki, Brushes.Salmon,
        Brushes.Plum, Brushes.SkyBlue, Brushes.PaleGreen, Brushes.Wheat,
    };

    internal void InitializeProductionWorkspaceV4()
    {
        if (_productionWorkspaceV4Initialized) return;
        _productionWorkspaceV4Initialized = true;

        InitializeProductionWorkspaceV3();
        _wasRunningV4 = _viewModel.IsRunning;

        _viewModel.Peaks.CollectionChanged += OnFbgTracePeaksChanged;
        _viewModel.PropertyChanged += OnFbgTraceViewModelChanged;
        foreach (CalibrationPeakRowViewModel row in _viewModel.Peaks) AttachFbgTraceRow(row);

        CalibrationReferenceStatusStore.Instance.Changed += OnFbgReferenceTraceChanged;
        if (_sylexFosIntegration is not null)
            _sylexFosIntegration.RowValidationFailed += OnSylexRowValidationFailedV4;

        Closed += OnProductionWorkspaceV4Closed;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            EnsureFbgLiveTracePanel();
            RefreshFbgTraceFilterPanel();
            RefreshFbgLiveTraceCharts();
            _ = PrimeReferenceReadAsync();
        }));
    }

    private void EnsureFbgLiveTracePanel()
    {
        if (_fbgPeakChartsPanel is not null) return;
        _liveMonitorTab ??= _productionTabs?.Items.OfType<TabItem>()
            .FirstOrDefault(item => HeaderText(item.Header) == "Live monitor");
        if (_liveMonitorTab?.Content is not DockPanel liveDock) return;

        _fbgTraceSummary = new TextBlock
        {
            Text = "Čakám na vybrané FBG peaky a prvé live vzorky…",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 8),
            Opacity = 0.8,
        };

        _fbgPeakChartsPanel = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        _fbgPeakChartsPanel.SizeChanged += (_, e) =>
            _fbgPeakChartsPanel.Columns = e.NewSize.Width >= 1000 ? 2 : 1;
        _fbgReferenceTraceChart = new ChartView
        {
            ChartTitle = "WIKA referenčná teplota",
            Unit = " °C",
            EmptyText = "Čakám na prvú automatickú WIKA vzorku…",
            Height = 185,
            MinHeight = 160,
        };

        var soundToggle = new CheckBox
        {
            Content = "Pípnuť pri zlom SN / nezhode sondy",
            IsChecked = OperatorAlertSoundService.Enabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            ToolTip = "Jednorazový systémový zvuk pri neplatnom/duplicitnom SN alebo keď Sylex FOS API nepotvrdí sondu pre daný kanál. Nastavenie platí pre celú aplikáciu.",
        };
        soundToggle.Checked += (_, _) => OperatorAlertSoundService.Enabled = true;
        soundToggle.Unchecked += (_, _) => OperatorAlertSoundService.Enabled = false;

        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(soundToggle, Dock.Right);
        header.Children.Add(soundToggle);
        header.Children.Add(new TextBlock
        {
            Text = "Live priebeh kalibrácie",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });

        _fbgTraceFilterSummary = new TextBlock
        {
            Text = "Graf: —",
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8,
            Margin = new Thickness(0, 0, 10, 0),
        };

        Button showAllButton = CreateFbgTraceFilterButton("Všetky");
        showAllButton.ToolTip = "Zobraziť v grafe všetky peaky, ktoré sú označené na kalibráciu.";
        showAllButton.Click += (_, _) => SetAllFbgTraceVisibility(true);

        Button showNoneButton = CreateFbgTraceFilterButton("Žiadny");
        showNoneButton.ToolTip = "Dočasne skryť všetky wavelength krivky. Zber dát a kalibrácia pokračujú bez zmeny.";
        showNoneButton.Click += (_, _) => SetAllFbgTraceVisibility(false);

        var filterHeader = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 5) };
        var filterButtons = new StackPanel { Orientation = Orientation.Horizontal };
        filterButtons.Children.Add(showAllButton);
        filterButtons.Children.Add(showNoneButton);
        DockPanel.SetDock(filterButtons, Dock.Right);
        filterHeader.Children.Add(filterButtons);
        filterHeader.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock
                {
                    Text = "Peaky v grafe",
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0),
                },
                _fbgTraceFilterSummary,
            },
        });

        _fbgTraceFilterPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 4, 2),
        };
        var filterScroll = new ScrollViewer
        {
            Content = _fbgTraceFilterPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 105,
            Margin = new Thickness(0, 0, 0, 7),
        };

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_fbgTraceSummary);
        var modes = new WrapPanel { Margin = new Thickness(0, 6, 0, 12) };
        var chartMode = new ComboBox { Width = 210, ItemsSource = new[] { "FBG peaky", "WIKA teplota", "Komora teplota" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 12, 0) };
        _peakDisplayMode = new ComboBox { Width = 210, ItemsSource = new[] { "Iba aktívny peak", "Všetky peaky", "Vybrané peaky" }, SelectedIndex = 1 };
        modes.Children.Add(chartMode);
        modes.Children.Add(_peakDisplayMode);
        stack.Children.Add(modes);
        _chamberTraceChart = new ChartView { ChartTitle = "Komora · aktuálna teplota", Unit = " °C", Height = 300, EmptyText = "Čaká na údaje z kalibrácie", Visibility = Visibility.Collapsed };
        _fbgReferenceTraceChart.Visibility = Visibility.Collapsed;
        chartMode.SelectionChanged += (_, _) =>
        {
            _fbgPeakChartsPanel.Visibility = chartMode.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            _fbgReferenceTraceChart.Visibility = chartMode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            _chamberTraceChart.Visibility = chartMode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            _peakDisplayMode.Visibility = chartMode.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            filterHeader.Visibility = chartMode.SelectedIndex == 0 && _peakDisplayMode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            filterScroll.Visibility = filterHeader.Visibility;
        };
        _peakDisplayMode.SelectionChanged += (_, _) =>
        {
            filterHeader.Visibility = _peakDisplayMode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            filterScroll.Visibility = filterHeader.Visibility;
            RefreshFbgLiveTraceCharts();
        };
        filterHeader.Visibility = filterScroll.Visibility = Visibility.Collapsed;
        stack.Children.Add(filterHeader);
        stack.Children.Add(filterScroll);
        stack.Children.Add(_fbgPeakChartsPanel);
        stack.Children.Add(new TextBlock
        {
            Text = "Každý peak má vlastný graf a mierku v nm. Filter mení iba zobrazenie; zber dát a kalibrácia pokračujú aj pre skryté peaky.",
            Margin = new Thickness(0, 7, 0, 4),
            Opacity = 0.7,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(_fbgReferenceTraceChart);
        stack.Children.Add(_chamberTraceChart);

        var traceCard = new Border
        {
            Background = TryFindResource("SurfaceAltBrush") as Brush ?? Brushes.Transparent,
            BorderBrush = TryFindResource("BorderBrush") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 10, 12, 12),
            Margin = new Thickness(8, 4, 8, 8),
            Child = stack,
        };

        // Keep the pre-existing monitor content as the fill child; trace card is docked above it.
        UIElement? fill = liveDock.Children.Count > 1 ? liveDock.Children[^1] : null;
        int insertIndex = fill is null ? liveDock.Children.Count : liveDock.Children.IndexOf(fill);
        DockPanel.SetDock(traceCard, Dock.Top);
        liveDock.Children.Insert(insertIndex, traceCard);
    }

    private Button CreateFbgTraceFilterButton(string text)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(5, 0, 0, 0),
            MinWidth = 62,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        if (TryFindResource("AccentOutlineButton") is Style style)
            button.Style = style;
        return button;
    }

    private void RefreshFbgTraceFilterPanel()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(RefreshFbgTraceFilterPanel));
            return;
        }
        if (_fbgTraceFilterPanel is null) return;

        _fbgTraceFilterPanel.Children.Clear();
        CalibrationPeakRowViewModel[] calibrationRows = _viewModel.Peaks.Where(row => row.Selected).ToArray();
        foreach (CalibrationPeakRowViewModel row in calibrationRows)
        {
            string key = FbgTraceKey(row);
            if (!_fbgTraceVisibilityByKey.ContainsKey(key))
                _fbgTraceVisibilityByKey[key] = true;

            var check = new CheckBox
            {
                IsChecked = _fbgTraceVisibilityByKey[key],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 16, 4),
                ToolTip = BuildFbgTraceToolTip(row),
            };

            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new Border
            {
                Width = 13,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = GetFbgTraceBrush(row),
                Margin = new Thickness(5, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            label.Children.Add(new TextBlock
            {
                Text = BuildFbgTraceLabel(row),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 360,
            });
            check.Content = label;
            check.Checked += (_, _) =>
            {
                _fbgTraceVisibilityByKey[key] = true;
                RefreshFbgLiveTraceCharts();
                UpdateFbgTraceFilterSummary();
            };
            check.Unchecked += (_, _) =>
            {
                _fbgTraceVisibilityByKey[key] = false;
                RefreshFbgLiveTraceCharts();
                UpdateFbgTraceFilterSummary();
            };
            _fbgTraceFilterPanel.Children.Add(check);
        }

        if (calibrationRows.Length == 0)
        {
            _fbgTraceFilterPanel.Children.Add(new TextBlock
            {
                Text = "V Zapojení zatiaľ nie je označený žiadny peak na kalibráciu.",
                Opacity = 0.65,
                Margin = new Thickness(0, 4, 0, 5),
            });
        }
        UpdateFbgTraceFilterSummary();
    }

    private void SetAllFbgTraceVisibility(bool visible)
    {
        foreach (CalibrationPeakRowViewModel row in _viewModel.Peaks.Where(row => row.Selected))
            _fbgTraceVisibilityByKey[FbgTraceKey(row)] = visible;
        RefreshFbgTraceFilterPanel();
        RefreshFbgLiveTraceCharts();
    }

    private void UpdateFbgTraceFilterSummary()
    {
        if (_fbgTraceFilterSummary is null) return;
        int total = _viewModel.Peaks.Count(row => row.Selected);
        int visible = _viewModel.Peaks.Count(row => row.Selected && IsFbgTraceVisible(row));
        _fbgTraceFilterSummary.Text = $"zobrazené {visible} / {total}";
    }

    private static string FbgTraceKey(CalibrationPeakRowViewModel row) =>
        $"{row.PeakLoggerDeviceSerialNumber}|{row.Channel}|{row.PeakId}";

    private bool IsFbgTraceVisible(CalibrationPeakRowViewModel row)
    {
        if (_peakDisplayMode?.SelectedIndex == 1) return true;
        if (_peakDisplayMode?.SelectedIndex == 2)
            return !_fbgTraceVisibilityByKey.TryGetValue(FbgTraceKey(row), out bool visible) || visible;
        string active = _viewModel.Dashboard.ActivePeakKey;
        var activeRow = _viewModel.Peaks.FirstOrDefault(p => p.Selected && $"{p.SerialNumber}|{p.Channel}|{p.PeakId}" == active)
            ?? _viewModel.Peaks.FirstOrDefault(p => p.Selected);
        return ReferenceEquals(row, activeRow);
    }

    private Brush GetFbgTraceBrush(CalibrationPeakRowViewModel row)
    {
        int index = _viewModel.Peaks.IndexOf(row);
        if (index < 0) index = 0;
        return FbgTracePalette[index % FbgTracePalette.Length];
    }

    private static string BuildFbgTraceLabel(CalibrationPeakRowViewModel row)
    {
        string channelSn = string.IsNullOrWhiteSpace(row.ChannelSerialNumber) ? "bez SN" : row.ChannelSerialNumber;
        if (!string.IsNullOrWhiteSpace(row.ChainSerialNumber))
            return $"SN {channelSn} · CHAIN {row.ChainSerialNumber} · {row.Channel}/{row.PeakId}";
        return $"SN {channelSn} · {row.Channel}/{row.PeakId}";
    }

    private static string BuildFbgTraceToolTip(CalibrationPeakRowViewModel row)
    {
        string channelSn = string.IsNullOrWhiteSpace(row.ChannelSerialNumber) ? "—" : row.ChannelSerialNumber;
        string chainSn = string.IsNullOrWhiteSpace(row.ChainSerialNumber) ? "—" : row.ChainSerialNumber;
        return $"FBG sensor SN (kanál): {channelSn}\nCHAIN SN: {chainSn}\nKanál: {row.Channel}\nPeak ID: {row.PeakId}\nFBG index: {row.PeakIndex}\nAktuálna λ: {row.CurrentWavelengthNm:F6} nm";
    }

    private void OnFbgTracePeaksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (CalibrationPeakRowViewModel row in _fbgLiveObservedRows.ToArray()) DetachFbgTraceRow(row);
            foreach (CalibrationPeakRowViewModel row in _viewModel.Peaks) AttachFbgTraceRow(row);
        }
        else
        {
            if (e.OldItems is not null)
                foreach (object? item in e.OldItems)
                    if (item is CalibrationPeakRowViewModel row) DetachFbgTraceRow(row);
            if (e.NewItems is not null)
                foreach (object? item in e.NewItems)
                    if (item is CalibrationPeakRowViewModel row) AttachFbgTraceRow(row);
        }
        RefreshFbgTraceFilterPanel();
        RefreshFbgLiveTraceCharts();
    }

    private void AttachFbgTraceRow(CalibrationPeakRowViewModel? row)
    {
        if (row is null || !_fbgLiveObservedRows.Add(row)) return;
        row.PropertyChanged += OnFbgTraceRowChanged;
        _lastSnWarningState[row] = row.HasSerialNumberWarning;
        if (row.Selected && !_fbgTraceVisibilityByKey.ContainsKey(FbgTraceKey(row)))
            _fbgTraceVisibilityByKey[FbgTraceKey(row)] = true;
    }

    private void DetachFbgTraceRow(CalibrationPeakRowViewModel? row)
    {
        if (row is null || !_fbgLiveObservedRows.Remove(row)) return;
        row.PropertyChanged -= OnFbgTraceRowChanged;
        _lastSnWarningState.Remove(row);
        _fbgLiveTrace.Remove(row);
    }

    private void OnFbgTraceRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CalibrationPeakRowViewModel row) return;

        if (e.PropertyName == nameof(CalibrationPeakRowViewModel.LastWavelengthUpdate) &&
            row.Selected && row.LastWavelengthUpdate is { } timestamp && double.IsFinite(row.CurrentWavelengthNm))
        {
            if (!_fbgLiveTrace.TryGetValue(row, out List<(DateTimeOffset Time, double Wavelength)>? trace))
            {
                trace = new List<(DateTimeOffset, double)>();
                _fbgLiveTrace[row] = trace;
            }
            if (trace.Count == 0 || trace[^1].Time != timestamp)
            {
                trace.Add((timestamp, row.CurrentWavelengthNm));
                if (trace.Count > MaxLiveTracePoints) trace.RemoveRange(0, trace.Count - MaxLiveTracePoints);
            }
            RefreshFbgLiveTraceCharts();
        }
        else if (e.PropertyName == nameof(CalibrationPeakRowViewModel.Selected))
        {
            if (row.Selected && !_fbgTraceVisibilityByKey.ContainsKey(FbgTraceKey(row)))
                _fbgTraceVisibilityByKey[FbgTraceKey(row)] = true;
            RefreshFbgTraceFilterPanel();
            RefreshFbgLiveTraceCharts();
        }
        else if (e.PropertyName == nameof(CalibrationPeakRowViewModel.SerialNumber))
        {
            RefreshFbgTraceFilterPanel();
        }
        else if (e.PropertyName is nameof(CalibrationPeakRowViewModel.SerialNumberWarning) or nameof(CalibrationPeakRowViewModel.HasSerialNumberWarning))
        {
            bool previous = _lastSnWarningState.GetValueOrDefault(row);
            bool current = row.HasSerialNumberWarning;
            _lastSnWarningState[row] = current;
            if (current && !previous)
            {
                string identity = string.IsNullOrWhiteSpace(row.SerialNumber) ? $"{row.Channel}/{row.PeakId}" : row.SerialNumber;
                OperatorAlertSoundService.PlayWarning($"serial-warning:{identity}:{row.SerialNumberWarning}");
                AppNotificationService.Warning("FBG zapojenie", row.SerialNumberWarning, $"fbg-sn:{identity}:{row.SerialNumberWarning}");
            }
        }
    }

    private void OnSylexRowValidationFailedV4(object? sender, SylexFosRowValidationIssue issue)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => OnSylexRowValidationFailedV4(sender, issue)));
            return;
        }
        OperatorAlertSoundService.PlayWarning($"sylex:{issue.SerialNumber}:{issue.Row.Channel}");
        AppNotificationService.Warning("Sylex FOS kontrola", issue.Message, $"sylex:{issue.SerialNumber}:{issue.Row.Channel}");
    }

    private void OnFbgReferenceTraceChanged(object? sender, CalibrationReferenceChangedEventArgs e)
    {
        if (e.ChamberId != _chamberId) return;
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(RefreshFbgLiveTraceCharts));
            return;
        }
        RefreshFbgLiveTraceCharts();
    }

    private void OnFbgTraceViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalibrationViewModel.IsRunning))
        {
            if (_viewModel.IsRunning && !_wasRunningV4)
            {
                _fbgLiveTrace.Clear();
                _liveTraceOrigin = DateTimeOffset.Now;
            }
            _wasRunningV4 = _viewModel.IsRunning;
            RefreshFbgLiveTraceCharts();
        }
        else if (e.PropertyName == nameof(CalibrationViewModel.SelectedF100))
        {
            _ = PrimeReferenceReadAsync();
        }
    }

    private async Task PrimeReferenceReadAsync()
    {
        if (_viewModel.IsRunning || _viewModel.SelectedF100 is null) return;
        _primeReferenceCts?.Cancel();
        _primeReferenceCts?.Dispose();
        _primeReferenceCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            ThermometerDeviceViewModel device = _viewModel.SelectedF100;
            device.SelectedChannel = _viewModel.SelectedF100Channel;
            await device.ReadReferenceTemperatureAsync(_primeReferenceCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Automatic first sample is best-effort. Manual diagnostics remain available.
            VotschVc3.Core.Diagnostics.AppLog.Info("WIKA auto graph", $"Prvá automatická vzorka sa nepodarila: {ex.Message}");
        }
    }

    private void RefreshFbgLiveTraceCharts()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(RefreshFbgLiveTraceCharts));
            return;
        }
        if (_fbgPeakChartsPanel is null || _fbgReferenceTraceChart is null) return;
        if (_viewModel.IsRunning && _viewModel.Dashboard.LastUpdate != _lastChamberSnapshot && _viewModel.Dashboard.ActualTemperature is { } chamberTemperature)
        {
            _lastChamberSnapshot = _viewModel.Dashboard.LastUpdate;
            _chamberTrace.Add((DateTimeOffset.Now, chamberTemperature));
            if (_chamberTrace.Count > 7200) _chamberTrace.RemoveRange(0, _chamberTrace.Count - 7200);
        }

        IReadOnlyList<CalibrationReferenceTracePoint> reference = CalibrationReferenceTraceStore.Instance.GetTrace(_chamberId);
        DateTimeOffset? firstFbg = _fbgLiveTrace
            .Where(pair => pair.Key.Selected && IsFbgTraceVisible(pair.Key) && pair.Value.Count > 0)
            .Select(pair => (DateTimeOffset?)pair.Value[0].Time)
            .OrderBy(value => value)
            .FirstOrDefault();
        DateTimeOffset? firstRef = reference.Count > 0 ? reference[0].Timestamp : null;
        DateTimeOffset origin = _viewModel.IsRunning
            ? _liveTraceOrigin
            : new[] { firstFbg, firstRef }.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(_liveTraceOrigin).Min();
        if (_chamberTraceChart is not null)
            _chamberTraceChart.Series = new[] { new ChartSeries("Komora", Brushes.CornflowerBlue,
                _chamberTrace.Where(p => p.Time >= _liveTraceOrigin).Select(p => new Point((p.Time - origin).TotalMinutes, p.Temperature)).ToArray(), strokeThickness: 2.3) };

        var visibleRows = _viewModel.Peaks.Where(p => p.Selected && IsFbgTraceVisible(p)).ToArray();
        foreach (var removed in _peakCharts.Keys.Where(row => !visibleRows.Contains(row)).ToArray())
        {
            _fbgPeakChartsPanel.Children.Remove((UIElement)_peakCharts[removed].Parent);
            _peakCharts.Remove(removed);
        }
        foreach (CalibrationPeakRowViewModel row in visibleRows)
        {
            if (!_peakCharts.TryGetValue(row, out ChartView? chart))
            {
                chart = new ChartView
                {
                    Unit = " nm", Height = 220, MinHeight = 180,
                    Margin = new Thickness(4, 4, 8, 12),
                    EmptyText = "Čakám na prvé vzorky tohto peaku…",
                };
                var peakCard = new StackPanel { Margin = new Thickness(4, 4, 8, 12) };
                var title = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4, 0, 0) };
                title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ChartView.ChartTitle)) { Source = chart });
                peakCard.Children.Add(title);
                peakCard.Children.Add(chart);
                _peakCharts.Add(row, chart);
            }
            var card = (UIElement)chart.Parent;
            int index = Array.IndexOf(visibleRows, row);
            if (_fbgPeakChartsPanel.Children.IndexOf(card) != index)
            {
                _fbgPeakChartsPanel.Children.Remove(card);
                _fbgPeakChartsPanel.Children.Insert(index, card);
            }
            chart.ChartTitle = BuildFbgTraceLabel(row);
            Point[] points = _fbgLiveTrace.TryGetValue(row, out var trace)
                ? trace.Where(p => !_viewModel.IsRunning || p.Time >= _liveTraceOrigin)
                    .Select(p => new Point((p.Time - origin).TotalMinutes, p.Wavelength)).ToArray()
                : Array.Empty<Point>();
            chart.Series = points.Length == 0 ? Array.Empty<ChartSeries>()
                : new[] { new ChartSeries(BuildFbgTraceLabel(row), GetFbgTraceBrush(row), points, strokeThickness: 1.8) };
        }

        IEnumerable<CalibrationReferenceTracePoint> referenceForView = _viewModel.IsRunning
            ? reference.Where(p => p.Timestamp >= _liveTraceOrigin)
            : reference;
        Point[] referencePoints = referenceForView
            .Select(p => new Point((p.Timestamp - origin).TotalMinutes, p.TemperatureC))
            .ToArray();
        Brush referenceBrush = TryFindResource("DangerBrush") as Brush ?? Brushes.IndianRed;
        _fbgReferenceTraceChart.Series = referencePoints.Length == 0
            ? Array.Empty<ChartSeries>()
            : new[] { new ChartSeries("WIKA referencia", referenceBrush, referencePoints, strokeThickness: 2.3) };

        if (_fbgTraceSummary is not null)
        {
            int calibrationSelected = _viewModel.Peaks.Count(p => p.Selected);
            int graphSelected = _viewModel.Peaks.Count(p => p.Selected && IsFbgTraceVisible(p));

            CalibrationReferenceSnapshot snapshot = CalibrationReferenceStatusStore.Instance.GetSnapshot(_chamberId);
            string referenceValue = snapshot.TemperatureC is { } t ? $"{t:F3} °C" : "—";
            _fbgTraceSummary.Text = $"Peaky: {calibrationSelected} · stabilné: {_viewModel.Dashboard.StableCount} · aktívny: {_viewModel.Dashboard.ActivePeak} · WIKA: {referenceValue} · komora: {_viewModel.Dashboard.Actual}";
        }
        UpdateFbgTraceFilterSummary();
    }

    private void OnProductionWorkspaceV4Closed(object? sender, EventArgs e)
    {
        _viewModel.Peaks.CollectionChanged -= OnFbgTracePeaksChanged;
        _viewModel.PropertyChanged -= OnFbgTraceViewModelChanged;
        foreach (CalibrationPeakRowViewModel row in _fbgLiveObservedRows.ToArray()) DetachFbgTraceRow(row);
        CalibrationReferenceStatusStore.Instance.Changed -= OnFbgReferenceTraceChanged;
        if (_sylexFosIntegration is not null)
            _sylexFosIntegration.RowValidationFailed -= OnSylexRowValidationFailedV4;
        _primeReferenceCts?.Cancel();
        _primeReferenceCts?.Dispose();
        Closed -= OnProductionWorkspaceV4Closed;
    }
}
