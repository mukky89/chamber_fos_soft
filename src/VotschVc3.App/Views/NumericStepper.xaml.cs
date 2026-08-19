using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VotschVc3.App.Views;

/// <summary>
/// A small numeric field with ▲ / ▼ nudge buttons. Two-way bind <see cref="Value"/>
/// and set <see cref="Step"/> (and optionally <see cref="Minimum"/>/<see cref="Maximum"/>)
/// to add small increments – e.g. °C for a temperature or minutes/hours for a time.
/// The text box commits every valid keystroke straight to <see cref="Value"/> (so a
/// bound chart/preview updates live while typing) without reformatting the text the
/// user is mid-typing; it only canonicalises on <c>Enter</c> or losing focus.
/// </summary>
public partial class NumericStepper : UserControl
{
    private bool _suppressTextSync;

    public NumericStepper()
    {
        InitializeComponent();
        ValueBox.TextChanged += (_, _) => TryCommitFromText();
        ValueBox.LostFocus += (_, _) => SyncTextFromValue();
        ValueBox.PreviewKeyDown += ValueBox_PreviewKeyDown;
        SyncTextFromValue();
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(NumericStepper),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

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

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var stepper = (NumericStepper)d;
        // Only reformat the box for a change that came from outside (the bound view
        // model, or the ▲/▼ buttons) – a change we just pushed from the text box
        // itself (see TryCommitFromText) must not rewrite the text mid-keystroke.
        if (!stepper._suppressTextSync)
        {
            stepper.SyncTextFromValue();
        }
    }

    /// <summary>Reformats the text box from the current <see cref="Value"/>. Used on
    /// load, after an external value change, and on losing focus – which canonicalises
    /// whatever the user left typed (e.g. "22,50" -&gt; "22.5").</summary>
    private void SyncTextFromValue()
    {
        string formatted = Value.ToString("0.###", CultureInfo.InvariantCulture);
        if (ValueBox.Text != formatted)
        {
            ValueBox.Text = formatted;
        }
    }

    /// <summary>
    /// Parses the current text – accepting either "," or "." as the decimal separator,
    /// regardless of the control's Language/culture – and, if it is a complete valid
    /// number, pushes it straight into <see cref="Value"/> without touching the text
    /// box. This is what makes a bound chart/preview update live while typing: an
    /// incomplete value (e.g. "-", "22,", empty) is simply left alone until the user
    /// finishes it, rather than being rejected or reformatted mid-keystroke.
    /// </summary>
    /// <returns>True if the text currently parses to a valid number.</returns>
    private bool TryCommitFromText()
    {
        string text = ValueBox.Text.Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return false;
        }

        parsed = Math.Clamp(parsed, Minimum, Maximum);
        if (Math.Abs(parsed - Value) > 1e-9)
        {
            _suppressTextSync = true;
            Value = parsed;
            _suppressTextSync = false;
        }

        return true;
    }

    /// <summary>
    /// Enter is a safety net: force-applies and canonicalises the current text even in
    /// an edge case where typing didn't already commit it (e.g. text pasted in one go).
    /// </summary>
    private void ValueBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        TryCommitFromText();
        SyncTextFromValue();
        ValueBox.SelectAll();
        e.Handled = true;
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
