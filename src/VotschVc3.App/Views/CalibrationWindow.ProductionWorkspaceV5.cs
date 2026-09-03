using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.Views;

/// <summary>
/// Diagnostic view over the production runner. It intentionally reads the same ViewModel progress
/// snapshots that are produced by CalibrationOrchestrator, so operator diagnostics follow the actual
/// decision path: temperature -> parallel FBG stability -> per-FBG measurement samples -> save point.
/// </summary>
internal static class CalibrationWindowProductionWorkspaceV5Bootstrap
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
        if (sender is CalibrationWindow window) window.InitializeProductionWorkspaceV5();
    }
}

public partial class CalibrationWindow
{
    private bool _productionWorkspaceV5Initialized;
    private DispatcherTimer? _calibrationDiagnosticsTimerV5;
    private TextBlock? _temperaturePhaseV5;
    private TextBlock? _fbgPhaseV5;
    private TextBlock? _measurementPhaseV5;
    private TextBlock? _decisionV5;
    private TextBlock? _etaV5;
    private TextBlock? _nextStepV5;
    private StackPanel? _timelineV5;

    internal void InitializeProductionWorkspaceV5()
    {
        if (_productionWorkspaceV5Initialized) return;
        _productionWorkspaceV5Initialized = true;

        InitializeProductionWorkspaceV4();
        Closed += OnProductionWorkspaceV5Closed;

        _calibrationDiagnosticsTimerV5 = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _calibrationDiagnosticsTimerV5.Tick += (_, _) => RefreshCalibrationDiagnosticsV5();
        _calibrationDiagnosticsTimerV5.Start();

        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            RenamePlateauDurationColumnV5();
            RenameStabilitySampleSettingV5();
            EnsureCalibrationDiagnosticsPanelV5();
            RefreshCalibrationDiagnosticsV5();
        }));
    }

    private void EnsureCalibrationDiagnosticsPanelV5()
    {
        if (_decisionV5 is not null) return;
        _liveMonitorTab ??= _productionTabs?.Items.OfType<TabItem>()
            .FirstOrDefault(item => HeaderText(item.Header) == "Live monitor");
        if (_liveMonitorTab?.Content is not DockPanel liveDock) return;

        _temperaturePhaseV5 = CreatePhaseValueV5();
        _fbgPhaseV5 = CreatePhaseValueV5();
        _measurementPhaseV5 = CreatePhaseValueV5();
        _decisionV5 = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 9, 0, 2),
        };
        _etaV5 = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78,
            Margin = new Thickness(0, 3, 0, 2),
        };
        _nextStepV5 = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.9,
            Margin = new Thickness(0, 2, 0, 7),
        };
        _timelineV5 = new StackPanel { Orientation = Orientation.Horizontal };

        var phaseCards = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        phaseCards.Children.Add(CreatePhaseCardV5(
            "1–2 · TEPLOTA",
            _temperaturePhaseV5,
            "Komora dostane cieľ plata. Stabilitu určuje WIKA, ak je priradená; inak interná sonda. FBG stabilizácia sa ešte nezačína."));
        phaseCards.Children.Add(CreatePhaseCardV5(
            "3 · STABILIZÁCIA FBG",
            _fbgPhaseV5,
            "Po stabilnej teplote sa všetky vybrané FBG peaky kontrolujú paralelne podľa samples, range, StdDev a driftu."));
        phaseCards.Children.Add(CreatePhaseCardV5(
            "4 · MERANIE SAMPLES",
            _measurementPhaseV5,
            "Každý peak začne vlastné meracie samples hneď po svojej stabilizácii. Ostatné peaky môžu ďalej stabilizovať."));

        var timelineScroll = new ScrollViewer
        {
            Content = _timelineV5,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 2, 0, 0),
            MaxHeight = 112,
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "Diagnostika kalibrácie · podľa čoho runner rozhoduje",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
        });
        content.Children.Add(new TextBlock
        {
            Text = "Stabilizačné samples sa nepoužijú do výsledku. Po stabilizácii každého FBG začne samostatné meracie okno 0/N. Ak sa FBG počas merania rozkolíše, jeho meracie samples sa zahodia a vracia sa do stabilizácie.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 2, 0, 0),
        });
        content.Children.Add(phaseCards);
        content.Children.Add(_decisionV5);
        content.Children.Add(_etaV5);
        content.Children.Add(_nextStepV5);
        content.Children.Add(new TextBlock
        {
            Text = "Timeline kalibračných plat",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 5, 0, 2),
        });
        content.Children.Add(timelineScroll);

        var card = new Border
        {
            Background = TryFindResource("SurfaceAltBrush") as Brush ?? Brushes.Transparent,
            BorderBrush = TryFindResource("BorderBrush") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(8, 4, 8, 8),
            Child = content,
        };

        DockPanel.SetDock(card, Dock.Top);
        liveDock.Children.Insert(0, card);
    }

    private TextBlock CreatePhaseValueV5() => new()
    {
        FontWeight = FontWeights.SemiBold,
        FontSize = 13.5,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0),
    };

    private Border CreatePhaseCardV5(string title, TextBlock value, string tooltip)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11.5,
            Opacity = 0.75,
        });
        stack.Children.Add(value);
        return new Border
        {
            Width = 330,
            MinHeight = 76,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 8, 7),
            BorderBrush = TryFindResource("BorderBrush") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            ToolTip = tooltip,
            Child = stack,
        };
    }

    private void RefreshCalibrationDiagnosticsV5()
    {
        if (_temperaturePhaseV5 is null || _fbgPhaseV5 is null || _measurementPhaseV5 is null ||
            _decisionV5 is null || _etaV5 is null || _nextStepV5 is null || _timelineV5 is null)
            return;

        CalibrationTargetProgressViewModel[] rows = _viewModel.TargetProgress.ToArray();
        int total = rows.Length;
        int waitingTemp = rows.Count(row => row.State == CalibrationTargetState.WaitingForTemperature);
        int measuring = rows.Count(IsMeasuringV5);
        int completed = rows.Count(row => row.State is CalibrationTargetState.Stable or CalibrationTargetState.Overridden);
        int failed = rows.Count(row => row.State is CalibrationTargetState.TimedOut or CalibrationTargetState.PeakLost or CalibrationTargetState.Disconnected or CalibrationTargetState.Failed);
        int stabilizing = Math.Max(0, total - waitingTemp - measuring - completed - failed);

        bool waitingForTemperature = _viewModel.RunState.Contains("WaitingForChamberStability", StringComparison.OrdinalIgnoreCase) ||
                                     waitingTemp > 0;
        bool running = _viewModel.IsRunning;

        _temperaturePhaseV5.Text = !running
            ? "Pripravené"
            : waitingForTemperature
                ? $"ČAKÁM · {_viewModel.ReferenceTemperatureLabel} · {_viewModel.TemperatureLabel}"
                : $"STABILNÁ ✓ · {_viewModel.ReferenceTemperatureLabel}";

        _fbgPhaseV5.Text = total == 0
            ? "Čakám na FBG dáta"
            : waitingForTemperature
                ? $"ČAKÁ · {total} peakov začne až po stabilnej teplote"
                : $"{stabilizing} stabilizuje · {measuring} už meria · {completed} hotovo";

        int measurementCollected = rows.Where(IsMeasuringV5).Sum(row => row.StableSamples);
        int measurementRequired = rows.Where(IsMeasuringV5).Sum(row => row.RequiredSamples);
        _measurementPhaseV5.Text = total == 0
            ? "—"
            : $"meria {measuring} · hotovo {completed}/{total}" +
              (measuring > 0 ? $" · samples {measurementCollected}/{measurementRequired}" : string.Empty);

        string blocker;
        if (!running)
            blocker = "Kalibrácia nie je spustená.";
        else if (waitingForTemperature)
            blocker = "ČAKÁM NA: stabilnú referenčnú/internú teplotu a minimálny čas teplotného plata.";
        else if (stabilizing > 0)
            blocker = $"ČAKÁM NA: stabilizáciu {stabilizing} FBG peak(ov). Stabilné peaky už môžu súčasne merať samples.";
        else if (measuring > 0)
            blocker = $"ČAKÁM NA: dokončenie meracích samples pre {measuring} FBG peak(ov).";
        else if (failed > 0)
            blocker = $"POZOR: {failed} peak(ov) skončilo warning/error stavom; kontrolujem failure policy.";
        else
            blocker = "Všetky FBG hotové ✓ · runner môže uložiť kalibračný bod a pokračovať.";

        _decisionV5.Text = blocker;
        _nextStepV5.Text = "Aktuálny runner: " + _viewModel.StatusMessage;
        _etaV5.Text = BuildEtaTextV5(rows, waitingForTemperature, running);
        RefreshTimelineV5();
    }

    private string BuildEtaTextV5(
        IReadOnlyList<CalibrationTargetProgressViewModel> rows,
        bool waitingForTemperature,
        bool running)
    {
        if (!running) return "Odhad: po spustení sa bude priebežne prepočítavať podľa aktívnej fázy.";
        if (waitingForTemperature)
            return "Odhad: teplotná stabilizácia je fyzikálne dynamická; presný čas sa nedá garantovať. Po otvorení teplotnej brány sa zobrazí odhad samples.";

        int longestRemainingSamples = rows
            .Where(IsMeasuringV5)
            .Select(row => Math.Max(0, row.RequiredSamples - row.StableSamples))
            .DefaultIfEmpty(0)
            .Max();
        int stabilizing = rows.Count(row => !IsMeasuringV5(row) && row.State == CalibrationTargetState.Stabilizing);
        string sampleEstimate = longestRemainingSamples > 0
            ? $"min. ~{TimeSpan.FromSeconds(longestRemainingSamples):mm\:ss} pre už merajúce peaky pri ~1 Hz"
            : "žiadne rozbehnuté meracie samples";
        return $"Odhad aktuálneho plata: {sampleEstimate}; {stabilizing} peak(ov) má ešte neurčitý čas stabilizácie.";
    }

    private void RefreshTimelineV5()
    {
        if (_timelineV5 is null) return;
        _timelineV5.Children.Clear();
        CalibrationPointRowViewModel[] points = _viewModel.CalibrationPoints.Where(point => point.Selected).ToArray();
        int current = ParseCurrentPlateauIndexV5(_viewModel.PlateauLabel);

        for (int index = 0; index < points.Length; index++)
        {
            CalibrationPointRowViewModel point = points[index];
            bool isCurrent = index == current;
            bool isDone = current >= 0 && index < current;
            string state = isDone ? "✓ hotovo" : isCurrent ? "▶ aktuálne" : "○ čaká";
            var text = new TextBlock
            {
                Text = $"{index + 1}. {point.Name}\n{point.TemperatureC:F1} °C · min {FormatTimelineDurationV5(point.Duration)}\n{state}",
                TextWrapping = TextWrapping.Wrap,
                Width = 180,
            };
            _timelineV5.Children.Add(new Border
            {
                Child = text,
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(0, 0, 7, 3),
                BorderBrush = isCurrent
                    ? TryFindResource("AccentBrush") as Brush ?? Brushes.CornflowerBlue
                    : TryFindResource("BorderBrush") as Brush ?? Brushes.Gray,
                BorderThickness = new Thickness(isCurrent ? 2 : 1),
                CornerRadius = new CornerRadius(6),
                Opacity = isDone ? 0.65 : 1,
            });
        }
    }

    private void RenamePlateauDurationColumnV5()
    {
        if (_productionTabs is null) return;
        TabItem? plateaus = _productionTabs.Items.OfType<TabItem>()
            .FirstOrDefault(item => HeaderText(item.Header) == "Kalibračné plata");
        if (plateaus is null) return;

        foreach (DataGrid grid in FindVisualChildrenV5<DataGrid>(plateaus))
        {
            DataGridColumn? column = grid.Columns.FirstOrDefault(item =>
                string.Equals(item.Header?.ToString(), "Min. čas plata", StringComparison.OrdinalIgnoreCase));
            if (column is null) continue;
            column.Header = new TextBlock
            {
                Text = "Min. čas teplotného plata",
                TextWrapping = TextWrapping.Wrap,
                ToolTip = "Počas tohto času sa sleduje stabilita WIKA/internej sondy. FBG stabilizácia začne až keď je teplota stabilná a tento minimálny čas už uplynul.",
            };
            column.Width = 180;
            break;
        }
    }

    private void RenameStabilitySampleSettingV5()
    {
        if (_productionTabs is null) return;
        foreach (TextBlock text in FindVisualChildrenV5<TextBlock>(_productionTabs))
        {
            if (!string.Equals(text.Text, "Počet stabilných samples", StringComparison.OrdinalIgnoreCase)) continue;
            text.Text = "Samples: stabilita / finálne meranie";
            text.ToolTip = "N samples tvorí rolling okno na potvrdenie stability. Po stabilizácii sa spustí nové, oddelené meracie okno s rovnakým počtom N samples; stabilizačné samples sa do výsledku nepoužijú.";
        }
    }

    private static bool IsMeasuringV5(CalibrationTargetProgressViewModel row) =>
        row.State == CalibrationTargetState.Live ||
        (row.Detail?.StartsWith("MERANIE", StringComparison.OrdinalIgnoreCase) ?? false);

    private static int ParseCurrentPlateauIndexV5(string label)
    {
        // Expected ViewModel label: "Plato X / Y".
        if (string.IsNullOrWhiteSpace(label)) return -1;
        string[] parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (int.TryParse(parts[i], out int value)) return Math.Max(0, value - 1);
        }
        return -1;
    }

    private static string FormatTimelineDurationV5(TimeSpan duration) =>
        duration.TotalHours >= 1 ? duration.ToString(@"hh\:mm\:ss") : duration.ToString(@"mm\:ss");

    private static IEnumerable<T> FindVisualChildrenV5<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (T descendant in FindVisualChildrenV5<T>(child)) yield return descendant;
        }
    }

    private void OnProductionWorkspaceV5Closed(object? sender, EventArgs e)
    {
        if (_calibrationDiagnosticsTimerV5 is not null)
        {
            _calibrationDiagnosticsTimerV5.Stop();
            _calibrationDiagnosticsTimerV5 = null;
        }
        Closed -= OnProductionWorkspaceV5Closed;
    }
}
