namespace VotschVc3.Core.Calibration;

public enum CalibrationOperatorDecision { Extend15, Extend30, Retry, Skip, Stop }
public enum CalibrationOperatorIssue { Temperature, Sensors, Communication, Validation }
public sealed record CalibrationOperatorAnswer(CalibrationOperatorDecision Decision, string Reason);

public sealed class CalibrationOperatorRequest
{
    private readonly TaskCompletionSource<CalibrationOperatorAnswer> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CalibrationOperatorRequest(int plateauIndex, CalibrationOperatorIssue issue, string message)
    {
        PlateauIndex = plateauIndex; Issue = issue; Message = message;
        Deadline = DateTimeOffset.Now.AddMinutes(30);
    }
    public int PlateauIndex { get; }
    public CalibrationOperatorIssue Issue { get; }
    public string Message { get; }
    public DateTimeOffset Deadline { get; }
    public Task<CalibrationOperatorAnswer> Completion => _answer.Task;
    public bool CanExtend => Issue is CalibrationOperatorIssue.Temperature or CalibrationOperatorIssue.Sensors;
    public bool CanRetry => true;
    public bool CanSkip => Issue != CalibrationOperatorIssue.Communication;
    public bool Submit(CalibrationOperatorDecision decision, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) ||
            (decision is CalibrationOperatorDecision.Extend15 or CalibrationOperatorDecision.Extend30 && !CanExtend) ||
            (decision == CalibrationOperatorDecision.Retry && !CanRetry) ||
            (decision == CalibrationOperatorDecision.Skip && !CanSkip)) return false;
        return _answer.TrySetResult(new(decision, reason.Trim()));
    }
    public async Task<CalibrationOperatorAnswer> WaitAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        TimeSpan remaining = timeout ?? (Deadline - DateTimeOffset.Now);
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        try { return await _answer.Task.WaitAsync(remaining, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            var answer = new CalibrationOperatorAnswer(CalibrationOperatorDecision.Stop, "Operátor nerozhodol do 30 minút; automatické riadené ukončenie.");
            _answer.TrySetResult(answer);
            return await _answer.Task.ConfigureAwait(false);
        }
        finally { if (!_answer.Task.IsCompleted) _answer.TrySetCanceled(cancellationToken); }
    }
}

public sealed class CalibrationSupervisionStoppedException : Exception
{
    public CalibrationSupervisionStoppedException(string message) : base(message) { }
}

public sealed partial class CalibrationOrchestrator
{
    public event Action<CalibrationOperatorRequest>? OperatorAttentionRequired;
    public async Task<CalibrationOperatorAnswer> AskOperatorAsync(CalibrationRunRecord run, CalibrationRunWriter writer,
        int plateauIndex, CalibrationOperatorIssue issue, string message, CancellationToken cancellationToken)
    {
        var request = new CalibrationOperatorRequest(plateauIndex, issue, message);
        run.State = CalibrationRunState.AwaitingOperator;
        run.PendingOperatorIssue = message;
        run.OperatorDecisionDeadline = request.Deadline;
        RaiseWarning(run, new CalibrationWarning { Code = "OPERATOR_DECISION_REQUIRED", PlateauIndex = plateauIndex,
            Message = $"OPERÁTORSKÝ DOHĽAD · {message} Lehota na rozhodnutie: 30 min. Pri nečinnosti sa beh ukončí a komora zastaví." });
        writer.WriteDiagnostic("WARNING", "OPERATOR_DECISION_REQUIRED", message);
        writer.SaveSummary();
        CalibrationOperatorAnswer answer;
        try
        {
            if (OperatorAttentionRequired is null)
                throw new CalibrationSupervisionStoppedException("Operátorský dohľad nemá pripojené ovládanie. Beh sa ukončuje.");
            OperatorAttentionRequired.Invoke(request);
            answer = await request.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            run.PendingOperatorIssue = null;
            run.OperatorDecisionDeadline = null;
            writer.SaveSummary();
        }
        var audit = new CalibrationWarning { Code = "OPERATOR_DECISION", PlateauIndex = plateauIndex,
            Overridden = true, OverrideReason = answer.Reason,
            Message = $"Operátor {run.Operator}: {answer.Decision}; dôvod: {answer.Reason}" };
        RaiseWarning(run, audit);
        writer.WriteDiagnostic("WARNING", audit.Code, audit.Message);
        writer.SaveSummary();
        if (answer.Decision == CalibrationOperatorDecision.Stop)
            throw new CalibrationSupervisionStoppedException(answer.Reason);
        return answer;
    }
}
