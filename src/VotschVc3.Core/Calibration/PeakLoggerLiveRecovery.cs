namespace VotschVc3.Core.Calibration;

/// <summary>Retries the existing endpoint without replacing the client's run identity or wiring.</summary>
public static class PeakLoggerLiveRecovery
{
    public static async Task<IReadOnlyList<PeakLoggerMeasurement>> ReadAsync(
        IPeakLoggerClient client, PeakLoggerSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!client.IsConnected)
            await client.ConnectAsync(settings, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return await client.ReadMeasurementsAsync(token).ConfigureAwait(false);
    }
}
