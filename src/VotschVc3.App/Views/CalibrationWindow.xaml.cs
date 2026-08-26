using System.ComponentModel;
using System.Windows;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

public partial class CalibrationWindow : Window
{
    private readonly CalibrationViewModel _viewModel = new();
    private bool _disposing;

    public CalibrationWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_disposing)
        {
            return;
        }

        // Keep the window alive until the long-running calibration resources have
        // been cancelled/disposed. Then remove this handler and perform the real close.
        e.Cancel = true;
        _disposing = true;
        Closing -= OnClosing;
        try
        {
            await _viewModel.DisposeAsync();
        }
        finally
        {
            Close();
        }
    }
}
