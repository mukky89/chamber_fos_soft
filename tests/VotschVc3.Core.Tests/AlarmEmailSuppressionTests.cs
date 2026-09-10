using VotschVc3.Core.Notifications;
using Xunit;

namespace VotschVc3.Core.Tests;

public class AlarmEmailSuppressionTests
{
    [Theory]
    [InlineData(NotificationType.DeviceAlarm, "Strata spojenia: POL-EKO GET_STATUS: LIMIT_LOGGED_EXT_CLIENT", true)]
    [InlineData(NotificationType.DeviceAlarm, "Strata spojenia: pol-eko GET_STATUS: limit_logged_ext_client", true)]
    [InlineData(NotificationType.DeviceAlarm, "Strata spojenia: POL-EKO GET_STATUS: TIMEOUT", false)]
    [InlineData(NotificationType.DeviceAlarm, "Teplota 95 °C mimo limitu [10; 80]", false)]
    [InlineData(NotificationType.DeviceAlarm, "STOP zlyhal: POL-EKO LIMIT_LOGGED_EXT_CLIENT", false)]
    [InlineData(NotificationType.CalibrationWarning, "Strata spojenia: POL-EKO GET_STATUS: LIMIT_LOGGED_EXT_CLIENT", false)]
    public async Task OnlyClientLimitConnectionAlarmIsSkipped(NotificationType type, string message, bool skipped)
    {
        Assert.Equal(skipped, AlarmNotificationPolicy.IsSuppressed(type, message));

        var notifier = new EmailNotifier
        {
            Settings = new EmailSettings
            {
                Enabled = true,
                Recipient = "test@example.invalid",
                Method = EmailMethod.Http,
                HttpEndpoint = string.Empty,
            },
        };

        var result = await notifier.SendAsync(type, "⚠ ALARM – Sušiareň",
            $"Komora: Sušiareň\r\nČas: 10.09.2026 07:48:31\r\n\r\n{message}");

        Assert.Equal(skipped, result.Skipped);
        Assert.False(result.Sent);
        if (skipped)
            Assert.Null(result.Error);
        else
            // Reaching local transport validation proves other alarms are not filtered.
            Assert.Equal("HTTP endpoint nie je nastavený.", result.Error);
    }
}
