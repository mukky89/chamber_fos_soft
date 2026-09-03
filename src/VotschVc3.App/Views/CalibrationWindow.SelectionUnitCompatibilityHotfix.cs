using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace VotschVc3.App.Views;

/// <summary>
/// Compatibility hotfix for the production wiring grid.
/// Operator UX selects individual cells, while the older new-peak autofocus path still assigns
/// DataGrid.SelectedItem before moving CurrentCell. WPF throws when SelectionUnit is Cell only.
/// CellOrRowHeader preserves cell-centric editing/selection while allowing that legacy row selection
/// assignment to coexist safely until the autofocus path is consolidated.
/// </summary>
internal static class CalibrationWindowSelectionUnitCompatibilityHotfix
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

        _ = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            foreach (DataGrid grid in FindVisualChildren<DataGrid>(window))
            {
                Binding? itemsBinding = BindingOperations.GetBinding(grid, ItemsControl.ItemsSourceProperty);
                if (!string.Equals(itemsBinding?.Path?.Path, "Peaks", StringComparison.Ordinal)) continue;

                grid.SelectionUnit = DataGridSelectionUnit.CellOrRowHeader;
                grid.SelectionMode = DataGridSelectionMode.Extended;
            }
        }));
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is not Visual && root is not System.Windows.Media.Media3D.Visual3D)
            yield break;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (T nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }
}
