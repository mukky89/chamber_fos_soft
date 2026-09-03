using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VotschVc3.App.Charting;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

/// <summary>Live WIKA CTH7000 trace opened by clicking the Referencia metric on a device card.</summary>
public sealed class ReferenceTemperatureChartWindow : Window
{
    private readonly Guid _chamberId;
    private readonly ChartView _chart;
    private readonly TextBlock _status;

    public ReferenceTemperatureChartWindow(Guid chamberId, string chamberName)
    {
        _chamberId = chamberId;
        Title = $"Referenčná teplota · {chamberName}";
        Width = 1050;
        Height = 650;
        MinWidth = 720;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Application.Current.TryFindResource("BackgroundBrush") as Brush ?? Brushes.White;

        _status = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Application.Current.TryFindResource("MutedBrush") as Brush ?? Brushes.Gray,
        };

        _chart = new ChartView
        {
            ChartTitle = $"WIKA CTH7000 · {chamberName}",
            Unit = " °C",
            EmptyText = "Čakám na prvú automatickú vzorku z WIKA CTH7000…",
            MinHeight = 360,
        };

        var close = new Button
        {
            Content = "Zavrieť",
            Padding = new Thickness(14, 7, 14, 7),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        close.Click += (_, _) => Close();

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text = "Priebeh referenčnej teploty",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        stack.Children.Add(_status);
        stack.Children.Add(_chart);
        stack.Children.Add(close);
        Content = stack;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CalibrationReferenceStatusStore.Instance.Changed += OnReferenceChanged;
        RefreshChart();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        CalibrationReferenceStatusStore.Instance.Changed -= OnReferenceChanged;
    }

    private void OnReferenceChanged(object? sender, CalibrationReferenceChangedEventArgs e)
    {
        if (e.ChamberId != _chamberId) return;
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(RefreshChart));
            return;
        }
        RefreshChart();
    }

    private void RefreshChart()
    {
        IReadOnlyList<CalibrationReferenceTracePoint> trace = CalibrationReferenceTraceStore.Instance.GetTrace(_chamberId);
        CalibrationReferenceSnapshot snapshot = CalibrationReferenceStatusStore.Instance.GetSnapshot(_chamberId);

        if (trace.Count == 0)
        {
            _chart.Series = Array.Empty<ChartSeries>();
        }
        else
        {
            DateTimeOffset origin = trace[0].Timestamp;
            Point[] points = trace
                .Select(p => new Point((p.Timestamp - origin).TotalMinutes, p.TemperatureC))
                .ToArray();
            Brush stroke = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
            _chart.Series = new[]
            {
                new ChartSeries(
                    $"WIKA {snapshot.PortName} · {snapshot.Channel}",
                    stroke,
                    points,
                    strokeThickness: 2.4),
            };
        }

        string temperature = snapshot.TemperatureC is { } value ? $"{value:F3} °C" : "—";
        string updated = snapshot.UpdatedAt is { } time ? time.ToLocalTime().ToString("HH:mm:ss") : "—";
        _status.Text = $"Aktuálne: {temperature} · {snapshot.PortName} / kanál {snapshot.Channel} · posledná vzorka {updated} · vzoriek {trace.Count}";
    }
}
