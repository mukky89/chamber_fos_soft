using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace VotschVc3.App.Views;

/// <summary>
/// Small dark-themed Yes/No confirmation dialog matching the app style, used instead
/// of the plain system MessageBox for destructive actions (e.g. deleting a profile).
/// </summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string message, string title, string confirmText, bool danger)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;

        if (!danger)
        {
            // Neutral (accent) look for non-destructive confirmations.
            ConfirmButton.Style = (Style)FindResource("AccentButton");
            if (TryFindResource("AccentBrush") is Brush accent)
            {
                IconBadge.BorderBrush = accent;
                IconGlyph.Foreground = accent;
                IconGlyph.Text = "?";
            }
        }
    }

    /// <summary>True when the user confirmed the action.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>
    /// Shows a modal confirmation and returns <c>true</c> if the user confirmed. Owned by
    /// the main window and centred on it.
    /// </summary>
    public static bool Ask(string message, string title = "Potvrdenie", string confirmText = "Áno", bool danger = true)
    {
        var dialog = new ConfirmDialog(message, title, confirmText, danger);
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
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
