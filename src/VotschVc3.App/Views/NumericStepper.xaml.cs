using System.Windows;
using System.Windows.Controls;

namespace VotschVc3.App.Views;

/// <summary>
/// A small numeric field with ▲ / ▼ nudge buttons. Two-way bind <see cref="Value"/>
/// and set <see cref="Step"/> (and optionally <see cref="Minimum"/>/<see cref="Maximum"/>)
/// to add small increments – e.g. °C for a temperature or minutes/hours for a time.
/// </summary>
public partial class NumericStepper : UserControl
{
    public NumericStepper() => InitializeComponent();

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(NumericStepper),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>The current value (two-way bound by default).</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(NumericStepper), new PropertyMetadata(1.0));

    /// <summary>How much each ▲ / ▼ press adds or removes.</summary>
    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(NumericStepper), new PropertyMetadata(double.MinValue));

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(NumericStepper), new PropertyMetadata(double.MaxValue));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    private void Up_Click(object sender, RoutedEventArgs e) => Nudge(+1);

    private void Down_Click(object sender, RoutedEventArgs e) => Nudge(-1);

    private void Nudge(int direction)
    {
        double next = Value + (direction * Step);
        next = Math.Max(Minimum, Math.Min(Maximum, next));
        Value = Math.Round(next, 3);
    }
}
