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
    /// dashboard. Off by default – it takes a lot of vertical space and the device
    /// cards are what an operator watches; the button in the timeline's header
    /// shows it, and the choice is remembered.
    /// </summary>
    public bool ShowTimeline { get; set; }

    /// <summary>
    /// Set once <see cref="ShowTimeline"/>'s new "hidden by default" behaviour has been
    /// applied to a settings file written before it existed. Such a file still carries the
    /// old <c>ShowTimeline = true</c>, so the new default would never reach an existing
    /// installation; <see cref="UiSettingsStore.Load"/> resets it exactly once and records
    /// it here, leaving every later explicit choice of the operator alone.
    /// </summary>
    public bool TimelineDefaultApplied { get; set; }

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

    /// <summary>
    /// Admin toggle (persisted): which dashboard layout operators see —
    /// Administrácia → Vzhľad a ovládanie → Režim ovládania. Defaults to
    /// <see cref="UiControlMode.Classic"/> so existing installs keep the
    /// layout they already know until an admin opts into the new one.
    /// </summary>
    public UiControlMode ControlMode { get; set; } = UiControlMode.Classic;

    /// <summary>
    /// Admin toggle (persisted): require a Yes/No confirmation before the
    /// Professional dashboard stops a device or a running profile. On by
    /// default for the new layout; the Classic layout is unaffected.
    /// </summary>
    public bool ConfirmStopAction { get; set; } = true;

    /// <summary>
    /// Admin toggle (persisted): require a Yes/No confirmation (with range,
    /// duration and cycle count) before the Professional dashboard starts a
    /// profile. On by default; the Classic layout is unaffected.
    /// </summary>
    public bool ConfirmProfileStart { get; set; } = true;

    /// <summary>
    /// Admin toggle (persisted): show the alarm center panel on the
    /// Professional dashboard. On by default.
    /// </summary>
    public bool ShowAlarmCenter { get; set; } = true;

    /// <summary>
    /// Persisted: whether the Professional dashboard's sidebar is collapsed to
    /// icons only. Off by default (full labels shown).
    /// </summary>
    public bool ProfessionalSidebarCollapsed { get; set; }
}
