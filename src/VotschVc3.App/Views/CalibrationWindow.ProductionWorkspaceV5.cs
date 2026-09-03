using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.Views;

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
            HideProfileDurationColumnV5();
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
            "Komora dostane cieľ kalibračného bodu. Stabilitu určuje WIKA, ak je priradená; inak interná sonda. Čas hold segmentu z profilu sa pri FBG kalibrácii ignoruje."));
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
            Text = "FBG kalibrácia ignoruje čas hold segmentu z profilu. Prechod riadi iba reálna stabilita teploty a následne stabilita a meranie jednotlivých FBG peakov. Stabilizačné samples sa nepoužijú do výsledku.",
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
            Text = "Timeline kalibračných bodov",
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

        bool waitingForTemperature = _viewModel.RunState.Contains("WaitingForChamberStability", StringComparison.OrdinalIgnoreCase) || waitingTemp > 0;
        bool running = _viewModel.IsRunning;

        _temperaturePhaseV5.Text = !running
            ? "Pripravené"
            : waitingForTemperature
                ? $"ČAKÁM · ref {_viewModel.ReferenceTemperatureLabel} · komora {_viewModel.TemperatureLabel}"
                : $"STABILNÁ ✓ · ref {_viewModel.ReferenceTemperatureLabel}";

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

        if (!running)
            _decisionV5.Text = "Kalibrácia nie je spustená.";
        else if (waitingForTemperature)
            _decisionV5.Text = "ČAKÁM NA: stabilnú referenčnú/internú teplotu.";
        else if (stabilizing > 0)
            _decisionV5.Text = $"ČAKÁM NA: stabilizáciu {stabilizing} FBG peak(ov). Stabilné peaky už môžu súčasne merať samples.";
        else if (measuring > 0)
            _decisionV5.Text = $"ČAKÁM NA: dokončenie meracích samples pre {measuring} FBG peak(ov).";
        else if (failed > 0)
            _decisionV5.Text = $"POZOR: {failed} peak(ov) skončilo warning/error stavom; kontrolujem failure policy.";
        else
            _decisionV5.Text = "Všetky FBG hotové ✓ · runner môže uložiť kalibračný bod a pokračovať.";

        _nextStepV5.Text = "Aktuálny runner: " + SanitizeRunnerStatusV5(_viewModel.StatusMessage);
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
            return "Odhad: teplotná stabilizácia je fyzikálne dynamická; presný čas sa nedá garantovať.";

        int remainingSamples = rows
            .Where(IsMeasuringV5)
            .Select(row => Math.Max(0, row.RequiredSamples - row.StableSamples))
            .DefaultIfEmpty(0)
            .Max();
        int stabilizing = rows.Count(row => !IsMeasuringV5(row) && row.State == CalibrationTargetState.Stabilizing);
        string sampleEstimate = remainingSamples > 0
            ? $"min. ~{FormatTimeV5(TimeSpan.FromSeconds(remainingSamples))} pre už merajúce peaky pri ~1 Hz"
            : "žiadne rozbehnuté meracie samples";
        return $"Odhad aktuálneho bodu: {sampleEstimate}; {stabilizing} peak(ov) má ešte neurčitý čas stabilizácie.";
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
                Text = $"{index + 1}. {point.Name}\n{point.TemperatureC:F1} °C\n{state}",
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

    private void HideProfileDurationColumnV5()
    {
        if (_productionTabs is null) return;
        TabItem? plateaus = _productionTabs.Items.OfType<TabItem>()
            .FirstOrDefault(item => HeaderText(item.Header) == "Kalibračné plata");
        if (plateaus is null) return;

        foreach (DataGrid grid in FindVisualChildrenV5<DataGrid>(plateaus))
        {
            DataGridColumn? column = grid.Columns.FirstOrDefault(item =>
            {
                string header = item.Header switch
                {
                    TextBlock text => text.Text,
                    _ => item.Header?.ToString() ?? string.Empty,
                };
                return header.Contains("čas plata", StringComparison.OrdinalIgnoreCase) ||
                       header.Contains("čas teplotného plata", StringComparison.OrdinalIgnoreCase);
            });
            if (column is null) continue;
            column.Visibility = Visibility.Collapsed;
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

    private static string SanitizeRunnerStatusV5(string status) =>
        (status ?? string.Empty)
            .Replace("minimum hold: bez minima · ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("minimum hold: bez minima", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("teplotnej/minimálnej brány", "teplotnej brány", StringComparison.OrdinalIgnoreCase)
            .Trim(' ', '·');

    private static int ParseCurrentPlateauIndexV5(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return -1;
        string[] parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            if (int.TryParse(part, out int value)) return Math.Max(0, value - 1);
        }
        return -1;
    }

    private static string FormatTimeV5(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
        return $"{(int)value.TotalMinutes:00}:{value.Seconds:00}";
    }

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
