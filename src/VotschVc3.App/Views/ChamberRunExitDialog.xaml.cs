using System.Windows;
using System.Windows.Input;

namespace VotschVc3.App.Views;

public enum ChamberRunExitChoice
{
    Cancel,
    KeepRunning,
    StopChamber,
}

public partial class ChamberRunExitDialog : Window
{
    private ChamberRunExitDialog(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
    }

    public ChamberRunExitChoice Choice { get; private set; } = ChamberRunExitChoice.Cancel;

    public static ChamberRunExitChoice Ask(string title, string message)
    {
        var dialog = new ChamberRunExitDialog(title, message);
        Window? owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? Application.Current?.MainWindow;
        if (owner is not null && owner.IsVisible && !ReferenceEquals(owner, dialog))
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
        return dialog.Choice;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void KeepRunning_Click(object sender, RoutedEventArgs e) => Finish(ChamberRunExitChoice.KeepRunning);
    private void StopChamber_Click(object sender, RoutedEventArgs e) => Finish(ChamberRunExitChoice.StopChamber);
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Finish(ChamberRunExitChoice choice)
    {
        Choice = choice;
        DialogResult = true;
        Close();
    }
}
