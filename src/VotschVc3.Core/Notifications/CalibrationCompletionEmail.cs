using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using VotschVc3.Core.Calibration;

namespace VotschVc3.Core.Notifications;

public sealed record CalibrationCompletionMessage(
    string Subject, string Text, string Html, IReadOnlyList<EmailAttachment> Attachments);

/// <summary>Builds the final FBG calibration report and a portable archive of the run files.</summary>
public static class CalibrationCompletionEmail
{
    public static CalibrationCompletionMessage Create(CalibrationRunRecord run, string runDirectory)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);

        bool passed = run.State is CalibrationRunState.Completed or CalibrationRunState.CompletedWithWarnings;
        string result = run.State == CalibrationRunState.Completed ? "PASS" : passed ? "PASS S UPOZORNENIAMI" : "FAIL";
        string subjectStatus = run.State == CalibrationRunState.Completed
            ? "COMPLETED"
            : run.State == CalibrationRunState.CompletedWithWarnings ? "COMPLETED WITH WARNINGS" : "FAILED";
        string subject = $"Kalibrácia FBG – {subjectStatus} – {run.DisplayProfileId}";
        string finished = run.CompletedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture) ?? "—";
        TimeSpan? duration = run.CompletedAt - run.StartedAt;
        int targetCount = run.Plateaus.Sum(p => p.Targets.Count);
        int failedCount = run.Plateaus.Sum(p => p.Targets.Count(t => !IsTargetPass(t.Status)));
        List<TemperatureCalibrationResult> calibrationResults = run.CalibrationResults.Count > 0
            ? run.CalibrationResults
            : TemperatureCalibrationAnalyzer.Analyze(run);
        run.CalibrationResults = calibrationResults;
        int calibrationPassCount = calibrationResults.Count(item => item.Result == "PASS");

        string text = $"Výsledok: {result}\r\nRun ID: {run.DisplayRunId}\r\nProfil ID: {run.DisplayProfileId}\r\n" +
            $"Komora: {run.ChamberName}\r\nProfil: {run.ProfileName}\r\nOperátor: {run.Operator}\r\n" +
            $"Spustené: {run.StartedAt.ToLocalTime():dd.MM.yyyy HH:mm:ss}\r\nDokončené: {finished}\r\n" +
            $"Trvanie: {(duration is { } d ? FormatDuration(d) : "—")}\r\n" +
            $"WIKA: {run.ReferenceThermometerPort} / {run.ReferenceThermometerChannel} / SN {Value(run.ReferenceThermometerSerialNumber)}\r\n" +
            $"Plata: {run.Plateaus.Count}\r\nFBG výsledky: {targetCount - failedCount} PASS / {failedCount} FAIL\r\n" +
            $"Kalibračné modely: {calibrationPassCount} PASS / {calibrationResults.Count - calibrationPassCount} FAIL\r\n" +
            $"Upozornenia: {run.Warnings.Count}\r\n\r\nLokálny priečinok: {Path.GetFullPath(runDirectory)}";

        string rows = string.Join(string.Empty, run.Plateaus.SelectMany(plateau => plateau.Targets.Select(target =>
        {
            bool targetPassed = IsTargetPass(target.Status);
            string targetResult = targetPassed ? "PASS" : "FAIL";
            string color = targetPassed ? "#087F5B" : "#C92A2A";
            return $"<tr>" +
                Cell((plateau.PlateauIndex + 1).ToString(CultureInfo.InvariantCulture)) +
                Cell($"{plateau.TargetTemperatureC:0.###} °C") +
                Cell(plateau.ReferenceTemperatureC is { } reference ? $"{reference:0.###} °C" : "—") +
                Cell(Value(target.SerialNumber)) + Cell(Value(target.Channel)) + Cell(Value(target.PeakId)) +
                Cell($"{target.MeanWavelengthNm:0.000000} nm") + Cell(target.SampleCount.ToString(CultureInfo.InvariantCulture)) +
                $"<td style=\"padding:9px;border-bottom:1px solid #E5EBF2;color:{color};font-weight:700\">{targetResult}</td>" +
                Cell(string.IsNullOrWhiteSpace(target.Problem) ? StatusText(target.Status) : target.Problem!) + "</tr>";
        })));
        if (rows.Length == 0)
            rows = "<tr><td colspan=\"10\" style=\"padding:14px;color:#C92A2A\">Nie sú dostupné žiadne výsledky FBG peakov.</td></tr>";

        string coefficientRows = string.Join(string.Empty, calibrationResults.Select(item =>
        {
            bool modelPassed = item.Result == "PASS";
            string color = modelPassed ? "#087F5B" : "#C92A2A";
            return "<tr>" + Cell(Value(item.SerialNumber)) + Cell(Value(item.Channel)) + Cell(Value(item.PeakId)) +
                Cell(Value(item.CalibrationType)) + Cell($"{item.LambdaTRefNm:0.000000} nm") +
                Cell($"{item.SensitivityPmPerC:0.######} pm/°C") + Cell(FormatCoefficients(item)) +
                Cell($"{item.MaxErrorC:0.######} °C") + Cell(item.RSquared.ToString("0.########", CultureInfo.InvariantCulture)) +
                $"<td style=\"padding:9px;border-bottom:1px solid #E5EBF2;color:{color};font-weight:700\">{H(item.Result)}</td></tr>";
        }));
        if (coefficientRows.Length == 0)
            coefficientRows = "<tr><td colspan=\"10\" style=\"padding:14px;color:#C92A2A\">Koeficienty nebolo možné vypočítať – nie sú dostupné aspoň tri platné teplotné body.</td></tr>";

        string fullDirectory = Path.GetFullPath(runDirectory);
        string folderUri = new Uri(fullDirectory.EndsWith(Path.DirectorySeparatorChar) ? fullDirectory : fullDirectory + Path.DirectorySeparatorChar).AbsoluteUri;
        string details = $"""
<tr><td class="content-pad" style="padding:0 32px 28px">
<h2 style="margin:0 0 12px;color:#182A40;font-size:19px">Výsledky kalibrácie</h2>
<div style="margin:0 0 14px;padding:12px 16px;border-radius:8px;background:{(passed ? "#E8F7F1" : "#FDECEC")};color:{(passed ? "#087F5B" : "#C92A2A")};font-size:18px;font-weight:700">{H(result)}</div>
<div style="overflow-x:auto"><table width="100%" cellspacing="0" cellpadding="0" style="border-collapse:collapse;font-size:12px;color:#334155">
<thead><tr style="background:#EEF3F8"><th style="padding:9px;text-align:left">Plato</th><th style="padding:9px;text-align:left">Cieľ</th><th style="padding:9px;text-align:left">WIKA</th><th style="padding:9px;text-align:left">SN</th><th style="padding:9px;text-align:left">Kanál</th><th style="padding:9px;text-align:left">Peak</th><th style="padding:9px;text-align:left">Priemer</th><th style="padding:9px;text-align:left">Vzorky</th><th style="padding:9px;text-align:left">Výsledok</th><th style="padding:9px;text-align:left">Poznámka</th></tr></thead>
<tbody>{rows}</tbody></table></div>
</td></tr>
<tr><td class="content-pad" style="padding:0 32px 28px">
<h2 style="margin:0 0 12px;color:#182A40;font-size:19px">Kalibračné koeficienty</h2>
<div style="overflow-x:auto"><table width="100%" cellspacing="0" cellpadding="0" style="border-collapse:collapse;font-size:11px;color:#334155">
<thead><tr style="background:#EEF3F8"><th style="padding:9px;text-align:left">SN</th><th style="padding:9px;text-align:left">Kanál</th><th style="padding:9px;text-align:left">Peak</th><th style="padding:9px;text-align:left">Kalibrácia</th><th style="padding:9px;text-align:left">λTref</th><th style="padding:9px;text-align:left">Citlivosť</th><th style="padding:9px;text-align:left">Koeficienty</th><th style="padding:9px;text-align:left">Max. chyba</th><th style="padding:9px;text-align:left">R²</th><th style="padding:9px;text-align:left">Výsledok</th></tr></thead>
<tbody>{coefficientRows}</tbody></table></div>
</td></tr>
<tr><td class="content-pad" style="padding:0 32px 28px"><table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#F7F9FC;border:1px solid #E5EBF2;border-radius:10px"><tr><td style="padding:18px 20px;color:#52647C;font-size:13px;line-height:22px;word-break:break-word">
<strong style="color:#182A40">Súbory kalibrácie</strong><br>Koeficienty sú priložené v Exceli a CSV; všetky dáta zostávajú aj v kompletnom ZIP archíve.<br><a href="{H(folderUri)}" style="color:#1769AA">Otvoriť lokálny priečinok behu</a><br><span style="color:#75849A">{H(fullDirectory)}</span>
</td></tr></table></td></tr>
""";

        var attachments = BuildAttachments(fullDirectory, run.DisplayRunId);
        string html = LabControlEmailTemplate.Create(subject, text,
            passed ? LabControlEmailTemplate.EmailTone.Success : LabControlEmailTemplate.EmailTone.Error, details);
        return new(subject, text, html, attachments);
    }

    private static List<EmailAttachment> BuildAttachments(string runDirectory, string runId)
    {
        var files = Directory.Exists(runDirectory)
            ? Directory.EnumerateFiles(runDirectory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        var attachments = new List<EmailAttachment>();
        string? summary = files.FirstOrDefault(path => string.Equals(Path.GetFileName(path), "summary.csv", StringComparison.OrdinalIgnoreCase));
        if (summary is not null)
            attachments.Add(new("calibration-results.csv", ReadShared(summary), "text/csv"));
        string? coefficientsCsv = files.FirstOrDefault(path => string.Equals(Path.GetFileName(path), "calibration-coefficients.csv", StringComparison.OrdinalIgnoreCase));
        if (coefficientsCsv is not null)
            attachments.Add(new("calibration-coefficients.csv", ReadShared(coefficientsCsv), "text/csv"));
        string? coefficientsWorkbook = files.FirstOrDefault(path => string.Equals(Path.GetFileName(path), "calibration-coefficients.xlsx", StringComparison.OrdinalIgnoreCase));
        if (coefficientsWorkbook is not null)
            attachments.Add(new("calibration-coefficients.xlsx", ReadShared(coefficientsWorkbook), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string file in files)
            {
                string relativePath = Path.GetRelativePath(runDirectory, file).Replace('\\', '/');
                ZipArchiveEntry entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
                using Stream destination = entry.Open();
                using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                source.CopyTo(destination);
            }
        }
        attachments.Add(new($"calibration-{SafeFileName(runId)}-files.zip", buffer.ToArray(), "application/zip"));
        return attachments;
    }

    private static byte[] ReadShared(string path)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool IsTargetPass(CalibrationTargetState status) => status is CalibrationTargetState.Stable or CalibrationTargetState.Overridden;
    private static string StatusText(CalibrationTargetState status) => status == CalibrationTargetState.Overridden ? "Schválený override" : status.ToString();
    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1 ? $"{(int)duration.TotalHours} h {duration.Minutes:00} min" : $"{Math.Max(0, duration.Minutes)} min {Math.Max(0, duration.Seconds):00} s";
    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private static string SafeFileName(string value) => string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
    private static string FormatCoefficients(TemperatureCalibrationResult item)
    {
        var values = new List<string>();
        Add("s1", item.CoefficientS1); Add("s2", item.CoefficientS2); Add("A", item.CoefficientA);
        Add("B", item.CoefficientB); Add("C", item.CoefficientC); Add("D", item.CoefficientD);
        return values.Count == 0 ? "—" : string.Join(" · ", values);

        void Add(string name, double? value)
        {
            if (value is { } number) values.Add($"{name}={number.ToString("G10", CultureInfo.InvariantCulture)}");
        }
    }
    private static string Cell(string value) => $"<td style=\"padding:9px;border-bottom:1px solid #E5EBF2\">{H(value)}</td>";
    private static string H(string value) => WebUtility.HtmlEncode(value);
}
