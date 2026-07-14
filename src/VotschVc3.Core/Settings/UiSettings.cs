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
}
