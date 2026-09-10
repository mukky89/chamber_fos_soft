using System.Net;
using System.Text;

namespace VotschVc3.Core.Notifications;

/// <summary>
/// Shared notification design. Table layout and inline styles are the baseline;
/// mobile CSS only enhances spacing. No remote images, fonts or scripts are required.
/// </summary>
public static class LabControlEmailTemplate
{
    public static string Create(string subject, string body) =>
        Create(subject, body, DetectTone(subject ?? string.Empty, body ?? string.Empty), string.Empty);

    // Only application-owned markup belongs in additionalHtml. All notification data is encoded.
    internal static string Create(string subject, string body, EmailTone tone, string additionalHtml, int contentWidth = 680)
    {
        subject ??= string.Empty;
        body ??= string.Empty;
        (string accent, string soft, string badge, string _) = tone switch
        {
            EmailTone.Error => ("#B42335", "#FFF1F2", "CHYBA / ALARM", "🚨"),
            EmailTone.Warning => ("#925C0D", "#FFF7E8", "UPOZORNENIE", "⚠️"),
            EmailTone.Success => ("#167451", "#EDF8F2", "DOKONČENÉ", "✅"),
            _ => ("#285DB5", "#EEF4FF", "INFORMÁCIA", "ℹ️"),
        };
        ParseBody(body, out var details, out string message);
        string preheader = string.IsNullOrWhiteSpace(message) ? subject : message.Split('\n')[0];
        var detailRows = new StringBuilder();
        int rowIndex = 0;
        foreach ((string key, string value) in details)
        {
            string rowColor = rowIndex++ % 2 == 0 ? "#F5F8FC" : "#FFFFFF";
            detailRows.Append($"""
<tr bgcolor="{rowColor}" style="background:{rowColor}"><td width="28%" style="width:28%;padding:11px 12px;border-bottom:1px solid #E5EBF2;vertical-align:top;color:#66758B;font-size:13px;line-height:21px;word-break:break-word">{H(key)}</td>
<td style="padding:11px 12px;border-bottom:1px solid #E5EBF2;vertical-align:top;color:#182A40;font-size:14px;line-height:21px;font-weight:600;word-break:break-word;overflow-wrap:anywhere">{H(value)}</td></tr>
""");
        }
        string detailsHtml = details.Count == 0 ? string.Empty : $"""
<tr><td class="content-pad" style="padding:0 32px 28px">
<h2 style="margin:0 0 10px;font-size:12px;line-height:18px;font-weight:700;letter-spacing:1.2px;color:#66758B">DETAILY UDALOSTI</h2>
<table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="width:100%;table-layout:fixed;border-collapse:collapse">{detailRows}</table>
</td></tr>
""";
        string messageHtml = string.IsNullOrWhiteSpace(message) ? string.Empty : $"""
<table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="width:100%;background:{soft};border-radius:10px">
<tr><td style="padding:16px 18px;border-left:4px solid {accent};font-size:14px;line-height:23px;color:#24364D;word-break:break-word;overflow-wrap:anywhere">{H(message).Replace("\n", "<br>")}</td></tr>
</table>
""";
        const string responsiveStyles = """
<style>
body,table,td { -webkit-text-size-adjust:100%; -ms-text-size-adjust:100%; }
table,td { mso-table-lspace:0pt; mso-table-rspace:0pt; }
@media only screen and (max-width:600px) {
 .outer-pad { padding:16px 8px !important; }
 .content-pad { padding-left:20px !important; padding-right:20px !important; }
 .email-title { font-size:21px !important; line-height:28px !important; }
}
</style>
""";
        return $"""
<!doctype html>
<html lang="sk" xmlns="http://www.w3.org/1999/xhtml">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="light"><meta name="supported-color-schemes" content="light">
<title>{H(subject)}</title>
{responsiveStyles}
</head>
<body style="margin:0;padding:0;width:100%;background:#EDF1F6;font-family:Segoe UI,Arial,Helvetica,sans-serif;color:#182A40">
<div style="display:none;font-size:1px;line-height:1px;color:#EDF1F6;max-height:0;max-width:0;overflow:hidden;opacity:0;mso-hide:all">{H(preheader)}</div>
<table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" bgcolor="#EDF1F6" style="width:100%;background:#EDF1F6">
<tr><td align="center" class="outer-pad" style="padding:24px 12px">
<!--[if mso]><table role="presentation" width="{contentWidth}" align="center" cellspacing="0" cellpadding="0" border="0"><tr><td><![endif]-->
<table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="width:100%;max-width:{contentWidth}px;table-layout:fixed;font-family:Segoe UI,Arial,Helvetica,sans-serif;background:#FFFFFF;border:1px solid #DCE4EE;border-radius:16px;overflow:hidden">
<tr><td height="5" bgcolor="{accent}" style="height:5px;background:{accent};font-size:1px;line-height:5px">&nbsp;</td></tr>
<tr><td class="content-pad" bgcolor="#122237" style="padding:18px 32px;background:#122237">
<table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0"><tr>
<td style="font-size:12px;line-height:20px;font-weight:700;letter-spacing:1px;color:#D5E1EF">SYLEX · LAB CONTROL</td>
<td align="right" style="font-size:11px;line-height:18px;color:#A9BED8">NOTIFIKÁCIA</td>
</tr></table>
</td></tr>
<tr><td class="content-pad" style="padding:24px 32px 18px">
<table role="presentation" cellspacing="0" cellpadding="0" border="0"><tr><td bgcolor="{soft}" style="padding:6px 10px;background:{soft};border:1px solid {accent};border-radius:6px;color:{accent};font-size:11px;line-height:16px;letter-spacing:0.6px;font-weight:700">{badge}</td></tr></table>
<h1 class="email-title" style="margin:14px 0 0;color:#182A40;font-size:23px;line-height:31px;font-weight:700;word-break:break-word;overflow-wrap:anywhere">{H(subject)}</h1>
<p style="margin:8px 0 0;color:#66758B;font-size:12px;line-height:19px">Riadenie komôr a FBG kalibrácia</p>
</td></tr>
<tr><td class="content-pad" style="padding:0 32px 24px">{messageHtml}</td></tr>
{detailsHtml}
{additionalHtml}
<tr><td class="content-pad" bgcolor="#F7F9FC" style="padding:16px 32px;background:#F7F9FC;border-top:1px solid #E5EBF2">
<p style="margin:0 0 4px;color:#52647C;font-size:12px;line-height:19px;font-weight:600">Automatická správa · Lab Control</p>
<p style="margin:0;color:#75849A;font-size:12px;line-height:19px">Podrobnosti nájdete v aplikácii. Na tento e-mail nie je potrebné odpovedať.</p>
</td></tr>
</table>
<!--[if mso]></td></tr></table><![endif]-->
</td></tr></table>
</body></html>
""";
    }

