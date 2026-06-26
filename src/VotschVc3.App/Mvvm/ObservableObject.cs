using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VotschVc3.App.Mvvm;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base class. Hand rolled to keep
/// the application free of external NuGet dependencies so it builds out of the
/// box with the bare .NET SDK.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Sets <paramref name="field"/> and raises change notification when the value differs.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
