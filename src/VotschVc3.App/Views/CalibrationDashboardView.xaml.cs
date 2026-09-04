using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using VotschVc3.App.Charting;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;

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

public partial class CalibrationDashboardView : UserControl
{
    public CalibrationDashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += (_, _) => RequestReferenceTraceRefresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CalibrationReferenceTraceStore.Instance.Changed += OnReferenceTraceChanged;
        CalibrationReferenceStatusStore.Instance.Changed += OnReferenceStatusChanged;
        RequestReferenceTraceRefresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
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

        IReadOnlyList<CalibrationReferenceTracePoint> trace = CalibrationReferenceTraceStore.Instance.GetTrace(chamberId);
        if (trace.Count == 0)
        {
            ReferenceTraceChart.Series = Array.Empty<ChartSeries>();
            StabilitySamplesChart.Series = Array.Empty<ChartSeries>();
            StabilitySamplesList.ItemsSource = null;
            return;
        }

        DateTimeOffset origin = CalibrationReferenceTraceStore.Instance.GetRunStart(chamberId) ?? trace[0].Timestamp;
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
        StabilitySamplesChart.Series = Array.Empty<ChartSeries>();
        StabilitySamplesList.ItemsSource = null;
    }

    private sealed record StabilitySampleRow(string Time, string Temperature, string Delta, Brush DeltaBrush);
}
