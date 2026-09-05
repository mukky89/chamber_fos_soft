using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Globalization;
using VotschVc3.App.Charting;
using VotschVc3.Core.Charting;

namespace VotschVc3.App.Views;

/// <summary>
/// Minimal, dependency-free line chart. Renders one or more
/// <see cref="ChartSeries"/> onto a canvas with auto-scaled axes, gridlines and
/// a small legend. Redraws on resize and whenever the series collection is
/// replaced.
///
/// The time (X) axis zooms with the mouse wheel around the cursor; while zoomed
/// in, dragging pans and a double-click goes back to the whole range. The window
/// is kept in data units, so a live chart that keeps appending points does not
/// drag the operator's view along with it.
/// </summary>
public partial class ChartView : UserControl
{
    private const double PadLeft = 72;
    private const double PadRight = 12;
    private const double PadTop = 10;
    /// <summary>Room under the plot for the time labels and the draggable mini-map strip.</summary>
    private const double PadBottom = 30;
    /// <summary>Zoom per full wheel notch (120 units); partial deltas scale smoothly.</summary>
    private const double ZoomStep = 1.4;
    /// <summary>One press of ＋ / －.</summary>
    private const double ButtonZoomStep = 1.8;

    // Plot transform captured on the last Redraw, so mouse handlers can map a
    // cursor position back to a data point (hover read-out).
    private bool _hasPlot;
    private double _minX, _maxX, _minY, _maxY, _plotW, _plotH;
    private ValueAxis _yAxis = new(0, 1, 1, 1);
    private ChartSeries? _hoverSeries;
    private readonly List<UIElement> _overlay = new();

    // Zoom/pan of the X axis (shared, tested logic in Core) + the full data range and
    // the window resolved on the last Redraw.
    private readonly TimeAxisViewport _viewport = new(minimumSpan: 1);
    private AxisWindow _window = new(0, 1, 1);
    private double _fullMinX;
    private double _fullMaxX = 1;
    private bool _isPanning;
    private Point _panStartMouse;
    private double _panStartMin;
    private bool _isSelecting;
    private bool _redrawPendingDuringSelection;
    private Point _selectionStart;
    private Rectangle? _selectionRectangle;
    private double? _selectedMinY;
    private double? _selectedMaxY;

    // Mini-map strip under the plot: clicking or dragging it jumps straight to that part
    // of the recording, instead of panning across the whole range.
    private double _trackTop;
    private bool _isScrubbing;

    public ChartView()
    {
        InitializeComponent();
        PlotCanvas.SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();
        // The dashboard stays alive (hidden) while another screen is open, so
        // charts skip drawing while invisible and catch up when shown again.
        IsVisibleChanged += (_, _) => Redraw();
        PlotCanvas.MouseMove += OnPlotMouseMove;
        PlotCanvas.MouseLeave += (_, _) => ClearOverlay();
        // Tunnelling, and on the control itself: a bubbling handler on the canvas only
        // fires if whatever sits under the cursor lets the event through, and the plot is
        // covered in curves, dots, bands and the hover chip.
        PreviewMouseWheel += OnPlotMouseWheel;
        PlotCanvas.MouseLeftButtonDown += OnPlotMouseDown;
        PlotCanvas.MouseLeftButtonUp += OnPlotMouseUp;
        PlotCanvas.MouseRightButtonDown += OnPlotRightMouseDown;
        PlotCanvas.MouseRightButtonUp += OnPlotRightMouseUp;
    }

    public static readonly DependencyProperty AllowZoomProperty = DependencyProperty.Register(
        nameof(AllowZoom), typeof(bool), typeof(ChartView), new PropertyMetadata(true));
    public bool AllowZoom { get => (bool)GetValue(AllowZoomProperty); set => SetValue(AllowZoomProperty, value); }

    public static readonly DependencyProperty ChartTitleProperty = DependencyProperty.Register(
        nameof(ChartTitle), typeof(string), typeof(ChartView), new PropertyMetadata("Graf"));
    public string ChartTitle { get => (string)GetValue(ChartTitleProperty); set => SetValue(ChartTitleProperty, value); }

