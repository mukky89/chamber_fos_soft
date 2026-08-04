using System.Windows;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.Views;

/// <summary>Bulk profile export tool window (pick profiles, export to one JSON file).</summary>
public partial class BulkExportWindow : Window
{
    private readonly BulkExportViewModel _viewModel;

    private BulkExportWindow(ProfileStore store)
    {
        InitializeComponent();
        _viewModel = new BulkExportViewModel(store);
        DataContext = _viewModel;
    }

    /// <summary>Shows the tool modally; returns <c>true</c> if at least one export was written.</summary>
    public static bool Show(ProfileStore store)
    {
        var window = new BulkExportWindow(store);
        Window? owner = Application.Current?.MainWindow;
        if (owner is not null && owner.IsVisible && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.ShowDialog();
        return window._viewModel.ExportedAnything;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
