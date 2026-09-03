using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace VotschVc3.App.Views;

/// <summary>
/// The source profile's hold duration is intentionally irrelevant for FBG calibration points.
/// Remove that column from the calibration workspace at object level rather than trying to hide it
/// later by localized header text. This also works when the tab has not been visually realized yet.
/// </summary>
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
    }

    private static void RemoveDurationColumns(DependencyObject root)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DataGrid grid && IsCalibrationPointsGrid(grid))
            {
                foreach (DataGridColumn column in grid.Columns.ToArray())
                {
                    if (column is not DataGridBoundColumn bound) continue;
                    if (bound.Binding is Binding binding &&
                        string.Equals(binding.Path?.Path, "Duration", StringComparison.Ordinal))
                    {
                        grid.Columns.Remove(column);
                    }
                }
            }

            if (child is DependencyObject dependencyObject)
                RemoveDurationColumns(dependencyObject);
        }
    }

    private static bool IsCalibrationPointsGrid(DataGrid grid)
    {
        Binding? itemsBinding = BindingOperations.GetBinding(grid, ItemsControl.ItemsSourceProperty);
        return string.Equals(itemsBinding?.Path?.Path, "CalibrationPoints", StringComparison.Ordinal);
    }
}
