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
    /// When <c>true</c> the POL-EKO devices are disabled: hidden from the
    /// dashboard and the timeline, and the app does not connect to them.
    /// On (disabled) by default; an admin can re-enable them in the admin screen.
    /// </summary>
    public bool PolEkoDisabled { get; set; } = true;
}
