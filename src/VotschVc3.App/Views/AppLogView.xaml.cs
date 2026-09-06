using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace VotschVc3.App.Views;

public partial class AppLogView : UserControl
{
    private ScrollViewer? _scrollViewer;
    private INotifyCollectionChanged? _items;
    private double _anchorOffset;
    private int _pendingLeadingRows;
    private bool _anchorRestoreScheduled;

    public AppLogView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_items is not null)
            _items.CollectionChanged -= OnItemsChanged;
        _scrollViewer = FindDescendant<ScrollViewer>(LogGrid);
        _items = LogGrid.Items as INotifyCollectionChanged;
        if (_items is not null)
            _items.CollectionChanged += OnItemsChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_items is not null)
            _items.CollectionChanged -= OnItemsChanged;
        _items = null;
        _scrollViewer = null;
        _pendingLeadingRows = 0;
        _anchorRestoreScheduled = false;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // New log records are inserted at index 0. When the operator is reading older rows,
        // compensate for those inserted rows so the same record stays under the mouse.
        if (_scrollViewer is null || _scrollViewer.VerticalOffset <= 0 ||
            e.Action != NotifyCollectionChangedAction.Add || e.NewStartingIndex != 0)
            return;

        if (!_anchorRestoreScheduled)
        {
            _anchorOffset = _scrollViewer.VerticalOffset;
            _anchorRestoreScheduled = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RestoreScrollAnchor);
        }

        _pendingLeadingRows += e.NewItems?.Count ?? 1;
    }

    private void RestoreScrollAnchor()
    {
        if (_scrollViewer is not null && _pendingLeadingRows > 0)
            _scrollViewer.ScrollToVerticalOffset(_anchorOffset + _pendingLeadingRows);
        _pendingLeadingRows = 0;
        _anchorRestoreScheduled = false;
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;
            if (FindDescendant<T>(child) is { } nested)
                return nested;
        }
        return null;
    }
}
