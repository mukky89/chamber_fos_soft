using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VotschVc3.App.Views;

/// <summary>
/// WPF nested controls (DataGrid, ScrollViewer, ComboBox templates) can consume a wheel event even
/// when their own vertical extent cannot move. After the application regains foreground focus this
/// was especially visible as a page that occasionally appeared to stop scrolling. Route each wheel
/// event to the nearest scrollable vertical ScrollViewer under the pointer; when that viewer is at
/// its edge, fall through to the next scrollable parent.
/// </summary>
internal static class ReliableMouseWheelScrolling
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheel),
            handledEventsToo: true);
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Window window || !window.IsVisible) return;
        if (e.OriginalSource is not DependencyObject source) return;

        // An open ComboBox owns the wheel for its popup list.
        ComboBox? combo = FindAncestor<ComboBox>(source);
        if (combo?.IsDropDownOpen == true) return;

        int direction = Math.Sign(e.Delta);
        if (direction == 0) return;

        foreach (ScrollViewer viewer in EnumerateScrollViewers(source))
        {
            if (viewer.ScrollableHeight <= 0.5) continue;
            bool canMove = direction > 0
                ? viewer.VerticalOffset > 0.5
                : viewer.VerticalOffset < viewer.ScrollableHeight - 0.5;
            if (!canMove) continue;

            double lines = Math.Max(1, SystemParameters.WheelScrollLines);
            double delta = Math.Max(34d, viewer.ViewportHeight / 12d) * lines;
            double target = direction > 0
                ? viewer.VerticalOffset - delta
                : viewer.VerticalOffset + delta;
            viewer.ScrollToVerticalOffset(Math.Clamp(target, 0d, viewer.ScrollableHeight));
            e.Handled = true;
            return;
        }
    }

    private static IEnumerable<ScrollViewer> EnumerateScrollViewers(DependencyObject source)
    {
        var yielded = new HashSet<ScrollViewer>();
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is ScrollViewer viewer && yielded.Add(viewer)) yield return viewer;
            current = GetParent(current);
        }
    }

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is T typed) return typed;
            current = GetParent(current);
        }
        return null;
    }

    private static DependencyObject? GetParent(DependencyObject value)
    {
        if (value is Visual or System.Windows.Media.Media3D.Visual3D)
            return VisualTreeHelper.GetParent(value);
        return LogicalTreeHelper.GetParent(value);
    }
}
