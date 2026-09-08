using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationOperatorSupervisionTests
{
    [Fact]
    public async Task NoDecisionExpiresAndRejectsLateAnswer()
    {
        var request = new CalibrationOperatorRequest(0, CalibrationOperatorIssue.Sensors, "Timeout");
        Assert.InRange((request.Deadline - DateTimeOffset.Now).TotalMinutes, 29.9, 30.1);
        var answer = await request.WaitAsync(CancellationToken.None, TimeSpan.FromMilliseconds(5));
        Assert.Equal(CalibrationOperatorDecision.Stop, answer.Decision);
        Assert.False(request.Submit(CalibrationOperatorDecision.Extend30, "Neskoro"));
        Assert.Equal(answer, await request.Completion);
    }

    [Fact]
    public async Task CancellationClosesPendingDecision()
    {
        var request = new CalibrationOperatorRequest(0, CalibrationOperatorIssue.Temperature, "WIKA");
        using var cancellation = new CancellationTokenSource();
        var wait = request.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.True(request.Completion.IsCanceled);
        Assert.False(request.Submit(CalibrationOperatorDecision.Retry, "Neskoro"));
    }

    [Fact]
    public async Task DecisionNeedsReasonAndOnlyOneAnswerIsAccepted()
    {
        var request = new CalibrationOperatorRequest(1, CalibrationOperatorIssue.Validation, "Odozva");
        Assert.False(request.Submit(CalibrationOperatorDecision.Extend15, "Predĺžiť"));
        Assert.False(request.Submit(CalibrationOperatorDecision.Retry, " "));
        Assert.True(request.Submit(CalibrationOperatorDecision.Retry, " Nový pokus "));
        Assert.False(request.Submit(CalibrationOperatorDecision.Skip, "Druhé rozhodnutie"));
        Assert.Equal(new CalibrationOperatorAnswer(CalibrationOperatorDecision.Retry, "Nový pokus"),
            await request.WaitAsync(CancellationToken.None));
    }

    [Fact]
    public void SupervisionIsOptInAndCheckpointClonePreservesIt()
    {
        var settings = new CalibrationProfileSettings();
        Assert.False(settings.OperatorSupervisionEnabled);
        settings.OperatorSupervisionEnabled = true;
        Assert.True(CalibrationCheckpointRecovery.CloneSettings(settings).OperatorSupervisionEnabled);
    }
}
