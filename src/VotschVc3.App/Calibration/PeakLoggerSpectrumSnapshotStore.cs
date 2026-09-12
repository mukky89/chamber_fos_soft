using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
        SavePng(Path.Combine(directory, stem + "_full.png"), points, null);
        double focus = points.OrderByDescending(p => p.Intensity).First().WavelengthNm;
        SavePng(Path.Combine(directory, stem + "_zoom.png"), points, focus);
        string manifest = Path.Combine(runDirectory, "spectrum-snapshots.csv");
        bool newManifest = !File.Exists(manifest);
        using (var writer = new StreamWriter(manifest, append: true, new UTF8Encoding(false)))
        {
            if (newManifest) writer.WriteLine("Timestamp;Plateau;Phase;CalibrationType;Channel;DeviceSerialNumber;TargetTemperatureC;ChamberTemperatureC;ReferenceTemperatureC;Reason;Json;FullPng;ZoomPng");
            writer.WriteLine(string.Join(';',
                metadata.Timestamp.ToString("O", CultureInfo.InvariantCulture), metadata.PlateauIndex + 1, phase,
                metadata.CalibrationType, metadata.Channel, metadata.DeviceSerialNumber,
                metadata.TargetTemperatureC?.ToString("G17", CultureInfo.InvariantCulture),
                metadata.ChamberTemperatureC?.ToString("G17", CultureInfo.InvariantCulture),
                metadata.ReferenceTemperatureC?.ToString("G17", CultureInfo.InvariantCulture), metadata.Reason,
                Path.GetRelativePath(runDirectory, jsonPath), Path.GetRelativePath(runDirectory, Path.Combine(directory, stem + "_full.png")),
                Path.GetRelativePath(runDirectory, Path.Combine(directory, stem + "_zoom.png"))));
        }
        return jsonPath;
    }

    private static void SavePng(string path, IReadOnlyList<PeakLoggerSpectrumPoint> points, double? focus)
    {
        if (points.Count < 2) return;
        double minX = points.Min(p => p.WavelengthNm), maxX = points.Max(p => p.WavelengthNm);
        if (focus is { } f) { minX = Math.Max(minX, f - 2.5); maxX = Math.Min(maxX, f + 2.5); }
        var visible = points.Where(p => p.WavelengthNm >= minX && p.WavelengthNm <= maxX).ToArray();
        if (visible.Length < 2) return;
        double minY = visible.Min(p => p.Intensity), maxY = visible.Max(p => p.Intensity);
        if (maxX <= minX) maxX = minX + 1; if (maxY <= minY) maxY = minY + 1;
        const int width = 1400, height = 720, margin = 70;
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            var pen = new Pen(Brushes.DarkSlateBlue, 2);
            Point Map(PeakLoggerSpectrumPoint p) => new(margin + (p.WavelengthNm - minX) / (maxX - minX) * (width - 2 * margin),
                height - margin - (p.Intensity - minY) / (maxY - minY) * (height - 2 * margin));
            Point previous = Map(visible[0]);
            foreach (PeakLoggerSpectrumPoint point in visible.Skip(1)) { Point current = Map(point); dc.DrawLine(pen, previous, current); previous = current; }
            if (focus is { } marker && marker >= minX && marker <= maxX)
            {
                double x = margin + (marker - minX) / (maxX - minX) * (width - 2 * margin);
                dc.DrawLine(new Pen(Brushes.OrangeRed, 2) { DashStyle = DashStyles.Dash }, new Point(x, margin), new Point(x, height - margin));
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path); encoder.Save(stream);
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char c in value.Trim()) builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        return builder.Length == 0 ? "unknown" : builder.ToString();
    }
}
