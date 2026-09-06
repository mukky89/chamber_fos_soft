namespace VotschVc3.Core.Calibration;

/// <summary>Compares PeakLogger topology snapshots without treating an omitted source SN as rewiring.</summary>
public static class PeakTopologyComparer
{
    public static bool AreEquivalent(IEnumerable<string> live, IEnumerable<string> displayed)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(displayed);

        HashSet<string> liveSet = live.ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> displayedSet = displayed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (liveSet.SetEquals(displayedSet)) return true;

        // Some PeakLogger /peaks responses omit the interrogator serial after a service or
        // calibration resume. Channel + peak still describe the same physical topology.
        if (liveSet.Any(HasMissingSource) || displayedSet.Any(HasMissingSource))
            return liveSet.Select(WithoutSource).ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(displayedSet.Select(WithoutSource));

        return false;
    }

    private static bool HasMissingSource(string identity) =>
        string.IsNullOrWhiteSpace(identity.Split('|', 2)[0]);

    private static string WithoutSource(string identity)
    {
        int separator = identity.IndexOf('|');
        return separator < 0 ? identity : identity[(separator + 1)..];
    }
}
