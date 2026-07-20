using System.Windows;
using System.Windows.Input;

namespace VotschVc3.App.Views;

/// <summary>What the user chose in the <see cref="ExitDialog"/>.</summary>
public enum ExitChoice
{
    /// <summary>Keep running – do nothing.</summary>
    Cancel,

    /// <summary>Hide the window to the tray, keep the app running.</summary>
    MinimizeToTray,

    /// <summary>Really close the application.</summary>
    Exit,
}

/// <summary>
/// Dark-themed confirmation shown before the app is closed. Offers three clear
/// options – exit, hide to tray or cancel – instead of the plain system MessageBox.
/// </summary>
public partial class ExitDialog : Window
{
    public ExitDialog() => InitializeComponent();

    /// <summary>The user's choice; valid once <see cref="Window.ShowDialog"/> returns.</summary>
    public ExitChoice Choice { get; private set; } = ExitChoice.Cancel;

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
            Finish(ExitChoice.Cancel);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Finish(ExitChoice.Exit);

    private void Tray_Click(object sender, RoutedEventArgs e) => Finish(ExitChoice.MinimizeToTray);

    private void Cancel_Click(object sender, RoutedEventArgs e) => Finish(ExitChoice.Cancel);

    private void Finish(ExitChoice choice)
    {
        Choice = choice;
        DialogResult = true;
        Close();
    }
}
