using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace VotschVc3.App.Notifications;

/// <summary>Kind of desktop notification – selects the sound and balloon icon.</summary>
public enum DesktopNotificationKind
{
    /// <summary>A profile / queue finished successfully.</summary>
    Success,

    /// <summary>An alarm was raised (limit exceeded, connection lost…).</summary>
    Alarm,
}

/// <summary>
/// Desktop notifications for long-running lab tests: plays a sound, shows a
/// Windows tray balloon (renders as a toast on Windows 10/11) and flashes the
/// taskbar button when the app is in the background – so an operator away from
/// the monitor notices a finished profile or an alarm. Complements the e-mail
/// notification; every call is best-effort and never throws into the caller.
/// </summary>
public static class DesktopNotifier
{
    private static WinForms.NotifyIcon? _tray;

    /// <summary>Shows a notification with a sound appropriate for <paramref name="kind"/>.</summary>
    public static void Notify(string title, string message, DesktopNotificationKind kind)
    {
        try
        {
            (kind == DesktopNotificationKind.Alarm ? SystemSounds.Hand : SystemSounds.Asterisk).Play();

            EnsureTrayIcon();
            _tray?.ShowBalloonTip(
                8000, title, string.IsNullOrWhiteSpace(message) ? title : message,
                kind == DesktopNotificationKind.Alarm ? WinForms.ToolTipIcon.Error : WinForms.ToolTipIcon.Info);

            FlashTaskbarIfInactive();
        }
        catch
        {
            // Notifications are auxiliary – they must never break chamber control.
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
    }

    private static void FlashTaskbarIfInactive()
    {
        Window? window = Application.Current?.MainWindow;
        if (window is null || window.IsActive)
        {
            return;
        }

        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == 0)
        {
            return;
        }

        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = handle,
            dwFlags = 0x00000002 | 0x0000000C, // FLASHW_TASKBAR | FLASHW_TIMERNOFG (until focused)
            uCount = uint.MaxValue,
            dwTimeout = 0,
        };
        _ = FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public nint hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
}
