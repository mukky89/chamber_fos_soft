namespace VotschVc3.Core.Calibration;

public sealed partial class CalibrationOrchestrator
{
    public async Task<IReadOnlyList<PeakLoggerMeasurement>> ObserveIdentityAsync(CalibrationRunRecord run,
        CalibrationSetup setup, CalibrationRunWriter writer, CancellationToken token)
    {
        if (run.PeakIdentityChannels.Count == 0)
        {
            var sensors = await _peakLogger.DiscoverSensorsAsync(token).ConfigureAwait(false);
            run.PeakIdentityChannels = PeakIdentityGuard.Initialize(sensors, setup.ActiveMappings, DateTimeOffset.UtcNow);
            // Never interpret absence of a selected channel as an unguarded valid source.
            foreach (var mapping in setup.ActiveMappings.Where(m => m.Selected))
                if (!run.PeakIdentityChannels.Any(c => PeakIdentityGuard.Same(c.Device, mapping.SourceDeviceSerialNumber) &&
                    PeakIdentityGuard.Same(c.Channel, mapping.Channel)))
                    run.PeakIdentityChannels.Add(new PeakIdentityChannel { Device = mapping.SourceDeviceSerialNumber,
                        Channel = mapping.Channel, Problem = "Vybraný kanál nie je dostupný na overenie identity." });
        }
        IReadOnlyList<PeakLoggerMeasurement> batch;
        try { batch = await _peakLogger.ReadMeasurementsAsync(token).ConfigureAwait(false); }
        catch (Exception ex) when (!token.IsCancellationRequested &&
            ex is IOException or HttpRequestException or TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            foreach (var channel in run.PeakIdentityChannels.Where(c => c.Problem is null))
            {
                channel.Problem = "Výpadok PeakLogger dát prerušil dôkaz identity: " + ex.Message;
                Audit(new PeakIdentityEvent { Timestamp = DateTimeOffset.UtcNow, Device = channel.Device,
                    Channel = channel.Channel, Reason = channel.Problem });
            }
            writer.SaveSummary();
            return Array.Empty<PeakLoggerMeasurement>();
        }
        // Preserve original detections, including rejected/unselected peaks, before any reassignment/averaging.
        await writer.AppendPeakObservationsAsync(batch, token).ConfigureAwait(false);
        int eventsBefore = run.PeakIdentityEvents.Count;
        var accepted = PeakIdentityGuard.Observe(batch, run.PeakIdentityChannels, setup.Settings, DateTimeOffset.UtcNow, Audit);
        if (run.PeakIdentityEvents.Count != eventsBefore) writer.SaveSummary();
        return accepted;

        void Audit(PeakIdentityEvent item)
        {
            run.PeakIdentityEvents.Add(item);
            writer.WriteDiagnostic("WARNING", "FBG_IDENTITY_EVENT",
                $"{item.Device}/{item.Channel}; FBG={item.PhysicalFbgId}; {item.PreviousApiPeakId} -> {item.CurrentApiPeakId}; {item.Reason}");
        }
    }

    private void SkipUncertainTargets(CalibrationRunRecord run, int plateauIndex,
        IEnumerable<TargetTracker> trackers, CalibrationRunWriter writer)
    {
        foreach (var tracker in trackers.Where(t => !t.IsTerminal))
        {
            var channel = run.PeakIdentityChannels.FirstOrDefault(c =>
                PeakIdentityGuard.Same(c.Device, tracker.Mapping.SourceDeviceSerialNumber) &&
                PeakIdentityGuard.Same(c.Channel, tracker.Mapping.Channel));
            if (channel is null) continue;
            string? problem = channel.Problem;
            if (problem is null && !channel.Tracks.Any(t => t.OriginalPeakId == tracker.Mapping.PeakId))
                problem = "Pre vybraný FBG neexistuje overená počiatočná stopa.";
            if (problem is null) continue;
            var warning = RaiseWarning(run, new CalibrationWarning
            {
                Code = "FBG_POINT_SKIPPED_IDENTITY", PlateauIndex = plateauIndex,
                SerialNumber = tracker.Mapping.SerialNumber, PeakId = tracker.Mapping.PeakId,
                Message = $"FBG SN {tracker.Mapping.SerialNumber}, kanál {tracker.Mapping.Channel}, peak {tracker.Mapping.PeakId}: " +
                    $"bod automaticky vynechaný – neistá identita. {problem} " +
                    "Vzorky sa nepoužijú na kalibráciu. Ostatné kanály pokračujú; po ich dokončení nasleduje ďalší bod bez zásahu operátora.",
            });
            tracker.SkipIdentity(warning.Message);
            writer.WriteDiagnostic("WARNING", warning.Code, warning.Message);
        }
    }
}
