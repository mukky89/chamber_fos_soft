using VotschVc3.App.Mvvm;

namespace VotschVc3.App.ViewModels;

/// <summary>Application-wide snapshot shown on the dashboard while the calibration window is hidden.</summary>
public sealed class CalibrationStatusViewModel : ObservableObject
{
    public static CalibrationStatusViewModel Instance { get; } = new();

    private bool _isRunning;
    private string _stateText = "Vypnutá";
    private string _detailText = "FBG kalibrácia nie je spustená.";
    private double _progressPercent;

    private CalibrationStatusViewModel() { }

    public bool IsRunning { get => _isRunning; private set => SetProperty(ref _isRunning, value); }
    public string StateText { get => _stateText; private set => SetProperty(ref _stateText, value); }
    public string DetailText { get => _detailText; private set => SetProperty(ref _detailText, value); }
    public double ProgressPercent { get => _progressPercent; private set => SetProperty(ref _progressPercent, value); }

    public void Update(bool isRunning, string profileName, string runState, string plateau, double progressPercent)
    {
        IsRunning = isRunning;
        StateText = isRunning ? "Prebieha" : "Vypnutá";
        ProgressPercent = Math.Clamp(progressPercent, 0, 100);
        DetailText = isRunning
            ? $"{profileName} · {runState} · {plateau}"
            : "FBG kalibrácia nie je spustená.";
    }
}
