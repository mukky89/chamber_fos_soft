using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace VotschVc3.App.Views;

internal static class CalibrationWindowHideProfileDurationColumnBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(CalibrationWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded),
            handledEventsToo: true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not CalibrationWindow window) return;
        RemoveDurationColumns(window);

        foreach (TabControl tabs in FindVisualChildren<TabControl>(window))
        {
            tabs.SelectionChanged -= Tabs_SelectionChanged;
            tabs.SelectionChanged += Tabs_SelectionChanged;
        }
    }

    private static void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl tabs) return;
        _ = tabs.Dispatcher.BeginInvoke(new Action(() => RemoveDurationColumns(tabs)));
    }

    private static void RemoveDurationColumns(DependencyObject root)
    {
        // Logical-tree objects such as RowDefinition/GridLength helpers are DependencyObjects but
        // are not Visual/Visual3D. VisualTreeHelper throws InvalidOperationException when called on
        // those objects, so visual traversal must be explicitly guarded.
        if (root is Visual or Visual3D)
        {
            foreach (DataGrid grid in FindVisualChildren<DataGrid>(root))
                RemoveDurationColumn(grid);
        }

        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DataGrid grid)
                RemoveDurationColumn(grid);

            if (child is DependencyObject dependencyObject)
                RemoveDurationColumns(dependencyObject);
        }
    }

    private static void RemoveDurationColumn(DataGrid grid)
    {
        if (!IsCalibrationPointsGrid(grid)) return;
        foreach (DataGridColumn column in grid.Columns.ToArray())
        {
            if (column is DataGridBoundColumn bound &&
                bound.Binding is Binding binding &&
                string.Equals(binding.Path?.Path, "Duration", StringComparison.Ordinal))
            {
                grid.Columns.Remove(column);
            }
        }
    }

    private static bool IsCalibrationPointsGrid(DataGrid grid)
    {
        Binding? itemsBinding = BindingOperations.GetBinding(grid, ItemsControl.ItemsSourceProperty);
        return string.Equals(itemsBinding?.Path?.Path, "CalibrationPoints", StringComparison.Ordinal);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is not Visual && root is not Visual3D)
            yield break;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
                yield return typed;

            foreach (T nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }
}
