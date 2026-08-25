namespace VotschVc3.Core.Profiles;

/// <summary>One reconstructed point of a quick profile: a setpoint and how long it is held there.</summary>
public readonly record struct QuickProfilePoint(double Temperature, double PlateauMinutes);

/// <summary>
/// Reverse-engineers the parameters of the quick profile builder ("Rýchly vytvárač
/// profilov") from an already saved segment list, so an existing profile can be
/// loaded back into the builder with its <em>real</em> parameters – temperature
/// range, step count/size, plateau and ramp length, double peak, descending run,
/// lead-in ramp and closing safety hold – instead of only as a flat list of points.
/// <para>
/// The analysis is deliberately conservative: whatever it cannot recognise as a
/// symmetric sweep stays a plain sequence of points (<see cref="Points"/>), which
/// always reproduces the original profile faithfully.
/// </para>
/// </summary>
public sealed class QuickProfileShape
{
    /// <summary>Temperatures closer than this are considered the same setpoint.</summary>
    private const double TemperatureEpsilon = 0.05;

    /// <summary>Durations closer than this (minutes) are considered equal.</summary>
    private const double MinutesEpsilon = 0.01;

    /// <summary>
    /// Slack allowed when checking that consecutive setpoints are evenly spaced. Setpoints
    /// are rounded to a tenth of a degree, so with a step that does not divide the range
    /// evenly (e.g. 80&#160;°C over 7 intervals) neighbouring differences legitimately vary
    /// by up to 0.1&#160;°C. The candidate sweep is verified exactly afterwards.
    /// </summary>
    private const double StepEpsilon = 0.15;

    /// <summary>Largest intermediate step count the parametric builder accepts.</summary>
    private const int MaxIntermediateSteps = 50;

    private QuickProfileShape(IReadOnlyList<QuickProfilePoint> points) => Points = points;

    /// <summary>The profile as an editable list of setpoints, each with its own hold length.
    /// Excludes the lead-in ramp and the closing safety hold when those were recognised.</summary>
    public IReadOnlyList<QuickProfilePoint> Points { get; }

    /// <summary><c>true</c> when the points form a symmetric sweep the parametric builder can
    /// regenerate exactly, so the sweep fields below are meaningful.</summary>
    public bool IsParametric { get; private init; }

    /// <summary>First (lowest) sweep temperature. Only set when <see cref="IsParametric"/>.</summary>
    public double LowTemperature { get; private init; }

    /// <summary>Last (highest) sweep temperature. Only set when <see cref="IsParametric"/>.</summary>
    public double HighTemperature { get; private init; }

    /// <summary>Number of temperatures strictly between the endpoints. Only set when <see cref="IsParametric"/>.</summary>
    public int IntermediateSteps { get; private init; }

    /// <summary>Temperature difference between two consecutive setpoints. Only set when <see cref="IsParametric"/>.</summary>
    public double TemperatureStep { get; private init; }

    /// <summary>The sweep also runs back down to the low temperature.</summary>
    public bool IncludeDescending { get; private init; }

    /// <summary>The peak is split in two by a lower notch (see <see cref="PeakDipCelsius"/>).</summary>
    public bool DoublePeak { get; private init; }

    /// <summary>How much lower (°C) the notch between the two peaks is.</summary>
    public double PeakDipCelsius { get; private init; }

    /// <summary>The hold length shared by every plateau (the most common one when they differ).</summary>
    public double PlateauMinutes { get; private init; }

    /// <summary>The ramp length shared by the transitions (the most common one when they differ).</summary>
    public double RampMinutes { get; private init; }

    /// <summary>The profile opens with a lead-in ramp to the first setpoint.</summary>
    public bool HasLeadIn { get; private init; }

    /// <summary>Length (min) of the lead-in ramp. Only set when <see cref="HasLeadIn"/>.</summary>
    public double LeadInMinutes { get; private init; }

    /// <summary>The profile closes with a ramp to a safe temperature plus a long hold.</summary>
    public bool HasEndHold { get; private init; }

    /// <summary>Temperature (°C) of the closing safety hold. Only set when <see cref="HasEndHold"/>.</summary>
    public double EndTemperature { get; private init; }

    /// <summary>Length (min) of the closing safety hold. Only set when <see cref="HasEndHold"/>.</summary>
    public double EndHoldMinutes { get; private init; }

