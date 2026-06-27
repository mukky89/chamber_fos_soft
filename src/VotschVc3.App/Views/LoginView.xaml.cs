using System.Windows.Controls;
using System.Windows.Input;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();

    private void Login_Click(object sender, System.Windows.RoutedEventArgs e) => TryLogin();

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryLogin();
            e.Handled = true;
        }
    }

    private void TryLogin()
    {
        if (DataContext is LoginViewModel vm)
        {
            vm.TryLogin(PasswordBox.Password);
        }
    }
}
