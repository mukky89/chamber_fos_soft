using System.Globalization;
using System.Net;
using System.Text;
using VotschVc3.Core.Calibration;

namespace VotschVc3.Core.Notifications;

public sealed record CalibrationCompletionMessage(
    string Subject, string Text, string Html, IReadOnlyList<EmailAttachment> Attachments);

/// <summary>Compact result summary; complete data stays in the server run folder.</summary>
public static class CalibrationCompletionEmail
{
    private static readonly CultureInfo Sk = CultureInfo.GetCultureInfo("sk-SK");
    private static string Key(string device, string sn, string channel, string peak, int index) =>
        $"{device}|{sn}|{channel}|{peak}|{index}";
    private static string Key(TemperatureCalibrationResult r) => Key(r.PeakLoggerDeviceSerialNumber, r.SerialNumber, r.Channel, r.PeakId, r.PeakIndex);
    private static string Key(CalibrationMeasurementResult r) => Key(r.PeakLoggerDeviceSerialNumber, r.SerialNumber, r.Channel, r.PeakId, r.PeakIndex);
    private static bool Pass(string status) => status == "PASS";
    private static string Aggregate(IEnumerable<string> statuses)
    {
        var values = statuses.ToArray();
        if (values.Contains("FAIL")) return "FAIL";
        if (values.Length == 0 || values.Any(s => string.IsNullOrWhiteSpace(s) || s == "N/A")) return "N/A";
        return values.All(Pass) ? "PASS" : "WARNING";
    }