    /// <summary>Reconstructs the builder parameters from a saved segment list.</summary>
    public static QuickProfileShape Analyze(IReadOnlyList<ProfileSegment>? segments)
    {
        var body = new List<ProfileSegment>(segments ?? Array.Empty<ProfileSegment>());
        if (body.Count == 0)
        {
            return new QuickProfileShape(Array.Empty<QuickProfilePoint>());
        }

        // A profile built here opens with the lead-in ramp when one was requested, so a
        // leading ramp segment is exactly that. Peeling it off keeps its length (the plain
        // sequence would silently swallow it) and lets the checkbox come back on.
        bool hasLeadIn = false;
        double leadInMinutes = 0;
        if (body.Count > 1 && body[0].IsRamp)
        {
            hasLeadIn = true;
            leadInMinutes = Minutes(body[0]);
            body.RemoveAt(0);
        }

        double bodyRamp = MostCommon(body.Where(s => s.IsRamp).Select(Minutes).ToList(), 20);

        // Closing safety cool-down: ramp to a temperature plus a hold of at least an hour.
        // Only peeled off when its ramp matches the shared ramp length, so re-saving
        // reproduces exactly the same two segments.
        bool hasEndHold = false;
        double endTemperature = 0, endHoldMinutes = 0;
        if (body.Count >= 4 && !body[^1].IsRamp && body[^2].IsRamp &&
            Math.Abs(body[^1].TargetTemperature - body[^2].TargetTemperature) <= TemperatureEpsilon &&
            Minutes(body[^1]) >= 60 &&
            Math.Abs(Minutes(body[^2]) - bodyRamp) <= MinutesEpsilon)
        {
            hasEndHold = true;
            endTemperature = Math.Round(body[^1].TargetTemperature, 1);
            endHoldMinutes = Minutes(body[^1]);
            body.RemoveRange(body.Count - 2, 2);
            bodyRamp = MostCommon(body.Where(s => s.IsRamp).Select(Minutes).ToList(), bodyRamp);
        }

        List<QuickProfilePoint> points = ExtractPoints(body);
        double plateau = MostCommon(points.Select(p => p.PlateauMinutes).ToList(), 30);

        var shape = new QuickProfileShape(points)
        {
            PlateauMinutes = plateau,
            RampMinutes = bodyRamp,
            HasLeadIn = hasLeadIn,
            LeadInMinutes = leadInMinutes,
            HasEndHold = hasEndHold,
            EndTemperature = endTemperature,
            EndHoldMinutes = endHoldMinutes,
        };

        return TryDescribeSweep(body, points, plateau, bodyRamp, shape) ?? shape;
    }

    /// <summary>
    /// Turns segments into points: every hold becomes a point carrying its own duration,
    /// and a ramp establishes the point its following hold then folds into – so per-step
    /// hold times survive a load/save round trip.
    /// </summary>
    private static List<QuickProfilePoint> ExtractPoints(IReadOnlyList<ProfileSegment> segments)
    {
        var result = new List<QuickProfilePoint>();
        foreach (ProfileSegment s in segments)
        {
            double t = Math.Round(s.TargetTemperature, 1);
            double hold = s.IsRamp ? 0 : Math.Max(0, Minutes(s));

            if (result.Count > 0 && Math.Abs(result[^1].Temperature - t) <= TemperatureEpsilon)
            {
                result[^1] = result[^1] with { PlateauMinutes = result[^1].PlateauMinutes + hold };
            }
            else
            {
                result.Add(new QuickProfilePoint(t, hold));
            }
        }

        return result;
    }

