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

    /// <summary>Nastaví premenné prostredia na dobu testu a potom ich vždy upratá.</summary>
    private static void WithEnvironment(Action body, params (string Name, string? Value)[] variables)
    {
        var previous = variables.Select(v => (v.Name, Old: Environment.GetEnvironmentVariable(v.Name))).ToList();
        foreach ((string name, string? value) in variables)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        try
        {
            body();
        }
        finally
        {
            foreach ((string name, string? old) in previous)
            {
                Environment.SetEnvironmentVariable(name, old);
            }
        }
    }

    [Fact]
    public void AnEmptySenderComesFromTheEnvironment()
    {
        EmailSettings settings = Brevo();
        settings.From = string.Empty;

        WithEnvironment(
            () =>
            {
                Assert.Equal("no-reply@mmucka.xyz", settings.ResolveFrom());
                Assert.Equal(string.Empty, settings.DescribeMissing());
                Assert.Contains("odosielateľ", settings.DescribeEnvironmentSources());
            },
            (EmailEnvironment.Sender, "  no-reply@mmucka.xyz  "));
    }

    [Fact]
    public void AnEnteredSenderBeatsTheEnvironment()
    {
        EmailSettings settings = Brevo();
        settings.From = "ruly@sylex.sk";

        WithEnvironment(
            () =>
            {
                Assert.Equal("ruly@sylex.sk", settings.ResolveFrom());
                Assert.DoesNotContain("odosielateľ", settings.DescribeEnvironmentSources());
            },
            (EmailEnvironment.Sender, "no-reply@mmucka.xyz"));
    }

    [Fact]
    public void SmtpLoginAndPasswordComeFromTheEnvironment()
    {
        var settings = new EmailSettings
        {
            Method = EmailMethod.Smtp,
            Recipient = "test@sylex.sk",
            From = string.Empty,
            SmtpHost = string.Empty,
            SmtpPort = 0,
            SmtpUser = string.Empty,
            SmtpPassword = string.Empty,
        };

        WithEnvironment(
            () =>
            {
                Assert.Equal("smtp-relay.brevo.com", settings.ResolveSmtpHost());
                Assert.Equal(587, settings.ResolveSmtpPort());
                Assert.Equal("a98111001@smtp-brevo.com", settings.ResolveSmtpUser());
                Assert.Equal("secret", settings.ResolveSmtpPassword());
                Assert.Equal(string.Empty, settings.DescribeMissing());
            },
            (EmailEnvironment.Sender, "no-reply@mmucka.xyz"),
            (EmailEnvironment.SmtpHost, "smtp-relay.brevo.com"),
            (EmailEnvironment.SmtpPort, "587"),
            (EmailEnvironment.SmtpUser, "a98111001@smtp-brevo.com"),
            (EmailEnvironment.SmtpPassword, "secret"));
    }

    [Fact]
    public void ANonNumericPortFromTheEnvironmentIsIgnored()
    {
        EmailSettings settings = Brevo();
        settings.SmtpPort = 0;

        WithEnvironment(
            () => Assert.Equal(0, settings.ResolveSmtpPort()),
            (EmailEnvironment.SmtpPort, "nezmysel"));
    }

    [Fact]
    public void NothingIsReportedAsComingFromTheEnvironmentWhenNoVariablesAreSet()
    {
        EmailSettings settings = Brevo();

        WithEnvironment(
            () => Assert.Equal(string.Empty, settings.DescribeEnvironmentSources()),
            (EmailEnvironment.Sender, null),
            (EmailEnvironment.ApiKey, null));
    }
}
