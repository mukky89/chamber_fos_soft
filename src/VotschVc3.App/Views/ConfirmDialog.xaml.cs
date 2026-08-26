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
    private ConfirmDialog(string message, string title, string confirmText, bool danger, string? cancelText)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        if (!string.IsNullOrWhiteSpace(cancelText))
        {
            CancelButton.Content = cancelText;
        }

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
    /// <param name="message">What the user is being asked.</param>
    /// <param name="title">Dialog caption.</param>
    /// <param name="confirmText">Label of the confirming button.</param>
    /// <param name="danger">Red (destructive) styling; <c>false</c> gives the neutral accent look.</param>
    /// <param name="cancelText">Label of the other button – set it when the choice is
    /// between two actions ("Vytvoriť nový") rather than doing nothing ("Zrušiť").</param>
    public static bool Ask(
        string message, string title = "Potvrdenie", string confirmText = "Áno",
        bool danger = true, string? cancelText = null)
    {
        var dialog = new ConfirmDialog(message, title, confirmText, danger, cancelText);
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