    /// <summary>
    /// Recognises the point list as an ascending run (constant step) with an optional
    /// double peak and an optional descending run back down – the exact shape the
    /// parametric builder produces. Returns <c>null</c> when it is anything else, or when
    /// the plateaus / ramps are not uniform (the parametric builder shares one of each).
    /// </summary>
    private static QuickProfileShape? TryDescribeSweep(
        IReadOnlyList<ProfileSegment> body,
        IReadOnlyList<QuickProfilePoint> points,
        double plateau,
        double ramp,
        QuickProfileShape sequenceShape)
    {
        if (points.Count < 2)
        {
            return null;
        }

        // The parametric builder holds every plateau equally long and ramps equally fast.
        if (points.Any(p => Math.Abs(p.PlateauMinutes - plateau) > MinutesEpsilon) ||
            body.Any(s => s.IsRamp && Math.Abs(Minutes(s) - ramp) > MinutesEpsilon))
        {
            return null;
        }

        List<double> temps = points.Select(p => p.Temperature).ToList();
        double step = temps[1] - temps[0];
        if (step <= TemperatureEpsilon)
        {
            return null; // a sweep always starts by going up
        }

        int last = 1;
        while (last + 1 < temps.Count && Math.Abs(temps[last + 1] - temps[last] - step) <= StepEpsilon)
        {
            last++;
        }

        int ascending = last + 1;
        double low = temps[0];
        double high = temps[last];
        if (ascending - 2 > MaxIntermediateSteps)
        {
            return null;
        }

        int i = last + 1;
        bool doublePeak = false;
        double dip = 0;
        if (i + 1 < temps.Count && temps[i] < high - TemperatureEpsilon &&
            Math.Abs(temps[i + 1] - high) <= TemperatureEpsilon)
        {
            doublePeak = true;
            dip = high - temps[i];
            i += 2;
        }

        bool descending = false;
        if (i < temps.Count)
        {
            if (temps.Count - i != ascending - 1)
            {
                return null;
            }

            for (int j = 0; j < ascending - 1; j++)
            {
                if (Math.Abs(temps[i + j] - temps[last - 1 - j]) > TemperatureEpsilon)
                {
                    return null;
                }
            }

            descending = true;
            i = temps.Count;
        }

        if (i != temps.Count)
        {
            return null;
        }

        // Final safety net: regenerate the sweep from the deduced parameters and require it
        // to reproduce every setpoint. Re-saving a loaded profile must never move a setpoint,
        // so anything that does not match exactly stays a plain sequence of points.
        List<double> expected = ExpectedSweepTemperatures(low, high, ascending, doublePeak, dip, descending);
        if (expected.Count != temps.Count)
        {
            return null;
        }

        for (int k = 0; k < temps.Count; k++)
        {
            if (Math.Abs(Math.Round(expected[k], 1) - temps[k]) > TemperatureEpsilon)
            {
                return null;
            }
        }

        return new QuickProfileShape(points)
        {
            IsParametric = true,
            LowTemperature = low,
            HighTemperature = high,
            IntermediateSteps = Math.Max(0, ascending - 2),
            TemperatureStep = step,
            IncludeDescending = descending,
            DoublePeak = doublePeak,
            PeakDipCelsius = dip,
            PlateauMinutes = plateau,
            RampMinutes = ramp,
            HasLeadIn = sequenceShape.HasLeadIn,
            LeadInMinutes = sequenceShape.LeadInMinutes,
            HasEndHold = sequenceShape.HasEndHold,
            EndTemperature = sequenceShape.EndTemperature,
            EndHoldMinutes = sequenceShape.EndHoldMinutes,
        };
    }

    /// <summary>The setpoints the parametric builder produces for the given parameters,
    /// in the order it visits them (ascending run, optional double peak, optional descent).</summary>
    private static List<double> ExpectedSweepTemperatures(
        double low, double high, int ascending, bool doublePeak, double dip, bool descending)
    {
        double delta = (high - low) / (ascending - 1);
        var up = new List<double>(ascending);
        for (int i = 0; i < ascending; i++)
        {
            up.Add(low + delta * i);
        }

        up[ascending - 1] = high; // pin the endpoint, exactly as the builder does

        var temps = new List<double>(up);
        if (doublePeak)
        {
            temps.Add(high - dip);
            temps.Add(high);
        }

        if (descending)
        {
            for (int i = ascending - 2; i >= 0; i--)
            {
                temps.Add(up[i]);
            }
        }

        return temps;
    }

    private static double Minutes(ProfileSegment segment) => segment.Duration.TotalMinutes;

    /// <summary>The most frequent value in the list (ties broken by the first occurrence),
    /// or <paramref name="fallback"/> when the list is empty.</summary>
    private static double MostCommon(List<double> values, double fallback)
    {
        if (values.Count == 0)
        {
            return fallback;
        }

        return values
            .GroupBy(v => Math.Round(v, 2))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => values.IndexOf(g.First()))
            .First()
            .First();
    }
}
