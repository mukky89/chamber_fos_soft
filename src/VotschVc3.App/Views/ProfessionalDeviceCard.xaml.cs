using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

/// <summary>
/// Code-behind for the Professional dashboard's device card. Every button here
/// executes the same <see cref="ChamberViewModel"/> commands the Classic
/// dashboard uses; the only added behaviour is an optional Yes/No confirmation
/// (admin setting) before Stop / profile start, exactly like the confirmation
/// already used elsewhere in the app for destructive actions
/// (<see cref="ConfirmDialog"/>).
/// </summary>
public partial class ProfessionalDeviceCard : UserControl
{
    public ProfessionalDeviceCard() => InitializeComponent();

    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(ProfessionalDeviceCard), new PropertyMetadata(null));

    /// <summary>The shell view model — supplies the Detail-navigation command and the
    /// admin's confirmation preferences. Bound from the dashboard's item template.</summary>
    public ShellViewModel? Shell
    {
        get => (ShellViewModel?)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ChamberViewModel chamber)
        {
            return;
        }

        if (Shell?.ConfirmStopAction == true &&
            !ConfirmDialog.Ask($"Zastaviť zariadenie „{chamber.Name}“? Regulácia sa vypne.", "Zastaviť zariadenie", "Zastaviť"))
        {
            return;
        }

        if (chamber.StopChamberCommand.CanExecute(null))
        {
            chamber.StopChamberCommand.Execute(null);
        }
    }

    private void StopProfile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ChamberViewModel chamber)
        {
            return;
        }

        if (Shell?.ConfirmStopAction == true &&
            !ConfirmDialog.Ask($"Zastaviť bežiaci profil „{chamber.ProfileName}“ na zariadení „{chamber.Name}“?", "Zastaviť profil", "Zastaviť"))
        {
            return;
        }

        if (chamber.StopProfileCommand.CanExecute(null))
        {
            chamber.StopProfileCommand.Execute(null);
        }
    }

    private void StartProfile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ChamberViewModel chamber)
        {
            return;
        }

        if (Shell?.ConfirmProfileStart == true && chamber.SelectedHistoryProfile is { } profile)
        {
            string message =
                $"Zariadenie: {chamber.Name}\n" +
                $"Profil: {profile.Name}\n" +
                $"Segmentov: {profile.Segments.Count}\n" +
                $"Trvanie: {FormatDuration(profile.TotalDuration)}\n" +
                $"Cyklov: {Math.Max(1, profile.Cycles)}";

            if (!ConfirmDialog.Ask(message, "Spustiť profil?", "Spustiť profil", danger: false))
            {
                return;
            }
        }

        if (chamber.StartSelectedProfileCommand.CanExecute(null))
        {
            chamber.StartSelectedProfileCommand.Execute(null);
        }
    }

    private static string FormatDuration(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours} h {d.Minutes} min" : $"{d.Minutes} min";

    // Unlock uses a PasswordBox (Password can't be data-bound) — same pattern as HomeView.
    private void UnlockPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is PasswordBox box)
        {
            Unlock(box);
            e.Handled = true;
        }
    }

    private void Unlock_Click(object sender, RoutedEventArgs e)
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
