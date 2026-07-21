using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

/// <summary>
/// Interactive temperature-profile editor. Renders the programmed profile and
/// lets the user drag the handle of each segment up/down to change its target
/// temperature directly in the graph. Durations stay editable in the grid.
/// </summary>
public partial class ProfileEditorChart : UserControl
{
    private const double PadLeft = 42;
    private const double PadRight = 12;
    private const double PadTop = 12;
    private const double PadBottom = 22;

    private double _minY;
    private double _maxY;
    private int _dragIndex = -1;

    public ProfileEditorChart()
    {
        InitializeComponent();
        PlotCanvas.SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();
    }

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(System.Collections.IEnumerable), typeof(ProfileEditorChart),
        new PropertyMetadata(null, OnSegmentsChanged));

    public System.Collections.IEnumerable? Segments
    {
        get => (System.Collections.IEnumerable?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public static readonly DependencyProperty MeasuredStartProperty = DependencyProperty.Register(
        nameof(MeasuredStart), typeof(double), typeof(ProfileEditorChart),
        new PropertyMetadata(double.NaN, (d, _) => ((ProfileEditorChart)d).Redraw()));

    /// <summary>Optional start temperature (e.g. current measured value).</summary>
    public double MeasuredStart
    {
        get => (double)GetValue(MeasuredStartProperty);
        set => SetValue(MeasuredStartProperty, value);
    }

    public static readonly DependencyProperty CycleStartProperty = DependencyProperty.Register(
        nameof(CycleStart), typeof(int), typeof(ProfileEditorChart),
        new PropertyMetadata(0, (d, _) => ((ProfileEditorChart)d).Redraw()));

    /// <summary>Zero-based first segment index of the repeated region.</summary>
    public int CycleStart { get => (int)GetValue(CycleStartProperty); set => SetValue(CycleStartProperty, value); }

    public static readonly DependencyProperty CycleEndProperty = DependencyProperty.Register(
        nameof(CycleEnd), typeof(int), typeof(ProfileEditorChart),
        new PropertyMetadata(int.MaxValue, (d, _) => ((ProfileEditorChart)d).Redraw()));

    /// <summary>Zero-based last segment index (inclusive) of the repeated region.</summary>
    public int CycleEnd { get => (int)GetValue(CycleEndProperty); set => SetValue(CycleEndProperty, value); }

    public static readonly DependencyProperty CycleCountProperty = DependencyProperty.Register(
        nameof(CycleCount), typeof(int), typeof(ProfileEditorChart),
        new PropertyMetadata(1, (d, _) => ((ProfileEditorChart)d).Redraw()));

    /// <summary>How many times the region repeats (band is shown only when &gt; 1).</summary>
    public int CycleCount { get => (int)GetValue(CycleCountProperty); set => SetValue(CycleCountProperty, value); }

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (ProfileEditorChart)d;
        if (e.OldValue is INotifyCollectionChanged oldCol)
        {
            oldCol.CollectionChanged -= chart.OnCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCol)
        {
            newCol.CollectionChanged += chart.OnCollectionChanged;
        }

        chart.HookItems();
        chart.Redraw();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HookItems();
        Redraw();
    }

    private void HookItems()
    {
        if (Segments is null)
        {
            return;
        }

        foreach (object item in Segments)
        {
            if (item is INotifyPropertyChanged inpc)
            {
                inpc.PropertyChanged -= OnItemChanged;
                inpc.PropertyChanged += OnItemChanged;
            }
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_dragIndex < 0)
        {
            Redraw();
        }
    }

    private List<SegmentViewModel> GetSegments() =>
        Segments?.OfType<SegmentViewModel>().ToList() ?? new List<SegmentViewModel>();

    private Brush Muted => TryFindResource("MutedBrush") as Brush ?? Brushes.Gray;
    private Brush Accent => TryFindResource("AccentBrush") as Brush ?? Brushes.SteelBlue;
    private Brush Line => TryFindResource("BorderBrush") as Brush ?? Brushes.DimGray;

    private void Redraw()
    {
        PlotCanvas.Children.Clear();
        double w = PlotCanvas.ActualWidth, h = PlotCanvas.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        List<SegmentViewModel> segments = GetSegments();
        if (segments.Count == 0)
        {
            AddText("Pridaj segmenty…", w / 2 - 40, h / 2 - 8, Muted, 12);
            return;
        }

        double startTemp = double.IsNaN(MeasuredStart) ? segments[0].TargetTemperature : MeasuredStart;
        double totalMin = Math.Max(1, segments.Sum(s => Math.Max(0, s.DurationMinutes)));

        var allTemps = new List<double> { startTemp };
        allTemps.AddRange(segments.Select(s => s.TargetTemperature));
        _minY = allTemps.Min();
        _maxY = allTemps.Max();
        if (_maxY - _minY < 1)
        {
            _maxY += 1;
            _minY -= 1;
        }

        double pad = (_maxY - _minY) * 0.12;
        _minY -= pad;
        _maxY += pad;

        double plotW = w - PadLeft - PadRight;
        double plotH = h - PadTop - PadBottom;

        // Gridlines + Y labels.
        for (int i = 0; i <= 4; i++)
        {
            double frac = i / 4.0;
            double py = PadTop + plotH * frac;
            double yVal = _maxY - (_maxY - _minY) * frac;
            PlotCanvas.Children.Add(new Line { X1 = PadLeft, Y1 = py, X2 = PadLeft + plotW, Y2 = py, Stroke = Line, StrokeThickness = 1, Opacity = 0.4 });
            AddText($"{yVal:0.#}", 2, py - 8, Muted, 10);
        }

        // Build the profile polyline + handle positions (one per segment end).
        double Xpx(double min) => PadLeft + min / totalMin * plotW;
        double Ypx(double t) => PadTop + (1 - (t - _minY) / (_maxY - _minY)) * plotH;

        // Cycled-region band (drawn behind the profile line): shows which segments repeat
        // and how many times. Only when a repeat count > 1 is set.
        if (CycleCount > 1)
        {
            int cs = Math.Clamp(CycleStart, 0, segments.Count - 1);
            int ce = Math.Clamp(CycleEnd, cs, segments.Count - 1);
            double startMin = 0;
            for (int i = 0; i < cs; i++)
            {
                startMin += Math.Max(0, segments[i].DurationMinutes);
            }

            double endMin = startMin;
            for (int i = cs; i <= ce; i++)
            {
                endMin += Math.Max(0, segments[i].DurationMinutes);
            }

            double bx1 = Xpx(startMin), bx2 = Xpx(endMin);
            var band = new Rectangle { Width = Math.Max(0, bx2 - bx1), Height = plotH, Fill = Accent, Opacity = 0.14 };
            Canvas.SetLeft(band, bx1);
            Canvas.SetTop(band, PadTop);
            PlotCanvas.Children.Add(band);

            foreach (double bx in new[] { bx1, bx2 })
            {
                PlotCanvas.Children.Add(new Line
                {
                    X1 = bx, Y1 = PadTop, X2 = bx, Y2 = PadTop + plotH,
                    Stroke = Accent, StrokeThickness = 1.5, Opacity = 0.7,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                });
            }

            AddText($"⟲ cyklus ×{CycleCount}  (segmenty {cs + 1}–{ce + 1})",
                bx1 + 4, PadTop + 2, Accent, 11);
        }

        var linePoints = new PointCollection { new(Xpx(0), Ypx(startTemp)) };
        var handles = new List<(int index, double x, double y)>();
        double cum = 0;

        foreach ((SegmentViewModel seg, int idx) in segments.Select((s, i) => (s, i)))
        {
            double dur = Math.Max(0, seg.DurationMinutes);
            if (seg.IsRamp)
            {
                cum += dur;
                linePoints.Add(new Point(Xpx(cum), Ypx(seg.TargetTemperature)));
            }
            else
            {
                linePoints.Add(new Point(Xpx(cum), Ypx(seg.TargetTemperature)));
                cum += dur;
                linePoints.Add(new Point(Xpx(cum), Ypx(seg.TargetTemperature)));
            }

            handles.Add((idx, Xpx(cum), Ypx(seg.TargetTemperature)));
        }

        PlotCanvas.Children.Add(new Polyline { Points = linePoints, Stroke = Accent, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round });

        // Draggable handles.
        foreach ((int index, double x, double y) in handles)
        {
            var dot = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = Accent,
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                Cursor = Cursors.SizeNS,
                Tag = index,
            };
            Canvas.SetLeft(dot, x - 6);
            Canvas.SetTop(dot, y - 6);
            dot.MouseLeftButtonDown += Handle_MouseDown;
            PlotCanvas.Children.Add(dot);
        }
    }

    private void Handle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Ellipse { Tag: int index })
        {
            _dragIndex = index;
            PlotCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void PlotCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragIndex < 0)
        {
            return;
        }

        List<SegmentViewModel> segments = GetSegments();
        if (_dragIndex >= segments.Count)
        {
            return;
        }

        double plotH = PlotCanvas.ActualHeight - PadTop - PadBottom;
        if (plotH <= 0)
        {
            return;
        }

        double py = e.GetPosition(PlotCanvas).Y;
        double t = _minY + (1 - (py - PadTop) / plotH) * (_maxY - _minY);
        t = Math.Clamp(t, -90, 250);
        segments[_dragIndex].TargetTemperature = Math.Round(t, 1);
        Redraw();
    }

    private void PlotCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragIndex >= 0)
        {
            _dragIndex = -1;
            PlotCanvas.ReleaseMouseCapture();
        }
    }

    private void AddText(string text, double left, double top, Brush brush, double size)
    {
        var tb = new TextBlock { Text = text, Foreground = brush, FontSize = size };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb, top);
        PlotCanvas.Children.Add(tb);
    }
}
