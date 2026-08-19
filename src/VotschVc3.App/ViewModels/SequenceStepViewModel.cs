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
}
