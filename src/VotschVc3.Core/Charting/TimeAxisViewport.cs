namespace VotschVc3.Core.Charting;

/// <summary>Visible window of a chart's X axis, resolved against the current data range.</summary>
/// <param name="Min">First visible X value (data units).</param>
/// <param name="Max">Last visible X value (data units).</param>
/// <param name="Zoom">How many times the window is narrower than the whole range; 1 = everything.</param>
public readonly record struct AxisWindow(double Min, double Max, double Zoom)
{
    public double Span => Max - Min;

    /// <summary>True when the window really is a slice of the data, not the whole range.</summary>
    public bool IsZoomed => Zoom > 1.0001;
}

/// <summary>
/// Zoom/pan state of a chart's X (time) axis, shared by the profile editor and the
/// measurement charts.
///
/// The window is kept in <b>data units</b> (minutes), not as a fraction of the range,
/// so that a live chart which keeps appending points does not drag the operator's
/// view along with it. Every resolve re-clamps the window against the data range,
/// so a shrinking range (a different profile, a cleared recording) can never leave
/// the viewport pointing outside the data.
/// </summary>
public sealed class TimeAxisViewport
{
    /// <summary>Hard ceiling so a single stray wheel gesture cannot zoom into nothing.</summary>
    public const double MaxZoom = 200;

    private readonly double _minimumSpan;
    private double _start = double.NaN;
    private double _span = double.NaN;

    /// <param name="minimumSpan">Narrowest window in data units (e.g. 1 minute).</param>
    public TimeAxisViewport(double minimumSpan = 1)
    {
        _minimumSpan = minimumSpan > 0 ? minimumSpan : 1;
    }

    /// <summary>True while a slice (not the whole range) is selected.</summary>
    public bool IsZoomed => !double.IsNaN(_span);

    /// <summary>Back to the whole data range.</summary>
    public void Reset()
    {
        _start = double.NaN;
        _span = double.NaN;
    }

    /// <summary>
    /// Returns the window to draw for the given data range, re-clamping (and storing)
    /// the state so it stays inside the range.
    /// </summary>
    public AxisWindow Resolve(double dataMin, double dataMax)
    {
        double full = dataMax - dataMin;
        if (full <= 0 || !IsZoomed)
        {
            return new AxisWindow(dataMin, dataMax, 1);
        }

        double span = Math.Clamp(_span, MinimumSpanFor(full), full);
        double start = Math.Clamp(_start, dataMin, dataMax - span);
        _span = span;
        _start = start;
        return new AxisWindow(start, start + span, full / span);
    }

    /// <summary>
    /// Zooms by <paramref name="factor"/> (&gt; 1 zooms in) while keeping the value
    /// under <paramref name="anchorFraction"/> — the cursor position across the plot,
    /// 0 = left edge, 1 = right edge — in place.
    /// </summary>
    /// <returns><c>true</c> when the window actually changed.</returns>
    public bool Zoom(double factor, double anchorFraction, double dataMin, double dataMax)
    {
        double full = dataMax - dataMin;
        if (full <= 0 || factor <= 0 || double.IsNaN(factor))
        {
            return false;
        }

        AxisWindow current = Resolve(dataMin, dataMax);
        double newSpan = Math.Clamp(current.Span / factor, MinimumSpanFor(full), full);
        if (Math.Abs(newSpan - current.Span) <= current.Span * 1e-6)
        {
            return false;
        }

        if (newSpan >= full)
        {
            Reset();
            return true;
        }

        double anchor = Math.Clamp(anchorFraction, 0, 1);
        double anchorValue = current.Min + anchor * current.Span;
        _span = newSpan;
        _start = Math.Clamp(anchorValue - anchor * newSpan, dataMin, dataMax - newSpan);
        return true;
    }

    /// <summary>Moves the window so that it starts at <paramref name="start"/> (data units).</summary>
    /// <returns><c>true</c> when the window actually moved.</returns>
    public bool MoveTo(double start, double dataMin, double dataMax)
    {
        if (!IsZoomed || double.IsNaN(start))
        {
            return false;
        }

        AxisWindow current = Resolve(dataMin, dataMax);
        double clamped = Math.Clamp(start, dataMin, dataMax - current.Span);
        if (Math.Abs(clamped - current.Min) <= current.Span * 1e-6)
        {
            return false;
        }

        _start = clamped;
        return true;
    }

    private double MinimumSpanFor(double full) => Math.Min(Math.Max(_minimumSpan, full / MaxZoom), full);
}
