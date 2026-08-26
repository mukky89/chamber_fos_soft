namespace VotschVc3.Core.Charting;

/// <summary>
/// Rounds an auto-scaled value axis to human-readable bounds.
///
/// Scaling straight to the data gives labels like <c>68 / 75,3 / 82,7 / 90 / 97,3 °C</c>
/// and – worse – makes the axis wobble on every pixel while the operator pans a zoomed
/// chart. Snapping the bounds to a "nice" step (1 / 2 / 2,5 / 5 × 10ⁿ) keeps the labels
/// round and the axis still while the window moves.
/// </summary>
public static class NiceAxis
{
    /// <summary>Headroom added above and below the data before rounding.</summary>
    private const double Padding = 0.08;

    /// <summary>
    /// Bounds for <paramref name="intervals"/> equal gridline steps that cover
    /// <paramref name="min"/>..<paramref name="max"/> with a bit of headroom.
    /// </summary>
    public static (double Min, double Max) Round(double min, double max, int intervals = 4)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max))
        {
            return (min, max);
        }

        if (max < min)
        {
            (min, max) = (max, min);
        }

        double range = max - min;
        if (range <= 0)
        {
            // A flat line still needs an axis to sit on.
            min -= 1;
            max += 1;
            range = max - min;
        }

        double padding = range * Padding;
        min -= padding;
        max += padding;
        intervals = Math.Max(1, intervals);

        double step = NiceStep((max - min) / intervals);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            double low = Math.Floor(min / step) * step;
            double high = low + (intervals * step);
            if (high >= max - (step * 1e-9))
            {
                return (low, high);
            }

            step = NextNiceStep(step);
        }

        return (min, max);
    }

    /// <summary>Nearest 1 / 1,5 / 2 / 2,5 / 3 / 5 × 10ⁿ step at or above <paramref name="raw"/>.</summary>
    public static double NiceStep(double raw)
    {
        if (!double.IsFinite(raw) || raw <= 0)
        {
            return 1;
        }

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double normalized = raw / magnitude;
        double nice =
            normalized <= 1 ? 1 :
            normalized <= 1.5 ? 1.5 :
            normalized <= 2 ? 2 :
            normalized <= 2.5 ? 2.5 :
            normalized <= 3 ? 3 :
            normalized <= 5 ? 5 : 10;
        return nice * magnitude;
    }

    /// <summary>The next coarser step in the same series.</summary>
    public static double NextNiceStep(double step)
    {
        if (!double.IsFinite(step) || step <= 0)
        {
            return 1;
        }

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(step)));
        double normalized = step / magnitude;
        double next =
            normalized < 1.2 ? 1.5 :
            normalized < 1.7 ? 2 :
            normalized < 2.2 ? 2.5 :
            normalized < 2.7 ? 3 :
            normalized < 3.5 ? 5 : 10;
        return next * magnitude;
    }
}
