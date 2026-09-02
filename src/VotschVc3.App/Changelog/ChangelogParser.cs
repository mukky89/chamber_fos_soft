using System;
using System.Collections.Generic;

namespace VotschVc3.App.Changelog;

/// <summary>
/// Minimal parser for the project's <c>CHANGELOG.md</c> (Keep a Changelog style):
/// splits it into releases (<c>## [version] – date</c>), sections (<c>### Title</c>)
/// and bullet items (<c>- …</c>, with indented continuation lines folded in).
/// </summary>
public static class ChangelogParser
{
    public static IReadOnlyList<ChangelogRelease> Parse(string markdown)
    {
        var releases = new List<ChangelogRelease>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return releases;
        }

        string[] lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        string? version = null;
        string date = string.Empty;
        List<ChangelogSection> sections = new();
        string? sectionTitle = null;
        List<string> items = new();

        void FlushSection()
        {
            if (sectionTitle is not null)
            {
                sections.Add(new ChangelogSection(sectionTitle, ClassifySection(sectionTitle), items));
            }

            sectionTitle = null;
            items = new List<string>();
        }

        void FlushRelease()
        {
            FlushSection();
            if (version is not null)
            {
                releases.Add(new ChangelogRelease(version, date, sections));
            }

            version = null;
            date = string.Empty;
            sections = new List<ChangelogSection>();
        }

        foreach (string raw in lines)
        {
            string line = raw.TrimEnd();
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                FlushRelease();

                if (TryParseReleaseHeader(trimmed[3..], out string parsedVersion, out string parsedDate))
                {
                    version = parsedVersion;
                    date = parsedDate;
                }
            }
            else if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                FlushSection();
                sectionTitle = trimmed[4..].Trim();
            }
            else if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                if (sectionTitle is not null)
                {
                    items.Add(trimmed[2..].Trim());
                }
            }
            else if (line.StartsWith("  ", StringComparison.Ordinal) && items.Count > 0)
            {
                // Continuation of the previous bullet (wrapped line).
                items[^1] = $"{items[^1]} {trimmed}".Trim();
            }
        }

        FlushRelease();
        return releases;
    }

    private static bool TryParseReleaseHeader(string header, out string version, out string date)
    {
        // Expected: "[1.26.0] – 2026-07-21" (en dash or hyphen separator).
        version = string.Empty;
        date = string.Empty;

        int open = header.IndexOf('[');
        int close = header.IndexOf(']');
        if (open < 0 || close <= open)
        {
            return false;
        }

        string candidate = header[(open + 1)..close].Trim();
        if (!LooksLikeVersion(candidate))
        {
            // Keep-a-Changelog commonly uses an [Unreleased]/[Nezverejnené]
            // heading. It is not a release and must not render as "vNezverejnené".
            return false;
        }

        version = candidate;
        string rest = header[(close + 1)..].Trim();
        date = ReformatDate(rest.TrimStart('–', '-', ' ').Trim());
        return true;
    }

    private static bool LooksLikeVersion(string value)
    {
        string[] parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        foreach (string part in parts)
        {
            if (!int.TryParse(part, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Turns an ISO date (2026-07-21) into the Slovak dd.MM.yyyy form; passes others through.</summary>
    private static string ReformatDate(string date)
    {
        if (DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime value))
        {
            return value.ToString("dd.MM.yyyy");
        }

        return date;
    }

    private static ChangelogSectionKind ClassifySection(string title)
    {
        string t = title.ToLowerInvariant();
        // "Zmenené / opravené" counts as changed; pure "Opravené" as fixed.
        if (t.Contains("pridan")) return ChangelogSectionKind.Added;
        if (t.Contains("zmen")) return ChangelogSectionKind.Changed;
        if (t.Contains("oprav")) return ChangelogSectionKind.Fixed;
        return ChangelogSectionKind.Other;
    }
}
