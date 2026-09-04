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
        if (_overviewTab?.Content is not UIElement dashboard)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(AttachLiveTerminalV10));
            return;
        }

        _overviewTab.Content = null;
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
        var terminalCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(8, 12, 18)),
            BorderBrush = TryFindResource("BorderBrush") as Brush ?? Brushes.DimGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(12, 4, 12, 10),
            Height = 190,
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
        DockPanel.SetDock(((DockPanel)terminalCard.Child).Children[0], Dock.Top);

        var layout = new DockPanel();
        DockPanel.SetDock(terminalCard, Dock.Bottom);
        layout.Children.Add(terminalCard);
        layout.Children.Add(dashboard);
        _overviewTab.Content = layout;
    }

    private void OnLiveTerminalLinesChangedV10(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_liveTerminalV10 is null || _viewModel.CalibrationTerminalLines.Count == 0) return;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            _liveTerminalV10?.ScrollIntoView(_viewModel.CalibrationTerminalLines[^1])));
    }

    private void OnLiveTerminalV10Closed(object? sender, EventArgs e)
    {
        _viewModel.CalibrationTerminalLines.CollectionChanged -= OnLiveTerminalLinesChangedV10;
        Closed -= OnLiveTerminalV10Closed;
    }
}
