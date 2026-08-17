namespace VotschVc3.Core.Settings;

/// <summary>
/// Dashboard control-mode selectable by an admin in Administrácia → Vzhľad a
/// ovládanie. Determines which device-control layout operators see. Existing
/// installs default to <see cref="Classic"/> so nobody is switched to the new
/// layout without an admin explicitly opting in.
/// </summary>
public enum UiControlMode
{
    /// <summary>The original roomy card layout (unchanged).</summary>
    Classic,

    /// <summary>New compact operational-center dashboard: dense device grid,
    /// status bar with fleet counts and an alarm center.</summary>
    Professional,

    /// <summary>Classic layout, forced to the compact card scale regardless of
    /// the separate "Kompaktný režim nástenky" checkbox.</summary>
    Compact,
}
