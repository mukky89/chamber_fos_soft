using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        // Keep the terminal scrolled to the newest line.
        _viewModel.TerminalLines.CollectionChanged += OnTerminalLinesChanged;
        Closed += OnClosed;
    }

    private void OnTerminalLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && _viewModel.TerminalLines.Count > 0)
        {
            TerminalList.ScrollIntoView(_viewModel.TerminalLines[^1]);
        }
    }

    private void TerminalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel.SendTerminalCommand.CanExecute(null))
        {
            _viewModel.SendTerminalCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void OnClosed(object? sender, EventArgs e) => await _viewModel.DisposeAsync();
}