    private void OnZoomClick(object sender, RoutedEventArgs e)
    {
        if (AllowZoom) ChartZoomWindow.Show(this, ChartTitle);
    }

    private void OnZoomInClick(object sender, RoutedEventArgs e) => ZoomAroundCentre(ButtonZoomStep);

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => ZoomAroundCentre(1 / ButtonZoomStep);

    private void OnZoomResetClick(object sender, RoutedEventArgs e) => ResetZoom();

    /// <summary>Restore both axes to the complete data range.</summary>
    public void ResetZoom()
    {
        if (!_viewport.IsZoomed && !_selectedMinY.HasValue)
        {
            return;
        }

        _viewport.Reset();
        _selectedMinY = _selectedMaxY = null;
        ClearOverlay();
        Redraw();
    }

    /// <summary>Zoom from the buttons keeps the middle of the current window in place.</summary>
    private void ZoomAroundCentre(double factor)
    {
        if (!_hasPlot || !_viewport.Zoom(factor, 0.5, _fullMinX, _fullMaxX))
        {
            return;
        }

        ClearOverlay();
        Redraw();
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

    public static readonly DependencyProperty MinimumYDecimalsProperty = DependencyProperty.Register(
        nameof(MinimumYDecimals), typeof(int), typeof(ChartView),
        new PropertyMetadata(0, OnVisualChanged));

    /// <summary>Minimum decimal places shown on every Y-axis label.</summary>
    public int MinimumYDecimals
    {
        get => (int)GetValue(MinimumYDecimalsProperty);
        set => SetValue(MinimumYDecimalsProperty, Math.Clamp(value, 0, 6));
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

    public static readonly DependencyProperty ShowStagesProperty = DependencyProperty.Register(
        nameof(ShowStages), typeof(bool), typeof(ChartView),
        new PropertyMetadata(false, OnVisualChanged));

    /// <summary>
    /// When <c>true</c>, annotates the primary (profile) series with its stages:
    /// a dot at every breakpoint, a subtle warm band over each flat "výdrž" (hold)
    /// sub-segment, and a ramp/hold label in the hover read-out. Enable it only for
    /// profile previews – live temperature/humidity charts leave it off.
    /// </summary>
    public bool ShowStages
    {
        get => (bool)GetValue(ShowStagesProperty);
        set => SetValue(ShowStagesProperty, value);
    }

    public static readonly DependencyProperty CycleStartXProperty = DependencyProperty.Register(
        nameof(CycleStartX), typeof(double), typeof(ChartView),
        new PropertyMetadata(double.NaN, OnVisualChanged));

    /// <summary>X value (same unit as the series, e.g. minutes) where the cycled region starts.</summary>
    public double CycleStartX { get => (double)GetValue(CycleStartXProperty); set => SetValue(CycleStartXProperty, value); }

    public static readonly DependencyProperty CycleEndXProperty = DependencyProperty.Register(
        nameof(CycleEndX), typeof(double), typeof(ChartView),
        new PropertyMetadata(double.NaN, OnVisualChanged));

    /// <summary>X value where the cycled region ends.</summary>
    public double CycleEndX { get => (double)GetValue(CycleEndXProperty); set => SetValue(CycleEndXProperty, value); }

    public static readonly DependencyProperty CycleCountProperty = DependencyProperty.Register(
        nameof(CycleCount), typeof(int), typeof(ChartView),
        new PropertyMetadata(1, OnVisualChanged));

    /// <summary>Repeat count; the cycle band is drawn only when &gt; 1.</summary>
    public int CycleCount { get => (int)GetValue(CycleCountProperty); set => SetValue(CycleCountProperty, value); }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ChartView)d).Redraw();

    private Brush AccentBrush => TryFindResource("AccentBrush") as Brush ?? Brushes.SteelBlue;

    private Brush MutedBrush => TryFindResource("MutedBrush") as Brush ?? Brushes.Gray;

    private Brush GridBrush => TryFindResource("BorderBrush") as Brush ?? Brushes.DimGray;

    private Brush HighlightBrush => TryFindResource("DangerBrush") as Brush ?? Brushes.IndianRed;

