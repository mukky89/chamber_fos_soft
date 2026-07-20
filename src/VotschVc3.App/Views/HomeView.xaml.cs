using System.Windows.Controls;
using System.Windows.Input;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();

    /// <summary>Exit button: routes to the window's confirmed-exit flow (the only real way out).</summary>
    private void Exit_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (System.Windows.Window.GetWindow(this) is MainWindow window)
        {
            window.RequestExit();
        }
    }

    // Unlock uses a PasswordBox (Password can't be data-bound), so the value is
    // read here and handed to the chamber's view model. Each card has its own
    // PasswordBox in the DataTemplate namescope; sender/Tag resolve the right one.
    private void UnlockPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is PasswordBox box)
        {
            Unlock(box);
            e.Handled = true;
        }
    }

    private void Unlock_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is Button { Tag: PasswordBox box })
        {
            Unlock(box);
        }
    }

    private static void Unlock(PasswordBox box)
    {
        if (box.DataContext is ChamberViewModel vm)
        {
            vm.TryUnlock(box.Password);
            box.Clear();
        }
    }
}
