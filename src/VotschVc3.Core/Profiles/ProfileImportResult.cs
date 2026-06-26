namespace VotschVc3.Core.Profiles;

/// <summary>Outcome of importing a profile from an external file.</summary>
public sealed class ProfileImportResult
{
    public ProfileImportResult(TestProfile profile, string formatDescription, IReadOnlyList<string> warnings)
    {
        Profile = profile;
        FormatDescription = formatDescription;
        Warnings = warnings;
    }

    /// <summary>The imported profile, ready to load into the editor.</summary>
    public TestProfile Profile { get; }

    /// <summary>Human readable description of the detected source format.</summary>
    public string FormatDescription { get; }

    /// <summary>Non-fatal issues encountered while importing (e.g. skipped rows).</summary>
    public IReadOnlyList<string> Warnings { get; }
}
