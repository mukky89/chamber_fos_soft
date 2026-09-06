using System.Windows;
using WinForms = System.Windows.Forms;

namespace VotschVc3.App.Notifications;

/// <summary>Kind of desktop notification – selects the sound and balloon icon.</summary>
public enum DesktopNotificationKind
{
    /// <summary>A profile / queue finished successfully.</summary>
    Success,

    /// <summary>A non-critical warning (out-of-range value, failed action…).</summary>
    Warning,

    /// <summary>An alarm was raised (limit exceeded, connection lost…).</summary>
    Alarm,
}

/// <summary>
/// Desktop notifications for long-running lab tests. When the app is visible the same event
/// goes through AppNotificationService so every operator-facing transient message uses
/// one in-app popup UX. The tray icon remains navigation-only and does not duplicate alerts.
/// </summary>
public static class DesktopNotifier
{
    private static WinForms.NotifyIcon? _tray;

    /// <summary>Invoked when the user asks to re-open the app from the tray (double-click or menu).</summary>
    public static Action? ShowRequested { get; set; }

    /// <summary>Invoked when the user picks "Ukončiť" from the tray menu.</summary>
    public static Action? ExitRequested { get; set; }

    /// <summary>
    /// Ensures the tray icon exists and shows a short balloon telling the operator the
    /// app keeps running in the background (used when the window is closed to the tray).
    /// </summary>
    public static void ShowMinimizedToTrayHint()
    {
        try
        {
            EnsureTrayIcon();
            _tray?.ShowBalloonTip(
                4000,
                "Beží na pozadí",
                "Aplikácia je stále spustená a riadi zariadenia. Otvoríš ju dvojklikom na ikonu v oznamovacej oblasti alebo cez menu ikony → Zobraziť.",
                WinForms.ToolTipIcon.Info);
        }
        catch
        {
            // Best-effort only.
        }
    }

    /// <summary>Shows one application notification in the unified in-app toast host.</summary>
    public static void Notify(string title, string message, DesktopNotificationKind kind)
    {
        try
        {
            AppNotificationService.Show(
                title,
                message,
                kind switch
                {
                    DesktopNotificationKind.Alarm => AppNotificationKind.Error,
                    DesktopNotificationKind.Warning => AppNotificationKind.Warning,
                    _ => AppNotificationKind.Success,
                },
                dedupeKey: $"desktop:{kind}:{title}:{message}");
        }
        catch
        {
            // In-app notifications are auxiliary. Continue with the desktop path.
        }

    }

    /// <summary>Removes the tray icon (call on application exit).</summary>
    public static void Shutdown()
    {
        _tray?.Dispose();
        _tray = null;
    }

    private static void EnsureTrayIcon()
    {
        if (_tray is not null)
        {
            return;
        }

        System.Drawing.Icon? icon = null;
        try
        {
            if (Environment.ProcessPath is { } path)
            {
                icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Fall through to the generic application icon.
        }

        _tray = new WinForms.NotifyIcon
        {
            Icon = icon ?? System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Riadenie laboratórnych zariadení",
        };

        // Right-click menu: re-open the app or exit; double-click re-opens it.
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Zobraziť aplikáciu", null, (_, _) => ShowRequested?.Invoke());
        menu.Items.Add("Ukončiť…", null, (_, _) => ExitRequested?.Invoke());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

}
