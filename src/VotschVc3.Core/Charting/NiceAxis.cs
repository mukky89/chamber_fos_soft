namespace VotschVc3.Core.Charting;

/// <summary>
/// Rounds an auto-scaled value axis to human-readable bounds.
///
/// Scaling straight to the data gives labels like <c>68 / 75,3 / 82,7 / 90 / 97,3 °C</c>
/// and – worse – makes the axis wobble on every pixel while the operator pans a zoomed
/// chart. Snapping the bounds to a "nice" step (1 / 2 / 2,5 / 5 × 10ⁿ) keeps the labels
/// round and the axis still while the window moves.
/// </summary>
/// <summary>
/// A resolved value axis: the bounds to draw, the step between two gridlines and how
/// many steps there are (so the renderer draws <c>Intervals + 1</c> labels).
/// </summary>
/// <param name="Min">Lower bound of the axis.</param>
/// <param name="Max">Upper bound of the axis.</param>
/// <param name="Step">Distance between two gridlines.</param>
/// <param name="Intervals">Number of steps between <paramref name="Min"/> and <paramref name="Max"/>.</param>
public readonly record struct ValueAxis(double Min, double Max, double Step, int Intervals)
{
    public double Span => Max - Min;

    /// <summary>Value of the <paramref name="index"/>-th gridline, counted from the bottom.</summary>
    public double LabelAt(int index) => Min + (index * Step);
}

public static class NiceAxis
{
    /// <summary>
    /// An axis whose bounds are exactly the supplied data bounds. Use for planned
    /// profile charts where the operator expects the scale to state the configured
    /// minimum and maximum without visual padding beyond either limit.
    /// </summary>
    public static ValueAxis Exact(double min, double max, int intervals = 4)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max))
        {
            return new ValueAxis(min, max, 1, 1);
        }

        if (max < min) (min, max) = (max, min);
        if (max - min <= 0)
        {
            min -= 1;
            max += 1;
        }

        intervals = Math.Max(1, intervals);
        return new ValueAxis(min, max, (max - min) / intervals, intervals);
    }

    /// <summary>
    /// Bounds for exactly <paramref name="intervals"/> equal gridline steps covering
    /// <paramref name="min"/>..<paramref name="max"/>. Prefer <see cref="Scale"/>, which
    /// is free to pick the number of steps and therefore crops much closer to the data.
    /// </summary>
    public static (double Min, double Max) Round(double min, double max, int intervals = 4)
    {
        ValueAxis axis = Fit(min, max, Math.Max(1, intervals));
        return (axis.Min, axis.Max);
    }

    /// <summary>
    /// The tightest readable value axis for <paramref name="min"/>..<paramref name="max"/>:
    /// round labels on a 1 / 1,5 / 2 / 2,5 / 3 / 5 × 10ⁿ step, and as little empty plot
    /// above and below the data as those labels allow.
    /// <para>
    /// The number of gridline steps is part of the search, not a fixed 4 – forcing four
    /// steps is what put a -40…120 °C profile on an axis running from -100 to 300 °C,
    /// with the curve squashed into the bottom third of the chart.
    /// </para>
    /// </summary>
    /// <param name="min">Lowest value that must be visible.</param>
    /// <param name="max">Highest value that must be visible.</param>
    /// <param name="minIntervals">Fewest gridline steps to consider.</param>
    /// <param name="maxIntervals">Most gridline steps to consider.</param>
    public static ValueAxis Scale(double min, double max, int minIntervals = 3, int maxIntervals = 6)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max))
        {
            return new ValueAxis(min, max, 1, 1);
        }

        if (max < min)
        {
            (min, max) = (max, min);
        }

        if (max - min <= 0)
        {
            // A flat line still needs an axis to sit on.
            min -= 1;
            max += 1;
        }

        minIntervals = Math.Max(1, minIntervals);
        maxIntervals = Math.Max(minIntervals, maxIntervals);

        ValueAxis best = Fit(min, max, maxIntervals);
        for (int intervals = minIntervals; intervals <= maxIntervals; intervals++)
        {
            ValueAxis candidate = Fit(min, max, intervals);

            // Smallest axis wins; on a tie take the one with fewer labels.
            if (candidate.Span < best.Span - 1e-9 ||
                (candidate.Span < best.Span + 1e-9 && candidate.Intervals < best.Intervals))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// The smallest nice step whose gridlines cover the data in at most
    /// <paramref name="intervals"/> steps, padded out to exactly that many.
    /// </summary>
    private static ValueAxis Fit(double min, double max, int intervals)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max))
        {
            return new ValueAxis(min, max, 1, intervals);
        }

        if (max < min)
        {
            (min, max) = (max, min);
        }

        if (max - min <= 0)
        {
            min -= 1;
            max += 1;
        }

        double step = NiceStep((max - min) / intervals);
        for (int attempt = 0; attempt < 14; attempt++)
        {
            // Snap outwards to the nearest gridline on both sides, then check whether the
            // data really fits. Flooring only the lower bound and adding `intervals` steps
            // on top (what this used to do) throws all the slack above the data.
            double low = Math.Floor((min / step) + 1e-9) * step;
            double high = Math.Ceiling((max / step) - 1e-9) * step;
            int used = (int)Math.Round((high - low) / step);
            if (used <= intervals)
            {
                // Pad out to exactly `intervals` equal steps, always extending the side
                // that currently sits closer to the data, so the curve stays centred.
                while (used < intervals)
                {
                    if (min - low <= high - max)
                    {
                        low -= step;
                    }
                    else
                    {
                        high += step;
                    }

                    used++;
                }

                return new ValueAxis(low, high, step, intervals);
            }

            step = NextNiceStep(step);
        }

        return new ValueAxis(min, max, (max - min) / intervals, intervals);
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

    /// <summary>
    /// Steps a time axis is comfortably read in: whole minutes, quarters, halves, hours,
    /// then multiples of an hour up to whole days and weeks. A purely decimal step
    /// (<see cref="NiceStep"/>) puts gridlines at 1000 or 2000 minutes, which nobody
    /// converts to hours in their head.
    /// </summary>
    private static readonly double[] TimeSteps =
    {
        1, 2, 5, 10, 15, 20, 30, 60, 120, 180, 240, 360, 480, 720, 1440, 2880, 4320, 10080,
    };

    /// <summary>
    /// Gridline step (minutes) for a time axis spanning <paramref name="spanMinutes"/>,
    /// aiming for roughly <paramref name="targetTicks"/> labels.
    /// </summary>
    public static double NiceTimeStep(double spanMinutes, int targetTicks = 6)
    {
        if (!double.IsFinite(spanMinutes) || spanMinutes <= 0)
        {
            return 1;
        }

        double raw = spanMinutes / Math.Max(1, targetTicks);
        foreach (double step in TimeSteps)
        {
            if (step >= raw)
            {
                return step;
            }
        }

        // Longer than a few weeks per gridline – fall back to the decimal ladder, rounded
        // to whole days so the labels stay on day boundaries.
        return Math.Max(1440, Math.Round(NiceStep(raw) / 1440) * 1440);
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
