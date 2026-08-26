using VotschVc3.App.Mvvm;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// One editable point of a "Postupnosť teplôt" sequence in the quick profile builder: a
/// target temperature and how long the profile holds it (its own plateau length) before
/// ramping to the next point. Unlike the old single shared plateau length, every step
/// can have a different hold time – see <see cref="QuickProfileViewModel.SequenceSteps"/>.
/// </summary>
public sealed class SequenceStepViewModel : ObservableObject
{
    public SequenceStepViewModel(double temperature, double plateauMinutes)
    {
        _temperature = temperature;
        _plateauMinutes = Math.Max(0, plateauMinutes);
    }

    private double _temperature;
    public double Temperature
    {
        get => _temperature;
        set => SetProperty(ref _temperature, value);
    }

    private double _plateauMinutes;
    public double PlateauMinutes
    {
        get => _plateauMinutes;
        set => SetProperty(ref _plateauMinutes, Math.Max(0, value));
    }

    private int _number = 1;
    /// <summary>
    /// The point's 1-based position in the sequence, kept up to date by
    /// <see cref="QuickProfileViewModel"/> whenever points are added, removed or
    /// reordered. Bound instead of <c>ItemsControl.AlternationIndex</c>, which WPF does
    /// not recompute after a <see cref="System.Collections.ObjectModel.ObservableCollection{T}.Move"/> –
    /// the numbers then stayed put and the reorder buttons looked as if they did nothing.
    /// </summary>
    public int Number
    {
        get => _number;
        internal set => SetProperty(ref _number, value);
    }

    private bool _canMoveUp;
    /// <summary>False for the first point, so its "hore" button is disabled instead of silently doing nothing.</summary>
    public bool CanMoveUp
    {
        get => _canMoveUp;
        internal set => SetProperty(ref _canMoveUp, value);
    }

    private bool _canMoveDown;
    /// <summary>False for the last point (see <see cref="CanMoveUp"/>).</summary>
    public bool CanMoveDown
    {
        get => _canMoveDown;
        internal set => SetProperty(ref _canMoveDown, value);
    }
}
