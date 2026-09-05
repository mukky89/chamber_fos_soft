using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using VotschVc3.App.Charting;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Charting;

namespace VotschVc3.App.Views;

public sealed class FbgStabilitySeriesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not IReadOnlyList<FbgStabilitySample> samples || samples.Count == 0)
            return Array.Empty<ChartSeries>();
        return new[]
        {
            new ChartSeries("FBG peak", Brushes.DeepSkyBlue, samples
                .Select(sample => new Point(sample.Minutes, sample.WavelengthNm))
                .ToArray(), strokeThickness: 2.1),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class ChamberTemperatureSeriesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not IReadOnlyList<DashboardTemperatureSample> samples || samples.Count == 0)
            return Array.Empty<ChartSeries>();
        DateTimeOffset origin = samples[0].Timestamp;
        IReadOnlyList<DashboardTemperatureSample> visible = TimeSeriesEnvelopeReducer.Reduce(
            samples, sample => sample.TemperatureC, 240);
        return new[]
        {
            new ChartSeries("Komora", Brushes.DodgerBlue, visible
                .Select(sample => new Point((sample.Timestamp - origin).TotalMinutes, sample.TemperatureC))
                .ToArray(), strokeThickness: 1.8),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class WikaStabilityScoreSeriesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not IReadOnlyList<DashboardStabilityScoreSample> samples || samples.Count == 0)
            return Array.Empty<ChartSeries>();
        DateTimeOffset origin = samples[0].Timestamp;
        IReadOnlyList<DashboardStabilityScoreSample> visible = TimeSeriesEnvelopeReducer.Reduce(
            samples, sample => sample.ScoreSeconds, 240);
        Point[] score = visible
            .Select(sample => new Point((sample.Timestamp - origin).TotalMinutes, sample.ScoreSeconds))
            .ToArray();
        double endMinutes = Math.Max(score[^1].X, 1d / 60d);
        double required = samples[^1].RequiredSeconds;
        return new[]
        {
            new ChartSeries("Stabilné skóre", Brushes.DodgerBlue, score, strokeThickness: 2),
            new ChartSeries($"Cieľ {required:0} s", Brushes.MediumSeaGreen,
                new[] { new Point(0, required), new Point(endMinutes, required) }, dashed: true, strokeThickness: 1.4),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        Binding.DoNothing;
}

public partial class CalibrationDashboardView : UserControl
{
    private Popup? _helpPopup;

