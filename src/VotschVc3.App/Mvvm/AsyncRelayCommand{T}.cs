using System.Windows.Input;

namespace VotschVc3.App.Mvvm;

/// <summary>
/// Asynchronous <see cref="ICommand"/> with a typed parameter. Disables itself
/// while the operation runs and surfaces unhandled exceptions through an
/// optional callback instead of crashing the dispatcher.
/// </summary>
public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _isRunning;

    public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null, Action<Exception>? onError = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged;

    private bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke(Cast(parameter)) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        IsRunning = true;
        try
        {
            await _execute(Cast(parameter)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is an expected, benign outcome.
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
        finally
        {
            IsRunning = false;
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static T? Cast(object? parameter) => parameter is T value ? value : default;
}
