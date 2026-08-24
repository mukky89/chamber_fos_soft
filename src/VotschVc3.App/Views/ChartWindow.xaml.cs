using System.Windows;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

/// <summary>
/// Full-screen view of a chamber's profile chart (⛶ on the dashboard card).
/// Binds straight to the chamber's <see cref="ChamberViewModel.ProfilePreview"/>,
/// so the curve and the "now" marker keep updating live while the window is open.
/// </summary>
public partial class ChartWindow : Window
{
    public ChartWindow(ChamberViewModel chamber)
    {
        InitializeComponent();
        DataContext = chamber ?? throw new ArgumentNullException(nameof(chamber));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
