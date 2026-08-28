namespace VotschVc3.Core.Profiles;

/// <summary>
/// The SYLEX sensor product codes offered as ready-made choices in the "Snímače" pickers,
/// so an operator picks a type instead of retyping it (and spelling it differently every
/// time, which would split the profile library's grouping).
/// <para>
/// Sensors only – the S-line accessories (Scan, Switch, Splitter, Comp, Battery Pack) are
/// deliberately left out: a temperature profile is never authored "for" one of those.
/// Types actually used by saved profiles are merged on top of this list, so anything the
/// lab adds later still shows up without a new build.
/// </para>
/// </summary>
public static class SensorCatalog
{
    /// <summary>Known sensor type codes, alphabetically.</summary>
    public static IReadOnlyList<string> Types { get; } = new[]
    {
        "D-04",
        "DSP-01",
        "DSP-01/T",
        "DSS-00",
        "DSS-00/T",
        "DSS-01",
        "DTP-01",
        "DTP-02",
        "ES-03",
        "FFA-01",
        "FFA-01 FBGS",
        "GFA-01",
        "HS-01",
        "HS-02",
        "HTA-01",
        "LLS-01",
        "MS-03",
        "MS-11",
        "MSA-01",
        "P-05",
        "SAA-01",
        "SAA-02",
        "SAA-04",
        "SAT-01",
        "SAT-02",
        "SAT-03",
        "SB-01",
        "SC-01",
        "SC-01/T",
        "SDS-02",
        "SDS-02/T",
        "SF-01",
        "SF-02",
        "SG-01 FBGS",
        "SSR-01/T",
        "STS-03",
        "STS-04",
        "STS-11",
        "SWA-00",
        "SWA-00/T",
        "SWA-01",
        "SWS-02",
        "SWS-02/T",
        "SWS-03",
        "TC-04",
        "TP-01",
        "TP-03",
        "TPA-01",
    };

    /// <summary>
    /// The catalogue merged with the types <paramref name="used"/> by the saved profiles,
    /// case-insensitively de-duplicated and sorted – what the "Snímače" picker offers.
    /// </summary>
    public static List<string> Merge(IEnumerable<string?>? used) =>
        Types
            .Concat((used ?? Enumerable.Empty<string?>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
