using System.Net;
using System.Text;

namespace VotschVc3.Core.Notifications;

/// <summary>
/// Shared, mail-client friendly HTML shell for Lab Control notifications. The template intentionally
/// uses only inline CSS and table layout so it renders consistently in Outlook desktop/web and
/// mobile clients. Plain-text key/value lines before the first blank line are promoted to a compact
/// details card; the remaining text is rendered as the event message.
/// </summary>
public static class LabControlEmailTemplate
{
    public static string Create(string subject, string body)
    {
        subject ??= string.Empty;
        body ??= string.Empty;

        EmailTone tone = DetectTone(subject, body);
        (string accent, string soft, string badge, string icon) = tone switch
        {
            EmailTone.Error => ("#c93647", "#fff1f2", "CHYBA", "!"),
            EmailTone.Warning => ("#c47a0a", "#fff7e8", "UPOZORNENIE", "!"),
            EmailTone.Success => ("#1f8a5b", "#edf9f3", "DOKONČENÉ", "✓"),
            _ => ("#3b6fd8", "#eef4ff", "INFORMÁCIA", "i"),
        };

        ParseBody(body, out List<(string Key, string Value)> details, out string message);

        var detailRows = new StringBuilder();
        foreach ((string key, string value) in details)
        {
            detailRows.Append("<tr>")
                .Append("<td style=\"padding:8px 10px 8px 0;color:#6f7b90;font-size:13px;vertical-align:top;white-space:nowrap\">")
                .Append(H(key))
                .Append("</td>")
                .Append("<td style=\"padding:8px 0;color:#182235;font-size:14px;font-weight:600;vertical-align:top\">")
                .Append(H(value))
                .Append("</td></tr>");
        }

        string detailsHtml = details.Count == 0
            ? string.Empty
            : $"<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"margin:20px 0 0;background:#f7f9fc;border:1px solid #e4e9f1;border-radius:10px\"><tr><td style=\"padding:14px 18px\"><table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\">{detailRows}</table></td></tr></table>";

        string messageHtml = string.IsNullOrWhiteSpace(message)
            ? string.Empty
            : $"<div style=\"margin:20px 0 0;padding:16px 18px;background:{soft};border-left:4px solid {accent};border-radius:8px;color:#253149;font-size:15px;line-height:1.55\">{H(message).Replace("\r\n", "<br>").Replace("\n", "<br>")}</div>";

        return $"""
<!doctype html>
<html lang="sk">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
</head>
<body style="margin:0;background:#eef2f7;font-family:Segoe UI,Arial,Helvetica,sans-serif;color:#182235">
  <div style="display:none;max-height:0;overflow:hidden;opacity:0">{H(subject)}</div>
  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="width:100%;background:#eef2f7">
    <tr>
      <td align="center" style="padding:28px 12px">
        <table role="presentation" width="660" cellspacing="0" cellpadding="0" style="width:100%;max-width:660px;background:#ffffff;border:1px solid #dfe5ee;border-radius:14px;overflow:hidden">
          <tr>
            <td style="padding:0;background:#172033">
              <div style="height:5px;background:{accent};font-size:0;line-height:0">&nbsp;</div>
              <div style="padding:22px 28px 24px;color:#ffffff">
                <div style="font-size:12px;letter-spacing:1.6px;color:#aebbd0;font-weight:700">SYLEX · LAB CONTROL</div>
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="margin-top:10px">
                  <tr>
                    <td style="vertical-align:middle">
                      <div style="font-size:24px;line-height:1.25;font-weight:700;color:#ffffff">{H(subject)}</div>
                    </td>
                    <td align="right" style="width:120px;vertical-align:middle;padding-left:14px">
                      <span style="display:inline-block;background:{accent};color:#ffffff;border-radius:999px;padding:7px 11px;font-size:11px;font-weight:800;letter-spacing:.6px;white-space:nowrap">{icon}&nbsp;&nbsp;{badge}</span>
                    </td>
                  </tr>
                </table>
              </div>
            </td>
          </tr>
          <tr>
            <td style="padding:26px 28px 28px">
              {detailsHtml}
              {messageHtml}
            </td>
          </tr>
          <tr>
            <td style="padding:15px 28px;background:#f7f9fc;border-top:1px solid #e8edf4;color:#7c8799;font-size:12px;line-height:1.45">
              Automatická správa z aplikácie Lab Control. Na tento e-mail nie je potrebné odpovedať.
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>
""";
    }

    private static void ParseBody(
        string body,
        out List<(string Key, string Value)> details,
        out string message)
    {
        details = new List<(string Key, string Value)>();
        var messageLines = new List<string>();
        bool detailsPhase = true;

        foreach (string rawLine in body.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.TrimEnd();
            if (detailsPhase && string.IsNullOrWhiteSpace(line))
            {
                if (details.Count > 0)
                {
                    detailsPhase = false;
                    continue;
                }
            }

            if (detailsPhase)
            {
                int colon = line.IndexOf(':');
                if (colon > 0 && colon <= 28)
                {
                    string key = line[..colon].Trim();
                    string value = line[(colon + 1)..].Trim();
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                    {
                        details.Add((key, value));
                        continue;
                    }
                }

                if (details.Count > 0)
                {
                    detailsPhase = false;
                }
            }

            messageLines.Add(line);
        }

        message = string.Join("\n", messageLines).Trim();
    }

    private static EmailTone DetectTone(string subject, string body)
    {
        // The subject is authoritative. This prevents a clean completion email containing
        // a metadata row like "Upozornenia: 0" from being styled as a warning.
        if (ContainsAny(subject, "chyba", "error", "failed", "zlyhan", "alarm")) return EmailTone.Error;
        if (ContainsAny(subject, "warning", "upozornen", "pozor", "timeout", "neustál", "nestabil")) return EmailTone.Warning;
        if (ContainsAny(subject, "completed", "dokončen", "úspešne", "success")) return EmailTone.Success;

        if (ContainsAny(body, "chyba", "error", "failed", "zlyhan", "alarm")) return EmailTone.Error;
        if (ContainsAny(body, "warning", "upozornen", "pozor", "timeout", "neustál", "nestabil")) return EmailTone.Warning;
        if (ContainsAny(body, "completed", "dokončen", "úspešne", "success")) return EmailTone.Success;
        return EmailTone.Info;
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string H(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private enum EmailTone
    {
        Info,
        Success,
        Warning,
        Error,
    }
}