    internal static string DecorateSubject(string subject, string body)
    {
        string symbol = DetectTone(subject, body) switch
        {
            EmailTone.Error => "🚨",
            EmailTone.Warning => "⚠️",
            EmailTone.Success => "✅",
            _ => "ℹ️",
        };
        string title = subject.Trim();
        foreach (string previousIcon in new[] { "🚨", "⚠️", "⚠", "✅", "ℹ️", "ℹ", "✓", "✔" })
        {
            if (!title.StartsWith(previousIcon, StringComparison.Ordinal)) continue;
            title = title[previousIcon.Length..].TrimStart();
            break;
        }
        return $"{symbol} {title}";
    }
    private static void ParseBody(string body, out List<(string Key, string Value)> details, out string message)
    {
        details = new();
        var lines = new List<string>();
        bool detailsPhase = true;
        foreach (string rawLine in body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = rawLine.TrimEnd();
            if (detailsPhase && string.IsNullOrWhiteSpace(line))
            {
                if (details.Count > 0) detailsPhase = false;
                continue;
            }
            if (detailsPhase)
            {
                int colon = line.IndexOf(':');
                if (colon > 0 && colon <= 28)
                {
                    string key = line[..colon].Trim(), value = line[(colon + 1)..].Trim();
                    if (key.Length > 0 && value.Length > 0)
                    {
                        details.Add((key, value));
                        continue;
                    }
                }
                detailsPhase = false;
            }
            lines.Add(line);
        }
        message = string.Join("\n", lines).Trim();
    }

    private static EmailTone DetectTone(string subject, string body)
    {
        // Subject wins: metadata such as "Upozornenia: 0" must not override completion.
        if (ContainsAny(subject, "chyba", "error", "failed", "zlyhan", "alarm", "kalibrácia zastavená")) return EmailTone.Error;
        if (ContainsAny(subject, "warning", "upozornen", "pozor", "timeout", "neustál", "nestabil", "čaká na rozhodnutie", "operátorský dohľad")) return EmailTone.Warning;
        if (ContainsAny(subject, "completed", "dokončen", "úspešne", "success")) return EmailTone.Success;
        if (ContainsAny(body, "chyba", "error", "failed", "zlyhan", "alarm", "kalibrácia zastavená")) return EmailTone.Error;
        if (ContainsAny(body, "warning", "upozornen", "pozor", "timeout", "neustál", "nestabil", "čaká na rozhodnutie", "operátorský dohľad")) return EmailTone.Warning;
        if (ContainsAny(body, "completed", "dokončen", "úspešne", "success")) return EmailTone.Success;
        return EmailTone.Info;
    }
    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    private static string H(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
    internal enum EmailTone { Info, Success, Warning, Error }
}
