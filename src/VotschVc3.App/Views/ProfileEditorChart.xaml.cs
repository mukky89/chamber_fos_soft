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
/// Cycling is drawn out in full: the repeated body appears once per cycle on the
/// time axis (so the curve is as long as the run really is), with every repetition
/// shaded and numbered. Only the first pass carries drag handles – the repeats are
/// the same segments and are edited through it.
///
/// The time (X) axis can be zoomed with the wheel around the cursor, with the
/// ＋ / － buttons, or by dragging the mini-map strip under the plot; while zoomed
/// in, dragging the plot pans and a double-click resets the view. Long profiles
/// (many hours, dozens of steps) are otherwise squeezed into a few pixels per
/// segment, which makes both reading and handle dragging impossible.
/// </summary>
public partial class ProfileEditorChart : UserControl
{
    private const double PadLeft = 46;
    private const double PadRight = 12;
    private const double PadTop = 12;
    /// <summary>Room under the plot for the time labels and the mini-map strip.</summary>
    private const double PadBottom = 38;

    /// <summary>Never zoom past a window this short – below it the handles overlap.</summary>
    private const double MinVisibleMinutes = 1;
    /// <summary>Zoom per full wheel notch (120 units); partial deltas scale smoothly.</summary>
    private const double ZoomStep = 1.4;
    /// <summary>One press of ＋ / －.</summary>
    private const double ButtonZoomStep = 1.8;

    /// <summary>
    /// Handles closer together than this many pixels are not drawn: on a profile with
    /// dozens of steps they merge into an unreadable string of blobs and cannot be hit
    /// with the mouse anyway. Zooming in brings them back.
    /// </summary>
    private const double MinHandleGapPx = 13;

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

    // Mini-map strip under the plot: clicking or dragging it moves the visible window,
    // which is far quicker than panning across a multi-day profile.
    private double _trackTop;
    private bool _isScrubbing;

    // Plot transform + curve captured on the last Redraw, so the hover read-out can
    // map a cursor position back to a temperature/time without recomputing the chart.
    private double _plotW;
    private double _plotH;
    private List<Point> _hoverPoints = new();
    private readonly List<UIElement> _hoverOverlay = new();

    // The steps as drawn on the (cycle-expanded) time axis, so the hover read-out can say
    // whether the cursor is on a ramp or a hold, and how long that step is.
    private List<DrawnStep> _steps = new();
    private ValueAxis _yAxis = new(0, 1, 1, 1);

    public ProfileEditorChart()
    {
        InitializeComponent();
        PlotCanvas.SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();

        // Tunnelling, and on the control itself: the canvas is covered in bands, the curve,
        // drag handles and the hover chip, and a bubbling handler only fires if whatever
        // sits under the cursor lets the event through.
        PreviewMouseWheel += PlotCanvas_MouseWheel;
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
        new PropertyMetadata(1, (d, _) => ((ProfileEditorChart)d).OnCycleCountChanged()));

