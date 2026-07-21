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
                (version, date) = ParseReleaseHeader(trimmed[3..]);
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

    private static (string version, string date) ParseReleaseHeader(string header)
    {
        // Expected: "[1.26.0] – 2026-07-21" (en dash or hyphen separator).
        string version = header.Trim();
        string date = string.Empty;

        int open = header.IndexOf('[');
        int close = header.IndexOf(']');
        if (open >= 0 && close > open)
        {
            version = header[(open + 1)..close].Trim();
            string rest = header[(close + 1)..].Trim();
            date = rest.TrimStart('–', '-', ' ').Trim();
        }

        return (version, ReformatDate(date));
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
