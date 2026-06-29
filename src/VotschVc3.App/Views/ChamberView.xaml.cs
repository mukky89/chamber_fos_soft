using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

public partial class ChamberView : UserControl
{
    private ChamberViewModel? _viewModel;

    public ChamberView() => InitializeComponent();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.TerminalLines.CollectionChanged -= OnTerminalLinesChanged;
        }

        _viewModel = e.NewValue as ChamberViewModel;

        if (_viewModel is not null)
        {
            _viewModel.TerminalLines.CollectionChanged += OnTerminalLinesChanged;

            // The humidity profile column is only relevant for humidity chambers.
            if (HumidityColumn is not null)
            {
                HumidityColumn.Visibility = _viewModel.SupportsHumidity ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void OnTerminalLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }

        // Defer the scroll instead of scrolling synchronously here. ScrollIntoView forces an
        // immediate layout pass, and at this point the ListBox's ItemContainerGenerator has not
        // necessarily processed this same CollectionChanged event yet. Forcing layout mid-event
        // makes the VirtualizingStackPanel verify the generator against a collection that is one
        // item ahead, throwing "An ItemsControl is inconsistent with its items source." Running on
        // the dispatcher lets every CollectionChanged listener catch up before we scroll.
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () =>
            {
                if (_viewModel is { } vm && vm.TerminalLines.Count > 0)
                {
                    TerminalList.ScrollIntoView(vm.TerminalLines[^1]);
                }
            });
    }

    private void TerminalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel?.SendTerminalCommand.CanExecute(null) == true)
        {
            _viewModel.SendTerminalCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ToggleMaximize_Click(object sender, RoutedEventArgs e)
    {
        Window? window = Window.GetWindow(this);
        if (window is not null)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }
}
