using VotschVc3.App.Mvvm;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// Admin-only settings screen. Groups the e-mail notification configuration and
/// chamber management (add / remove) that used to live on the home page so they
/// are out of the operator's way and only reachable by administrators.
/// </summary>
/// <remarks>
/// The screen owns no state itself: it wraps the <see cref="ShellViewModel"/> and
/// the view binds to the shell's existing e-mail and chamber-management members
/// through <see cref="Shell"/>.
/// </remarks>
public sealed class AdminViewModel : ObservableObject
{
    public AdminViewModel(ShellViewModel shell)
    {
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
    }

    /// <summary>The root view model that owns the actual settings and commands.</summary>
    public ShellViewModel Shell { get; }
}
