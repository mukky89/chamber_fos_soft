using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Charting;

namespace VotschVc3.App.Views;

/// <summary>
/// Interactive temperature-profile editor. Renders the programmed profile and
/// lets the user drag the handle of each segment: vertically to change its
/// target temperature, horizontally to resize its duration (stretch/shrink the
/// ramp or plateau). Both are editable in the grid too.
///
/// The time (X) axis can be zoomed with the mouse wheel around the cursor;
/// while zoomed in, dragging the empty plot area pans and a double-click resets
/// the view. Long profiles (many hours) are otherwise squeezed into a few pixels
/// per segment, which makes both reading and handle dragging impossible.
/// </summary>
public partial class ProfileEditorChart : UserControl
{
    private const double PadLeft = 42;
    private const double PadRight = 12;
    private const double PadTop = 12;
    private const double PadBottom = 22;

    /// <summary>Never zoom past a window this short – below it the handles overlap.</summary>
    private const double MinVisibleMinutes = 1;
    private const double ZoomStep = 1.25;

    private double _minY;
    private double _maxY;
    private double _pxPerMinute = 1;
    private int _dragIndex = -1;
    private Point _dragStartMouse;
    private double _dragStartDuration;
    private double _dragPxPerMinute = 1;

    // Time-axis viewport (shared, tested logic in Core) + the window resolved on the
    // last Redraw, which the mouse handlers map cursor positions through.
    private readonly TimeAxisViewport _viewport = new(MinVisibleMinutes);
    private AxisWindow _window = new(0, 1, 1);
    private double _totalMin = 1;
    private bool _isPanning;
    private Point _panStartMouse;
    private double _panStartViewStart;

    // Plot transform + curve captured on the last Redraw, so the hover read-out can
    // map a cursor position back to a temperature/time without recomputing the chart.
    private double _plotW;
    private double _plotH;
    private List<Point> _hoverPoints = new();
    private readonly List<UIElement> _hoverOverlay = new();

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

        // A different profile is a different time axis – start from the full view.
        chart._viewport.Reset();
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
        _hoverOverlay.Clear();
        _hoverPoints = new List<Point>();
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

