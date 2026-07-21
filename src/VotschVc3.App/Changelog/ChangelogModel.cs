using System.Collections.Generic;

namespace VotschVc3.App.Changelog;

/// <summary>Category of a changelog section, used to colour-code it.</summary>
public enum ChangelogSectionKind
{
    Other,
    Added,
    Changed,
    Fixed,
}

/// <summary>One "### …" block within a release (e.g. "Pridané") and its bullet items.</summary>
public sealed class ChangelogSection
{
    public ChangelogSection(string title, ChangelogSectionKind kind, IReadOnlyList<string> items)
    {
        Title = title;
        Kind = kind;
        Items = items;
    }

    public string Title { get; }

    public ChangelogSectionKind Kind { get; }

    /// <summary>Bullet items, still carrying <c>**bold**</c> markers for inline rendering.</summary>
    public IReadOnlyList<string> Items { get; }
}

/// <summary>One "## [version] – date" release entry with its sections.</summary>
public sealed class ChangelogRelease
{
    public ChangelogRelease(string version, string date, IReadOnlyList<ChangelogSection> sections)
    {
        Version = version;
        Date = date;
        Sections = sections;
    }

    public string Version { get; }

    public string Date { get; }

    public IReadOnlyList<ChangelogSection> Sections { get; }

    /// <summary>Header label shown on the release card (e.g. "v1.26.0 · 21.07.2026").</summary>
    public string Header => string.IsNullOrEmpty(Date) ? $"v{Version}" : $"v{Version} · {Date}";
}
