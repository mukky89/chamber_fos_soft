using System.Windows;
using System.Windows.Controls;

namespace VotschVc3.App.Views;

public partial class ProfileLibraryView : UserControl
{
    public ProfileLibraryView() => InitializeComponent();

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
