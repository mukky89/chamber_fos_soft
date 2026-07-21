using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VotschVc3.App.Charting;

namespace VotschVc3.App.Views;

/// <summary>
/// Minimal, dependency-free line chart. Renders one or more
/// <see cref="ChartSeries"/> onto a canvas with auto-scaled axes, gridlines and
/// a small legend. Redraws on resize and whenever the series collection is
/// replaced.
/// </summary>
public partial class ChartView : UserControl
{
    private const double PadLeft = 46;
    private const double PadRight = 12;
    private const double PadTop = 10;
    private const double PadBottom = 22;

    // Plot transform captured on the last Redraw, so mouse handlers can map a
    // cursor position back to a data point (hover read-out).
    private bool _hasPlot;
    private double _minX, _maxX, _minY, _maxY, _plotW, _plotH;
    private ChartSeries? _hoverSeries;
    private readonly List<UIElement> _overlay = new();

    public ChartView()
    {
        InitializeComponent();
        PlotCanvas.SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();
        PlotCanvas.MouseMove += OnPlotMouseMove;
        PlotCanvas.MouseLeave += (_, _) => ClearOverlay();
    }

    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IEnumerable<ChartSeries>), typeof(ChartView),
        new PropertyMetadata(null, OnVisualChanged));

    /// <summary>The series to plot.</summary>
    public IEnumerable<ChartSeries>? Series
    {
        get => (IEnumerable<ChartSeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public static readonly DependencyProperty YMinProperty = DependencyProperty.Register(
        nameof(YMin), typeof(double), typeof(ChartView),
        new PropertyMetadata(double.NaN, OnVisualChanged));

    /// <summary>Fixed lower Y bound, or <see cref="double.NaN"/> for auto.</summary>
    public double YMin
    {
        get => (double)GetValue(YMinProperty);
        set => SetValue(YMinProperty, value);
    }

    public static readonly DependencyProperty YMaxProperty = DependencyProperty.Register(
        nameof(YMax), typeof(double), typeof(ChartView),
        new PropertyMetadata(double.NaN, OnVisualChanged));

    /// <summary>Fixed upper Y bound, or <see cref="double.NaN"/> for auto.</summary>
    public double YMax
    {
        get => (double)GetValue(YMaxProperty);
        set => SetValue(YMaxProperty, value);
    }

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(ChartView),
        new PropertyMetadata(string.Empty, OnVisualChanged));

    /// <summary>Unit suffix shown on the Y axis labels (e.g. "°C", "%").</summary>
    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText), typeof(string), typeof(ChartView),
        new PropertyMetadata("Žiadne dáta", OnVisualChanged));

    /// <summary>Placeholder text shown when there is nothing to plot.</summary>
    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ChartView)d).Redraw();

    private Brush MutedBrush => TryFindResource("MutedBrush") as Brush ?? Brushes.Gray;

    private Brush GridBrush => TryFindResource("BorderBrush") as Brush ?? Brushes.DimGray;

    private void Redraw()
    {
        PlotCanvas.Children.Clear();

        double width = PlotCanvas.ActualWidth;
        double height = PlotCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _hasPlot = false;
        _overlay.Clear();

        List<ChartSeries> series = Series?.Where(s => s.Points.Count > 0).ToList() ?? new List<ChartSeries>();
        if (series.Count == 0)
        {
            AddText(EmptyText, width / 2 - 40, height / 2 - 10, MutedBrush, 12);
            return;
        }

        double minX = series.Min(s => s.Points.Min(p => p.X));
        double maxX = series.Max(s => s.Points.Max(p => p.X));
        double minY = double.IsNaN(YMin) ? series.Min(s => s.Points.Min(p => p.Y)) : YMin;
        double maxY = double.IsNaN(YMax) ? series.Max(s => s.Points.Max(p => p.Y)) : YMax;

        if (maxX <= minX) maxX = minX + 1;
        if (maxY <= minY) { maxY = minY + 1; minY -= 1; }
        else if (double.IsNaN(YMin) && double.IsNaN(YMax))
        {
            double pad = (maxY - minY) * 0.08;
            minY -= pad;
            maxY += pad;
        }

        double plotW = width - PadLeft - PadRight;
        double plotH = height - PadTop - PadBottom;
        if (plotW <= 0 || plotH <= 0)
        {
            return;
        }

        double ToPx(double x) => PadLeft + (x - minX) / (maxX - minX) * plotW;
        double ToPy(double y) => PadTop + (1 - (y - minY) / (maxY - minY)) * plotH;

        // Remember the transform so the hover read-out can map cursor -> data.
        _minX = minX; _maxX = maxX; _minY = minY; _maxY = maxY; _plotW = plotW; _plotH = plotH;
        _hoverSeries = series.FirstOrDefault(s => !s.Dashed) ?? series[0];
        _hasPlot = true;

        // Horizontal gridlines + Y labels.
        for (int i = 0; i <= 4; i++)
        {
            double frac = i / 4.0;
            double yVal = minY + (maxY - minY) * (1 - frac);
            double py = PadTop + plotH * frac;
            AddLine(PadLeft, py, PadLeft + plotW, py, GridBrush, 1, dashed: i is not 0 and not 4);
            AddText($"{yVal:0.#}{Unit}", 2, py - 8, MutedBrush, 10, PadLeft - 6, TextAlignment.Right);
        }

        // X axis labels (min / max) with an hours/days read-out for longer spans.
        AddText(FormatMinutes(minX), PadLeft, PadTop + plotH + 4, MutedBrush, 10);
        AddText(FormatMinutes(maxX), PadLeft + plotW - 150, PadTop + plotH + 4, MutedBrush, 10, 150, TextAlignment.Right);

        // Series lines.
        foreach (ChartSeries s in series)
        {
            var poly = new Polyline
            {
                Stroke = s.Stroke,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                Points = new PointCollection(s.Points.Select(p => new Point(ToPx(p.X), ToPy(p.Y)))),
            };
            if (s.Dashed)
            {
                poly.StrokeDashArray = new DoubleCollection { 4, 3 };
            }

            PlotCanvas.Children.Add(poly);
        }

        // Legend (top-right).
        double legendY = PadTop + 2;
        foreach (ChartSeries s in series)
        {
            var swatch = new Rectangle { Width = 14, Height = 3, Fill = s.Stroke };
            Canvas.SetRight(swatch, PadRight + 56);
            Canvas.SetTop(swatch, legendY + 6);
            PlotCanvas.Children.Add(swatch);
            AddText(s.Name, 0, legendY, MutedBrush, 10, 52, TextAlignment.Right, right: PadRight);
            legendY += 15;
        }
    }

    private void AddLine(double x1, double y1, double x2, double y2, Brush brush, double thickness, bool dashed)
    {
        var line = new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = brush,
            StrokeThickness = thickness,
            Opacity = dashed ? 0.4 : 0.7,
        };
        if (dashed)
        {
            line.StrokeDashArray = new DoubleCollection { 3, 3 };
        }

        PlotCanvas.Children.Add(line);
    }

    private void AddText(
        string text, double left, double top, Brush brush, double size,
        double? width = null, TextAlignment align = TextAlignment.Left, double? right = null)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontSize = size,
            TextAlignment = align,
        };
        if (width is { } w)
        {
            tb.Width = w;
        }

        if (right is { } r)
        {
            Canvas.SetRight(tb, r);
        }
        else
        {
            Canvas.SetLeft(tb, left);
        }

        Canvas.SetTop(tb, top);
        PlotCanvas.Children.Add(tb);
    }

    // ===== Hover read-out: crosshair + value chip following the cursor =====

    private void OnPlotMouseMove(object sender, MouseEventArgs e)
    {
        if (!_hasPlot || _hoverSeries is null || _hoverSeries.Points.Count == 0 || _plotW <= 0)
        {
            ClearOverlay();
            return;
        }

        double left = PadLeft;
        double mx = Math.Clamp(e.GetPosition(PlotCanvas).X, left, left + _plotW);
        double dataX = _minX + (mx - left) / _plotW * (_maxX - _minX);
        if (InterpolateY(_hoverSeries.Points, dataX) is not { } yv)
        {
            ClearOverlay();
            return;
        }

        double px = left + (dataX - _minX) / (_maxX - _minX) * _plotW;
        double py = PadTop + (1 - (yv - _minY) / (_maxY - _minY)) * _plotH;

        ClearOverlay();
        Brush accent = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;

        AddOverlay(new Line
        {
            X1 = px, Y1 = PadTop, X2 = px, Y2 = PadTop + _plotH,
            Stroke = accent, StrokeThickness = 1, Opacity = 0.6,
            StrokeDashArray = new DoubleCollection { 3, 3 },
        });

        var dot = new Ellipse { Width = 8, Height = 8, Fill = accent, Stroke = Brushes.White, StrokeThickness = 1 };
        Canvas.SetLeft(dot, px - 4);
        Canvas.SetTop(dot, py - 4);
        AddOverlay(dot);

        var chip = new Border
        {
            Background = accent,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock
            {
                Text = $"{yv:0.0}{Unit}  ·  {FormatMinutes(dataX)}",
                Foreground = Brushes.White,
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI Semibold"),
            },
        };
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double cx = px + 8;
        if (cx + chip.DesiredSize.Width > left + _plotW)
        {
            cx = px - chip.DesiredSize.Width - 8;
        }

        double cy = py - 26 < PadTop ? py + 10 : py - 26;
        Canvas.SetLeft(chip, Math.Max(left, cx));
        Canvas.SetTop(chip, cy);
        AddOverlay(chip);
    }

    /// <summary>
    /// Formats an X-axis value (in minutes) as minutes plus a human-readable
    /// hours / days breakdown once it is long enough to matter, e.g.
    /// <c>135 min (2 h 15 min)</c> or <c>1620 min (1 d 3 h)</c>.
    /// </summary>
    private static string FormatMinutes(double minutes)
    {
        string baseText = $"{minutes:0.#} min";
        if (minutes < 60)
        {
            return baseText;
        }

        var ts = TimeSpan.FromMinutes(minutes);
        string human;
        if (ts.TotalDays >= 1)
        {
            int days = (int)ts.TotalDays;
            human = $"{days} d" +
                (ts.Hours > 0 ? $" {ts.Hours} h" : string.Empty) +
                (ts.Minutes > 0 ? $" {ts.Minutes} min" : string.Empty);
        }
        else
        {
            human = $"{(int)ts.TotalHours} h" +
                (ts.Minutes > 0 ? $" {ts.Minutes} min" : string.Empty);
        }

        return $"{baseText} ({human})";
    }

    private static double? InterpolateY(IReadOnlyList<Point> pts, double x)
    {
        if (pts.Count == 0)
        {
            return null;
        }

        if (x <= pts[0].X) return pts[0].Y;
        if (x >= pts[^1].X) return pts[^1].Y;
        for (int i = 1; i < pts.Count; i++)
        {
            if (x <= pts[i].X)
            {
                Point a = pts[i - 1];
                Point b = pts[i];
                double dx = b.X - a.X;
                double t = dx == 0 ? 0 : (x - a.X) / dx;
                return a.Y + (b.Y - a.Y) * t;
            }
        }

        return pts[^1].Y;
    }

    private void AddOverlay(UIElement element)
    {
        _overlay.Add(element);
        PlotCanvas.Children.Add(element);
    }

    private void ClearOverlay()
    {
        foreach (UIElement element in _overlay)
        {
            PlotCanvas.Children.Remove(element);
        }

        _overlay.Clear();
    }
}
