using System.Windows;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.Views;

/// <summary>
/// Bulk profile import tool window (multi-file import, rename and standardise).
/// </summary>
public partial class BulkImportWindow : Window
{
    private readonly BulkImportViewModel _viewModel;

    private BulkImportWindow(ProfileStore store)
    {
        InitializeComponent();
        _viewModel = new BulkImportViewModel(store);
        DataContext = _viewModel;
    }

    /// <summary>
    /// Shows the tool modally over the main window and returns <c>true</c> when at
    /// least one profile was imported (so the caller refreshes the library).
    /// </summary>
    public static bool Show(ProfileStore store)
    {
        var window = new BulkImportWindow(store);
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
        return window._viewModel.ImportedAnything;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
