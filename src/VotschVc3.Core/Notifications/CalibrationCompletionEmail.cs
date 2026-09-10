using System.Globalization;
using System.Net;
using System.Text;
using VotschVc3.Core.Calibration;

namespace VotschVc3.Core.Notifications;

public sealed record CalibrationCompletionMessage(
    string Subject, string Text, string Html, IReadOnlyList<EmailAttachment> Attachments);

/// <summary>Builds the final FBG calibration report with a server-folder link and no attachments.</summary>
public static class CalibrationCompletionEmail
{
    private static readonly CultureInfo SlovakCulture = CultureInfo.GetCultureInfo("sk-SK");

    public static CalibrationCompletionMessage Create(CalibrationRunRecord run, string? serverRunDirectory)
    {
        ArgumentNullException.ThrowIfNull(run);
        string serverPath = string.IsNullOrWhiteSpace(serverRunDirectory) ? "Serverový priečinok nie je nastavený." : serverRunDirectory;

        bool passed = run.State is CalibrationRunState.Completed or CalibrationRunState.CompletedWithWarnings;
        string result = run.State == CalibrationRunState.Completed ? "PASS" : passed ? "PASS S UPOZORNENIAMI" : "FAIL";
        string subjectStatus = run.State == CalibrationRunState.Completed
            ? "COMPLETED"
            : run.State == CalibrationRunState.CompletedWithWarnings ? "COMPLETED WITH WARNINGS" : "FAILED";
        string subject = $"Kalibrácia FBG – {subjectStatus} – {run.DisplayProfileId}";
        string finished = run.CompletedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture) ?? "—";
        TimeSpan? duration = run.CompletedAt - run.StartedAt;
        int targetCount = run.Plateaus.Sum(p => p.Targets.Count);
        int stabilityWarningCount = run.Plateaus.Sum(p => p.Targets.Count(t => t.Status == CalibrationTargetState.CompletedWithStabilityWarning));
        int failedCount = run.Plateaus.Sum(p => p.Targets.Count(t => !IsTargetPass(t.Status) && t.Status != CalibrationTargetState.CompletedWithStabilityWarning));
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
            $"Plata: {run.Plateaus.Count}\r\nFBG výsledky: {targetCount - failedCount - stabilityWarningCount} PASS / {stabilityWarningCount} UPOZORNENIE / {failedCount} FAIL\r\n" +
            $"Kalibračné modely: {calibrationPassCount} PASS / {calibrationResults.Count - calibrationPassCount} FAIL\r\n" +
            $"Upozornenia: {run.Warnings.Count}\r\n\r\nServerový priečinok: {serverPath}";

