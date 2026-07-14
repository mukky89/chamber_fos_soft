using VotschVc3.App.Mvvm;
using VotschVc3.Core.Security;

namespace VotschVc3.App.ViewModels;

/// <summary>Login screen shown before the main application is accessible.</summary>
public sealed class LoginViewModel : ObservableObject
{
    private readonly UserStore _store;
    private readonly Action<User> _onSuccess;

    public LoginViewModel(UserStore store, Action<User> onSuccess)
    {
        _store = store;
        _onSuccess = onSuccess;
        _userNames = store.LoadAll().Select(u => u.Name).ToList();
        _selectedUserName = _userNames.FirstOrDefault() ?? string.Empty;
    }

    private IReadOnlyList<string> _userNames;
    public IReadOnlyList<string> UserNames { get => _userNames; private set => SetProperty(ref _userNames, value); }

    /// <summary>Reloads the user list (after an admin adds or removes a user).</summary>
    public void RefreshUsers()
    {
        UserNames = _store.LoadAll().Select(u => u.Name).ToList();
        if (!UserNames.Contains(SelectedUserName, StringComparer.OrdinalIgnoreCase))
        {
            SelectedUserName = UserNames.FirstOrDefault() ?? string.Empty;
        }
    }

    private string _selectedUserName;
    public string SelectedUserName { get => _selectedUserName; set => SetProperty(ref _selectedUserName, value); }

    private string _error = string.Empty;
    public string Error { get => _error; private set => SetProperty(ref _error, value); }

    /// <summary>Validates the credentials and signs the user in on success.</summary>
    public void TryLogin(string password)
    {
        User? user = _store.Find(SelectedUserName);
        if (user is null)
        {
            Error = "Neznámy užívateľ.";
            return;
        }

        if (!user.VerifyPassword(password))
        {
            Error = "Nesprávne heslo.";
            return;
        }

        Error = string.Empty;
        _onSuccess(user);
    }
}
