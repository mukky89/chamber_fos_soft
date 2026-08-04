using System.Collections.Generic;
using System.Text;

namespace VotschVc3.App.Changelog;

/// <summary>
/// Renders parsed changelog releases into a self-contained, dark-themed HTML page
/// matching the application's look (no external assets – safe to open anywhere).
/// </summary>
public static class ChangelogHtmlWriter
{
    public static string BuildHtml(IReadOnlyList<ChangelogRelease> releases, string title = "Changelog — Riadenie laboratórnych zariadení")
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"sk\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>").Append(Escape(title)).Append("</title>\n");
        sb.Append("<style>\n").Append(Css).Append("\n</style>\n</head>\n<body>\n");

        sb.Append("<header class=\"top\">\n");
        sb.Append("<h1>").Append(Escape(title)).Append("</h1>\n");
        sb.Append("<p class=\"sub\">História verzií · ").Append(releases.Count).Append(" záznamov</p>\n");
        sb.Append("</header>\n<main>\n");

        foreach (ChangelogRelease release in releases)
        {
            sb.Append("<section class=\"release\">\n");
            sb.Append("<div class=\"rel-head\"><span class=\"ver\">v").Append(Escape(release.Version)).Append("</span>");
            if (!string.IsNullOrEmpty(release.Date))
            {
                sb.Append("<span class=\"date\">").Append(Escape(release.Date)).Append("</span>");
            }

            sb.Append("</div>\n");

            foreach (ChangelogSection section in release.Sections)
            {
                sb.Append("<div class=\"sec\">\n");
                sb.Append("<span class=\"chip ").Append(KindClass(section.Kind)).Append("\">")
                    .Append(Escape(section.Title)).Append("</span>\n<ul>\n");
                foreach (string item in section.Items)
                {
                    sb.Append("<li>").Append(InlineHtml(item)).Append("</li>\n");
                }

                sb.Append("</ul>\n</div>\n");
            }

            sb.Append("</section>\n");
        }

        sb.Append("</main>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static string KindClass(ChangelogSectionKind kind) => kind switch
    {
        ChangelogSectionKind.Added => "added",
        ChangelogSectionKind.Changed => "changed",
        ChangelogSectionKind.Fixed => "fixed",
        _ => "other",
    };

    /// <summary>Escapes HTML and converts <c>**bold**</c> to <c>&lt;strong&gt;</c>.</summary>
    private static string InlineHtml(string text)
    {
        var sb = new StringBuilder();
        bool bold = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                sb.Append(bold ? "</strong>" : "<strong>");
                bold = !bold;
                i++;
                continue;
            }

            sb.Append(EscapeChar(text[i]));
        }

        if (bold)
        {
            sb.Append("</strong>");
        }

        return sb.ToString();
    }

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(EscapeChar(c));
        }

        return sb.ToString();
    }

    private static string EscapeChar(char c) => c switch
    {
        '&' => "&amp;",
        '<' => "&lt;",
        '>' => "&gt;",
        '"' => "&quot;",
        _ => c.ToString(),
    };

    private const string Css = @"
:root { color-scheme: dark; }
* { box-sizing: border-box; }
body { margin: 0; padding: 0 16px 64px; background: #181A26; color: #E6E8F2;
       font-family: 'Segoe UI', system-ui, -apple-system, sans-serif; line-height: 1.5; }
.top { max-width: 860px; margin: 0 auto; padding: 32px 0 8px; }
.top h1 { font-size: 26px; margin: 0; font-weight: 600; }
.sub { color: #969BB5; margin: 6px 0 0; font-size: 14px; }
main { max-width: 860px; margin: 0 auto; }
.release { background: #22243A; border: 1px solid #3A3D5C; border-radius: 12px;
           padding: 18px 20px; margin: 18px 0; }
.rel-head { display: flex; align-items: baseline; gap: 12px; border-bottom: 1px solid #3A3D5C;
            padding-bottom: 10px; margin-bottom: 12px; }
.ver { font-size: 19px; font-weight: 700; color: #6F9CF2; }
.date { color: #969BB5; font-size: 13px; }
.sec { margin: 12px 0; }
.chip { display: inline-block; font-size: 12px; font-weight: 600; letter-spacing: .02em;
        padding: 3px 10px; border-radius: 999px; border: 1px solid; }
.chip.added   { color: #4FC17A; border-color: #4FC17A55; background: #4FC17A18; }
.chip.changed { color: #FFB454; border-color: #FFB45455; background: #FFB45418; }
.chip.fixed   { color: #5B8DEF; border-color: #5B8DEF55; background: #5B8DEF18; }
.chip.other   { color: #969BB5; border-color: #969BB555; background: #969BB518; }
ul { margin: 8px 0 0; padding-left: 22px; }
li { margin: 5px 0; }
li::marker { color: #5B8DEF; }
strong { color: #FFFFFF; font-weight: 600; }
@media (max-width: 560px) { .top h1 { font-size: 21px; } }
";
}
