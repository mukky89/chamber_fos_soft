using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace VotschVc3.App.Views;

/// <summary>
/// Hotfix for the WPF ItemContainerGenerator race caused by the V7 channel-group refresh.
/// The previous implementation called DataGrid.UpdateLayout() while PeakLogger was still
/// publishing ObservableCollection changes. That can force generator verification between
/// CollectionChanged events and produce "ItemsControl is inconsistent with its items source".
///
/// Keep the visual grouping, but refresh only already-generated row containers after the
/// binding pipeline is idle. Never force layout from a CollectionChanged callback.
/// </summary>
internal static class CalibrationWindowItemsControlConsistencyHotfixV8Bootstrap
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

        // Defer until every Loaded class handler (including OperatorUxV7) has completed.
        // Background runs before V7's ContextIdle initial decoration pass, but after the
        // synchronous Loaded route, so replacing the CollectionChanged handler is reliable
        // regardless of module-initializer registration order.
        _ = window.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(window.InitializeItemsControlConsistencyHotfixV8));
    }
}

public partial class CalibrationWindow
{
    private bool _itemsControlConsistencyHotfixV8Initialized;
    private DispatcherOperation? _channelGroupRefreshV8;
    private int _channelGroupRefreshRetryV8;

    internal void InitializeItemsControlConsistencyHotfixV8()
    {
        if (_itemsControlConsistencyHotfixV8Initialized) return;
        _itemsControlConsistencyHotfixV8Initialized = true;

        // V7 subscribed a handler that ultimately calls UpdateLayout(). Remove it and use
        // a coalesced, generator-safe refresh instead.
        _viewModel.Peaks.CollectionChanged -= OnOperatorPeaksChangedV7;
        _viewModel.Peaks.CollectionChanged += OnOperatorPeaksChangedV8;
        Closed += OnItemsControlConsistencyHotfixV8Closed;

        QueueChannelGroupRefreshV8();
    }

    private void OnOperatorPeaksChangedV8(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Multiple PeakLogger add/remove events can arrive in one dispatcher turn. One visual
        // refresh after DataBind/Render is enough; do not touch the generator synchronously.
        QueueChannelGroupRefreshV8();
    }

    private void QueueChannelGroupRefreshV8()
    {
        if (_channelGroupRefreshV8 is { Status: DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing })
            return;

        _channelGroupRefreshV8 = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(RefreshGeneratedChannelGroupRowsV8));
    }

    private void RefreshGeneratedChannelGroupRowsV8()
    {
        _channelGroupRefreshV8 = null;
        if (_wiringGrid is null || !_wiringGrid.IsLoaded) return;

        ItemContainerGenerator generator = _wiringGrid.ItemContainerGenerator;
        if (generator.Status != GeneratorStatus.ContainersGenerated)
        {
            // The collection is still settling. Retry a few idle turns; LoadingRow continues
            // to decorate newly realized rows even if virtualization means generation never
            // reaches a globally complete state.
            if (_channelGroupRefreshRetryV8++ < 3)
                QueueChannelGroupRefreshV8();
            return;
        }

        _channelGroupRefreshRetryV8 = 0;

        // Important: no UpdateLayout(), no Refresh(), no ItemsSource reassignment here.
        // Touch only containers WPF has already generated.
        int count = _wiringGrid.Items.Count;
        for (int index = 0; index < count; index++)
        {
            if (generator.ContainerFromIndex(index) is DataGridRow row)
                ApplyChannelGroupBorderV7(row);
        }
    }

    private void OnItemsControlConsistencyHotfixV8Closed(object? sender, EventArgs e)
    {
        _viewModel.Peaks.CollectionChanged -= OnOperatorPeaksChangedV8;
        if (_channelGroupRefreshV8 is { Status: DispatcherOperationStatus.Pending })
            _channelGroupRefreshV8.Abort();
        _channelGroupRefreshV8 = null;
        Closed -= OnItemsControlConsistencyHotfixV8Closed;
    }
}
