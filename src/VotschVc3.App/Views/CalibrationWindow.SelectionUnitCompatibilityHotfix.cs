using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace VotschVc3.App.Views;

/// <summary>
/// Compatibility hotfix for the production wiring grid.
/// Keeps the production wiring grid on one consistent full-row selection mode after every older
/// initialization layer has run. The current cell still controls one-click editing, while the
/// selected row remains visibly highlighted across its complete width.
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
                if (!string.Equals(itemsBinding?.Path?.Path, "PeaksView", StringComparison.Ordinal)) continue;

                grid.SelectionUnit = DataGridSelectionUnit.FullRow;
                grid.SelectionMode = DataGridSelectionMode.Single;
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
