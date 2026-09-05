using VotschVc3.App.Mvvm;

namespace VotschVc3.App.ViewModels;

/// <summary>Application-wide snapshots shown on the dashboard while calibration workspaces are hidden.</summary>
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

    public void Update(
        Guid chamberId,
        string chamberName,
        bool isRunning,
        string profileName,
        string runId,
        string runDirectory,
        string runState,
        string plateau,
        double progressPercent,
        string displayState,
        string currentActivity,
        string target,
        string reference,
        string peakSummary,
        string progressLabel,
        string phaseElapsed,
        string eta,
        string estimatedFinish,
        string etaBasis,
        DateTimeOffset? startedAt,
        DateTimeOffset? estimatedFinishAt)
    {
        _workspaces[chamberId] = new(chamberName, isRunning, profileName, runId, runDirectory, runState, plateau, Math.Clamp(progressPercent, 0, 100),
            displayState, currentActivity, target, reference, peakSummary, progressLabel, phaseElapsed, eta, estimatedFinish, etaBasis,
            startedAt, estimatedFinishAt);
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

        // Per-device cards listen to PropertyChanged and need a pulse even when the aggregate
        // text happens to remain unchanged between two progress updates.
        OnPropertyChanged(nameof(WorkspaceRevision));
    }

    public long WorkspaceRevision => DateTimeOffset.UtcNow.Ticks;

    public CalibrationWorkspaceStatusSnapshot GetWorkspace(Guid chamberId)
    {
        if (!_workspaces.TryGetValue(chamberId, out WorkspaceStatus? status))
            return new(chamberId, string.Empty, false, string.Empty, "—", string.Empty, "Idle", string.Empty, 0,
                "READY · Pripravené", "Kalibrácia nie je spustená.", "—", "—", "0 / 0", "0 %", "—", "—", "—", string.Empty, null, null);
        return new(
            chamberId,
            status.ChamberName,
            status.IsRunning,
            status.ProfileName,
            status.RunId,
            status.RunDirectory,
            status.RunState,
            status.Plateau,
            status.ProgressPercent,
            status.DisplayState,
            status.CurrentActivity,
            status.Target,
            status.Reference,
            status.PeakSummary,
            status.ProgressLabel,
            status.PhaseElapsed,
            status.Eta,
            status.EstimatedFinish,
            status.EtaBasis,
            status.StartedAt,
            status.EstimatedFinishAt);
    }

    private sealed record WorkspaceStatus(
        string ChamberName,
        bool IsRunning,
        string ProfileName,
        string RunId,
        string RunDirectory,
        string RunState,
        string Plateau,
        double ProgressPercent,
        string DisplayState,
        string CurrentActivity,
        string Target,
        string Reference,
        string PeakSummary,
        string ProgressLabel,
        string PhaseElapsed,
        string Eta,
        string EstimatedFinish,
        string EtaBasis,
        DateTimeOffset? StartedAt,
        DateTimeOffset? EstimatedFinishAt);
}

public sealed record CalibrationWorkspaceStatusSnapshot(
    Guid ChamberId,
    string ChamberName,
    bool IsRunning,
    string ProfileName,
    string RunId,
    string RunDirectory,
    string RunState,
    string Plateau,
    double ProgressPercent,
    string DisplayState,
    string CurrentActivity,
    string Target,
    string Reference,
    string PeakSummary,
    string ProgressLabel,
    string PhaseElapsed,
    string Eta,
    string EstimatedFinish,
    string EtaBasis,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EstimatedFinishAt);
