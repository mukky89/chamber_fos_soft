using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VotschVc3.App.Views;

/// <summary>
/// Scalable vector graphic of a Vötsch test chamber with an interior fan that
/// spins while the chamber is running. Used both on the home page cards and the
/// chamber detail header. When <see cref="IsRunning"/> is <c>false</c> the fan
/// stops and the cabinet is washed to grey to show the chamber is idle.
/// </summary>
public partial class ChamberGraphic : UserControl
{
    private readonly DoubleAnimation _fanSpin = new()
    {
        From = 0,
        To = 360,
        Duration = new Duration(TimeSpan.FromSeconds(2.4)),
        RepeatBehavior = RepeatBehavior.Forever,
    };

    public ChamberGraphic()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateRunningState();
    }

    public static readonly DependencyProperty TitleTextProperty = DependencyProperty.Register(
        nameof(TitleText), typeof(string), typeof(ChamberGraphic),
        new PropertyMetadata("VT³ 7060"));

    /// <summary>Model label shown on the cabinet.</summary>
    public string TitleText
    {
        get => (string)GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(ChamberGraphic),
        new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x2B))));

    /// <summary>Accent colour for the door frame, louvers and brand text.</summary>
    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(
        nameof(IsRunning), typeof(bool), typeof(ChamberGraphic),
        new PropertyMetadata(true, OnIsRunningChanged));

    /// <summary>When <c>true</c> the fan spins; when <c>false</c> the chamber is greyed out.</summary>
    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    private static void OnIsRunningChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ChamberGraphic)d).UpdateRunningState();

    private void UpdateRunningState()
    {
        if (!IsLoaded)
        {
            // The animation needs the visual tree; the Loaded handler re-applies this.
            return;
        }

        if (IsRunning)
        {
            IdleVeil.Visibility = Visibility.Collapsed;
            FanRotate.BeginAnimation(RotateTransform.AngleProperty, _fanSpin);
        }
        else
        {
            // Remove the animation and hold the current angle; grey out the cabinet.
            FanRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            IdleVeil.Visibility = Visibility.Visible;
        }
    }
}
