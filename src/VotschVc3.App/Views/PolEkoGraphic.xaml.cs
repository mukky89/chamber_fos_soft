using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VotschVc3.App.Views;

/// <summary>
/// Scalable vector graphic of a POL-EKO SMART drying oven (SLN series): a
/// stainless-steel cabinet with a top touchscreen that shows the current
/// temperature. When <see cref="IsRunning"/> is <c>false</c> the cabinet is
/// washed to grey and the screen's status dot dims, mirroring the idle look of
/// <see cref="ChamberGraphic"/>.
/// </summary>
public partial class PolEkoGraphic : UserControl
{
    private readonly DoubleAnimation _fanSpin = new()
    {
        From = 0,
        To = 360,
        Duration = new Duration(TimeSpan.FromSeconds(2.4)),
        RepeatBehavior = RepeatBehavior.Forever,
    };

    public PolEkoGraphic()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateRunningState();
    }

    public static readonly DependencyProperty TitleTextProperty = DependencyProperty.Register(
        nameof(TitleText), typeof(string), typeof(PolEkoGraphic),
        new PropertyMetadata("SLN 115"));

    /// <summary>Model label shown on the door.</summary>
    public string TitleText
    {
        get => (string)GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public static readonly DependencyProperty TemperatureTextProperty = DependencyProperty.Register(
        nameof(TemperatureText), typeof(string), typeof(PolEkoGraphic),
        new PropertyMetadata("—"));

    /// <summary>Temperature value shown on the touchscreen (unit "°C" is appended by the view).</summary>
    public string TemperatureText
    {
        get => (string)GetValue(TemperatureTextProperty);
        set => SetValue(TemperatureTextProperty, value);
    }

    public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(
        nameof(IsRunning), typeof(bool), typeof(PolEkoGraphic),
        new PropertyMetadata(true, OnIsRunningChanged));

    /// <summary>When <c>false</c> the oven is greyed out and the status dot dims.</summary>
    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    private static void OnIsRunningChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PolEkoGraphic)d).UpdateRunningState();

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
