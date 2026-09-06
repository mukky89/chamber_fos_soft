namespace VotschVc3.Core.Calibration;

/// <summary>Compares physical PeakLogger rows without treating a refreshed interrogator SN as rewiring.</summary>
public static class PeakTopologyComparer
{
    public static bool AreEquivalent(IEnumerable<string> live, IEnumerable<string> displayed)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(displayed);

        HashSet<string> liveSet = live.ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> displayedSet = displayed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (liveSet.SetEquals(displayedSet)) return true;

        // During reconnect/resume the same /peaks rows can temporarily arrive without an
        // interrogator serial or with a refreshed API-side identifier. For row-add/remove UX,
        // channel + Peak ID are the stable physical topology. A real channel/peak change still
        // remains different and is reported.
        return liveSet.Select(WithoutSource).ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(displayedSet.Select(WithoutSource));
    }

    private static string WithoutSource(string identity)
    {
        int separator = identity.IndexOf('|');
        return separator < 0 ? identity : identity[(separator + 1)..];
    }
}