        string rows = string.Join(string.Empty, run.Plateaus.SelectMany(plateau => plateau.Targets.Select(target =>
        {
            bool targetPassed = IsTargetPass(target.Status);
            bool stabilityWarning = target.Status == CalibrationTargetState.CompletedWithStabilityWarning;
            string targetResult = stabilityWarning ? "UPOZORNENIE" : targetPassed ? "PASS" : "FAIL";
            string color = stabilityWarning ? "#9A6700" : targetPassed ? "#087F5B" : "#C92A2A";
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

        TemperatureCalibrationResult[] coefficientModels = calibrationResults
            .GroupBy(CoefficientColumnKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Channel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.PeakId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.CalibrationType, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string coefficientHeaders = string.Concat(coefficientModels.Select(item => HeaderCell(
            $"{Value(item.Channel)} / {Value(item.PeakId)} · {Value(item.CalibrationType)}")));
        string coefficientRows = string.Join(string.Empty, calibrationResults
            .GroupBy(item => Value(item.SerialNumber), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                Dictionary<string, TemperatureCalibrationResult> models = group
                    .GroupBy(CoefficientColumnKey, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(items => items.Key, items => items.First(), StringComparer.OrdinalIgnoreCase);
                return "<tr>" + Cell(group.Key) + string.Concat(coefficientModels.Select(column =>
                    models.TryGetValue(CoefficientColumnKey(column), out TemperatureCalibrationResult? item)
                        ? CoefficientCell(item)
                        : Cell("—"))) + "</tr>";
            }));
        if (coefficientRows.Length == 0)
            coefficientRows = "<tr><td colspan=\"1\" style=\"padding:14px;color:#C92A2A\">Koeficienty nebolo možné vypočítať – nie sú dostupné aspoň tri platné teplotné body.</td></tr>";

        string folderLink = string.IsNullOrWhiteSpace(serverRunDirectory)
            ? H(serverPath)
            : $"<a href=\"{H(new Uri(serverRunDirectory.TrimEnd('\\', '/') + '/').AbsoluteUri)}\" style=\"color:#1769AA\">Otvoriť priečinok behu na serveri</a><br><span style=\"color:#75849A\">{H(serverPath)}</span>";
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
<thead><tr style="background:#EEF3F8"><th style="padding:9px;text-align:left">SN</th>{coefficientHeaders}</tr></thead>
<tbody>{coefficientRows}</tbody></table></div>
</td></tr>
<tr><td class="content-pad" style="padding:0 32px 28px"><table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#F7F9FC;border:1px solid #E5EBF2;border-radius:10px"><tr><td style="padding:18px 20px;color:#52647C;font-size:13px;line-height:22px;word-break:break-word">
<strong style="color:#182A40">Súbory kalibrácie</strong><br>Výsledky, koeficienty a kompletné dáta nájdete v serverovom priečinku. Dostupnosť súborov závisí od dokončenia synchronizácie.<br>{folderLink}
</td></tr></table></td></tr>
""";

        string FinalValue(double? value) => value is double v && double.IsFinite(v) ? v.ToString("0.000", SlovakCulture) : "N/A";
        string verificationRows = string.Concat(calibrationResults.Select(item =>
            "<tr>" + Cell(item.SerialNumber) + Cell(item.Channel + " / " + item.PeakId + " / " + item.PeakIndex) +
            Cell(item.CalibrationType) + Cell(FinalValue(item.FinalCalculatedTemperatureC)) +
            Cell(FinalValue(item.FinalReferenceTemperatureC)) + Cell(FinalValue(item.FinalTemperatureErrorC)) +
            Cell(item.FinalCheckStatus) + Cell(item.FinalCheckProblem ?? "") + "</tr>"));
        text += "\r\n\r\nZáverečné overenie pri cieli 25 °C – teplota po aplikovaní koeficientov. Porovnanie so skutočnou WIKA; hodnoty sa neupravujú na 25 °C.\r\n";
        foreach (var item in calibrationResults)
            text += $"SN {item.SerialNumber} · {item.Channel}/{item.PeakId}/{item.PeakIndex} · {item.CalibrationType}: " +
                $"teplota z koeficientov {FinalValue(item.FinalCalculatedTemperatureC)} °C; WIKA {FinalValue(item.FinalReferenceTemperatureC)} °C; " +
                $"odchýlka {FinalValue(item.FinalTemperatureErrorC)} °C; {item.FinalCheckStatus}; {item.FinalCheckProblem}\r\n";
        if (calibrationResults.Count == 0)
            verificationRows = "<tr><td colspan=\"8\">N/A – nie sú dostupné kalibračné výsledky.</td></tr>";
        details += "<tr><td style=\"padding:20px 32px\"><h2>Záverečné overenie pri 25 °C</h2>" +
            "<p>Teplota každého peaku vypočítaná zo skutočnej kontrolnej λ pomocou uvedeného modelu. Odchýlka = teplota z koeficientov − WIKA. Cieľ komory je 25 °C.</p>" +
            "<table cellspacing=\"0\" style=\"width:100%;font-size:12px\"><thead><tr>" +
            string.Concat(new[] { "SN", "Kanál / peak / index", "Model", "Teplota z koef. [°C]", "WIKA [°C]", "Odchýlka [°C]", "Overenie", "Problém" }.Select(HeaderCell)) +
            "</tr></thead><tbody>" + verificationRows + "</tbody></table></td></tr>";

        string html = LabControlEmailTemplate.Create(subject, text,
            passed ? LabControlEmailTemplate.EmailTone.Success : LabControlEmailTemplate.EmailTone.Error, details);
        return new(subject, text, html, []);
    }

    private static bool IsTargetPass(CalibrationTargetState status) => status is CalibrationTargetState.Stable or CalibrationTargetState.Overridden;
    private static string StatusText(CalibrationTargetState status) => status switch
    {
        CalibrationTargetState.Overridden => "Schválený override",
        CalibrationTargetState.CompletedWithStabilityWarning => "Vzorkovanie dokončené · problém so stabilizáciou",
        _ => status.ToString(),
    };
    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1 ? $"{(int)duration.TotalHours} h {duration.Minutes:00} min" : $"{Math.Max(0, duration.Minutes)} min {Math.Max(0, duration.Seconds):00} s";
    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private static string FormatCoefficients(TemperatureCalibrationResult item)
    {
        var values = new List<string>();
        Add("s1", item.CoefficientS1); Add("s2", item.CoefficientS2); Add("A", item.CoefficientA);
        Add("B", item.CoefficientB); Add("C", item.CoefficientC); Add("D", item.CoefficientD);
        return values.Count == 0 ? "—" : string.Join(" · ", values);

        void Add(string name, double? value)
        {
            if (value is { } number) values.Add($"{name}={number.ToString("G10", SlovakCulture)}");
        }
    }
    private static string CoefficientColumnKey(TemperatureCalibrationResult item) =>
        $"{item.Channel}|{item.PeakId}|{item.CalibrationType}";
    private static string CoefficientCell(TemperatureCalibrationResult item)
    {
        string color = item.Result == "PASS" ? "#087F5B" : "#C92A2A";
        string value = $"λTref {item.LambdaTRefNm.ToString("0.000000", SlovakCulture)} nm · citlivosť {item.SensitivityPmPerC.ToString("0.######", SlovakCulture)} pm/°C · " +
            $"{FormatCoefficients(item)} · max. chyba {item.MaxErrorC.ToString("0.######", SlovakCulture)} °C · R² {item.RSquared.ToString("0.########", SlovakCulture)} · " +
            $"{Value(item.StabilityStatus)} · {Value(item.Result)}";
        return $"<td style=\"padding:9px;border-bottom:1px solid #E5EBF2;color:{color};font-weight:600;min-width:220px\">{H(value)}</td>";
    }
    private static string HeaderCell(string value) =>
        $"<th style=\"padding:9px;text-align:left;min-width:220px\">{H(value)}</th>";
    private static string Cell(string value) => $"<td style=\"padding:9px;border-bottom:1px solid #E5EBF2\">{H(value)}</td>";
    private static string H(string value) => WebUtility.HtmlEncode(value);
}
