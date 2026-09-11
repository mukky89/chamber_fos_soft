using VotschVc3.Core.Calibration;

namespace VotschVc3.App.ViewModels;

public sealed partial class CalibrationViewModel
{
    public string IgnoredChannelsLabel => $"Ignorované kanály ({_setup.IgnoredPeakLoggerChannels?.Count ?? 0})";
    public IReadOnlyList<string> IgnoredPeakLoggerChannels => _setup.IgnoredPeakLoggerChannels ?? new();
    public bool IsPeakLoggerChannelIgnored(string channel) => _setup.IsChannelIgnored(channel);
    public bool IsPeakLoggerSourceIgnored(string sourceIdentity)
    {
        string[] parts = sourceIdentity.Split('|');
        return parts.Length >= 3 && IsPeakLoggerChannelIgnored(parts[^2]);
    }

    public void SetIgnoredPeakLoggerChannels(IEnumerable<string> channels)
    {
        if (IsRunning) throw new InvalidOperationException("Kanály možno meniť iba mimo bežiacej kalibrácie.");
        if (SelectedProfile is null) throw new InvalidOperationException("Najprv vyber kalibračný profil.");
        var ignored = channels.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList();
        if (_resumeCheckpoint?.Mappings.Any(m => m.Selected && ignored.Contains(m.Channel, StringComparer.OrdinalIgnoreCase)) == true)
            throw new InvalidOperationException("Niektorý z kanálov patrí do rozpracovanej kalibrácie. Najprv ju dokonči alebo ukonči a ulož.");

        if (Peaks.Count > 0) PersistSetup(showStatus: false);
        _setup.IgnoredPeakLoggerChannels = ignored;
        foreach (var row in Peaks.Where(p => IsPeakLoggerChannelIgnored(p.Channel)).ToArray()) Peaks.Remove(row);
        // Restore saved identities without inventing a live reading. Normal polling reconnects them.
        foreach (var mapping in _setup.ActiveMappings.Where(m => !Peaks.Any(p =>
                     string.Equals($"{p.PeakLoggerDeviceSerialNumber}|{p.Channel}|{p.PeakId}", m.SourceIdentity, StringComparison.OrdinalIgnoreCase))))
        {
            var sensor = new PeakLoggerSensor(mapping.SourceDeviceSerialNumber, mapping.Channel, Array.Empty<PeakLoggerPeak>());
            var peak = new PeakLoggerPeak(mapping.PeakId, mapping.PeakIndex, mapping.CurrentWavelengthNm ?? mapping.NominalWavelengthNm ?? 0);
            var row = CreatePeakRow(sensor, peak, mapping);
            row.MarkDisconnected();
            Peaks.Add(row);
        }
        OnPropertyChanged(nameof(IgnoredChannelsLabel));
        NotifyPeakCounts();
        ValidateSerialNumbers();
        RefreshCommands();
        PersistSetup(showStatus: false);
    }
}
