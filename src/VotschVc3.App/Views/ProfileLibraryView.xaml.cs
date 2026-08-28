using System.Windows;
using System.Windows.Controls;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.Views;

public partial class ProfileLibraryView : UserControl
{
    public ProfileLibraryView() => InitializeComponent();

    /// <summary>
    /// Selecting a profile leaf in the tree shows its preview on the right; selecting a
    /// sensor group node is ignored (the group has nothing to preview).
    /// </summary>
    private void ProfileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TestProfile profile && DataContext is ProfileLibraryViewModel vm)
        {
            vm.SelectedHistoryProfile = profile;
        }
    }
}
