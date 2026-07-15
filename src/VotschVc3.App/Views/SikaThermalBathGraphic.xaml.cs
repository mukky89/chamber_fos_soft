using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VotschVc3.App.Views;

/// <summary>
/// Scalable vector graphic of a SIKA TP Premium calibration bath / dry block:
/// a red cabinet with the top cooling-fan grille, a small touchscreen showing
/// the current reference temperature, and the "ext. Ref." connector / I/O
/// panel from the real unit. When <see cref="IsRunning"/> is <c>false</c> the
/// cabinet is washed dark and the fan stops spinning, mirroring the idle look
/// of <see cref="PolEkoGraphic"/>.
/// </summary>
public partial class SikaThermalBathGraphic : UserControl
{
    private readonly DoubleAnimation _fanSpin = new()
    {
        From = 0,
        To = 360,
        Duration = new Duration(TimeSpan.FromSeconds(1.6)),
        RepeatBehavior = RepeatBehavior.Forever,
    };

    public SikaThermalBathGraphic()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateRunningState();
    }

    public static readonly DependencyProperty TitleTextProperty = DependencyProperty.Register(
        nameof(TitleText), typeof(string), typeof(SikaThermalBathGraphic),
        new PropertyMetadata("SIKA TP"));

    /// <summary>Model label shown under the reading.</summary>
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

    /// <summary>When <c>false</c> the bath is greyed out and the fan stops.</summary>
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

        if (IsRunning)
        {
            FanRotate.BeginAnimation(RotateTransform.AngleProperty, _fanSpin);
        }
        else
        {
            // Remove the animation and hold the current angle so the fan visibly stops.
            FanRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }
}
