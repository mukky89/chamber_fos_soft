using System.Windows;
using System.Windows.Threading;
using VotschVc3.App.Mvvm;
using VotschVc3.App.Notifications;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.ViewModels;

public sealed partial class CalibrationViewModel
{
    private CalibrationOperatorRequest? _operatorRequest;
    private string _operatorReason = string.Empty;
    public bool OperatorSupervisionEnabled
    {
        get => _setup.Settings.OperatorSupervisionEnabled;
        set
        {
            if (IsRunning) return;
            _setup.Settings.OperatorSupervisionEnabled = value;
            OnPropertyChanged(); OnPropertyChanged(nameof(OperatorSupervisionLabel));
            if (SelectedProfile is not null) SaveSetup();
        }
    }
    public bool HasOperatorDecision => _operatorRequest is not null;
    public string OperatorSupervisionLabel => HasOperatorDecision ? "‼ ČAKÁ NA ROZHODNUTIE OPERÁTORA" :
        OperatorSupervisionEnabled ? "⚠ OPERÁTORSKÝ DOHĽAD ZAPNUTÝ" : "Automatický režim";
    public string OperatorDecisionMessage => _operatorRequest is { } request ? $"Plato {request.PlateauIndex + 1} · {request.Message}" : string.Empty;
    public string OperatorDecisionCountdown => _operatorRequest is { } request
        ? $"Rozhodnite do {request.Deadline:HH:mm:ss} · zostáva {Math.Max(0, (request.Deadline - DateTimeOffset.Now).TotalMinutes):F1} min. Bez reakcie sa beh uloží a odošle STOP komore."
        : string.Empty;
    public string OperatorDecisionReason
    {
        get => _operatorReason;
        set { _operatorReason = value ?? string.Empty; OnPropertyChanged(); RefreshOperatorCommands(); }
    }
    private RelayCommand? _extend15, _extend30, _retryDecision, _skipDecision, _stopDecision;
    public RelayCommand ExtendOperator15Command => _extend15 ??= new(() => Decide(CalibrationOperatorDecision.Extend15), () => CanDecide && _operatorRequest?.CanExtend == true);
    public RelayCommand ExtendOperator30Command => _extend30 ??= new(() => Decide(CalibrationOperatorDecision.Extend30), () => CanDecide && _operatorRequest?.CanExtend == true);
    public RelayCommand RetryOperatorCommand => _retryDecision ??= new(() => Decide(CalibrationOperatorDecision.Retry), () => CanDecide && _operatorRequest?.CanRetry == true);
    public RelayCommand SkipOperatorCommand => _skipDecision ??= new(() => Decide(CalibrationOperatorDecision.Skip), () => CanDecide && _operatorRequest?.CanSkip == true);
    public RelayCommand StopOperatorCommand => _stopDecision ??= new(() => Decide(CalibrationOperatorDecision.Stop), () => HasOperatorDecision);
    private bool CanDecide => _operatorRequest is { Completion.IsCompleted: false } && !string.IsNullOrWhiteSpace(OperatorDecisionReason);
    private void Decide(CalibrationOperatorDecision decision) => _operatorRequest?.Submit(decision,
        decision == CalibrationOperatorDecision.Stop && string.IsNullOrWhiteSpace(OperatorDecisionReason)
            ? "Operátor zvolil ukončenie." : OperatorDecisionReason);
    private void RefreshOperatorCommands()
    {
        _extend15?.RaiseCanExecuteChanged(); _extend30?.RaiseCanExecuteChanged(); _retryDecision?.RaiseCanExecuteChanged();
        _skipDecision?.RaiseCanExecuteChanged(); _stopDecision?.RaiseCanExecuteChanged();
    }
    private void RefreshOperatorPanel()
    {
        OnPropertyChanged(nameof(HasOperatorDecision)); OnPropertyChanged(nameof(OperatorSupervisionLabel));
        OnPropertyChanged(nameof(OperatorDecisionMessage)); OnPropertyChanged(nameof(OperatorDecisionCountdown));
        RefreshOperatorCommands(); RefreshCommands(); PublishCalibrationStatus();
    }
    private async void OnOperatorAttentionRequired(CalibrationOperatorRequest request)
    {
        DispatcherTimer? timer = null;
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _operatorRequest = request; OperatorDecisionReason = string.Empty;
                StatusMessage = request.Message;
                RunState = CalibrationRunState.AwaitingOperator.ToString();
                timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                timer.Tick += (_, _) => RefreshOperatorPanel();
                timer.Start(); RefreshOperatorPanel();
                DesktopNotifier.Notify("OPERÁTORSKÝ DOHĽAD – rozhodnite do 30 min", OperatorDecisionMessage, DesktopNotificationKind.Alarm);
            });
            await SendWarningEmailAsync(_activeRun, new CalibrationWarning { Code = "OPERATOR_DECISION_REQUIRED",
                PlateauIndex = request.PlateauIndex, Message = request.Message +
                $"\n\nRozhodnite v aplikácii do {request.Deadline:dd.MM.yyyy HH:mm:ss}: predĺžiť 15/30 min (teplota/FBG), nový pokus, preskočiť bod alebo ukončiť. Bez reakcie sa beh uloží a komora zastaví. Preskočenie nikdy neznamená úspešný výsledok." });
        }
        catch (Exception ex) { VotschVc3.Core.Diagnostics.AppLog.Warn("Operátorský dohľad", $"Oznámenie: {ex.Message}"); }
        try { await request.Completion; } catch (OperationCanceledException) { }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                timer?.Stop();
                if (ReferenceEquals(_operatorRequest, request)) _operatorRequest = null;
                RefreshOperatorPanel();
            });
        }
    }
}