        _totalMin = totalMin;
        _window = _viewport.Resolve(0, totalMin);
        _pxPerMinute = plotW / _window.Span;
        _plotW = plotW;
        _plotH = plotH;
        PlotCanvas.Cursor = _window.IsZoomed ? Cursors.SizeWE : Cursors.Arrow;

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
        double Xpx(double min) => PadLeft + (min - _window.Min) / _window.Span * plotW;
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
        _hoverPoints = linePoints.ToList();

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
                Cursor = Cursors.SizeAll,
                Tag = index,
                ToolTip = "Ťahaj zvisle = teplota, vodorovne = trvanie segmentu",
            };
            Canvas.SetLeft(dot, x - 6);
            Canvas.SetTop(dot, y - 6);
            dot.MouseLeftButtonDown += Handle_MouseDown;
            PlotCanvas.Children.Add(dot);
        }

        DrawZoomIndicator(plotW, plotH);
    }

    /// <summary>Mini-map strip + chip telling which slice of the profile is shown.
    /// Nothing is drawn at 1x – the chart then shows the whole profile as before.</summary>
    private void DrawZoomIndicator(double plotW, double plotH)
    {
        if (!_window.IsZoomed || plotW <= 0 || _totalMin <= 0)
        {
            return;
        }

        double trackY = PadTop + plotH + 6;
        var track = new Rectangle
        {
            Width = plotW, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = Line, Opacity = 0.7,
        };
        Canvas.SetLeft(track, PadLeft);
        Canvas.SetTop(track, trackY);
        PlotCanvas.Children.Add(track);

        var thumb = new Rectangle
        {
            Width = Math.Max(6, _window.Span / _totalMin * plotW),
            Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = Accent,
        };
        Canvas.SetLeft(thumb, PadLeft + _window.Min / _totalMin * plotW);
        Canvas.SetTop(thumb, trackY);
        PlotCanvas.Children.Add(thumb);

        var chip = new Border
        {
            Background = TryFindResource("SurfaceBrush") as Brush ?? Brushes.Black,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 1, 6, 1),
            Opacity = 0.92,
            Child = new TextBlock
            {
                Text = $"🔍 {_window.Zoom:0.#}× · {FormatMinutesShort(_window.Min)} – {FormatMinutesShort(_window.Max)}"
                     + " · dvojklik = celý profil",
                Foreground = Muted,
                FontSize = 10,
            },
        };
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, Math.Max(PadLeft, PadLeft + plotW - chip.DesiredSize.Width - 2));
        Canvas.SetTop(chip, PadTop + 2);
        PlotCanvas.Children.Add(chip);
    }

    // ===== Zoom / posun časovej osi =====

    private void PlotCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double plotW = PlotCanvas.ActualWidth - PadLeft - PadRight;
        if (plotW <= 0 || _totalMin <= 0 || _pxPerMinute <= 0)
        {
            return;
        }

        // Zoom around the cursor, so it grabs the spot the operator is pointing at.
        double cursorX = Math.Clamp(e.GetPosition(PlotCanvas).X, PadLeft, PadLeft + plotW);
        if (!_viewport.Zoom(e.Delta > 0 ? ZoomStep : 1 / ZoomStep, (cursorX - PadLeft) / plotW, 0, _totalMin))
        {
            // Nothing left to zoom (already at full view / at the limit) – let the
            // wheel bubble so the page underneath scrolls as usual.
            return;
        }

        // A wheel that both zooms and scrolls the surrounding page is unusable.
        e.Handled = true;
        ClearHoverOverlay();
        Redraw();
    }

    private void PlotCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Handles have their own MouseLeftButtonDown and mark the event handled,
        // so panning only ever starts on the empty plot area.
        if (e.ClickCount == 2)
        {
            ResetZoom();
            e.Handled = true;
            return;
        }

        if (!_window.IsZoomed || _dragIndex >= 0)
        {
            return;
        }

        _isPanning = true;
        _panStartMouse = e.GetPosition(PlotCanvas);
        _panStartViewStart = _window.Min;
        PlotCanvas.CaptureMouse();
        ClearHoverOverlay();
        e.Handled = true;
    }

    private void ResetZoom()
    {
        if (!_viewport.IsZoomed)
        {
            return;
        }

        _viewport.Reset();
        ClearHoverOverlay();
        Redraw();
    }

    private void Handle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Ellipse { Tag: int index })
        {
            List<SegmentViewModel> segments = GetSegments();
            if (index >= segments.Count)
            {
                return;
            }

            _dragIndex = index;
            _dragStartMouse = e.GetPosition(PlotCanvas);
            _dragStartDuration = segments[index].DurationMinutes;
            // Fixed for the whole gesture: resizing this segment changes the total
            // duration, which would otherwise change px-per-minute mid-drag and
            // make the handle chase the pointer.
            _dragPxPerMinute = _pxPerMinute;
            PlotCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void PlotCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            if (_pxPerMinute > 0)
            {
                double shiftedStart = _panStartViewStart
                    - (e.GetPosition(PlotCanvas).X - _panStartMouse.X) / _pxPerMinute;
                if (_viewport.MoveTo(shiftedStart, 0, _totalMin))
                {
                    Redraw();
                }
            }

            return;
        }

        if (_dragIndex < 0)
        {
            UpdateHover(e.GetPosition(PlotCanvas));
            return;
        }

        ClearHoverOverlay();
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

        Point pos = e.GetPosition(PlotCanvas);

        // Vertical: absolute position maps straight to temperature.
        double t = _minY + (1 - (pos.Y - PadTop) / plotH) * (_maxY - _minY);
        t = Math.Clamp(t, -90, 250);
        segments[_dragIndex].TargetTemperature = Math.Round(t, 1);

        // Horizontal: relative offset from the drag start resizes this segment's
        // own duration – every later handle shifts along with it, earlier ones
        // are untouched.
        if (_dragPxPerMinute > 0)
        {
            double deltaMinutes = (pos.X - _dragStartMouse.X) / _dragPxPerMinute;
            double duration = Math.Max(1, Math.Round(_dragStartDuration + deltaMinutes));
            segments[_dragIndex].DurationMinutes = duration;
        }

        Redraw();
    }

    private void PlotCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            PlotCanvas.ReleaseMouseCapture();
            return;
        }

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

    // ===== Hover read-out: crosshair + temperature/time chip following the cursor =====

    private void PlotCanvas_MouseLeave(object sender, MouseEventArgs e) => ClearHoverOverlay();

    private void UpdateHover(Point pos)
    {
        ClearHoverOverlay();
        if (_dragIndex >= 0 || _hoverPoints.Count == 0 || _plotW <= 0 || _pxPerMinute <= 0)
        {
            return;
        }

        double left = PadLeft;
        double mx = Math.Clamp(pos.X, left, left + _plotW);
        if (InterpolateY(_hoverPoints, mx) is not { } py)
        {
            return;
        }

        double minutes = _window.Min + (mx - left) / _pxPerMinute;
        double temperature = _maxY - (py - PadTop) / _plotH * (_maxY - _minY);

        Brush accent = Accent;
        AddHoverOverlay(new Line
        {
            X1 = mx, Y1 = PadTop, X2 = mx, Y2 = PadTop + _plotH,
            Stroke = accent, StrokeThickness = 1, Opacity = 0.6,
            StrokeDashArray = new DoubleCollection { 3, 3 },
        });

        var dot = new Ellipse { Width = 8, Height = 8, Fill = accent, Stroke = Brushes.White, StrokeThickness = 1 };
        Canvas.SetLeft(dot, mx - 4);
        Canvas.SetTop(dot, py - 4);
        AddHoverOverlay(dot);

        var chip = new Border
        {
            Background = accent,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock
            {
                Text = $"{temperature:0.0} °C  ·  {FormatMinutesShort(minutes)}",
                Foreground = Brushes.White,
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI Semibold"),
            },
        };
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double cx = mx + 8;
        if (cx + chip.DesiredSize.Width > left + _plotW)
        {
            cx = mx - chip.DesiredSize.Width - 8;
        }

        double cy = py - 26 < PadTop ? py + 10 : py - 26;
        Canvas.SetLeft(chip, Math.Max(left, cx));
        Canvas.SetTop(chip, cy);
        AddHoverOverlay(chip);
    }

    private static string FormatMinutesShort(double minutes)
    {
        if (minutes < 60)
        {
            return $"{minutes:0.#} min";
        }

        var ts = TimeSpan.FromMinutes(minutes);
        return ts.TotalDays >= 1
            ? $"{(int)ts.TotalDays} d {ts.Hours} h"
            : $"{(int)ts.TotalHours} h {ts.Minutes} min";
    }

    /// <summary>Linearly interpolates the Y (px) of a monotonically non-decreasing-X
    /// point list at a given X (px); flat jumps between equal X values pick the
    /// nearer neighbour rather than dividing by zero.</summary>
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

    private void AddHoverOverlay(UIElement element)
    {
        _hoverOverlay.Add(element);
        PlotCanvas.Children.Add(element);
    }

    private void ClearHoverOverlay()
    {
        foreach (UIElement element in _hoverOverlay)
        {
            PlotCanvas.Children.Remove(element);
        }

        _hoverOverlay.Clear();
    }
}
