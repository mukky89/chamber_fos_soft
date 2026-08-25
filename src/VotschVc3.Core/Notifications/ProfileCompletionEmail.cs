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
        string text = $"Profil: {profiles}\r\nZariadenie: {info.ChamberName}\r\nDokončené: {info.Finished:dd.MM.yyyy HH:mm:ss}\r\nTrvanie: {durationText}\r\n{status}\r\nLog teploty je priložený k e-mailu.";

        const string chartCid = "temperature-chart";
        byte[] chart = Encoding.UTF8.GetBytes(BuildChart(info.Samples));
        var attachments = new List<EmailAttachment>
        {
            new("graf-teplot.svg", chart, "image/svg+xml", chartCid),
        };
        if (!string.IsNullOrWhiteSpace(info.LogFilePath) && File.Exists(info.LogFilePath))
        {
            attachments.Add(new EmailAttachment(Path.GetFileName(info.LogFilePath), File.ReadAllBytes(info.LogFilePath), "text/csv"));
        }

        string safeProfiles = H(profiles);
        string warningColor = info.ChamberPoweredOff ? "#15803d" : "#b45309";
        string badge = info.ChamberPoweredOff ? "DOKONČENÉ" : "SKONTROLUJ KOMORU";
        string html = $@"<!doctype html><html lang=""sk""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width""></head>
<body style=""margin:0;background:#eef2f6;font-family:Arial,Helvetica,sans-serif;color:#18212f""><div style=""display:none;max-height:0;overflow:hidden"">{H(subject)}</div>
<table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" style=""background:#eef2f6""><tr><td align=""center"" style=""padding:28px 12px""><table role=""presentation"" width=""640"" cellspacing=""0"" cellpadding=""0"" style=""max-width:640px;width:100%;background:#fff;border-radius:14px;overflow:hidden;box-shadow:0 8px 30px rgba(20,32,50,.10)""><tr><td style=""background:#172033;padding:24px 30px;color:#fff""><div style=""font-size:13px;letter-spacing:1.5px;color:#a9b7cc"">SYLEX · LAB CONTROL</div><div style=""font-size:27px;font-weight:bold;margin-top:7px"">Profil bol dokončený</div></td></tr>
<tr><td style=""padding:28px 30px""><span style=""display:inline-block;background:{warningColor};color:#fff;border-radius:20px;padding:7px 12px;font-size:12px;font-weight:bold;letter-spacing:.7px"">{badge}</span><h2 style=""font-size:21px;margin:18px 0 6px"">{safeProfiles}</h2><p style=""margin:0 0 22px;color:#647084"">Zariadenie {H(info.ChamberName)}</p>
<table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" style=""background:#f6f8fb;border-radius:10px""><tr><td style=""padding:15px""><b>Spustené</b><br><span style=""color:#647084"">{info.Started:dd.MM.yyyy HH:mm:ss}</span></td><td style=""padding:15px""><b>Dokončené</b><br><span style=""color:#647084"">{info.Finished:dd.MM.yyyy HH:mm:ss}</span></td><td style=""padding:15px""><b>Trvanie</b><br><span style=""color:#647084"">{durationText}</span></td></tr></table>
<h3 style=""margin:26px 0 10px"">Priebeh teploty</h3><img src=""cid:{chartCid}"" alt=""Graf nastavenej a nameranej teploty"" width=""580"" style=""width:100%;max-width:580px;height:auto;border:1px solid #dfe5ed;border-radius:10px"">
<p style=""margin:20px 0 0;padding:13px 15px;border-left:4px solid {warningColor};background:#f8fafc"">{H(status)}</p><p style=""font-size:13px;color:#647084;margin:18px 0 0"">Kompletný záznam merania nájdeš v priloženom CSV súbore.</p></td></tr><tr><td style=""background:#f6f8fb;padding:17px 30px;color:#7b8798;font-size:12px"">Automatická správa z aplikácie Lab Control.</td></tr></table></td></tr></table></body></html>";
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
        string Points(Func<ProfileTemperatureSample, double?> pick) => string.Join(' ', samples.Select((s, i) => pick(s) is { } v ? $"{X(i):0.0},{Y(v):0.0}" : null).Where(x => x is not null));
        var sb = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\"><rect width=\"100%\" height=\"100%\" rx=\"16\" fill=\"#f8fafc\"/>");
        for (int i = 0; i <= 5; i++) { double y = top + i * plotH / 5d, v = max - i * (max - min) / 5d; sb.Append(CultureInfo.InvariantCulture, $"<line x1=\"{left}\" y1=\"{y:0.0}\" x2=\"{left + plotW}\" y2=\"{y:0.0}\" stroke=\"#dce3ec\"/><text x=\"{left - 12}\" y=\"{y + 5:0.0}\" text-anchor=\"end\" font-family=\"Arial\" font-size=\"22\" fill=\"#64748b\">{v:0.#} °C</text>"); }
        sb.Append($"<polyline points=\"{Points(s => s.Setpoint)}\" fill=\"none\" stroke=\"#e31e36\" stroke-width=\"5\" stroke-linejoin=\"round\"/><polyline points=\"{Points(s => s.Measured)}\" fill=\"none\" stroke=\"#2563eb\" stroke-width=\"5\" stroke-linejoin=\"round\"/><text x=\"{left}\" y=\"420\" font-family=\"Arial\" font-size=\"22\" fill=\"#e31e36\">● Setpoint</text><text x=\"260\" y=\"420\" font-family=\"Arial\" font-size=\"22\" fill=\"#2563eb\">● Nameraná teplota</text></svg>");
        return sb.ToString();
    }

    private static string H(string value) => WebUtility.HtmlEncode(value);
}
