using System.Globalization;
using System.Net;
using System.Text;
using VotschVc3.Core.Recording;

namespace VotschVc3.Core.Notifications;

public sealed record ProfileCompletionInfo(
    string ChamberName,
    IReadOnlyList<string> ProfileNames,
    DateTime Started,
    DateTime Finished,
    bool ChamberPoweredOff,
    IReadOnlyList<ProfileTemperatureSample> Samples,
    string? LogFilePath);

public sealed record ProfileCompletionMessage(
    string Subject, string Text, string Html, IReadOnlyList<EmailAttachment> Attachments);

public static class ProfileCompletionEmail
{
    public static ProfileCompletionMessage Create(ProfileCompletionInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        string profiles = string.Join(", ", info.ProfileNames);
        string subject = $"Profil dokončený: {profiles} ({info.ChamberName})";
        TimeSpan duration = info.Finished - info.Started;
        string durationText = duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours} h {duration.Minutes} min"
            : $"{Math.Max(0, duration.Minutes)} min {Math.Max(0, duration.Seconds)} s";
        string status = info.ChamberPoweredOff ? "Výkon komory bol bezpečne vypnutý." : "Pozor: vypnutie výkonu komory sa nepotvrdilo.";
        bool hasCsv = !string.IsNullOrWhiteSpace(info.LogFilePath) && File.Exists(info.LogFilePath);
        string attachmentSummary = hasCsv
            ? "K správe je priložený graf teploty a CSV záznam merania."
            : "K správe je priložený graf teploty. CSV záznam pre tento beh nie je dostupný.";
        string text = $"Profil: {profiles}\r\nZariadenie: {info.ChamberName}\r\nSpustené: {info.Started:dd.MM.yyyy HH:mm:ss}\r\nDokončené: {info.Finished:dd.MM.yyyy HH:mm:ss}\r\nTrvanie: {durationText}\r\n\r\n{status}\r\n{attachmentSummary}";
        const string chartCid = "temperature-chart";
        byte[] chart = Encoding.UTF8.GetBytes(BuildChart(info.Samples));
        var attachments = new List<EmailAttachment>
        {
            new("graf-teplot.svg", chart, "image/svg+xml", chartCid),
        };
        if (hasCsv)
        {
            attachments.Add(new EmailAttachment(Path.GetFileName(info.LogFilePath!), File.ReadAllBytes(info.LogFilePath!), "text/csv"));
        }

        string chartSection = $"""
<tr><td class="content-pad" style="padding:0 32px 28px">
<h2 style="margin:0 0 12px;color:#182A40;font-size:19px;line-height:26px;font-weight:600">Priebeh teploty</h2>
<p style="margin:0 0 14px;color:#66758B;font-size:13px;line-height:21px">Vzorky: {info.Samples.Count} · Nastavená a nameraná teplota počas behu</p>
<img src="cid:{chartCid}" alt="Graf nastavenej a nameranej teploty. Podrobnosti sú dostupné v aplikácii Lab Control." width="614" style="display:block;width:100%;max-width:614px;height:auto;border:1px solid #E5EBF2;border-radius:10px;color:#66758B;font-size:13px;line-height:21px">
<p style="margin:12px 0 0;color:#75849A;font-size:12px;line-height:19px">Zobrazenie grafu závisí od e-mailového klienta. Kompletný priebeh nájdete v aplikácii.</p>
</td></tr>
<tr><td class="content-pad" style="padding:0 32px 28px">
<table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="width:100%;background:#F7F9FC;border:1px solid #E5EBF2;border-radius:10px">
<tr><td style="padding:18px 20px;font-size:13px;line-height:22px;color:#52647C;word-break:break-word;overflow-wrap:anywhere">
<strong style="color:#182A40">Súbory k tomuto behu</strong><br>graf-teplot.svg{(hasCsv ? "<br>" + H(Path.GetFileName(info.LogFilePath!)) : "<br>CSV záznam nie je dostupný.")}
</td></tr></table>
</td></tr>
""";
        string html = LabControlEmailTemplate.Create(subject, text,
            info.ChamberPoweredOff ? LabControlEmailTemplate.EmailTone.Success : LabControlEmailTemplate.EmailTone.Warning,
            chartSection);
        return new ProfileCompletionMessage(subject, text, html, attachments);
    }

    private static string BuildChart(IReadOnlyList<ProfileTemperatureSample> samples)
    {
        const int width = 1160, height = 440, left = 76, top = 35, plotW = 1040, plotH = 335;
        if (samples.Count == 0)
            return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\"><rect width=\"100%\" height=\"100%\" fill=\"#f8fafc\"/><text x=\"50%\" y=\"50%\" text-anchor=\"middle\" font-family=\"Arial\" fill=\"#64748b\">Pre tento beh nie sú dostupné vzorky teploty.</text></svg>";
        var values = samples.SelectMany(s => s.Measured is { } m ? new[] { s.Setpoint, m } : new[] { s.Setpoint }).ToArray();
        double min = Math.Floor(values.Min() / 5) * 5 - 5, max = Math.Ceiling(values.Max() / 5) * 5 + 5;
        if (max <= min) max = min + 10;
        double X(int i) => left + (samples.Count == 1 ? plotW / 2d : i * plotW / (samples.Count - 1d));
        double Y(double value) => top + (max - value) / (max - min) * plotH;
        string Points(Func<ProfileTemperatureSample, double?> pick) => string.Join(' ', samples.Select((s, i) => pick(s) is { } v ? FormattableString.Invariant($"{X(i):0.0},{Y(v):0.0}") : null).Where(x => x is not null));
        var sb = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\"><rect width=\"100%\" height=\"100%\" rx=\"16\" fill=\"#f8fafc\"/>");
        for (int i = 0; i <= 5; i++) { double y = top + i * plotH / 5d, v = max - i * (max - min) / 5d; sb.Append(CultureInfo.InvariantCulture, $"<line x1=\"{left}\" y1=\"{y:0.0}\" x2=\"{left + plotW}\" y2=\"{y:0.0}\" stroke=\"#dce3ec\"/><text x=\"{left - 12}\" y=\"{y + 5:0.0}\" text-anchor=\"end\" font-family=\"Arial\" font-size=\"22\" fill=\"#64748b\">{v:0.#} °C</text>"); }
        sb.Append($"<polyline points=\"{Points(s => s.Setpoint)}\" fill=\"none\" stroke=\"#e31e36\" stroke-width=\"5\" stroke-linejoin=\"round\"/><polyline points=\"{Points(s => s.Measured)}\" fill=\"none\" stroke=\"#2563eb\" stroke-width=\"5\" stroke-linejoin=\"round\"/><text x=\"{left}\" y=\"420\" font-family=\"Arial\" font-size=\"22\" fill=\"#e31e36\">● Setpoint</text><text x=\"260\" y=\"420\" font-family=\"Arial\" font-size=\"22\" fill=\"#2563eb\">● Nameraná teplota</text></svg>");
        return sb.ToString();
    }

    private static string H(string value) => WebUtility.HtmlEncode(value);
}