    public static CalibrationCompletionMessage Create(CalibrationRunRecord run, string? serverRunDirectory)
    {
        ArgumentNullException.ThrowIfNull(run);
        var results = run.CalibrationResults.Count > 0 ? run.CalibrationResults : TemperatureCalibrationAnalyzer.Analyze(run);
        var models = results.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToArray());
        var measurements = run.Plateaus.SelectMany(p => p.Targets).GroupBy(Key).ToDictionary(g => g.Key, g => g.ToArray());
        var keys = models.Keys.Union(measurements.Keys).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var failures = new List<string[]>();
        var warnings = new List<string[]>();
        var verification = new List<string[]>();
        int failedPeaks = 0, unknownPeaks = 0, warningPeaks = 0;
        string[] types = ["2nd · ABC", "3rd · ABCD", "FBGS · s1/s2"];
        foreach (string key in keys)
        {
            var peakModels = models.GetValueOrDefault(key) ?? [];
            var samples = measurements.GetValueOrDefault(key) ?? [];
            string sn = peakModels.FirstOrDefault()?.SerialNumber ?? samples[0].SerialNumber;
            string channel = peakModels.FirstOrDefault()?.Channel ?? samples[0].Channel;
            string peak = peakModels.FirstOrDefault()?.PeakId ?? samples[0].PeakId;
            int index = peakModels.FirstOrDefault()?.PeakIndex ?? samples[0].PeakIndex;
            string device = peakModels.FirstOrDefault()?.PeakLoggerDeviceSerialNumber ?? samples[0].PeakLoggerDeviceSerialNumber;
            string source = $"{channel} / {peak} / {index}" + (string.IsNullOrWhiteSpace(device) ? "" : $" · {device}");
            var failed = peakModels.Where(r => r.Result == "FAIL").ToArray();
            var unknown = peakModels.Where(r => r.Result != "PASS" && r.Result != "FAIL").ToArray();
            if (failed.Length > 0) failedPeaks++;
            else if (unknown.Length > 0 || peakModels.Length == 0) unknownPeaks++;
            foreach (var item in failed)
                failures.Add([sn, source, item.CalibrationType, $"Max. chyba {N(item.MaxErrorC)} °C; limit {N(item.ErrorToleranceC)} °C ({LimitPercent(item)}). {item.StabilityProblem}".Trim()]);
            foreach (var item in unknown)
                failures.Add([sn, source, item.CalibrationType, $"N/A – {item.StabilityProblem ?? "Model sa nedá vyhodnotiť."}"]);
            if (peakModels.Length == 0)
                failures.Add([sn, source, "N/A", "Chýbajú kalibračné modely. " + string.Join("; ", samples.Select(s => s.Problem).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct())]);
            string[] problems = samples.Where(s => s.Status != CalibrationTargetState.Stable)
                .Select(s => s.Problem ?? s.Status.ToString())
                .Concat(peakModels.Select(r => r.StabilityProblem ?? ""))
                .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
            if (problems.Length > 0)
            {
                warningPeaks++;
                warnings.Add([sn, source, string.Join("; ", problems)]);
            }
            string position = $"{channel} / {peak} / {index}";
            string[] deltas = types.Select(type => N(peakModels.FirstOrDefault(r => r.CalibrationType == type)?.FinalTemperatureErrorC)).ToArray();
            foreach (var item in peakModels.OrderBy(r => r.CalibrationType, StringComparer.Ordinal))
            {
                verification.Add(new[] { sn, position, item.CalibrationType,
                    N(item.FinalCalculatedTemperatureC), N(item.FinalReferenceTemperatureC), N(item.FinalTemperatureErrorC) }
                    .Concat(deltas).Concat(new[] {
                        item.FinalExpectedLambdaNm is double wavelength && double.IsFinite(wavelength) ? wavelength.ToString("0.000000", Sk) : "N/A",
                        item.FinalCheckStatus, item.FinalCheckProblem ?? "—" }).ToArray());
            }
            if (peakModels.Length == 0)
                verification.Add([sn, position, "N/A", "N/A", "N/A", "N/A", "N/A", "N/A", "N/A", "N/A", "N/A", "Chýbajú kalibračné modely."]);
        }
        string runStatus = run.State switch
        {
            CalibrationRunState.Completed => "DOKONČENÁ",
            CalibrationRunState.CompletedWithWarnings => "DOKONČENÁ S UPOZORNENIAMI",
            _ => $"NEDOKONČENÁ ({run.State})",
        };
        string calibrationStatus = failedPeaks > 0 ? "FAIL" : unknownPeaks > 0 || keys.Length == 0 ? "N/A" : warningPeaks > 0 ? "WARNING" : "PASS";
        string finalStatus = Aggregate(results.Select(r => r.FinalCheckStatus));
        string subject = $"FBG · {run.DisplayProfileId} · {runStatus} · {run.DisplayRunId}";
        string range = run.Plateaus.Count == 0 ? "—" : $"{N(run.Plateaus.Min(p => p.TargetTemperatureC))} až {N(run.Plateaus.Max(p => p.TargetTemperatureC))} °C";
        TimeSpan? duration = run.CompletedAt - run.StartedAt;
        string summary = $"Beh: {runStatus}\nKalibrácia: {calibrationStatus}\nZáverečné overenie: {finalStatus}\n" +
            $"Profil: {run.DisplayProfileId} · {run.ProfileName.Split('·')[0].Trim()}\nRun ID: {run.DisplayRunId}\nKomora: {run.ChamberName}\n" +
            $"Rozsah: {range}\nPlata / peaky: {run.Plateaus.Count} / {keys.Length}\n" +
            $"Nevyhovujúce peaky: {failedPeaks} · nevyhodnotené: {unknownPeaks} · s upozornením: {warningPeaks}\n" +
            $"Začiatok: {run.StartedAt.ToLocalTime():dd.MM.yyyy HH:mm}\nKoniec: {run.CompletedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—"}\n" +
            $"Trvanie: {(duration is { } d ? $"{(int)d.TotalHours} h {d.Minutes:00} min" : "—")}";
        string serverPath = string.IsNullOrWhiteSpace(serverRunDirectory) ? "Serverový priečinok nie je nastavený." : serverRunDirectory;
        string link = string.IsNullOrWhiteSpace(serverRunDirectory) ? H(serverPath) :
            $"<a href=\"{H(new Uri(serverRunDirectory.TrimEnd('\\', '/') + '/').AbsoluteUri)}\" style=\"display:inline-block;background:#1769AA;color:white;padding:14px 18px;text-decoration:none;border-radius:6px\">Otvoriť výsledky kalibrácie na serveri</a><p style=\"word-break:break-all\">{H(serverPath)}</p>";
        var plain = new StringBuilder(summary + "\n\nServerový priečinok: " + serverPath);
        string details = Section("Súbory kalibrácie", link);
        details += Render("Peaky, ktoré neprešli kalibráciou / nevyhodnotené", ["SN", "Kanál / peak", "Model", "Dôvod"], failures,
            keys.Length == 0 ? "Nie sú dostupné výsledky peakov." : "Žiadny peak nemá výsledok FAIL ani N/A.", plain);
        if (warnings.Count > 0)
            details += Render("Upozornenia kalibrácie", ["SN", "Kanál / peak", "Upozornenie"], warnings, "", plain);
        string reference = "WIKA pri kontrolnom odbere: " + string.Join("; ", results.Select(r => N(r.FinalReferenceTemperatureC)).Distinct()) + " °C.";
        string[] finalProblems = results.Select(r => r.FinalCheckProblem).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!).Distinct().ToArray();
        string explanation = reference + " " + string.Join(" ", finalProblems) + " ΔT = teplota vypočítaná z koeficientov − skutočná teplota WIKA. Odchýlka patrí modelu v danom riadku; stĺpce ΔT porovnávajú všetky tri modely toho istého peaku. Lambda at T je WL vypočítaná z modelu pri nameranej teplote WIKA, v nm. N/A znamená chýbajúce overenie.";
        plain.Append("\n\n").Append(explanation);
        details += Section("Podmienky záverečného overenia", H(explanation));
        details += Render("Záverečné overenie pri 25 °C", new[] { "SN", "Kanál / peak / index", "Model", "Teplota z koef. [°C]", "WIKA [°C]", "Odchýlka [°C]", "ΔT 2nd · ABC", "ΔT 3rd · ABCD", "ΔT FBGS · s1/s2", "Lambda at T [nm]", "Overenie", "Problém" }, verification, "N/A – nie sú dostupné kalibračné výsledky.", plain, groupBySerial: true);
        var tone = calibrationStatus == "FAIL" || finalStatus == "FAIL" || run.State is not (CalibrationRunState.Completed or CalibrationRunState.CompletedWithWarnings)
            ? LabControlEmailTemplate.EmailTone.Error
            : calibrationStatus != "PASS" || finalStatus != "PASS" || run.State == CalibrationRunState.CompletedWithWarnings
                ? LabControlEmailTemplate.EmailTone.Warning : LabControlEmailTemplate.EmailTone.Success;
        // Only basic data enters the shared template parser; detail tables occur once.
        return new(subject, plain.ToString(), LabControlEmailTemplate.Create(subject, summary, tone, details, contentWidth: 1600), []);
    }

    private static string LimitPercent(TemperatureCalibrationResult result)
    {
        double range = result.MaximumTemperatureC - result.MinimumTemperatureC;
        double percent = result.ErrorToleranceC / range * 100d;
        return range > 0 && double.IsFinite(range) && double.IsFinite(percent) && percent >= 0
            ? $"{percent.ToString("0.###", Sk)} % kalibračného rozsahu"
            : "% rozsahu: N/A";
    }

    private static string Render(string title, string[] headers, List<string[]> rows, string empty, StringBuilder plain, bool groupBySerial = false)
    {
        if (groupBySerial) rows = rows.GroupBy(row => row[0], StringComparer.OrdinalIgnoreCase).SelectMany(group => group).ToList();
        plain.Append("\n\n").AppendLine(title);
        if (rows.Count > 0) plain.AppendLine(string.Join(" · ", headers));
        foreach (var row in rows) plain.AppendLine(string.Join(" · ", row));
        if (rows.Count == 0) plain.AppendLine(empty);
        string table = rows.Count == 0 ? H(empty) : "<table width=\"100%\" cellspacing=\"0\" style=\"border-collapse:collapse;font-size:12px\"><thead><tr>" +
            string.Concat(headers.Select(h => $"<th style=\"padding:7px;text-align:left;background:#EEF3F8\">{H(h)}</th>")) + "</tr></thead><tbody>" +
            RenderRows(rows, groupBySerial) + "</tbody></table>";
        return Section(title, table);
    }
    private static string RenderRows(List<string[]> rows, bool grouped)
    {
        var html = new StringBuilder();
        int group = -1;
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            bool first = rowIndex == 0 || !string.Equals(rows[rowIndex - 1][0], row[0], StringComparison.OrdinalIgnoreCase);
            bool last = rowIndex == rows.Count - 1 || !string.Equals(rows[rowIndex + 1][0], row[0], StringComparison.OrdinalIgnoreCase);
            if (first) group++;
            string background = grouped && group % 2 == 0 ? "#F2F6FB" : "#FFFFFF";
            html.Append("<tr>");
            for (int column = 0; column < row.Length; column++)
            {
                string borders = grouped
                    ? $"border-top:{(first ? "2px solid #8198B2" : "1px solid #DCE4EE")};border-bottom:{(last ? "2px solid #8198B2" : "1px solid #DCE4EE")};" +
                      (column == 0 ? "border-left:2px solid #8198B2;font-weight:600;" : "") +
                      (column == row.Length - 1 ? "border-right:2px solid #8198B2;" : "")
                    : "border-bottom:1px solid #E5EBF2;";
                html.Append($"<td bgcolor=\"{background}\" style=\"padding:9px 7px;vertical-align:top;background:{background};{borders}\">{H(row[column])}</td>");
            }
            html.Append("</tr>");
        }
        return html.ToString();
    }
    private static string Section(string title, string content) => $"<tr><td style=\"padding:12px 24px 20px;color:#182A40\"><h2 style=\"font-size:17px\">{H(title)}</h2>{content}</td></tr>";
    private static string N(double? value) => value is double n && double.IsFinite(n) ? n.ToString("0.000", Sk) : "N/A";
    private static string H(string value) => WebUtility.HtmlEncode(value);
}
