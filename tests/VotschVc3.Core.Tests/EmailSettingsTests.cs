using VotschVc3.Core.Notifications;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>Čo musí byť vyplnené, aby notifikačný e-mail odišiel.</summary>
public class EmailSettingsTests
{
    private static EmailSettings Brevo() => new()
    {
        Method = EmailMethod.BrevoApi,
        Recipient = "test@sylex.sk",
        From = "no-reply@sylex.sk",
        HttpEndpoint = "https://api.brevo.com/v3/smtp/email",
        HttpApiKey = "xkeysib-test",
    };

    [Fact]
    public void CompleteBrevoSettingsAreNotMissingAnything() =>
        Assert.Equal(string.Empty, Brevo().DescribeMissing());

    [Fact]
    public void BrevoModeDoesNotAskForSmtpCredentials()
    {
        EmailSettings settings = Brevo();
        settings.SmtpUser = string.Empty;
        settings.SmtpPassword = string.Empty;

        Assert.Equal(string.Empty, settings.DescribeMissing());
    }

    [Fact]
    public void AMissingApiKeyIsReported()
    {
        EmailSettings settings = Brevo();
        settings.HttpApiKey = string.Empty;

        Assert.Contains("API kľúč", settings.DescribeMissing());
    }

    [Fact]
    public void AMissingRecipientIsReported()
    {
        EmailSettings settings = Brevo();
        settings.Recipient = "  ";

        Assert.Contains("adresát", settings.DescribeMissing());
    }

    [Fact]
    public void TheEnteredKeyWinsAndIsTrimmed()
    {
        EmailSettings settings = Brevo();
        settings.HttpApiKey = "  xkeysib-abc  ";

        Assert.Equal("xkeysib-abc", settings.ResolveApiKey());
    }

    [Fact]
    public void AnEmptyKeyFallsBackToTheEnvironmentVariable()
    {
        EmailSettings settings = Brevo();
        settings.HttpApiKey = string.Empty;
        Environment.SetEnvironmentVariable(EmailSettings.ApiKeyEnvironmentVariable, "xkeysib-from-env");
        try
        {
            Assert.Equal("xkeysib-from-env", settings.ResolveApiKey());
            Assert.Equal(string.Empty, settings.DescribeMissing());
        }
        finally
        {
            Environment.SetEnvironmentVariable(EmailSettings.ApiKeyEnvironmentVariable, null);
        }
    }

    [Fact]
    public void SmtpModeChecksTheServerInsteadOfTheApiKey()
    {
        EmailSettings settings = Brevo();
        settings.Method = EmailMethod.Smtp;
        settings.HttpApiKey = string.Empty;
        settings.SmtpHost = string.Empty;

        string missing = settings.DescribeMissing();

        Assert.Contains("SMTP host", missing);
        Assert.DoesNotContain("API kľúč", missing);
    }
}
