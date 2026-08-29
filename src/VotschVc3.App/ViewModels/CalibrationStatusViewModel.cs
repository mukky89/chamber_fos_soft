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
    private readonly Dictionary<Guid, WorkspaceStatus> _workspaces = new();

    private CalibrationStatusViewModel() { }

    public bool IsRunning { get => _isRunning; private set => SetProperty(ref _isRunning, value); }
    public string StateText { get => _stateText; private set => SetProperty(ref _stateText, value); }
    public string DetailText { get => _detailText; private set => SetProperty(ref _detailText, value); }
    public double ProgressPercent { get => _progressPercent; private set => SetProperty(ref _progressPercent, value); }

    public void Update(Guid chamberId, string chamberName, bool isRunning, string profileName, string runState, string plateau, double progressPercent)
    {
        _workspaces[chamberId] = new(chamberName, isRunning, profileName, runState, plateau, Math.Clamp(progressPercent, 0, 100));
        WorkspaceStatus[] active = _workspaces.Values.Where(status => status.IsRunning).ToArray();
        IsRunning = active.Length > 0;
        StateText = active.Length switch
        {
            0 => "Vypnutá",
            1 => "1 prebieha",
            _ => $"{active.Length} prebiehajú",
        };
        ProgressPercent = active.Length == 0 ? 0 : active.Average(status => status.ProgressPercent);
        DetailText = active.Length == 0
            ? "FBG kalibrácia nie je spustená."
            : string.Join(" · ", active.Select(status => $"{status.ChamberName}: {status.ProfileName}, {status.Plateau}"));
    }

    private sealed record WorkspaceStatus(
        string ChamberName, bool IsRunning, string ProfileName, string RunState, string Plateau, double ProgressPercent);
}
