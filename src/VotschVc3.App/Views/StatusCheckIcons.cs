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
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(string), typeof(StatusCheckIcons), new PropertyMetadata(null, SourceChanged));
    public static string GetSource(DependencyObject target) => (string)target.GetValue(SourceProperty);
    public static void SetSource(DependencyObject target, string value) => target.SetValue(SourceProperty, value);
    private static readonly DependencyProperty BusyProperty = DependencyProperty.RegisterAttached(
        "Busy", typeof(bool), typeof(StatusCheckIcons), new PropertyMetadata(false));
    private static readonly DependencyPropertyDescriptor TextDescriptor =
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))!;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock, object> Observed = new();
    private static System.Windows.Threading.DispatcherTimer? _discovery;
    public static void Initialize()
    {
        if (_discovery is not null) return;
        _discovery = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _discovery.Tick += (_, _) => RefreshOpenWindows();
        _discovery.Start();
    }
    public static void RefreshOpenWindows()
    {
        if (Application.Current is null) return;
        foreach (PresentationSource source in PresentationSource.CurrentSources)
            if (source.RootVisual is { } root) Observe(root);
    }
    private static void Observe(DependencyObject element)
    {
        if (element is TextBlock text)
        {
            if (!Observed.TryGetValue(text, out _))
            {
                Observed.Add(text, new object());
                text.Loaded += Loaded;
                text.Unloaded += Unloaded;
                if (text.IsLoaded) Loaded(text, new RoutedEventArgs());
            }
            return; // Inlines are renderer-owned, not separate status sources.
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            Observe(VisualTreeHelper.GetChild(element, i));
    }    private static void Loaded(object sender, RoutedEventArgs args)
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
        if (!value.Any(IsIcon)) return;
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
                if (!IsIcon(value[i])) continue;
                if (i > start) text.Inlines.Add(new Run(value[start..i]));
                double size = Math.Max(14, text.FontSize + 2);
                var icon = CreateIcon(value[i], size);
                text.Inlines.Add(new InlineUIContainer(icon) { BaselineAlignment = BaselineAlignment.Center });
                start = i + 1;
                if (start < value.Length && value[start] == '\uFE0F') { i++; start++; }
            }
            if (start < value.Length) text.Inlines.Add(new Run(value[start..]));
        }
        finally { text.SetValue(BusyProperty, false); }
    }
    private static bool IsIcon(char value) => value is '✓' or '✔' or '✅' or '⚠' or '❌' or '❎' or 'ℹ' or '▶' or '►' or '⏸' or 'Ⅱ' or '⏹' or '✎' or '↻' or '⟳';
    private static FrameworkElement CreateIcon(char marker, double size)
    {
        var (label, color, geometry) = marker switch
        {
            '⚠' => ("Upozornenie", "#CB891C", "M6,2 L6,7 M6,9 L6,10"),
            '❌' or '❎' => ("Chyba", "#D84459", "M3,3 L9,9 M9,3 L3,9"),
            'ℹ' => ("Informácia", "#287FCC", "M6,2 L6,3 M6,5 L6,10"),
            '▶' or '►' => ("Spustiť", "#14855B", "M3,2 L10,6 L3,10 Z"),
            '⏸' or 'Ⅱ' => ("Pozastavené / čaká", "#B87B13", "M4,2 L4,10 M8,2 L8,10"),
            '⏹' => ("Stop", "#D84459", "M3,3 L9,3 L9,9 L3,9 Z"),
            '✎' => ("Upraviť", "#287FCC", "M2,10 L3,7 L8,2 L10,4 L5,9 Z M7,3 L9,5"),
            '↻' or '⟳' => ("Obnoviť", "#7960CA", "M9,4 A4,4 0 1 0 10,7 M7,4 L10,4 L10,1"),
            _ => ("Splnené", "#14855B", "M2,6 L5,9 L10,3"),
        };
        var icon = new Grid { Width = size, Height = size, Margin = new Thickness(3, 0, 3, 0), IsHitTestVisible = false };
        icon.Children.Add(new Ellipse { Fill = (Brush)new BrushConverter().ConvertFromString(color)! });
        // Keep a fixed viewbox so thin information/pause glyphs do not stretch out of proportion.
        var drawing = new Canvas { Width = 12, Height = 12 };
        drawing.Children.Add(new Path { Data = Geometry.Parse(geometry), Stroke = Brushes.White,
            StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round });
        icon.Children.Add(new Viewbox { Child = drawing, Margin = new Thickness(size * .18) });
        System.Windows.Automation.AutomationProperties.SetName(icon, label);
        return icon;
    }}