    private void Redraw()
    {
        // Live series are replaced frequently. Clearing the canvas while the operator
        // is drawing a zoom rectangle makes the selection flash and disappear under
        // the cursor. Keep the current pixels/transform frozen until mouse-up; the
        // newest Series value is then rendered together with the selected viewport.
        if (_isSelecting)
        {
            _redrawPendingDuringSelection = true;
            return;
        }
        _redrawPendingDuringSelection = false;

        if (!IsVisible)
        {
            return;
        }

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

        _fullMinX = series.Min(s => s.Points.Min(p => p.X));
        _fullMaxX = series.Max(s => s.Points.Max(p => p.X));
        if (_fullMaxX <= _fullMinX) _fullMaxX = _fullMinX + 1;

        _window = _viewport.Resolve(_fullMinX, _fullMaxX);
        ZoomStateText.Text = _window.IsZoomed || _selectedMinY.HasValue
            ? $"Priblíženie {_window.Zoom:0.#}×"
            : "Celý rozsah";
        double minX = _window.Min;
        double maxX = _window.Max;

        // The value axis spans the whole recording, not just the visible window: while
        // zoomed into a plateau a window-scaled axis showed a flat line in the middle of
        // a 59…61 °C axis, and there was no way to tell where the profile's real maximum
        // and minimum are.
        double visibleMinY = series.Min(s => s.Points.Min(p => p.Y));
        double visibleMaxY = series.Max(s => s.Points.Max(p => p.Y));
        double minY;
        double maxY;
        if (_selectedMinY is { } selectedMinY && _selectedMaxY is { } selectedMaxY)
        {
            minY = selectedMinY;
            maxY = selectedMaxY;
            _yAxis = new ValueAxis(minY, maxY, (maxY - minY) / 4, 4);
        }
        else if (double.IsNaN(YMin) && double.IsNaN(YMax))
        {
            // Rounded bounds keep the labels readable and hold the axis still while a
            // zoomed window is panned. Scale also picks how many gridlines there are, so
            // the axis crops close to the data instead of padding it out to four steps.
            _yAxis = NiceAxis.Scale(visibleMinY, visibleMaxY);
            minY = _yAxis.Min;
            maxY = _yAxis.Max;
        }
        else
        {
            minY = double.IsNaN(YMin) ? visibleMinY : YMin;
            maxY = double.IsNaN(YMax) ? visibleMaxY : YMax;
            _yAxis = new ValueAxis(minY, maxY, (maxY - minY) / 4, 4);
        }

        if (maxX <= minX) maxX = minX + 1;
        if (maxY <= minY)
        {
            maxY = minY + 1;
            minY -= 1;
            _yAxis = new ValueAxis(minY, maxY, (maxY - minY) / 4, 4);
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
        PlotCanvas.Cursor = Cursors.Cross;
        _hoverSeries = series.FirstOrDefault(s => !s.Dashed) ?? series[0];
        _hasPlot = true;

        // Cycled region (behind the series). The series already contains every repetition,
        // so the band is split into CycleCount equal slices with a divider and a "2/4"
        // caption on each – one flat band across the lot made the repeats impossible to
        // tell apart, which is exactly what a cycled profile needs to show.
        if (CycleCount > 1 && !double.IsNaN(CycleStartX) && !double.IsNaN(CycleEndX) && CycleEndX > CycleStartX)
        {
            double sliceSpan = (CycleEndX - CycleStartX) / CycleCount;
            for (int c = 0; c < CycleCount; c++)
            {
                double from = Math.Clamp(CycleStartX + (c * sliceSpan), minX, maxX);
                double to = Math.Clamp(CycleStartX + ((c + 1) * sliceSpan), minX, maxX);
                double sx1 = ToPx(from);
                double sx2 = ToPx(to);
                if (sx2 - sx1 <= 0)
                {
                    continue;
                }

                var band = new System.Windows.Shapes.Rectangle
                {
                    Width = sx2 - sx1,
                    Height = plotH,
                    // Alternating tint keeps the repetitions countable even when each is
                    // only a few pixels wide.
                    Fill = AccentBrush,
                    Opacity = c % 2 == 0 ? 0.16 : 0.08,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(band, sx1);
                Canvas.SetTop(band, PadTop);
                PlotCanvas.Children.Add(band);

                AddLine(sx1, PadTop, sx1, PadTop + plotH, AccentBrush, 1.5, dashed: true);
                if (sx2 - sx1 >= 44)
                {
                    // Along the bottom of the band – the top-left corner is the legend.
                    AddText($"⟲ {c + 1}/{CycleCount}", sx1 + 4, PadTop + plotH - 16, AccentBrush, 11);
                }
            }

            AddLine(ToPx(Math.Clamp(CycleEndX, minX, maxX)), PadTop,
                ToPx(Math.Clamp(CycleEndX, minX, maxX)), PadTop + plotH, AccentBrush, 1.5, dashed: true);
        }

        // Horizontal gridlines + Y labels.
        int gridSteps = Math.Max(1, _yAxis.Intervals);
        for (int i = 0; i <= gridSteps; i++)
        {
            double yVal = _yAxis.LabelAt(i);
            double py = ToPy(yVal);
            AddLine(PadLeft, py, PadLeft + plotW, py, GridBrush, 1, dashed: i is not 0 && i != gridSteps);
            int decimals = NiceAxis.RequiredDecimalPlaces(_yAxis.Step, MinimumYDecimals);
            string yLabel = yVal.ToString($"F{decimals}", CultureInfo.CurrentCulture);
            AddText($"{yLabel}{Unit}", 2, py - 8, MutedBrush, 10.5, PadLeft - 8, TextAlignment.Right);
        }

        // Time axis: a gridline on a readable step (quarter hours / hours / days,
        // depending on the window) with the elapsed time under each one. Only the two
        // ends used to be labelled, so nothing in between could be placed in time.
        _trackTop = PadTop + plotH + 17;
        double timeStep = NiceAxis.NiceTimeStep(maxX - minX);
        if (timeStep > 0)
        {
            for (double t = Math.Ceiling(minX / timeStep) * timeStep; t <= maxX + 1e-9; t += timeStep)
            {
                double gx = ToPx(t);
                AddLine(gx, PadTop, gx, PadTop + plotH, GridBrush, 1, dashed: true);
                AddText(FormatMinutesShort(t), gx - 40, PadTop + plotH + 3, MutedBrush, 10, 80, TextAlignment.Center);
            }
        }


        // Hold ("výdrž") bands – shaded time columns under every flat sub-segment
        // of the profile, so ramps (sloped, un-shaded) and holds read apart at a
        // glance. Drawn before the lines so the curve stays on top.
        if (ShowStages && _hoverSeries is { } stageSeries)
        {
            Brush holdBrush = TryFindResource("WarnBrush") as Brush ?? Brushes.Orange;
            IReadOnlyList<Point> sp = stageSeries.Points;
            for (int i = 1; i < sp.Count; i++)
            {
                Point a = sp[i - 1], b = sp[i];
                if (b.X <= a.X || Math.Abs(b.Y - a.Y) >= 0.05)
                {
                    continue;
                }

                double bx = ToPx(a.X);
                var band = new Rectangle
                {
                    Width = Math.Max(0, ToPx(b.X) - bx),
                    Height = plotH,
                    Fill = holdBrush,
                    Opacity = 0.12,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(band, bx);
                Canvas.SetTop(band, PadTop);
                PlotCanvas.Children.Add(band);
            }
        }

        // Series lines.
        foreach (ChartSeries s in series)
        {
            var poly = new Polyline
            {
                Stroke = s.Stroke,
                StrokeThickness = s.StrokeThickness,
                StrokeLineJoin = PenLineJoin.Round,
                Points = new PointCollection(s.Points.Select(p => new Point(ToPx(p.X), ToPy(p.Y)))),
                IsHitTestVisible = false,
            };
            if (s.Dashed)
            {
                poly.StrokeDashArray = s.StrokeThickness > 2
                    ? new DoubleCollection { 6, 2.5 }
                    : new DoubleCollection { 4, 3 };
            }

            PlotCanvas.Children.Add(poly);

            if (!string.IsNullOrWhiteSpace(s.PointLabel) && s.Points.Count == 1)
            {
                Point marker = s.Points[0];
                double markerX = ToPx(marker.X);
                double markerY = ToPy(marker.Y);
                var dot = new Ellipse
                {
                    Width = 10, Height = 10, Fill = s.Stroke, Stroke = Brushes.White,
                    StrokeThickness = 2, IsHitTestVisible = false,
                };
                Canvas.SetLeft(dot, markerX - 5);
                Canvas.SetTop(dot, markerY - 5);
                PlotCanvas.Children.Add(dot);

                double labelLeft = Math.Min(markerX + 8, PadLeft + plotW - 118);
                double labelTop = Math.Max(PadTop + 2, markerY - 24);
                AddText(s.PointLabel, labelLeft, labelTop, s.Stroke, 11, 112, TextAlignment.Left);
            }
        }

        // Breakpoint dots on the profile curve – one per segment boundary.
        if (ShowStages && _hoverSeries is { } dotSeries)
        {
            foreach (Point p in dotSeries.Points)
            {
                var dot = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = dotSeries.Stroke,
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(dot, ToPx(p.X) - 3);
                Canvas.SetTop(dot, ToPy(p.Y) - 3);
                PlotCanvas.Children.Add(dot);
            }
        }

        DrawZoomIndicator(plotW, plotH);

        // Keep the legend in one opaque overlay. Target and stability-limit lines often
        // run through this corner, so separate text elements without a background become
        // unreadable even though they are painted after the series.
        ChartSeries[] legendSeries = series
            .Where(item => string.IsNullOrWhiteSpace(item.PointLabel))
            .ToArray();
        if (legendSeries.Length > 0)
        {
            var legendRows = new StackPanel();
            foreach (ChartSeries s in legendSeries)
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var swatch = new Line
                {
                    X1 = 1,
                    X2 = 15,
                    Y1 = 7,
                    Y2 = 7,
                    Stroke = s.Stroke,
                    StrokeThickness = Math.Max(2, s.StrokeThickness),
                    IsHitTestVisible = false,
                };
                if (s.Dashed)
                {
                    swatch.StrokeDashArray = new DoubleCollection { 3, 2 };
                }

                var label = new TextBlock
                {
                    Text = s.Name,
                    Foreground = MutedBrush,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                };
                Grid.SetColumn(label, 1);
                row.Children.Add(swatch);
                row.Children.Add(label);
                legendRows.Children.Add(row);
            }

            var legend = new Border
            {
                Background = TryFindResource("SurfaceBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(11, 18, 32)),
                BorderBrush = GridBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 4, 8, 4),
                IsHitTestVisible = false,
                Child = legendRows,
            };
            Canvas.SetLeft(legend, PadLeft + 3);
            Canvas.SetTop(legend, PadTop + 2);
            PlotCanvas.Children.Add(legend);
        }
    }

    /// <summary>Mini-map strip + chip telling which slice of the data is shown.
    /// Nothing is drawn while the whole range is visible.</summary>
    private void DrawZoomIndicator(double plotW, double plotH)
    {
        double full = _fullMaxX - _fullMinX;
        if (!_window.IsZoomed || plotW <= 0 || full <= 0)
        {
            return;
        }

        var track = new Rectangle
        {
            Width = plotW, Height = 6, RadiusX = 3, RadiusY = 3,
            Fill = GridBrush, Opacity = 0.7,
            Cursor = Cursors.Hand,
            ToolTip = "Klikni alebo ťahaj – posunieš výrez po celom rozsahu",
        };
        Canvas.SetLeft(track, PadLeft);
        Canvas.SetTop(track, _trackTop);
        PlotCanvas.Children.Add(track);

        var thumb = new Rectangle
        {
            Width = Math.Max(8, _window.Span / full * plotW),
            Height = 6, RadiusX = 3, RadiusY = 3,
            Fill = AccentBrush, IsHitTestVisible = false,
        };
        Canvas.SetLeft(thumb, PadLeft + (_window.Min - _fullMinX) / full * plotW);
        Canvas.SetTop(thumb, _trackTop);
        PlotCanvas.Children.Add(thumb);

        var chip = new Border
        {
            Background = TryFindResource("SurfaceBrush") as Brush ?? Brushes.Black,
            BorderBrush = GridBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 1, 6, 1),
            Opacity = 0.92,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = $"🔍 {_window.Zoom:0.#}× · {FormatMinutesShort(_window.Min)} – {FormatMinutesShort(_window.Max)}"
                     + " · dvojklik = celý rozsah",
                Foreground = MutedBrush,
                FontSize = 10,
            },
        };
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, PadLeft + 2);
        Canvas.SetTop(chip, PadTop + plotH - chip.DesiredSize.Height - 2);
        PlotCanvas.Children.Add(chip);
    }

    // ===== Zoom / posun časovej osi =====

    private void OnPlotMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double plotW = PlotCanvas.ActualWidth - PadLeft - PadRight;
        if (!_hasPlot || plotW <= 0)
        {
            return;
        }

        // Scale by how far the wheel actually turned: one notch is 120, a trackpad
        // sends much smaller deltas and would otherwise jump a full step each time.
        double notches = Math.Clamp(e.Delta / 120d, -3, 3);

        // Shift + wheel scrolls along the time axis instead of zooming – the usual gesture
        // once only a slice of a long recording is visible.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (_window.IsZoomed &&
                _viewport.MoveTo(_window.Min - (notches * _window.Span * 0.25), _fullMinX, _fullMaxX))
            {
                ClearOverlay();
                Redraw();
            }

            e.Handled = _window.IsZoomed;
            return;
        }

        double factor = Math.Pow(ZoomStep, notches);

        double cursorX = Math.Clamp(e.GetPosition(PlotCanvas).X, PadLeft, PadLeft + plotW);
        if (!_viewport.Zoom(factor, (cursorX - PadLeft) / plotW, _fullMinX, _fullMaxX))
        {
            // Nothing left to zoom. While the chart is zoomed in, the gesture still belongs
            // to it – letting it bubble scrolled the whole dashboard out from under the
            // operator the moment they hit the zoom limit on a plateau.
            e.Handled = _window.IsZoomed;
            return;
        }

        // A wheel that both zooms and scrolls the surrounding page is unusable.
        e.Handled = true;
        ClearOverlay();
        Redraw();
    }

    private void OnPlotMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            if (_viewport.IsZoomed || _selectedMinY.HasValue)
            {
                _viewport.Reset();
                _selectedMinY = _selectedMaxY = null;
                ClearOverlay();
                Redraw();
                e.Handled = true;
            }

            return;
        }

        Point pos = e.GetPosition(PlotCanvas);
        if (IsOnTrack(pos))
        {
            _isScrubbing = true;
            ScrubTo(pos.X);
            PlotCanvas.CaptureMouse();
            ClearOverlay();
            e.Handled = true;
            return;
        }

        if (pos.X < PadLeft || pos.X > PadLeft + _plotW || pos.Y < PadTop || pos.Y > PadTop + _plotH)
            return;

        _isSelecting = true;
        _selectionStart = pos;
        _selectionRectangle = new Rectangle
        {
            Stroke = AccentBrush,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = AccentBrush,
            Opacity = 0.22,
            IsHitTestVisible = false,
        };
        PlotCanvas.Children.Add(_selectionRectangle);
        PlotCanvas.CaptureMouse();
        ClearOverlay();
        e.Handled = true;
    }

    /// <summary>True while the cursor is over the mini-map strip under the plot.</summary>
    private bool IsOnTrack(Point pos) =>
        _window.IsZoomed && pos.Y >= _trackTop - 6 && pos.Y <= _trackTop + 12 &&
        pos.X >= PadLeft && pos.X <= PadLeft + _plotW;

    /// <summary>Centres the visible window on the point of the mini-map that was clicked.</summary>
    private void ScrubTo(double x)
    {
        if (_plotW <= 0 || _fullMaxX <= _fullMinX)
        {
            return;
        }

        double fraction = Math.Clamp((x - PadLeft) / _plotW, 0, 1);
        double centre = _fullMinX + (fraction * (_fullMaxX - _fullMinX));
        if (_viewport.MoveTo(centre - (_window.Span / 2), _fullMinX, _fullMaxX))
        {
            Redraw();
        }
    }

    private void OnPlotMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isScrubbing)
        {
            _isScrubbing = false;
            PlotCanvas.ReleaseMouseCapture();
            return;
        }

        if (!_isSelecting)
        {
            return;
        }

        Point end = ClampToPlot(e.GetPosition(PlotCanvas));
        Point start = ClampToPlot(_selectionStart);
        _isSelecting = false;
        PlotCanvas.ReleaseMouseCapture();
        if (_selectionRectangle is not null) PlotCanvas.Children.Remove(_selectionRectangle);
        _selectionRectangle = null;

        if (Math.Abs(end.X - start.X) < 6 || Math.Abs(end.Y - start.Y) < 6)
        {
            if (_redrawPendingDuringSelection) Redraw();
            return;
        }

        double x1 = _minX + ((start.X - PadLeft) / _plotW * (_maxX - _minX));
        double x2 = _minX + ((end.X - PadLeft) / _plotW * (_maxX - _minX));
        double y1 = _maxY - ((start.Y - PadTop) / _plotH * (_maxY - _minY));
        double y2 = _maxY - ((end.Y - PadTop) / _plotH * (_maxY - _minY));
        _viewport.SelectRange(x1, x2, _fullMinX, _fullMaxX);
        _selectedMinY = Math.Min(y1, y2);
        _selectedMaxY = Math.Max(y1, y2);
        ClearOverlay();
        Redraw();
    }

    private Point ClampToPlot(Point point) => new(
        Math.Clamp(point.X, PadLeft, PadLeft + _plotW),
        Math.Clamp(point.Y, PadTop, PadTop + _plotH));

    private void OnPlotRightMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_window.IsZoomed) return;
        _isPanning = true;
        _panStartMouse = e.GetPosition(PlotCanvas);
        _panStartMin = _window.Min;
        PlotCanvas.CaptureMouse();
        PlotCanvas.Cursor = Cursors.SizeWE;
        ClearOverlay();
        e.Handled = true;
    }

    private void OnPlotRightMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        PlotCanvas.ReleaseMouseCapture();
        PlotCanvas.Cursor = Cursors.Cross;
        e.Handled = true;
    }

    private void AddLine(double x1, double y1, double x2, double y2, Brush brush, double thickness, bool dashed)
    {
        var line = new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = brush,
            StrokeThickness = thickness,
            Opacity = dashed ? 0.4 : 0.7,
            IsHitTestVisible = false,
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
            IsHitTestVisible = false,
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
        if (_isScrubbing)
        {
            ScrubTo(e.GetPosition(PlotCanvas).X);
            return;
        }

        if (_isPanning)
        {
            if (_plotW > 0 && _window.Span > 0)
            {
                double perPixel = _window.Span / _plotW;
                double shiftedMin = _panStartMin - (e.GetPosition(PlotCanvas).X - _panStartMouse.X) * perPixel;
                if (_viewport.MoveTo(shiftedMin, _fullMinX, _fullMaxX))
                {
                    Redraw();
                }
            }

            return;
        }

        if (_isSelecting && _selectionRectangle is not null)
        {
            Point current = ClampToPlot(e.GetPosition(PlotCanvas));
            Point start = ClampToPlot(_selectionStart);
            Canvas.SetLeft(_selectionRectangle, Math.Min(start.X, current.X));
            Canvas.SetTop(_selectionRectangle, Math.Min(start.Y, current.Y));
            _selectionRectangle.Width = Math.Abs(current.X - start.X);
            _selectionRectangle.Height = Math.Abs(current.Y - start.Y);
            return;
        }

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

        double PxOf(double x) => left + (x - _minX) / (_maxX - _minX) * _plotW;
        double PyOf(double y) => PadTop + (1 - (y - _minY) / (_maxY - _minY)) * _plotH;

        double px = PxOf(dataX);
        double py = PyOf(yv);

        ClearOverlay();
        Brush accent = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;

        // Highlight the whole step (ramp or hold) the cursor is inside, so it is
        // obvious which segment the read-out belongs to.
        (Point A, Point B)? piece = ShowStages ? PieceAt(_hoverSeries.Points, dataX) : null;
        if (piece is { } step)
        {
            double sx1 = PxOf(step.A.X);
            double sx2 = PxOf(step.B.X);
            double bandLeft = Math.Max(left, sx1);
            double bandRight = Math.Min(left + _plotW, sx2);
            var band = new Rectangle
            {
                Width = Math.Max(1, bandRight - bandLeft),
                Height = _plotH,
                Fill = HighlightBrush,
                Opacity = 0.18,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(band, bandLeft);
            Canvas.SetTop(band, PadTop);
            AddOverlay(band);

            // Edges of the step – skipped when they fall outside the zoomed window.
            foreach (double edge in new[] { sx1, sx2 })
            {
                if (edge < left || edge > left + _plotW)
                {
                    continue;
                }

                AddOverlay(new Line
                {
                    X1 = edge, Y1 = PadTop, X2 = edge, Y2 = PadTop + _plotH,
                    Stroke = HighlightBrush, StrokeThickness = 1.5, Opacity = 0.85,
                    IsHitTestVisible = false,
                });
            }

            // The step's own stretch of the curve, redrawn on top in the same colour.
            AddOverlay(new Line
            {
                X1 = sx1, Y1 = PyOf(step.A.Y), X2 = sx2, Y2 = PyOf(step.B.Y),
                Stroke = HighlightBrush, StrokeThickness = 3, IsHitTestVisible = false,
            });
        }

        AddOverlay(new Line
        {
            X1 = px, Y1 = PadTop, X2 = px, Y2 = PadTop + _plotH,
            Stroke = accent, StrokeThickness = 1, Opacity = 0.6,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            IsHitTestVisible = false,
        });

        var dot = new Ellipse
        {
            Width = 8, Height = 8, Fill = accent, Stroke = Brushes.White, StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(dot, px - 4);
        Canvas.SetTop(dot, py - 4);
        AddOverlay(dot);

        var chipContent = new StackPanel();
        // FBG wavelengths are stored in nm, where 0.001 nm is already 1 pm. Keep the
        // hover read-out at the same six-decimal precision as the live measurement
        // tables; axis labels may stay shorter so they do not overlap.
        int hoverMinimumDecimals = string.Equals(Unit.Trim(), "nm", StringComparison.OrdinalIgnoreCase)
            ? 6
            : MinimumYDecimals;
        int hoverDecimals = NiceAxis.RequiredDecimalPlaces(_yAxis.Step, hoverMinimumDecimals);
        string hoverValue = yv.ToString($"F{hoverDecimals}", CultureInfo.CurrentCulture);
        chipContent.Children.Add(new TextBlock
        {
            Text = $"{hoverValue}{Unit}  ·  {FormatMinutes(dataX)}",
            Foreground = Brushes.White,
            FontSize = 11,
            FontFamily = new FontFamily("Segoe UI Semibold"),
        });
        if (piece is { } hovered)
        {
            chipContent.Children.Add(new TextBlock
            {
                Text = $"{StageLabel(StageOf(hovered))} · dĺžka {FormatMinutesShort(hovered.B.X - hovered.A.X)}",
                Foreground = Brushes.White,
                FontSize = 10,
                Opacity = 0.9,
            });
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

    /// <summary>Ramp direction / hold at data-space X on the profile curve.</summary>
    private enum Stage { Rising, Falling, Hold }

    /// <summary>The stretch of the curve (one ramp or one hold) containing data-space X.</summary>
    private static (Point A, Point B)? PieceAt(IReadOnlyList<Point> pts, double x)
    {
        for (int i = 1; i < pts.Count; i++)
        {
            Point a = pts[i - 1], b = pts[i];
            if (b.X <= a.X)
            {
                continue; // skip zero-width jumps between segments
            }

            if (x >= a.X && x <= b.X)
            {
                return (a, b);
            }
        }

        return null;
    }

    private static Stage StageOf((Point A, Point B) piece)
    {
        double dy = piece.B.Y - piece.A.Y;
        if (Math.Abs(dy) < 0.05) return Stage.Hold;
        return dy > 0 ? Stage.Rising : Stage.Falling;
    }

    /// <summary>Compact length of a step: <c>45 min</c> / <c>2 h 15 min</c> / <c>1 d 3 h</c>.</summary>
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

    private static string StageLabel(Stage s) => s switch
    {
        Stage.Rising => "↗ Rampa (ohrev)",
        Stage.Falling => "↘ Rampa (chladenie)",
        _ => "→ Výdrž (plato)",
    };

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
