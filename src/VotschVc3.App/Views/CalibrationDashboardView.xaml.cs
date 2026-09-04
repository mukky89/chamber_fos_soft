using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VotschVc3.App.Charting;
using VotschVc3.App.ViewModels;

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

        if (double.TryParse(vm.Target.Replace(" °C", string.Empty), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture, out double target) ||
            double.TryParse(vm.Target.Replace(" °C", string.Empty), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out target))
        {
            double minX = measured[0].X;
            double maxX = measured[^1].X;
            if (maxX <= minX) maxX = minX + 0.01;
            series.Add(new ChartSeries(
                "Cieľ plata",
                Brushes.Goldenrod,
                new[] { new Point(minX, target), new Point(maxX, target) },
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
