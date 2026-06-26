using System.Windows;
using System.Windows.Controls;
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

    public ChartView()
    {
        InitializeComponent();
        PlotCanvas.SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();
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

        // Horizontal gridlines + Y labels.
        for (int i = 0; i <= 4; i++)
        {
            double frac = i / 4.0;
            double yVal = minY + (maxY - minY) * (1 - frac);
            double py = PadTop + plotH * frac;
            AddLine(PadLeft, py, PadLeft + plotW, py, GridBrush, 1, dashed: i is not 0 and not 4);
            AddText($"{yVal:0.#}{Unit}", 2, py - 8, MutedBrush, 10, PadLeft - 6, TextAlignment.Right);
        }

        // X axis labels (min / max).
        AddText($"{minX:0.#} min", PadLeft, PadTop + plotH + 4, MutedBrush, 10);
        AddText($"{maxX:0.#} min", PadLeft + plotW - 50, PadTop + plotH + 4, MutedBrush, 10, 50, TextAlignment.Right);

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
}
