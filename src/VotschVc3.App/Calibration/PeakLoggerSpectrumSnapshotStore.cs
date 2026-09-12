using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace VotschVc3.App.Calibration;

public sealed record PeakLoggerSpectrumSnapshotMetadata(
    Guid RunId,
    string CalibrationType,
    int PlateauIndex,
    string Phase,
    DateTimeOffset Timestamp,
    string? ProfileName,
    string? Channel,
    string? DeviceSerialNumber,
    double? TargetTemperatureC,
    double? ChamberTemperatureC,
    double? ReferenceTemperatureC,
    string? Reason);

/// <summary>Persists raw PeakLogger spectra beside a calibration run for audit/report use.</summary>
public static class PeakLoggerSpectrumSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Save(string runDirectory, PeakLoggerSpectrumSnapshotMetadata metadata,
        IReadOnlyList<PeakLoggerSpectrumPoint> points)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(points);

        string phase = string.IsNullOrWhiteSpace(metadata.Phase) ? "snapshot" : Sanitize(metadata.Phase);
        string channel = string.IsNullOrWhiteSpace(metadata.Channel) ? "all" : Sanitize(metadata.Channel);
        string directory = Path.Combine(runDirectory, "spectrum-snapshots",
            $"plateau-{metadata.PlateauIndex + 1:00}", phase);
        Directory.CreateDirectory(directory);
        string stamp = metadata.Timestamp.ToLocalTime().ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        string stem = $"{stamp}_{channel}";
        string jsonPath = Path.Combine(directory, stem + ".json");
        string csvPath = Path.Combine(directory, stem + ".csv");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(new { metadata, points }, JsonOptions), Encoding.UTF8);

        var csv = new StringBuilder("WavelengthNm;Intensity\r\n");
        foreach (PeakLoggerSpectrumPoint point in points)
            csv.Append(point.WavelengthNm.ToString("G17", CultureInfo.InvariantCulture)).Append(';')
                .Append(point.Intensity.ToString("G17", CultureInfo.InvariantCulture)).Append("\r\n");
        File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(false));
        return jsonPath;
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char c in value.Trim()) builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        return builder.Length == 0 ? "unknown" : builder.ToString();
    }
}
