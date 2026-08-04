using System;
using System.Windows;
using System.Windows.Input;

namespace VotschVc3.App.Views;

/// <summary>
/// Dark-themed password prompt. Verifies the typed password with a caller-supplied
/// check and only closes as confirmed when it matches.
/// </summary>
public partial class PasswordDialog : Window
{
    private readonly Func<string, bool> _verify;

    private PasswordDialog(string title, string message, string confirmText, Func<string, bool> verify)
    {
        InitializeComponent();
        _verify = verify;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    /// <summary>True when the user entered a password that passed the verification.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>
    /// Shows the modal prompt; returns <c>true</c> only if the entered password passed
    /// <paramref name="verify"/>.
    /// </summary>
    public static bool Ask(string message, Func<string, bool> verify,
        string title = "Potvrď heslom", string confirmText = "Potvrdiť")
    {
        var dialog = new PasswordDialog(title, message, confirmText, verify);
        Window? owner = Application.Current?.MainWindow;
        if (owner is not null && owner.IsVisible && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        return dialog.Confirmed;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_verify(PasswordInput.Password))
        {
            Confirmed = true;
            DialogResult = true;
            Close();
            return;
        }

        ErrorText.Text = "Nesprávne heslo.";
        ErrorText.Visibility = Visibility.Visible;
        PasswordInput.Clear();
        PasswordInput.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.Enter)
        {
            Confirm_Click(sender, e);
        }
    }
}
