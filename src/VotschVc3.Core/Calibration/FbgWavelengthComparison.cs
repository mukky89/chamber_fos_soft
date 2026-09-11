namespace VotschVc3.Core.Calibration;

public sealed record SylexFbgWavelength(double? Wl, double? WlDropped, double? WlDrop = null, string? FbgType = null)
{
    public double? ExpectedNm => WlDropped ?? (Wl is double wl ? wl - (WlDrop ?? 0) : null);
}

public sealed record FbgWavelengthComparison(bool? Passed, string Detail)
{
    public const double ToleranceNm = 0.5;
    public static FbgWavelengthComparison Evaluate(IEnumerable<(string Label, double Value)> measured,
        IReadOnlyList<SylexFbgWavelength>? expected)
    {
        var actual = measured.OrderBy(p => p.Value).ToArray();
        if (expected is null || expected.Count == 0 || expected.Any(p => p.ExpectedNm is not double v || !double.IsFinite(v) || v <= 0))
            return new(null, "API nemá úplné hodnoty WL – nevyhodnotené.");
        var nominal = expected.Select(p => p.ExpectedNm!.Value).OrderBy(v => v).ToArray();
        if (actual.Length == 0) return new(null, "Čakám na pripojenie snímača…");
        if (actual.Any(p => !double.IsFinite(p.Value) || p.Value <= 0))
            return new(null, "Nameraná WL nie je platná – nevyhodnotené.");
        if (actual.Length != nominal.Length)
            return new(false, $"Počet peakov: namerané {actual.Length}, API {nominal.Length}.\nAPI WL: " + string.Join(" / ", nominal.Select(v => $"{v:F3} nm")));
        // Order-independent one-to-one comparison, never used as persistent peak identity.
        bool pass = true;
        var lines = new List<string>();
        for (int i = 0; i < actual.Length; i++)
        {
            double delta = actual[i].Value - nominal[i];
            bool matches = Math.Abs(delta) <= ToleranceNm + 1e-9;
            pass &= matches;
            lines.Add($"{(matches ? "PASS" : "FAIL")} · {actual[i].Label}: {actual[i].Value:F3} nm · API {nominal[i]:F3} nm · Δ {delta:+0.000;-0.000;0.000} nm");
        }
        return new(pass, string.Join("\n", lines));
    }
}
