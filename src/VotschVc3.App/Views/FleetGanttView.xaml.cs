using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

/// <summary>
/// Dashboard timeline (Gantt) of every device: one row per chamber. A running
/// profile draws a solid bar from its actual start to the estimated end; a
/// delayed start draws a translucent planned bar at its scheduled time; a
/// chamber that is only switched on manually (no profile) draws an open-ended
/// bar that fades out at the right edge with an "∞" — it runs until someone
/// switches it off. Redraws on resize, on relevant view-model changes and on
/// a 30 s timer so the "teraz" marker keeps moving.
/// </summary>
public partial class FleetGanttView : UserControl
{
    private const double LabelWidth = 170;
    private const double RowHeight = 30;
    private const double AxisHeight = 34;
    private const double BarHeight = 18;
    private const double PadRight = 14;

    /// <summary>Slovak day-of-week abbreviations, indexed by <see cref="DayOfWeek"/> (Sunday = 0).</summary>
    private static readonly string[] SkDayNames = { "Ne", "Po", "Ut", "St", "Št", "Pi", "So" };

    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly List<ChamberViewModel> _hooked = new();

    public FleetGanttView()
    {
        InitializeComponent();
        Loaded += (_, _) => { _refresh.Start(); Redraw(); };
        Unloaded += (_, _) => _refresh.Stop();
        _refresh.Tick += (_, _) => Redraw();
        SizeChanged += (_, _) => Redraw();
    }

    public static readonly DependencyProperty ChambersProperty = DependencyProperty.Register(
        nameof(Chambers), typeof(IEnumerable<ChamberViewModel>), typeof(FleetGanttView),
        new PropertyMetadata(null, OnChambersChanged));

    /// <summary>The devices to show, one row each (dashboard order).</summary>
    public IEnumerable<ChamberViewModel>? Chambers
    {
        get => (IEnumerable<ChamberViewModel>?)GetValue(ChambersProperty);
        set => SetValue(ChambersProperty, value);
    }

    private static void OnChambersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (FleetGanttView)d;
        if (e.OldValue is INotifyCollectionChanged oldCol)
        {
            oldCol.CollectionChanged -= view.OnCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCol)
        {
            newCol.CollectionChanged += view.OnCollectionChanged;
        }

