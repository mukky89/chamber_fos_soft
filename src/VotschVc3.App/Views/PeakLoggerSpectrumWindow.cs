using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VotschVc3.App.Calibration;

namespace VotschVc3.App.Views;

/// <summary>Lightweight read-only spectrum viewer opened from the wiring grid context menu.</summary>
public sealed class PeakLoggerSpectrumWindow : Window
{
    private readonly Canvas _canvas = new();
    private IReadOnlyList<PeakLoggerSpectrumPoint> _points;
    private readonly Func<Task<IReadOnlyList<PeakLoggerSpectrumPoint>>>? _refresh;
    private readonly DispatcherTimer? _timer;
    private readonly TextBlock _status = new() { Opacity = 0.7, Margin = new Thickness(0, 3, 0, 0) };
    private readonly double? _focusWavelengthNm;
    private bool _zoomToPeak;

    public PeakLoggerSpectrumWindow(
        string channel,
        string? deviceSerialNumber,
        IReadOnlyList<PeakLoggerSpectrumPoint> points,
        Func<Task<IReadOnlyList<PeakLoggerSpectrumPoint>>>? refresh = null,
        double? focusWavelengthNm = null)
    {
        _points = points.OrderBy(p => p.WavelengthNm).ToArray();
        _refresh = refresh;
        _focusWavelengthNm = focusWavelengthNm;
        Title = $"PeakLogger spektrum · kanál {channel}";
        Width = 920;
        Height = 560;
        MinWidth = 650;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(new TextBlock
        {
            Text = $"Kanál {channel}" + (string.IsNullOrWhiteSpace(deviceSerialNumber) ? string.Empty : $" · PeakLogger {deviceSerialNumber}"),
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
        });
        header.Children.Add(_status);
        UpdateStatus("Načítané spektrum");
        if (_focusWavelengthNm is not null)
        {
            var zoom = new Button { Content = "Zoom na peak", Margin = new Thickness(0, 7, 0, 0), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left };
            zoom.Click += (_, _) => { _zoomToPeak = !_zoomToPeak; zoom.Content = _zoomToPeak ? "Celé spektrum" : "Zoom na peak"; Draw(); };
            header.Children.Add(zoom);
        }
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var border = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = SystemColors.ControlDarkBrush,
            Padding = new Thickness(10),
            Child = _canvas,
        };
        Grid.SetRow(border, 1);
        root.Children.Add(border);
        Content = root;

        if (_refresh is not null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _timer.Tick += async (_, _) => await RefreshAsync();
            Closed += (_, _) => _timer.Stop();
            Loaded += async (_, _) => await RefreshAsync();
            _timer.Start();
        }

        _canvas.SizeChanged += (_, _) => Draw();
        Loaded += (_, _) => Draw();
    }

    private async Task RefreshAsync()
    {
        if (_refresh is null) return;
        try
        {
            IReadOnlyList<PeakLoggerSpectrumPoint> points = await _refresh();
            _points = points.OrderBy(p => p.WavelengthNm).ToArray();
            UpdateStatus($"Živé spektrum · {_points.Count} bodov · {_points.FirstOrDefault()?.WavelengthNm:F3} – {_points.LastOrDefault()?.WavelengthNm:F3} nm · {DateTime.Now:HH:mm:ss}");
            Draw();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Chyba načítania spektra: {ex.Message}");
        }
    }

    private void UpdateStatus(string prefix) => _status.Text = _points.Count == 0
        ? prefix
        : $"{prefix} · {_points.Count} bodov · {_points.First().WavelengthNm:F3} – {_points.Last().WavelengthNm:F3} nm";

    private void Draw()
    {
        _canvas.Children.Clear();
        if (_points.Count < 2 || _canvas.ActualWidth < 100 || _canvas.ActualHeight < 100) return;

        double left = 58;
        double right = 18;
        double top = 18;
        double bottom = 38;
        double width = Math.Max(1, _canvas.ActualWidth - left - right);
        double height = Math.Max(1, _canvas.ActualHeight - top - bottom);

        double minX = _points.Min(p => p.WavelengthNm);
        double maxX = _points.Max(p => p.WavelengthNm);
        if (_zoomToPeak && _focusWavelengthNm is { } focus)
        {
            double halfWindow = 2.5;
            minX = Math.Max(minX, focus - halfWindow);
            maxX = Math.Min(maxX, focus + halfWindow);
        }
        var visible = _points.Where(p => p.WavelengthNm >= minX && p.WavelengthNm <= maxX).ToArray();
        if (visible.Length >= 2) _points = _points.ToArray();
        double minY = _points.Min(p => p.Intensity);
        double maxY = _points.Max(p => p.Intensity);
        if (Math.Abs(maxX - minX) < 1e-12) maxX = minX + 1;
        if (Math.Abs(maxY - minY) < 1e-12) maxY = minY + 1;

        Brush axisBrush = SystemColors.ControlDarkBrush;
        Brush curveBrush = SystemColors.HighlightBrush;
        _canvas.Children.Add(new Line { X1 = left, X2 = left, Y1 = top, Y2 = top + height, Stroke = axisBrush, StrokeThickness = 1 });
        _canvas.Children.Add(new Line { X1 = left, X2 = left + width, Y1 = top + height, Y2 = top + height, Stroke = axisBrush, StrokeThickness = 1 });

        var polyline = new Polyline { Stroke = curveBrush, StrokeThickness = 1.5 };
        foreach (PeakLoggerSpectrumPoint point in _points.Where(p => p.WavelengthNm >= minX && p.WavelengthNm <= maxX))
        {
            double x = left + (point.WavelengthNm - minX) / (maxX - minX) * width;
            double y = top + height - (point.Intensity - minY) / (maxY - minY) * height;
            polyline.Points.Add(new Point(x, y));
        }
        _canvas.Children.Add(polyline);
        if (_focusWavelengthNm is { } marker && marker >= minX && marker <= maxX)
        {
            double x = left + (marker - minX) / (maxX - minX) * width;
            _canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = top, Y2 = top + height, Stroke = Brushes.OrangeRed, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 } });
            AddLabel($"peak {marker:F3}", Math.Max(left, x - 30), top + 2, HorizontalAlignment.Left);
        }

        AddLabel(minX.ToString("F3", CultureInfo.InvariantCulture), left, top + height + 5, HorizontalAlignment.Left);
        AddLabel(maxX.ToString("F3", CultureInfo.InvariantCulture), left + width - 55, top + height + 5, HorizontalAlignment.Right);
        AddLabel(maxY.ToString("F2", CultureInfo.InvariantCulture), 0, top - 7, HorizontalAlignment.Left);
        AddLabel(minY.ToString("F2", CultureInfo.InvariantCulture), 0, top + height - 12, HorizontalAlignment.Left);
        AddLabel("λ [nm]", left + width / 2 - 25, top + height + 20, HorizontalAlignment.Center);
    }

    private void AddLabel(string text, double x, double y, HorizontalAlignment alignment)
    {
        var label = new TextBlock { Text = text, FontSize = 11, Opacity = 0.75, HorizontalAlignment = alignment };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        _canvas.Children.Add(label);
    }
}