    public CalibrationDashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += (_, _) => RequestReferenceTraceRefresh();
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string text || string.IsNullOrWhiteSpace(text)) return;

        if (_helpPopup?.IsOpen == true && ReferenceEquals(_helpPopup.PlacementTarget, button))
        {
            _helpPopup.IsOpen = false;
            return;
        }

        if (_helpPopup is not null) _helpPopup.IsOpen = false;
        var content = new Border
        {
            MaxWidth = 560,
            Padding = new Thickness(13, 10, 13, 10),
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromRgb(42, 43, 76)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(66, 76, 116)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                Foreground = Brushes.White,
            },
        };
        var popup = new Popup
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom,
            HorizontalOffset = -12,
            StaysOpen = true,
            AllowsTransparency = true,
            Child = content,
        };
        content.MouseLeave += (_, _) => popup.IsOpen = false;
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_helpPopup, popup)) _helpPopup = null;
        };
        _helpPopup = popup;
        popup.IsOpen = true;
        e.Handled = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CalibrationReferenceTraceStore.Instance.Changed += OnReferenceTraceChanged;
        CalibrationReferenceStatusStore.Instance.Changed += OnReferenceStatusChanged;
        RequestReferenceTraceRefresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_helpPopup is not null) _helpPopup.IsOpen = false;
        CalibrationReferenceTraceStore.Instance.Changed -= OnReferenceTraceChanged;
        CalibrationReferenceStatusStore.Instance.Changed -= OnReferenceStatusChanged;
    }

    private void OnReferenceTraceChanged(object? sender, EventArgs e) =>
        RequestReferenceTraceRefresh();

    private void OnReferenceStatusChanged(object? sender, CalibrationReferenceChangedEventArgs e)
    {
        // The reference status bus may raise Changed from the serial/background sampling thread.
        // Never touch DataContext (a WPF DispatcherObject graph) before switching to the UI thread.
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => OnReferenceStatusChanged(sender, e)));
            return;
        }

        if (DataContext is CalibrationDashboardViewModel vm)
        {
            Guid? chamberId = vm.ReferenceChamberId ?? CalibrationReferenceTraceStore.Instance.GetSingleActiveChamberId();
            if (chamberId == e.ChamberId)
                RefreshReferenceTrace();
        }
    }

    private void RequestReferenceTraceRefresh()
    {
        if (Dispatcher.CheckAccess())
        {
            RefreshReferenceTrace();
            return;
        }

        _ = Dispatcher.BeginInvoke(new Action(RefreshReferenceTrace));
    }

    private void RefreshReferenceTrace()
    {
        if (!Dispatcher.CheckAccess())
        {
            RequestReferenceTraceRefresh();
            return;
        }

        if (DataContext is not CalibrationDashboardViewModel vm)
        {
            ClearReferenceTrace();
            return;
        }

        Guid? resolvedChamberId = vm.ReferenceChamberId ?? CalibrationReferenceTraceStore.Instance.GetSingleActiveChamberId();
        if (resolvedChamberId is not { } chamberId)
        {
            ClearReferenceTrace();
            return;
        }

        CalibrationReferenceSnapshot snapshot = CalibrationReferenceStatusStore.Instance.GetSnapshot(chamberId);
        ReferencePortText.Text = snapshot.IsAssigned
            ? $"{snapshot.PortName} · kanál {snapshot.Channel}"
            : "—";
        ReferenceCurrentTemperatureText.Text = snapshot.IsConnected && snapshot.TemperatureC is { } temperature
            ? $"{temperature:F3} °C"
            : "—";

        IReadOnlyList<CalibrationReferenceTracePoint> fullTrace = CalibrationReferenceTraceStore.Instance.GetTrace(chamberId);
        IReadOnlyList<CalibrationReferenceTracePoint> trace = vm.CurrentPlateauTraceStart is { } plateauStart
            ? fullTrace.Where(point => point.Timestamp >= plateauStart).ToArray()
            : fullTrace;
        if (trace.Count == 0)
        {
            ReferenceTraceChart.Series = Array.Empty<ChartSeries>();
            StabilitySamplesChart.Series = Array.Empty<ChartSeries>();
            StabilitySamplesList.ItemsSource = null;
            return;
        }

        DateTimeOffset origin = trace[0].Timestamp;
        var measured = trace
            .Select(point => new Point((point.Timestamp - origin).TotalMinutes, point.TemperatureC))
            .ToArray();

        var series = new List<ChartSeries>
        {
            new("WIKA CTH7000", Brushes.DeepSkyBlue, measured, strokeThickness: 2.2),
        };

        if (vm.TargetTemperatureC is { } target)
        {
            double minX = measured[0].X;
            double maxX = measured[^1].X;
            if (maxX <= minX) maxX = minX + 0.01;
            series.Add(new ChartSeries(
                $"Cieľ plata {target:F2} °C",
                Brushes.Goldenrod,
                new[] { new Point(minX, target), new Point(maxX, target) },
                dashed: true,
                strokeThickness: 1.5));

            int stableSeconds = vm.TemperatureStableScoreSeconds;
            if (stableSeconds > 0)
            {
                DateTimeOffset windowStart = trace[^1].Timestamp - TimeSpan.FromSeconds(stableSeconds);
                double[] stableValues = trace.Where(point => point.Timestamp >= windowStart)
                    .Select(point => point.TemperatureC)
                    .ToArray();
                if (stableValues.Length > 0)
                {
                    double stableMin = stableValues.Min();
                    double stableMax = stableValues.Max();
                    series.Add(new ChartSeries(
                        $"Ustálené minimum {stableMin:F3} °C",
                        Brushes.MediumSeaGreen,
                        new[] { new Point(minX, stableMin), new Point(maxX, stableMin) },
                        dashed: true,
                        strokeThickness: 1.5));
                    series.Add(new ChartSeries(
                        $"Ustálené maximum {stableMax:F3} °C",
                        Brushes.MediumSeaGreen,
                        new[] { new Point(minX, stableMax), new Point(maxX, stableMax) },
                        dashed: true,
                        strokeThickness: 1.5));
                }
            }
        }

        ReferenceTraceChart.Series = series;
        CompactReferenceChart.Series = new[]
        {
            new ChartSeries("WIKA CTH7000", Brushes.DeepSkyBlue,
                TimeSeriesEnvelopeReducer.Reduce(measured, point => point.Y, 240), strokeThickness: 1.8),
        };
        RefreshStabilitySamples(trace, vm.TargetTemperatureC, vm.StabilityToleranceC);
    }

    private void RefreshStabilitySamples(
        IReadOnlyList<CalibrationReferenceTracePoint> trace,
        double? targetTemperatureC,
        double toleranceC)
    {
        CalibrationReferenceTracePoint[] recent = trace.TakeLast(50).ToArray();
        if (recent.Length == 0)
        {
            StabilitySamplesChart.Series = Array.Empty<ChartSeries>();
            StabilitySamplesList.ItemsSource = null;
            return;
        }

        DateTimeOffset origin = recent[0].Timestamp;
        var sampleSeries = new List<ChartSeries>
        {
            new("WIKA vzorky", Brushes.DeepSkyBlue, recent
                .Select(sample => new Point((sample.Timestamp - origin).TotalMinutes, sample.TemperatureC))
                .ToArray(), strokeThickness: 2.2),
        };

        if (targetTemperatureC is { } target)
        {
            double maxX = Math.Max(0.01, (recent[^1].Timestamp - origin).TotalMinutes);
            sampleSeries.Add(new ChartSeries(
                $"Cieľ {target:F2} °C",
                Brushes.Goldenrod,
                new[] { new Point(0, target), new Point(maxX, target) },
                dashed: true,
                strokeThickness: 1.4));
        }

        StabilitySamplesChart.Series = sampleSeries;
        StabilitySamplesList.ItemsSource = recent.AsEnumerable()
            .Reverse()
            .Select(sample =>
            {
                double? delta = targetTemperatureC is { } target ? sample.TemperatureC - target : null;
                return new StabilitySampleRow(
                    sample.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                    $"{sample.TemperatureC:F3} °C",
                    delta is { } value ? $"{value:+0.000;-0.000;0.000} °C" : "—",
                    delta is { } difference && Math.Abs(difference) <= toleranceC ? Brushes.MediumSeaGreen : Brushes.OrangeRed);
            })
            .ToArray();
    }

    private void ClearReferenceTrace()
    {
        ReferencePortText.Text = "—";
        ReferenceCurrentTemperatureText.Text = "—";
        ReferenceTraceChart.Series = Array.Empty<ChartSeries>();
        CompactReferenceChart.Series = Array.Empty<ChartSeries>();
        StabilitySamplesChart.Series = Array.Empty<ChartSeries>();
        StabilitySamplesList.ItemsSource = null;
    }

    private sealed record StabilitySampleRow(string Time, string Temperature, string Delta, Brush DeltaBrush);
}
