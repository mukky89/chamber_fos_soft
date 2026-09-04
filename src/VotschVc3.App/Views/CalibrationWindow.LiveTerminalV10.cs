using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace VotschVc3.App.Views;

internal static class CalibrationWindowLiveTerminalV10Bootstrap
{
    [ModuleInitializer]
    internal static void Initialize() => EventManager.RegisterClassHandler(
        typeof(CalibrationWindow), FrameworkElement.LoadedEvent,
        new RoutedEventHandler((sender, _) => ((CalibrationWindow)sender).InitializeLiveTerminalV10()), true);
}

public partial class CalibrationWindow
{
    private bool _liveTerminalV10Initialized;
    private ListBox? _liveTerminalV10;
    private Border? _liveTerminalCardV10;
    private Button? _liveTerminalToggleV10;
    private ScrollViewer? _liveTerminalScrollV10;

    internal void InitializeLiveTerminalV10()
    {
        if (_liveTerminalV10Initialized) return;
        _liveTerminalV10Initialized = true;
        Closed += OnLiveTerminalV10Closed;
        _viewModel.CalibrationTerminalLines.CollectionChanged += OnLiveTerminalLinesChangedV10;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(AttachLiveTerminalV10));
    }

    private void AttachLiveTerminalV10()
    {
        if (_liveTerminalV10 is not null) return;
        if (_overviewTab?.Content is not CalibrationDashboardView dashboard ||
            dashboard.Content is not ScrollViewer dashboardScroll ||
            dashboardScroll.Content is not StackPanel dashboardContent)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(AttachLiveTerminalV10));
            return;
        }

        _liveTerminalV10 = new ListBox
        {
            ItemsSource = _viewModel.CalibrationTerminalLines,
            Background = new SolidColorBrush(Color.FromRgb(8, 12, 18)),
            Foreground = new SolidColorBrush(Color.FromRgb(166, 227, 161)),
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 11.5,
            Padding = new Thickness(8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        _liveTerminalCardV10 = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(8, 12, 18)),
            BorderBrush = TryFindResource("BorderBrush") as Brush ?? Brushes.DimGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(12, 4, 12, 10),
            Height = 190,
            Visibility = Visibility.Collapsed,
            Tag = "CALIBRATION_LIVE_TERMINAL_END",
            Child = new DockPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "  LIVE TERMINÁL KALIBRÁCIE  ·  posledných 500 udalostí",
                        Foreground = new SolidColorBrush(Color.FromRgb(139, 180, 250)),
                        Background = new SolidColorBrush(Color.FromRgb(16, 23, 34)),
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                        FontWeight = FontWeights.SemiBold,
                        Padding = new Thickness(8, 6, 8, 6),
                    },
                    _liveTerminalV10,
                },
            },
        };
        DockPanel.SetDock(((DockPanel)_liveTerminalCardV10.Child).Children[0], Dock.Top);

        _liveTerminalToggleV10 = new Button
        {
            Content = "Zobraziť live terminál",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 4, 12, 10),
            Padding = new Thickness(14, 7, 14, 7),
            MinWidth = 172,
            Style = TryFindResource("AccentOutlineButton") as Style,
            ToolTip = "Zobrazí alebo skryje podrobný diagnostický výpis kalibrácie.",
        };
        _liveTerminalToggleV10.Click += ToggleLiveTerminalV10;
        var terminalSection = new StackPanel
        {
            Children = { _liveTerminalToggleV10, _liveTerminalCardV10 },
        };

        // The dashboard owns its vertical ScrollViewer. Appending here places the terminal after
        // every chart/card. It stays collapsed by default and never moves the page on new output.
        dashboardContent.Children.Add(terminalSection);
    }

    private void ToggleLiveTerminalV10(object sender, RoutedEventArgs e)
    {
        if (_liveTerminalCardV10 is null || _liveTerminalToggleV10 is null) return;
        bool show = _liveTerminalCardV10.Visibility != Visibility.Visible;
        _liveTerminalCardV10.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _liveTerminalToggleV10.Content = show ? "Skryť live terminál" : "Zobraziť live terminál";
        if (show)
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ScrollLiveTerminalInternallyV10));
    }

    private void OnLiveTerminalLinesChangedV10(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_liveTerminalV10 is null || _liveTerminalCardV10?.Visibility != Visibility.Visible ||
            _viewModel.CalibrationTerminalLines.Count == 0) return;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ScrollLiveTerminalInternallyV10));
    }

    private void ScrollLiveTerminalInternallyV10()
    {
        if (_liveTerminalV10 is null || _liveTerminalCardV10?.Visibility != Visibility.Visible) return;
        _liveTerminalV10.UpdateLayout();
        _liveTerminalScrollV10 ??= FindLiveTerminalDescendantV10<ScrollViewer>(_liveTerminalV10);
        _liveTerminalScrollV10?.ScrollToEnd();
    }

    private static T? FindLiveTerminalDescendantV10<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindLiveTerminalDescendantV10<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private void OnLiveTerminalV10Closed(object? sender, EventArgs e)
    {
        _viewModel.CalibrationTerminalLines.CollectionChanged -= OnLiveTerminalLinesChangedV10;
        if (_liveTerminalToggleV10 is not null)
            _liveTerminalToggleV10.Click -= ToggleLiveTerminalV10;
        Closed -= OnLiveTerminalV10Closed;
    }
}
