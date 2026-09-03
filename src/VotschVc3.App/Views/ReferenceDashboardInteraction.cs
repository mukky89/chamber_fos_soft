using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

/// <summary>
/// One interaction hook for both Classic and Professional dynamically-injected reference metrics.
/// Clicking Referencia opens its live WIKA trace without coupling either dashboard layout to the
/// chart window implementation.
/// </summary>
internal static class ReferenceDashboardInteraction
{
    private const string ClassicTag = "CTH7000_REFERENCE_METRIC";
    private const string ProfessionalTag = "CTH7000_REFERENCE_METRIC_PRO";

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(UIElement),
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnMouseUp),
            handledEventsToo: false);
        EventManager.RegisterClassHandler(
            typeof(UIElement),
            UIElement.MouseEnterEvent,
            new MouseEventHandler(OnMouseEnter),
            handledEventsToo: false);
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not DependencyObject node) return;
        FrameworkElement? metric = FindReferenceMetric(node);
        if (metric is not null) metric.Cursor = Cursors.Hand;
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        FrameworkElement? metric = FindReferenceMetric(source);
        if (metric?.DataContext is not ChamberViewModel chamber) return;

        var window = new ReferenceTemperatureChartWindow(chamber.Id, chamber.Name)
        {
            Owner = Window.GetWindow(metric),
        };
        window.Show();
        e.Handled = true;
    }

    private static FrameworkElement? FindReferenceMetric(DependencyObject? start)
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is FrameworkElement element)
            {
                string? tag = element.Tag?.ToString();
                if (string.Equals(tag, ClassicTag, StringComparison.Ordinal) ||
                    string.Equals(tag, ProfessionalTag, StringComparison.Ordinal))
                    return element;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : null;
        }
        return null;
    }
}
