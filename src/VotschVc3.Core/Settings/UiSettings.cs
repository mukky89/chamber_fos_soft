namespace VotschVc3.Core.Settings;

/// <summary>
/// Global user-interface preferences that are not tied to a single chamber.
/// Persisted to a small JSON file so a lab keeps its choices across restarts.
/// </summary>
public sealed class UiSettings
{
    /// <summary>
    /// When <c>true</c> the dashboard cards show the reorder arrows (◀ ▶) for
    /// administrators, letting them change the order of chambers. Hidden by
    /// default so operators never see the controls by accident.
    /// </summary>
    public bool AllowChamberReorder { get; set; }

    /// <summary>
    /// When <c>true</c> the dashboard uses a compact layout – smaller cards,
    /// graphics and text – so more devices fit on screen. Off by default (the
    /// original, roomier layout).
    /// </summary>
    public bool CompactMode { get; set; }

    /// <summary>
    /// When <c>true</c> the fleet timeline (Gantt) is shown at the top of the
    /// dashboard. On by default; can be hidden to save vertical space.
    /// </summary>
    public bool ShowTimeline { get; set; } = true;

    /// <summary>
    /// How often, in seconds, a row is written to the per-profile temperature log
    /// while a profile runs. Default 30 s; polling itself stays faster, but the log
    /// keeps at most one row per this interval so the CSV files stay compact.
    /// </summary>
    public int ProfileLogIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// When <c>true</c> the POL-EKO drying oven (Sušiareň) is shown on the dashboard
    /// and connected automatically. Off by default – the lab does not normally use
    /// it, so it stays hidden until an admin turns it on.
    /// </summary>
    public bool ShowPolEko { get; set; }

    /// <summary>
    /// Tolerance (°C) for the guaranteed soak on SIKA thermal baths: on every hold the
    /// bath first reaches the target within this band before the dwell time starts.
    /// Small by default (0.3 °C) so the bath settles precisely on temperature.
    /// </summary>
    public double SikaSoakToleranceC { get; set; } = 0.3;
}
