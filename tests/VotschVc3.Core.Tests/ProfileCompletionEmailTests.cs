using VotschVc3.Core.Notifications;
using VotschVc3.Core.Recording;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class ProfileCompletionEmailTests
{
    [Fact]
    public void DefaultsMatchDashboardSmtpAndRequestedRecipients()
    {
        var settings = new EmailSettings();
        Assert.Equal("smtp-relay.brevo.com", settings.SmtpHost);
        Assert.Equal(587, settings.SmtpPort);
        Assert.Equal(EmailMethod.BrevoApi, settings.Method);
        Assert.Equal("no-reply@sylex.sk", settings.From);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", settings.HttpEndpoint);
        Assert.Equal(3, EmailAddressParser.Parse(settings.Recipient).Count);
    }

    [Fact]
    public void CompletionMessageContainsChartAndCsvAttachment()
    {
        string log = Path.GetTempFileName();
        try
        {
            File.WriteAllText(log, "Čas;Setpoint °C;Teplota komory °C\n");
            DateTime start = new(2026, 8, 24, 8, 0, 0);
            ProfileCompletionMessage message = ProfileCompletionEmail.Create(new ProfileCompletionInfo(
                "VT 3", ["FOS cyklus"], start, start.AddHours(2), true,
                [new(start, 23, 22.8, null, null), new(start.AddHours(2), 80, 79.7, null, null)], log));

            Assert.Contains("FOS cyklus", message.Subject);
            Assert.Contains("cid:temperature-chart", message.Html);
            Assert.Contains(message.Attachments, a => a.ContentId == "temperature-chart" && a.MediaType == "image/svg+xml");
            Assert.Contains(message.Attachments, a => a.MediaType == "text/csv");
        }
        finally
        {
            File.Delete(log);
        }
    }

    [Fact]
    public void RecipientParserSupportsSemicolonsCommasAndDuplicates()
    {
        IReadOnlyList<string> recipients = EmailAddressParser.Parse("a@sylex.sk; b@sylex.sk, A@sylex.sk");
        Assert.Equal(["a@sylex.sk", "b@sylex.sk"], recipients);
    }
}
