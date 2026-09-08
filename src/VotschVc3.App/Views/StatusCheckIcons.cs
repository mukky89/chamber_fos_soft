using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VotschVc3.App.Views;

/// <summary>Render legacy success markers as consistent vector icons, including bound and live text.</summary>
public static class StatusCheckIcons
{
    private static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(string), typeof(StatusCheckIcons), new PropertyMetadata(null, SourceChanged));
    private static readonly DependencyProperty BusyProperty = DependencyProperty.RegisterAttached(
        "Busy", typeof(bool), typeof(StatusCheckIcons), new PropertyMetadata(false));
    private static readonly DependencyPropertyDescriptor TextDescriptor =
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))!;
    public static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(TextBlock), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(Loaded));
        EventManager.RegisterClassHandler(typeof(TextBlock), FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(Unloaded));
    }
    private static void Loaded(object sender, RoutedEventArgs args)
    {
        var text = (TextBlock)sender;
        TextDescriptor.RemoveValueChanged(text, TextChanged);
        TextDescriptor.AddValueChanged(text, TextChanged);
        TextChanged(text, EventArgs.Empty);
    }
    private static void Unloaded(object sender, RoutedEventArgs args) =>
        TextDescriptor.RemoveValueChanged((TextBlock)sender, TextChanged);
    private static void SourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        Render((TextBlock)sender, args.NewValue as string ?? "");
    private static void TextChanged(object? sender, EventArgs args)
    {
        if (sender is not TextBlock text || (bool)text.GetValue(BusyProperty)) return;
        string value = text.Text;
        if (value.IndexOfAny(['✓', '✔', '✅']) < 0) return;
        var binding = BindingOperations.GetBindingBase(text, TextBlock.TextProperty);
        if (binding is not null)
        {
            text.SetValue(BusyProperty, true);
            BindingOperations.ClearBinding(text, TextBlock.TextProperty);
            BindingOperations.SetBinding(text, SourceProperty, binding);
            text.SetValue(BusyProperty, false);
        }
        Render(text, value);
    }
    private static void Render(TextBlock text, string value)
    {
        if ((bool)text.GetValue(BusyProperty)) return;
        text.SetValue(BusyProperty, true);
        try
        {
            text.Inlines.Clear();
            int start = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] is not ('✓' or '✔' or '✅')) continue;
                if (i > start) text.Inlines.Add(new Run(value[start..i]));
                double size = Math.Max(12, text.FontSize);
                var icon = new Grid { Width = size, Height = size, Margin = new Thickness(2, 0, 2, 0),
                    ToolTip = "Splnené", IsHitTestVisible = false };
                icon.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromRgb(30, 139, 100)) });
                icon.Children.Add(new Path { Data = Geometry.Parse("M 3,6 L 5,8 L 9,3"),
                    Stroke = Brushes.White, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                    Stretch = Stretch.Uniform, Margin = new Thickness(size * .23) });
                System.Windows.Automation.AutomationProperties.SetName(icon, "Splnené");
                text.Inlines.Add(new InlineUIContainer(icon) { BaselineAlignment = BaselineAlignment.Center });
                start = i + 1;
            }
            if (start < value.Length) text.Inlines.Add(new Run(value[start..]));
        }
        finally { text.SetValue(BusyProperty, false); }
    }
}
