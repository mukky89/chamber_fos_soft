using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace VotschVc3.App.Views;

public partial class CalibrationWindow
{
    private TabItem? _overviewTab;
    private TabItem? _settingsTab;
    private DispatcherTimer? _dashboardTimer;

    // Run after the existing workspace builders have completed their ContextIdle work.
    private void InitializeDashboardLayout()
    {
        if (_overviewTab is not null || _productionTabs is null) return;
        _viewModel.RefreshDashboardPlan();
        _overviewTab = new TabItem
        {
            Header = "Prehľad",
            Content = new CalibrationDashboardView { DataContext = _viewModel.Dashboard }
        };
        _productionTabs.Items.Insert(0, _overviewTab);

        // Move setup out of the operator's live viewport, preserving the original controls.
        DependencyObject? content = Content as DependencyObject;
        while (content is ScrollViewer scroll) content = scroll.Content as DependencyObject;
        if (content is Grid root)
        {
            var footer = root.Children.Cast<UIElement>().FirstOrDefault(c => Grid.GetRow(c) == 4);
            if (footer is not null)
            {
                footer.Visibility = Visibility.Collapsed;
                _productionTabs.SelectionChanged += (_, _) => footer.Visibility =
                    ReferenceEquals(_productionTabs.SelectedItem, _overviewTab) ? Visibility.Collapsed : Visibility.Visible;
            }
            var setup = new StackPanel();
            foreach (UIElement child in root.Children.Cast<UIElement>().Where(c => Grid.GetRow(c) is 1 or 2).ToArray())
            {
                root.Children.Remove(child);
                setup.Children.Add(child);
            }
            var configuration = new TabControl();
            configuration.Items.Add(new TabItem { Header = "Zariadenia", Content = new ScrollViewer { Content = setup, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            foreach (TabItem item in _productionTabs.Items.OfType<TabItem>().Where(t => HeaderText(t.Header) == "Nastavenia stability").ToArray())
            {
                _productionTabs.Items.Remove(item);
                configuration.Items.Add(item);
            }
            _settingsTab = new TabItem { Header = "Nastavenia", Content = configuration };
            _productionTabs.Items.Add(_settingsTab);
            if (root.RowDefinitions.Count > 3) root.RowDefinitions[3].Height = new GridLength(1, GridUnitType.Star);
            if (Content is ScrollViewer outer) { outer.Content = null; Content = root; }
        }
        _productionTabs.Height = double.NaN;
        _productionTabs.MinHeight = 0;

        // Charts get their own focused page; raw diagnostics remain reachable.
        if (_fbgPeakChartsPanel?.Parent is StackPanel chartStack && chartStack.Parent is Border card && card.Parent is DockPanel dock)
        {
            dock.Children.Remove(card);
            _productionTabs.Items.Insert(1, new TabItem
            {
                Header = "Live dáta",
                Content = new ScrollViewer { Content = card, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            });
        }
        if (_liveMonitorTab is not null)
        {
            _liveMonitorTab.Header = "Diagnostika";
            if (_liveMonitorTab.Content is UIElement diagnostic && diagnostic is not ScrollViewer)
            {
                _liveMonitorTab.Content = null;
                _liveMonitorTab.Content = new ScrollViewer { Content = diagnostic, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            }
        }
        var history = _productionTabs.Items.OfType<TabItem>().FirstOrDefault(t => HeaderText(t.Header) == "História");
        var data = _productionTabs.Items.OfType<TabItem>().FirstOrDefault(t => HeaderText(t.Header) == "Dáta");
        if (history is not null && data is not null)
        {
            _productionTabs.Items.Remove(data);
            object existing = history.Content;
            history.Content = null;
            var results = new TabControl();
            results.Items.Add(new TabItem { Header = "Výsledky a export", Content = existing });
            results.Items.Add(data);
            history.Header = "História / výsledky";
            history.Content = results;
        }
        foreach (TabItem item in _productionTabs.Items.OfType<TabItem>())
            if (HeaderText(item.Header) == "Kalibračné plata") item.Header = "Kalibračný plán";
        var wiring = _productionTabs.Items.OfType<TabItem>().FirstOrDefault(t => HeaderText(t.Header) == "Zapojenie");
        if (wiring is not null)
        {
            _productionTabs.Items.Remove(wiring);
            _productionTabs.Items.Insert(0, wiring);
        }
        if (_settingsTab is not null)
        {
            _productionTabs.Items.Remove(_settingsTab);
            _productionTabs.Items.Insert(0, _settingsTab);
        }
        _productionTabs.SelectedItem = _viewModel.IsRunning ? _overviewTab : _settingsTab ?? _overviewTab;
        _dashboardTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _dashboardTimer.Tick += DashboardTick;
        _dashboardTimer.Start();
        _viewModel.PropertyChanged += DashboardPropertyChanged;
        IsVisibleChanged += DashboardVisibilityChanged;
        Closed += DashboardClosed;
    }
    private void DashboardTick(object? sender, EventArgs e)
    {
        if (!_viewModel.IsRunning) _viewModel.RefreshDashboardPlan();
        _viewModel.Dashboard.Tick(DateTimeOffset.Now);
        RefreshFbgLiveTraceCharts();
    }
    private void DashboardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.CalibrationViewModel.WarningText))
            _viewModel.Dashboard.Warn(_viewModel.WarningText, DateTimeOffset.Now);
    }
    private void DashboardVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            if (_productionTabs is not null)
                _productionTabs.SelectedItem = _viewModel.IsRunning ? _overviewTab : _settingsTab ?? _overviewTab;
            DashboardTick(null, EventArgs.Empty);
            _dashboardTimer?.Start();
        }
        else _dashboardTimer?.Stop();
    }
    private void DashboardClosed(object? sender, EventArgs e)
    {
        if (_dashboardTimer is not null) { _dashboardTimer.Stop(); _dashboardTimer.Tick -= DashboardTick; }
        _viewModel.PropertyChanged -= DashboardPropertyChanged;
        IsVisibleChanged -= DashboardVisibilityChanged;
        Closed -= DashboardClosed;
    }
}
