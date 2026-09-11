using VotschVc3.Core.Calibration;

namespace VotschVc3.App.ViewModels;

public sealed partial class CalibrationViewModel
{
    public bool CanImportWiring => SelectedProfile is not null && !IsRunning && !IsLoadingDeviceData && !HasResumableCalibration;

    public void ImportWiring(IReadOnlyList<CalibrationSensorMapping> mappings)
    {
        if (!CanImportWiring) throw new InvalidOperationException("Zapojenie možno načítať iba mimo behu a obnovovania dát. Najprv ukonči prípadnú rozpracovanú kalibráciu.");
        if (SelectedChamber is not null && _calibrationStore.LoadCheckpoint(SelectedChamber.Config.Id) is not null)
            throw new InvalidOperationException("Komora má rozpracovanú kalibráciu. Najprv ju dokonči alebo ukonči a ulož.");
        var live = Peaks.Where(p => !p.IsDisconnected).ToDictionary(p => p.ToMapping().SourceIdentity, StringComparer.OrdinalIgnoreCase);
        var replacement = CalibrationWiringImporter.PrepareReplacement(_setup, mappings, live.Values.Select(p => p.ToMapping()));
        replacement.ProfileId = SelectedProfile!.Id;
        replacement.ChamberId = SelectedChamber?.Config.Id ?? _workspaceChamberId;
        replacement.CalibrationSegmentIndices = CalibrationPoints.Where(p => p.Selected).Select(p => p.SegmentIndex).ToList();

        _setupAutosaveCts?.Cancel();
        _restoringWiring = true;
        string? backup = null;
        try
        {
            var rows = replacement.ActiveMappings.Select(mapping =>
            {
                live.TryGetValue(mapping.SourceIdentity, out var current);
                var sensor = new PeakLoggerSensor(mapping.SourceDeviceSerialNumber, mapping.Channel, []);
                var peak = new PeakLoggerPeak(mapping.PeakId, current?.PeakIndex ?? mapping.PeakIndex,
                    current?.CurrentWavelengthNm ?? mapping.CurrentWavelengthNm ?? mapping.NominalWavelengthNm ?? 0,
                    current?.Intensity);
                var row = CreatePeakRow(sensor, peak, mapping);
                if (current is null) row.MarkDisconnected();
                return row;
            }).ToArray();
            // The original setup is backed up by the atomic writer before changing the UI.
            backup = _calibrationStore.SaveImportedSetup(replacement);
            _setup = replacement;
            Peaks.Clear();
            foreach (var row in rows) Peaks.Add(row);
        }
        finally { _restoringWiring = false; }
        ValidateSerialNumbers();
        NotifyPeakCounts();
        RefreshCommands();
        SetupSaveStatus = $"Uložené o {DateTime.Now:HH:mm:ss}";
        SetupSaveColor = "#55D6A0";
        SetupSaveDetail = "Zapojenie bolo načítané zo súboru a uložené pre aktuálny profil a komoru." +
            (backup is null ? "" : $" Pôvodné zapojenie: {backup}");
        StatusMessage = $"Načítané zapojenie: {mappings.Count} peakov. Skontroluj SN, výber a pripojenie pred spustením kalibrácie.";
    }
}
