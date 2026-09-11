using VotschVc3.Core.Communication;
using VotschVc3.Core.Protocol;

namespace VotschVc3.Core.Calibration;

/// <summary>Establishes a fresh, read-only chamber session before creating or resuming a run.</summary>
public static class CalibrationChamberStartupRecovery
{
    public static async Task<ChamberReading> ConnectAndReadAsync(
        IChamberDevice chamber, ChamberConnectionSettings settings,
        Action<string>? reportStatus, CancellationToken cancellationToken,
        TimeSpan? recoveryTimeout = null, TimeSpan? retryInterval = null)
    {
        TimeSpan budget = recoveryTimeout ?? TimeSpan.FromMinutes(30);
        TimeSpan delay = retryInterval ?? TimeSpan.FromSeconds(5);
        if (budget <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(recoveryTimeout));
        if (delay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retryInterval));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(budget);
        Exception? lastFailure = null;
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                try
                {
                    // A timed-out TCP stream may contain a late response. Never reuse it.
                    if (lastFailure is not null)
                        await chamber.DisconnectAsync().ConfigureAwait(false);
                    await chamber.ConnectAsync(settings, deadline.Token).ConfigureAwait(false);
                    var reading = await chamber.ReadAsync(deadline.Token).ConfigureAwait(false);
                    deadline.Token.ThrowIfCancellationRequested();
                    if (reading.Temperature is not double temperature || !double.IsFinite(temperature))
                        throw new IOException("Komora neposkytla platnú nameranú teplotu.");
                    if (lastFailure is not null)
                        reportStatus?.Invoke("Spojenie s komorou obnovené. Pokračujem v úvodnej kontrole kalibrácie.");
                    return reading;
                }
                catch (Exception ex) when (!deadline.IsCancellationRequested &&
                    ex is IOException or TimeoutException or System.Net.Sockets.SocketException or HttpRequestException or OperationCanceledException)
                {
                    lastFailure = ex;
                    reportStatus?.Invoke($"Komora neodpovedá · obnovujem spojenie o {delay.TotalSeconds:0.#} s (najviac {budget.TotalMinutes:0.#} min). Kalibrácia čaká na platnú teplotu. {ex.Message}");
                }
                await Task.Delay(delay, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("Spojenie s komorou sa neobnovilo v časovom limite úvodnej kontroly. Uložený checkpoint zostáva k dispozícii na pokračovanie.", lastFailure);
        }
    }
}
