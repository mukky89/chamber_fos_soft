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
    private ChartView? _fbgWavelengthTraceChart;
    private ChartView? _fbgReferenceTraceChart;
    private TextBlock? _fbgTraceSummary;
    private DateTimeOffset _liveTraceOrigin = DateTimeOffset.Now;
    private bool _wasRunningV4;
    private CancellationTokenSource? _primeReferenceCts;

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
            RefreshFbgLiveTraceCharts();
            _ = PrimeReferenceReadAsync();
        }));
    }

    private void EnsureFbgLiveTracePanel()
    {
        if (_fbgWavelengthTraceChart is not null) return;
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

        _fbgWavelengthTraceChart = new ChartView
        {
            ChartTitle = "FBG wavelength · všetky vybrané peaky",
            Unit = " nm",
            EmptyText = "Vyber peaky na kalibráciu a pripoj PeakLogger…",
            Height = 230,
            MinHeight = 200,
        };
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

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_fbgTraceSummary);
        stack.Children.Add(_fbgWavelengthTraceChart);
        stack.Children.Add(new TextBlock
        {
            Text = "Referencia je na samostatnej osi °C, aby sa neskresľovali wavelength krivky v nm.",
            Margin = new Thickness(0, 7, 0, 4),
            Opacity = 0.7,
            FontSize = 11.5,
        });
        stack.Children.Add(_fbgReferenceTraceChart);

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
        RefreshFbgLiveTraceCharts();
    }

    private void AttachFbgTraceRow(CalibrationPeakRowViewModel? row)
    {
        if (row is null || !_fbgLiveObservedRows.Add(row)) return;
        row.PropertyChanged += OnFbgTraceRowChanged;
        _lastSnWarningState[row] = row.HasSerialNumberWarning;
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
            RefreshFbgLiveTraceCharts();
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
        if (_fbgWavelengthTraceChart is null || _fbgReferenceTraceChart is null) return;

        IReadOnlyList<CalibrationReferenceTracePoint> reference = CalibrationReferenceTraceStore.Instance.GetTrace(_chamberId);
        DateTimeOffset? firstFbg = _fbgLiveTrace
            .Where(pair => pair.Key.Selected && pair.Value.Count > 0)
            .Select(pair => (DateTimeOffset?)pair.Value[0].Time)
            .OrderBy(value => value)
            .FirstOrDefault();
        DateTimeOffset? firstRef = reference.Count > 0 ? reference[0].Timestamp : null;
        DateTimeOffset origin = _viewModel.IsRunning
            ? _liveTraceOrigin
            : new[] { firstFbg, firstRef }.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(_liveTraceOrigin).Min();

        Brush[] palette =
        {
            Brushes.DeepSkyBlue, Brushes.Orange, Brushes.MediumSeaGreen, Brushes.Violet,
            Brushes.Gold, Brushes.Coral, Brushes.CornflowerBlue, Brushes.LightGreen,
            Brushes.HotPink, Brushes.Turquoise, Brushes.Khaki, Brushes.Salmon,
            Brushes.Plum, Brushes.SkyBlue, Brushes.PaleGreen, Brushes.Wheat,
        };

        var wavelengthSeries = new List<ChartSeries>();
        int colorIndex = 0;
        foreach (CalibrationPeakRowViewModel row in _viewModel.Peaks.Where(p => p.Selected))
        {
            if (!_fbgLiveTrace.TryGetValue(row, out List<(DateTimeOffset Time, double Wavelength)>? trace) || trace.Count == 0) continue;
            Point[] points = trace
                .Where(p => !_viewModel.IsRunning || p.Time >= _liveTraceOrigin)
                .Select(p => new Point((p.Time - origin).TotalMinutes, p.Wavelength))
                .ToArray();
            if (points.Length == 0) continue;
            string sn = string.IsNullOrWhiteSpace(row.SerialNumber) ? "bez SN" : row.SerialNumber;
            wavelengthSeries.Add(new ChartSeries(
                $"{sn} · {row.Channel}/{row.PeakId}",
                palette[colorIndex++ % palette.Length],
                points,
                strokeThickness: 1.8));
        }
        _fbgWavelengthTraceChart.Series = wavelengthSeries;

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
            int selected = _viewModel.Peaks.Count(p => p.Selected);
            int withTrace = wavelengthSeries.Count;
            CalibrationReferenceSnapshot snapshot = CalibrationReferenceStatusStore.Instance.GetSnapshot(_chamberId);
            string referenceValue = snapshot.TemperatureC is { } t ? $"{t:F3} °C" : "—";
            _fbgTraceSummary.Text = $"Vybrané peaky: {selected} · aktívne wavelength krivky: {withTrace} · WIKA: {referenceValue} · {snapshot.PortName}";
        }
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
