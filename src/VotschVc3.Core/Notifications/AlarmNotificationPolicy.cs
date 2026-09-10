namespace VotschVc3.Core.Notifications;

/// <summary>Shared suppression for connection notifications, not alarm handling.</summary>
public static class AlarmNotificationPolicy
{
    public static bool IsSuppressed(NotificationType type, string message) =>
        type == NotificationType.DeviceAlarm &&
        message.Contains("Strata spojenia: POL-EKO", StringComparison.OrdinalIgnoreCase) &&
        message.Contains("LIMIT_LOGGED_EXT_CLIENT", StringComparison.OrdinalIgnoreCase);
}
