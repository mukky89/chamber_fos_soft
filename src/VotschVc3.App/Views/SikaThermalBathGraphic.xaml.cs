using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VotschVc3.App.Views;

/// <summary>
/// Scalable vector graphic of the SIKA TP Premium dry-block calibrator drawn
/// after the real unit: red cabinet with a black front panel, carrying handle,
/// a bent reference probe in the top calibration well, and the touchscreen
/// showing the live reference temperature over a trend chart. While
/// <see cref="IsRunning"/> is <c>true</c> the trace endpoint pulses and the
/// front panel renders as a deep, high-contrast black (a clear "really
/// online" signal); when <c>false</c> the front panel fades to a washed-out
/// grey and the whole cabinet gets a dark veil, mirroring the idle look of
/// <see cref="PolEkoGraphic"/>.
/// </summary>
public partial class SikaThermalBathGraphic : UserControl
{
    private readonly DoubleAnimation _tracePulse = new()
    {
        From = 1.0,
        To = 0.2,
        Duration = new Duration(TimeSpan.FromSeconds(0.9)),
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever,
    };

    /// <summary>Deep, high-contrast black used for the front panel while online.</summary>
    private static readonly Brush OnlineFrontPanelBrush = CreateFrozenBrush("#26262B", "#08080A");

    /// <summary>Washed-out, low-contrast grey used for the front panel while offline –
    /// makes the "real black" look above stand out as an online-only signal.</summary>
    private static readonly Brush OfflineFrontPanelBrush = CreateFrozenBrush("#55555C", "#3A3A40");

    private static Brush CreateFrozenBrush(string topColor, string bottomColor)
    {
        var brush = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(topColor),
            (Color)ColorConverter.ConvertFromString(bottomColor),
            new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
    }

    public SikaThermalBathGraphic()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateRunningState();
    }

    public static readonly DependencyProperty TitleTextProperty = DependencyProperty.Register(
        nameof(TitleText), typeof(string), typeof(SikaThermalBathGraphic),
        new PropertyMetadata("SIKA TP"));

    /// <summary>Model label shown in the touchscreen title bar.</summary>
    public string TitleText
    {
        get => (string)GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public static readonly DependencyProperty TemperatureTextProperty = DependencyProperty.Register(
        nameof(TemperatureText), typeof(string), typeof(SikaThermalBathGraphic),
        new PropertyMetadata("—"));

    /// <summary>Temperature value shown on the touchscreen (unit "°C" is appended by the view).</summary>
    public string TemperatureText
    {
        get => (string)GetValue(TemperatureTextProperty);
        set => SetValue(TemperatureTextProperty, value);
    }

    public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(
        nameof(IsRunning), typeof(bool), typeof(SikaThermalBathGraphic),
        new PropertyMetadata(true, OnIsRunningChanged));

    /// <summary>When <c>false</c> the bath is greyed out and the trace pulse stops.</summary>
    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    private static void OnIsRunningChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SikaThermalBathGraphic)d).UpdateRunningState();

    private void UpdateRunningState()
    {
        if (!IsLoaded)
        {
            return;
        }

        IdleVeil.Visibility = IsRunning ? Visibility.Collapsed : Visibility.Visible;
        RunDot.Opacity = IsRunning ? 1.0 : 0.25;
        FrontPanel.Fill = IsRunning ? OnlineFrontPanelBrush : OfflineFrontPanelBrush;
        FrontPanel.Stroke = IsRunning ? Brushes.Black : new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x44));

        if (IsRunning)
        {
            TraceDot.BeginAnimation(OpacityProperty, _tracePulse);
        }
        else
        {
            // Remove the animation and hold full opacity so the dot visibly freezes.
            TraceDot.BeginAnimation(OpacityProperty, null);
            TraceDot.Opacity = 1.0;
        }
    }
}