        view.RehookItems();
        view.Redraw();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RehookItems();
        Redraw();
    }

    private void RehookItems()
    {
        foreach (ChamberViewModel vm in _hooked)
        {
            vm.PropertyChanged -= OnItemChanged;
        }

        _hooked.Clear();
        foreach (ChamberViewModel vm in Chambers ?? Enumerable.Empty<ChamberViewModel>())
        {
            vm.PropertyChanged += OnItemChanged;
            _hooked.Add(vm);
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChamberViewModel.IsActive)
            or nameof(ChamberViewModel.IsProfileRunning)
            or nameof(ChamberViewModel.ProfileRunStart)
            or nameof(ChamberViewModel.ProfileRunEnd)
            or nameof(ChamberViewModel.ActiveSince)
            or nameof(ChamberViewModel.ProfileName)
            or nameof(ChamberViewModel.Name))
        {
            Redraw();
        }
    }

    private Brush Token(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private Brush AccentBrush => Token("AccentBrush", Brushes.RoyalBlue);
    private Brush OkBrush => Token("OkBrush", Brushes.MediumSeaGreen);
    private Brush MutedBrush => Token("MutedBrush", Brushes.Gray);
    private Brush GridBrush => Token("BorderBrush", Brushes.DimGray);
    private Brush TextBrush => Token("TextBrush", Brushes.White);
    private Brush RowBrush => Token("SurfaceAltBrush", Brushes.DarkSlateGray);

    private void Redraw()
    {
        PlotCanvas.Children.Clear();

        double width = ActualWidth;
        if (width <= LabelWidth + 80)
        {
            return;
        }

        List<ChamberViewModel> items = Chambers?.ToList() ?? new List<ChamberViewModel>();
        double rowsHeight = Math.Max(1, items.Count) * RowHeight;
        PlotCanvas.Height = rowsHeight + AxisHeight;
        PlotCanvas.Width = width;

        if (items.Count == 0)
        {
            AddText("Žiadne zariadenia.", 0, rowsHeight / 2 - 8, MutedBrush, 12);
            return;
        }

        DateTime now = DateTime.Now;
        (DateTime winStart, DateTime winEnd) = ComputeWindow(items, now);
        double plotX = LabelWidth;
        double plotW = width - LabelWidth - PadRight;
        double totalSeconds = Math.Max(1, (winEnd - winStart).TotalSeconds);

        double X(DateTime t) => plotX + Math.Clamp((t - winStart).TotalSeconds / totalSeconds, 0d, 1d) * plotW;

        DrawAxis(winStart, winEnd, plotX, plotW, rowsHeight, X);

        for (int i = 0; i < items.Count; i++)
        {
            DrawRow(items[i], i * RowHeight, plotX, plotW, now, winEnd, X);
        }

        // "Now" marker across all rows, dashed like the chart views.
        double nowX = X(now);
        var marker = new Line
        {
            X1 = nowX, X2 = nowX, Y1 = 0, Y2 = rowsHeight,
            Stroke = TextBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 },
            Opacity = 0.65,
        };
        PlotCanvas.Children.Add(marker);
        AddText("teraz", Math.Min(nowX + 4, width - 40), rowsHeight + 4, TextBrush, 10);
    }

    /// <summary>Time window: at least (now − 1 h, now + 2 h), stretched to cover every bar.</summary>
    private static (DateTime Start, DateTime End) ComputeWindow(List<ChamberViewModel> items, DateTime now)
    {
        DateTime start = now.AddHours(-1);
        DateTime end = now.AddHours(2);

        foreach (ChamberViewModel vm in items)
        {
            if (BarStart(vm, now) is { } s && s < start)
            {
                start = s;
            }

            if (vm.IsProfileRunning && vm.ProfileRunEnd is { } e && e > end)
            {
                end = e;
            }

            if (vm.IsProfileRunning && vm.ProfileRunStart is null && vm.UseDelayedStart)
            {
                // Waiting for a delayed start: cover the whole planned bar.
                DateTime plannedEnd = vm.ScheduledStart + vm.PlannedProfileDuration;
                if (plannedEnd > end)
                {
                    end = plannedEnd;
                }
            }
        }

        return (start.AddMinutes(-10), end.AddMinutes(10));
    }

    /// <summary>Where the device's bar begins: profile start, scheduled start or first observed activity.</summary>
    private static DateTime? BarStart(ChamberViewModel vm, DateTime now)
    {
        if (vm.IsProfileRunning)
        {
            return vm.ProfileRunStart
                ?? (vm.UseDelayedStart && vm.ScheduledStart > now ? vm.ScheduledStart : now);
        }

        return vm.IsActive ? vm.ActiveSince ?? now : null;
    }

    private void DrawAxis(DateTime winStart, DateTime winEnd, double plotX, double plotW, double rowsHeight,
        Func<DateTime, double> x)
    {
        TimeSpan span = winEnd - winStart;

        // Denser hour ticks than before, so more hours are labelled on the X axis.
        int stepMinutes = span.TotalHours switch
        {
            <= 2 => 15,
            <= 4 => 30,
            <= 10 => 60,
            <= 20 => 120,
            <= 48 => 180,
            <= 96 => 360,
            _ => 720,
        };

        bool multiDay = span.TotalHours > 20;

        // Hour ticks + "HH:mm" labels; midnight lines are drawn stronger as day breaks.
        DateTime tick = winStart.Date + TimeSpan.FromMinutes(
            Math.Ceiling((winStart - winStart.Date).TotalMinutes / stepMinutes) * stepMinutes);

        for (; tick <= winEnd; tick = tick.AddMinutes(stepMinutes))
        {
            double tx = x(tick);
            if (tx < plotX - 0.5 || tx > plotX + plotW + 0.5)
            {
                continue;
            }

            bool midnight = tick is { Hour: 0, Minute: 0 };
            PlotCanvas.Children.Add(new Line
            {
                X1 = tx, X2 = tx, Y1 = 0, Y2 = rowsHeight,
                Stroke = GridBrush, StrokeThickness = midnight ? 1.4 : 1, Opacity = midnight ? 0.85 : 0.5,
            });
            AddText($"{tick:HH:mm}", tx - 14, rowsHeight + 3, MutedBrush, 10);
        }

        // Second label row: the day name + date for each day the window spans, so a
        // multi-day timeline reads clearly ("Ne 20.07.", "Po 21.07." …).
        if (multiDay)
        {
            for (DateTime day = winStart.Date; day <= winEnd; day = day.AddDays(1))
            {
                DateTime anchor = day < winStart ? winStart : day;
                double dx = x(anchor);
                if (dx > plotX + plotW - 4)
                {
                    continue;
                }

                AddText($"{SkDayNames[(int)day.DayOfWeek]} {day:dd.MM.}", dx + 3, rowsHeight + 16, TextBrush, 10.5);
            }
        }
    }

    private void DrawRow(ChamberViewModel vm, double y, double plotX, double plotW, DateTime now,
        DateTime winEnd, Func<DateTime, double> x)
    {
        double barY = y + (RowHeight - BarHeight) / 2;

        // Subtle row track so empty rows still read as rows.
        var track = new Rectangle
        {
            Width = plotW, Height = BarHeight, RadiusX = 5, RadiusY = 5,
            Fill = RowBrush, Opacity = 0.35,
        };
        Canvas.SetLeft(track, plotX);
        Canvas.SetTop(track, barY);
        PlotCanvas.Children.Add(track);

        // Device name on the left.
        var name = new TextBlock
        {
            Text = vm.Name,
            Foreground = TextBrush,
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI Semibold"),
            Width = LabelWidth - 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = vm.Name,
        };
        Canvas.SetLeft(name, 0);
        Canvas.SetTop(name, y + (RowHeight - 17) / 2);
        PlotCanvas.Children.Add(name);

        if (vm.IsProfileRunning)
        {
            bool waiting = vm.ProfileRunStart is null;
            DateTime start = BarStart(vm, now) ?? now;
            DateTime end = waiting
                ? start + vm.PlannedProfileDuration
                : vm.ProfileRunEnd ?? winEnd;
            string tooltip = waiting
                ? $"{vm.ProfileName} · naplánované {start:dd.MM HH:mm} → ~{end:dd.MM HH:mm}"
                : $"{vm.ProfileName} · {start:dd.MM HH:mm} → ~{end:dd.MM HH:mm}";
            DrawBar(barY, x(start), x(end), AccentBrush, vm.ProfileName, tooltip, dimmed: waiting);
        }
        else if (vm.IsActive)
        {
            // Manual run: no scheduled end — the bar runs to the edge and fades out ("∞").
            DateTime start = BarStart(vm, now) ?? now;
            double x1 = x(start);
            double x2 = plotX + plotW;
            Brush fade = MakeFadeBrush(OkBrush);
            DrawBar(barY, x1, x2, fade, "manuál", $"{vm.ActivityLabel} · od {start:dd.MM HH:mm} · beží, kým sa nevypne", dimmed: false);

            var infinity = new TextBlock
            {
                Text = "∞", Foreground = OkBrush, FontSize = 13, FontWeight = FontWeights.Bold,
                ToolTip = "Beží bez profilu – do vypnutia.",
            };
            Canvas.SetLeft(infinity, x2 - 14);
            Canvas.SetTop(infinity, barY - 2);
            PlotCanvas.Children.Add(infinity);
        }
    }

    private void DrawBar(double barY, double x1, double x2, Brush fill, string text, string tooltip, bool dimmed)
    {
        double w = Math.Max(3, x2 - x1);
        var bar = new Rectangle
        {
            Width = w, Height = BarHeight, RadiusX = 5, RadiusY = 5,
            Fill = fill, Opacity = dimmed ? 0.45 : 0.95, ToolTip = tooltip,
        };
        Canvas.SetLeft(bar, x1);
        Canvas.SetTop(bar, barY);
        PlotCanvas.Children.Add(bar);

        if (w > 70)
        {
            var label = new TextBlock
            {
                Text = text, Foreground = Brushes.White, FontSize = 11,
                Width = w - 14, TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(label, x1 + 7);
            Canvas.SetTop(label, barY + 1.5);
            PlotCanvas.Children.Add(label);
        }
    }

    /// <summary>Solid colour that fades to transparent on the right — the "runs forever" look.</summary>
    private static Brush MakeFadeBrush(Brush source)
    {
        Color color = source is SolidColorBrush solid ? solid.Color : Colors.MediumSeaGreen;
        var brush = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(color, 0),
                new GradientStop(color, 0.72),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
            },
            new Point(0, 0), new Point(1, 0));
        brush.Freeze();
        return brush;
    }

    private void AddText(string text, double xPos, double yPos, Brush brush, double size)
    {
        var block = new TextBlock { Text = text, Foreground = brush, FontSize = size };
        Canvas.SetLeft(block, xPos);
        Canvas.SetTop(block, yPos);
        PlotCanvas.Children.Add(block);
    }
}
