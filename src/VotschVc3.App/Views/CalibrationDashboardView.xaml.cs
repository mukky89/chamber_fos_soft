using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VotschVc3.App.Charting;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.Views;

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
            TemperatureStabilityBand band = TemperatureStabilityBand.Around(target, vm.StabilityToleranceC);
            series.Add(new ChartSeries(
                $"Cieľ plata {target:F2} °C",
                Brushes.Goldenrod,
                new[] { new Point(minX, target), new Point(maxX, target) },
                dashed: true,
                strokeThickness: 1.5));
            series.Add(new ChartSeries(
                $"Dolná hranica {band.LowerC:F2} °C",
                Brushes.OrangeRed,
                new[] { new Point(minX, band.LowerC), new Point(maxX, band.LowerC) },
                dashed: true,
                strokeThickness: 1.5));
            series.Add(new ChartSeries(
                $"Horná hranica {band.UpperC:F2} °C",
                Brushes.OrangeRed,
                new[] { new Point(minX, band.UpperC), new Point(maxX, band.UpperC) },
                dashed: true,
                strokeThickness: 1.5));
        }

        ReferenceTraceChart.Series = series;
    }

    private void ClearReferenceTrace()
    {
        ReferencePortText.Text = "—";
        ReferenceCurrentTemperatureText.Text = "—";
        ReferenceTraceChart.Series = Array.Empty<ChartSeries>();
    }
}
