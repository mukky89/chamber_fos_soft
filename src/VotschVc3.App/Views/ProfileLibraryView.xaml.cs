using System.Windows;
using System.Windows.Controls;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.Views;

public partial class ProfileLibraryView : UserControl
{
    public ProfileLibraryView() => InitializeComponent();

    /// <summary>
    /// Selecting a profile leaf in the tree loads it into the editor (the view model's
    /// <c>SelectedHistoryProfile</c> setter auto-applies it, matching the old list).
    /// Selecting a sensor group node is ignored.
    /// </summary>
    private void ProfileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TestProfile profile && DataContext is ProfileLibraryViewModel vm)
        {
            vm.SelectedHistoryProfile = profile;
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