    /// <summary>How many times the region repeats; every repetition is drawn on the time axis.</summary>
    public int CycleCount { get => (int)GetValue(CycleCountProperty); set => SetValue(CycleCountProperty, value); }

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(ProfileEditorChart),
        new PropertyMetadata(false, (d, _) => ((ProfileEditorChart)d).Redraw()));

    /// <summary>
    /// Preview mode: the curve, bands and zooming stay, but the drag handles are not drawn –
    /// the profile list only shows what a profile looks like, editing happens in the quick builder.
    /// </summary>
    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }

    private void OnCycleCountChanged()
    {
        // A different repeat count is a different time axis – a window resolved against
        // the old (shorter or longer) total would land somewhere unrelated.
        _viewport.Reset();
        Redraw();
    }

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
    private Brush Hold => TryFindResource("WarnBrush") as Brush ?? Brushes.Orange;
    private Brush Highlight => TryFindResource("DangerBrush") as Brush ?? Brushes.IndianRed;

    /// <summary>One step of the profile as it appears on the time axis.</summary>
    /// <param name="StartMin">Where the step starts (minutes from the profile start).</param>
    /// <param name="EndMin">Where it ends.</param>
    /// <param name="IsRamp">Ramp (sloped) rather than a hold.</param>
    /// <param name="FromTemperature">Temperature it starts at – gives a ramp its direction.</param>
    /// <param name="Target">Temperature it ends at.</param>
    /// <param name="Cycle">Repetition it belongs to; -1 for the lead-in / closing stages.</param>
    private readonly record struct DrawnStep(
        double StartMin, double EndMin, bool IsRamp, double FromTemperature, double Target, int Cycle);

    /// <summary>One drawn occurrence of a segment on the (cycle-expanded) time axis.</summary>
    /// <param name="Index">Index of the segment in the edited list.</param>
    /// <param name="StartMin">Where this occurrence starts on the (expanded) time axis.</param>
    /// <param name="Cycle">Zero-based repetition it belongs to; -1 for the lead-in / closing stages.</param>
    private readonly record struct Pass(int Index, double StartMin, int Cycle);

    /// <summary>
    /// The order in which segments appear on the time axis, with the cycled body repeated
    /// <see cref="CycleCount"/> times. Everything before <see cref="CycleStart"/> and after
    /// <see cref="CycleEnd"/> (the lead-in ramp and the closing safety hold) runs once.
    /// </summary>
    private List<Pass> BuildPasses(List<SegmentViewModel> segments, out double totalMinutes)
    {
        var passes = new List<Pass>();
        double t = 0;
        void Add(int index, int cycle)
        {
            passes.Add(new Pass(index, t, cycle));
            t += Math.Max(0, segments[index].DurationMinutes);
        }

        int cycles = Math.Max(1, CycleCount);
        if (cycles <= 1 || segments.Count == 0)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                Add(i, 0);
            }

            totalMinutes = t;
            return passes;
        }

        int cs = Math.Clamp(CycleStart, 0, segments.Count - 1);
        int ce = Math.Clamp(CycleEnd, cs, segments.Count - 1);

        for (int i = 0; i < cs; i++) Add(i, -1);
        for (int c = 0; c < cycles; c++)
        {
            for (int i = cs; i <= ce; i++) Add(i, c);
        }

        for (int i = ce + 1; i < segments.Count; i++) Add(i, -1);

        totalMinutes = t;
        return passes;
    }

    private void Redraw()
    {
        PlotCanvas.Children.Clear();
        _hoverOverlay.Clear();
        _hoverPoints = new List<Point>();
        _steps = new List<DrawnStep>();
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

        List<Pass> passes = BuildPasses(segments, out double totalMin);
        totalMin = Math.Max(1, totalMin);

        double startTemp = double.IsNaN(MeasuredStart) ? segments[0].TargetTemperature : MeasuredStart;

        var allTemps = new List<double> { startTemp };
        allTemps.AddRange(segments.Select(s => s.TargetTemperature));

        // A planned profile must show its real minimum and maximum as the axis bounds.
        // Rounded auto-scaling used to add visual padding (for example a -20 °C profile
        // minimum appeared on an axis extending to -30 °C), which suggested that the
        // profile would command a temperature it never actually contains.
        _yAxis = NiceAxis.Exact(allTemps.Min(), allTemps.Max());
        _minY = _yAxis.Min;
        _maxY = _yAxis.Max;

        double plotW = w - PadLeft - PadRight;
        double plotH = h - PadTop - PadBottom;
        if (plotW <= 0 || plotH <= 0)
        {
            return;
        }

        _totalMin = totalMin;
        _window = _viewport.Resolve(0, totalMin);
        _pxPerMinute = plotW / _window.Span;
        _plotW = plotW;
        _plotH = plotH;
        _trackTop = PadTop + plotH + 20;
        PlotCanvas.Cursor = _window.IsZoomed ? Cursors.SizeWE : Cursors.Arrow;

        double Xpx(double min) => PadLeft + (min - _window.Min) / _window.Span * plotW;
        double Ypx(double t) => PadTop + (1 - (t - _minY) / (_maxY - _minY)) * plotH;

        DrawCycleBands(passes, segments, plotH, Xpx);
        DrawHoldBands(passes, segments, plotH, Xpx);
        DrawGrid(plotW, plotH, Xpx);

        // Profile polyline over the whole (expanded) timeline + one handle per segment of
        // the first pass; the repeats show the same values and are edited through it.
        var linePoints = new PointCollection { new(Xpx(0), Ypx(startTemp)) };
        var handles = new List<(int Index, double X, double Y)>();
        _steps = new List<DrawnStep>(passes.Count);
        double previousTemp = startTemp;
        foreach (Pass pass in passes)
        {
            SegmentViewModel seg = segments[pass.Index];
            double dur = Math.Max(0, seg.DurationMinutes);
            double end = pass.StartMin + dur;
            _steps.Add(new DrawnStep(pass.StartMin, end, seg.IsRamp, previousTemp, seg.TargetTemperature, pass.Cycle));
            previousTemp = seg.TargetTemperature;
            if (seg.IsRamp)
            {
                linePoints.Add(new Point(Xpx(end), Ypx(seg.TargetTemperature)));
            }
            else
            {
                linePoints.Add(new Point(Xpx(pass.StartMin), Ypx(seg.TargetTemperature)));
                linePoints.Add(new Point(Xpx(end), Ypx(seg.TargetTemperature)));
            }

            if (pass.Cycle <= 0)
            {
                handles.Add((pass.Index, Xpx(end), Ypx(seg.TargetTemperature)));
            }
        }

        PlotCanvas.Children.Add(new Polyline
        {
            Points = linePoints,
            Stroke = Accent,
            StrokeThickness = 1.8,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        });
        _hoverPoints = linePoints.ToList();

        DrawHandles(handles, plotW, plotH);
        DrawZoomIndicator(plotW, plotH);
    }

    /// <summary>
    /// One shaded band per repetition of the cycled body, numbered "⟲ 2/4", with a dashed
    /// divider between neighbouring cycles. Alternating opacity makes the repeats
    /// countable even when they are only a few pixels wide.
    /// </summary>
    private void DrawCycleBands(List<Pass> passes, List<SegmentViewModel> segments, double plotH, Func<double, double> Xpx)
    {
        int cycles = Math.Max(1, CycleCount);
        if (cycles <= 1)
        {
            return;
        }

        for (int c = 0; c < cycles; c++)
        {
            List<Pass> inCycle = passes.Where(p => p.Cycle == c).ToList();
            if (inCycle.Count == 0)
            {
                continue;
            }

            double from = inCycle[0].StartMin;
            Pass last = inCycle[^1];
            double to = last.StartMin + Math.Max(0, segments[last.Index].DurationMinutes);
            double x1 = Xpx(from), x2 = Xpx(to);
            if (x2 <= x1)
            {
                continue;
            }

            var band = new Rectangle
            {
                Width = x2 - x1,
                Height = plotH,
                Fill = Accent,
                Opacity = c % 2 == 0 ? 0.16 : 0.08,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(band, x1);
            Canvas.SetTop(band, PadTop);
            PlotCanvas.Children.Add(band);

            PlotCanvas.Children.Add(new Line
            {
                X1 = x1, Y1 = PadTop, X2 = x1, Y2 = PadTop + plotH,
                Stroke = Accent, StrokeThickness = 1.5, Opacity = 0.7,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false,
            });

            // Only label a band that is wide enough to hold the text, so 30 cycles do not
            // turn the top of the chart into overlapping captions.
            if (x2 - x1 >= 52)
            {
                AddText($"⟲ {c + 1}/{cycles}", x1 + 4, PadTop + 2, Accent, 11);
            }
        }

        // Closing divider at the very end of the last repetition.
        List<Pass> lastCycle = passes.Where(p => p.Cycle == cycles - 1).ToList();
        if (lastCycle.Count > 0)
        {
            Pass last = lastCycle[^1];
            double endX = Xpx(last.StartMin + Math.Max(0, segments[last.Index].DurationMinutes));
            PlotCanvas.Children.Add(new Line
            {
                X1 = endX, Y1 = PadTop, X2 = endX, Y2 = PadTop + plotH,
                Stroke = Accent, StrokeThickness = 1.5, Opacity = 0.7,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false,
            });
        }
    }

    /// <summary>Faint warm column under every hold, so plateaus and ramps read apart at a
    /// glance on a profile with many steps. Skipped once the columns get too thin to tell.</summary>
    private void DrawHoldBands(List<Pass> passes, List<SegmentViewModel> segments, double plotH, Func<double, double> Xpx)
    {
        foreach (Pass pass in passes)
        {
            SegmentViewModel seg = segments[pass.Index];
            if (seg.IsRamp)
            {
                continue;
            }

            double x1 = Xpx(pass.StartMin);
            double x2 = Xpx(pass.StartMin + Math.Max(0, seg.DurationMinutes));
            if (x2 - x1 < 2)
            {
                continue;
            }

            var band = new Rectangle
            {
                Width = x2 - x1, Height = plotH, Fill = Hold, Opacity = 0.07, IsHitTestVisible = false,
            };
            Canvas.SetLeft(band, x1);
            Canvas.SetTop(band, PadTop);
            PlotCanvas.Children.Add(band);
        }
    }

    /// <summary>Horizontal value gridlines and vertical time gridlines, both on rounded steps.</summary>
    private void DrawGrid(double plotW, double plotH, Func<double, double> Xpx)
    {
        int intervals = Math.Max(1, _yAxis.Intervals);
        for (int i = 0; i <= intervals; i++)
        {
            double yVal = _yAxis.LabelAt(i);
            double py = PadTop + (1 - ((yVal - _minY) / (_maxY - _minY))) * plotH;
            PlotCanvas.Children.Add(new Line
            {
                X1 = PadLeft, Y1 = py, X2 = PadLeft + plotW, Y2 = py,
                Stroke = Line, StrokeThickness = 1, Opacity = 0.4, IsHitTestVisible = false,
            });
            AddText($"{yVal:0.#} °C", 0, py - 8, Muted, 10, PadLeft - 6, TextAlignment.Right);
        }

        // A labelled time axis – the chart used to have none at all, which is most of why
        // a long profile was impossible to read.
        double step = NiceAxis.NiceTimeStep(_window.Span);
        if (step <= 0)
        {
            return;
        }

        for (double t = Math.Ceiling(_window.Min / step) * step; t <= _window.Max + 1e-9; t += step)
        {
            double px = Xpx(t);
            PlotCanvas.Children.Add(new Line
            {
                X1 = px, Y1 = PadTop, X2 = px, Y2 = PadTop + plotH,
                Stroke = Line, StrokeThickness = 1, Opacity = 0.22,
                StrokeDashArray = new DoubleCollection { 3, 4 },
                IsHitTestVisible = false,
            });
            AddText(FormatMinutesShort(t), px - 34, PadTop + plotH + 3, Muted, 10, 68, TextAlignment.Center);
        }
    }

    /// <summary>
    /// Draggable handles, thinned out so they never overlap: on a 60-segment profile only
    /// the ones at least <see cref="MinHandleGapPx"/> apart are drawn and a hint tells the
    /// operator to zoom in for the rest.
    /// </summary>
    private void DrawHandles(List<(int Index, double X, double Y)> handles, double plotW, double plotH)
    {
        if (IsReadOnly)
        {
            return; // preview only – nothing to grab
        }

        double lastX = double.NegativeInfinity;
        int hidden = 0;
        foreach ((int index, double x, double y) in handles)
        {
            if (x < PadLeft - 8 || x > PadLeft + plotW + 8)
            {
                continue; // outside the zoomed window
            }

            if (x - lastX < MinHandleGapPx)
            {
                hidden++;
                continue;
            }

            lastX = x;
            var dot = new Ellipse
            {
                Width = 11,
                Height = 11,
                Fill = Accent,
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                Cursor = Cursors.SizeAll,
                Tag = index,
                ToolTip = "Ťahaj zvisle = teplota, vodorovne = trvanie segmentu",
            };
            Canvas.SetLeft(dot, x - 5.5);
            Canvas.SetTop(dot, y - 5.5);
            dot.MouseLeftButtonDown += Handle_MouseDown;
            PlotCanvas.Children.Add(dot);
        }

        if (hidden > 0)
        {
            AddChip($"⚠ {hidden} bodov skrytých – priblíž (koliesko / ＋) a objavia sa",
                PadLeft + 4, PadTop + plotH - 38, Muted);
        }
    }

    /// <summary>Mini-map strip + chip telling which slice of the profile is shown.
    /// Nothing is drawn at 1x – the chart then shows the whole profile as before.</summary>
    private void DrawZoomIndicator(double plotW, double plotH)
    {
        if (!_window.IsZoomed || plotW <= 0 || _totalMin <= 0)
        {
            return;
        }

        var track = new Rectangle
        {
            Width = plotW, Height = 6, RadiusX = 3, RadiusY = 3, Fill = Line, Opacity = 0.7,
            Cursor = Cursors.Hand,
            ToolTip = "Klikni alebo ťahaj – posunieš výrez po celom profile",
        };
        Canvas.SetLeft(track, PadLeft);
        Canvas.SetTop(track, _trackTop);
        PlotCanvas.Children.Add(track);

        var thumb = new Rectangle
        {
            Width = Math.Max(8, _window.Span / _totalMin * plotW),
            Height = 6, RadiusX = 3, RadiusY = 3, Fill = Accent, IsHitTestVisible = false,
        };
        Canvas.SetLeft(thumb, PadLeft + _window.Min / _totalMin * plotW);
        Canvas.SetTop(thumb, _trackTop);
        PlotCanvas.Children.Add(thumb);

        AddChip($"🔍 {_window.Zoom:0.#}× · {FormatMinutesShort(_window.Min)} – {FormatMinutesShort(_window.Max)}"
              + " · dvojklik = celý profil",
            PadLeft + 4, PadTop + plotH - 20, Muted);
    }

    private void AddChip(string text, double left, double top, Brush foreground)
    {
        var chip = new Border
        {
            Background = TryFindResource("SurfaceBrush") as Brush ?? Brushes.Black,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 1, 6, 1),
            Opacity = 0.92,
            IsHitTestVisible = false,
            Child = new TextBlock { Text = text, Foreground = foreground, FontSize = 10 },
        };
        Canvas.SetLeft(chip, left);
        Canvas.SetTop(chip, top);
        PlotCanvas.Children.Add(chip);
    }

    // ===== Zoom / posun časovej osi =====

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomAroundCentre(ButtonZoomStep);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomAroundCentre(1 / ButtonZoomStep);

    private void ZoomReset_Click(object sender, RoutedEventArgs e) => ResetZoom();

    /// <summary>Zoom from the buttons keeps the middle of the current window in place.</summary>
    private void ZoomAroundCentre(double factor)
    {
        if (_totalMin <= 0 || !_viewport.Zoom(factor, 0.5, 0, _totalMin))
        {
            return;
        }

        ClearHoverOverlay();
        Redraw();
    }

    private void PlotCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double plotW = PlotCanvas.ActualWidth - PadLeft - PadRight;
        if (plotW <= 0 || _totalMin <= 0 || _pxPerMinute <= 0)
        {
            return;
        }

        double notches = Math.Clamp(e.Delta / 120d, -3, 3);

        // Shift + wheel scrolls along the profile instead of zooming – the usual gesture
        // once a long profile is zoomed in and only a slice is visible.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (_window.IsZoomed &&
                _viewport.MoveTo(_window.Min - (notches * _window.Span * 0.25), 0, _totalMin))
            {
                ClearHoverOverlay();
                Redraw();
            }

            e.Handled = _window.IsZoomed;
            return;
        }

        // Scale by how far the wheel actually turned: one notch is 120, a trackpad
        // sends much smaller deltas and would otherwise jump a full step each time.
        double factor = Math.Pow(ZoomStep, notches);

        // Zoom around the cursor, so it grabs the spot the operator is pointing at.
        double cursorX = Math.Clamp(e.GetPosition(PlotCanvas).X, PadLeft, PadLeft + plotW);
        if (!_viewport.Zoom(factor, (cursorX - PadLeft) / plotW, 0, _totalMin))
        {
            // Nothing left to zoom. While the chart is zoomed in the gesture still belongs
            // to it – letting it bubble scrolled the page out from under the operator the
            // moment they hit the zoom limit inside a plateau.
            e.Handled = _window.IsZoomed;
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

        Point pos = e.GetPosition(PlotCanvas);
        if (IsOnTrack(pos))
        {
            _isScrubbing = true;
            ScrubTo(pos.X);
            PlotCanvas.CaptureMouse();
            ClearHoverOverlay();
            e.Handled = true;
            return;
        }

        _isPanning = true;
        _panStartMouse = pos;
        _panStartViewStart = _window.Min;
        PlotCanvas.CaptureMouse();
        ClearHoverOverlay();
        e.Handled = true;
    }

    /// <summary>True while the cursor is over the mini-map strip under the plot.</summary>
    private bool IsOnTrack(Point pos) =>
        _window.IsZoomed && pos.Y >= _trackTop - 6 && pos.Y <= _trackTop + 12 &&
        pos.X >= PadLeft && pos.X <= PadLeft + _plotW;

    /// <summary>Centres the visible window on the point of the mini-map that was clicked.</summary>
    private void ScrubTo(double x)
    {
        if (_plotW <= 0 || _totalMin <= 0)
        {
            return;
        }

        double fraction = Math.Clamp((x - PadLeft) / _plotW, 0, 1);
        if (_viewport.MoveTo((fraction * _totalMin) - (_window.Span / 2), 0, _totalMin))
        {
            Redraw();
        }
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
        if (_isScrubbing)
        {
            ScrubTo(e.GetPosition(PlotCanvas).X);
            return;
        }

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
        if (_isScrubbing)
        {
            _isScrubbing = false;
            PlotCanvas.ReleaseMouseCapture();
            return;
        }

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

    private void AddText(
        string text, double left, double top, Brush brush, double size,
        double? width = null, TextAlignment align = TextAlignment.Left)
    {
        var tb = new TextBlock
        {
            Text = text, Foreground = brush, FontSize = size, TextAlignment = align, IsHitTestVisible = false,
        };
        if (width is { } w)
        {
            tb.Width = w;
        }

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

        // Highlight the whole step under the cursor, so it is obvious where the ramp ends
        // and the hold begins – and how long each of them is.
        DrawnStep? hovered = StepAt(minutes);
        if (hovered is { } step)
        {
            double sx1 = left + (step.StartMin - _window.Min) * _pxPerMinute;
            double sx2 = left + (step.EndMin - _window.Min) * _pxPerMinute;
            double bandLeft = Math.Max(left, sx1);
            double bandRight = Math.Min(left + _plotW, sx2);
            var band = new Rectangle
            {
                Width = Math.Max(1, bandRight - bandLeft),
                Height = _plotH,
                Fill = Highlight,
                Opacity = 0.18,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(band, bandLeft);
            Canvas.SetTop(band, PadTop);
            AddHoverOverlay(band);

            foreach (double edge in new[] { sx1, sx2 })
            {
                if (edge < left || edge > left + _plotW)
                {
                    continue; // outside the zoomed window
                }

                AddHoverOverlay(new Line
                {
                    X1 = edge, Y1 = PadTop, X2 = edge, Y2 = PadTop + _plotH,
                    Stroke = Highlight, StrokeThickness = 1.5, Opacity = 0.85,
                    IsHitTestVisible = false,
                });
            }

            double YOf(double t) => PadTop + (1 - ((t - _minY) / (_maxY - _minY))) * _plotH;
            AddHoverOverlay(new Line
            {
                X1 = sx1, Y1 = YOf(step.IsRamp ? step.FromTemperature : step.Target),
                X2 = sx2, Y2 = YOf(step.Target),
                Stroke = Highlight, StrokeThickness = 3, IsHitTestVisible = false,
            });
        }

        AddHoverOverlay(new Line
        {
            X1 = mx, Y1 = PadTop, X2 = mx, Y2 = PadTop + _plotH,
            Stroke = accent, StrokeThickness = 1, Opacity = 0.6,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            IsHitTestVisible = false,
        });

        var dot = new Ellipse { Width = 8, Height = 8, Fill = accent, Stroke = Brushes.White, StrokeThickness = 1, IsHitTestVisible = false };
        Canvas.SetLeft(dot, mx - 4);
        Canvas.SetTop(dot, py - 4);
        AddHoverOverlay(dot);

        var chipContent = new StackPanel();
        chipContent.Children.Add(new TextBlock
        {
            Text = $"{temperature:0.0} °C  ·  {FormatMinutesShort(minutes)}",
            Foreground = Brushes.White,
            FontSize = 11,
            FontFamily = new FontFamily("Segoe UI Semibold"),
        });
        if (hovered is { } current)
        {
            chipContent.Children.Add(new TextBlock
            {
                Text = StepLabel(current),
                Foreground = Brushes.White,
                FontSize = 10,
                Opacity = 0.9,
            });
            if (current.Cycle >= 0 && CycleCount > 1)
            {
                chipContent.Children.Add(new TextBlock
                {
                    Text = $"⟲ cyklus {current.Cycle + 1}/{Math.Max(1, CycleCount)}",
                    Foreground = Brushes.White,
                    FontSize = 10,
                    Opacity = 0.75,
                });
            }
        }

        var chip = new Border
        {
            Background = accent,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 2, 6, 2),
            IsHitTestVisible = false,
            Child = chipContent,
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

    /// <summary>The step the given time falls in, or <c>null</c> outside the profile.</summary>
    private DrawnStep? StepAt(double minutes)
    {
        foreach (DrawnStep step in _steps)
        {
            if (minutes >= step.StartMin && minutes <= step.EndMin && step.EndMin > step.StartMin)
            {
                return step;
            }
        }

        return null;
    }

    /// <summary>"↗ Rampa (ohrev) na 60 °C · dĺžka 30 min" / "→ Výdrž (plato) 60 °C · dĺžka 1 h 40 min".</summary>
    private static string StepLabel(DrawnStep step)
    {
        string length = $"dĺžka {FormatMinutesShort(step.EndMin - step.StartMin)}";
        if (!step.IsRamp)
        {
            return $"→ Výdrž (plato) {step.Target:0.#} °C · {length}";
        }

        double delta = step.Target - step.FromTemperature;
        string direction = Math.Abs(delta) < 0.05
            ? "→ Rampa (bez zmeny)"
            : delta > 0 ? "↗ Rampa (ohrev)" : "↘ Rampa (chladenie)";
        return $"{direction} na {step.Target:0.#} °C · {length}";
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
