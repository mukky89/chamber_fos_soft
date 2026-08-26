namespace VotschVc3.Core.Notifications;

/// <summary>
/// Environment variables the e-mail settings fall back to when a field is left empty. The
/// names match the ones the FOS Dashboard already uses, so one set of variables on the lab
/// PC configures both – and no key or password ever has to be typed into the application
/// (or committed).
/// </summary>
public static class EmailEnvironment
{
    /// <summary>Brevo transactional API key.</summary>
    public const string ApiKey = "BREVO_API_KEY";

    /// <summary>Sender ("from") address – must be a sender verified in Brevo.</summary>
    public const string Sender = "EMAIL_SENDER";

    /// <summary>SMTP relay host.</summary>
    public const string SmtpHost = "SMTP_HOST";

    /// <summary>SMTP relay port.</summary>
    public const string SmtpPort = "SMTP_PORT";

    /// <summary>SMTP login.</summary>
    public const string SmtpUser = "SMTP_USER";

    /// <summary>SMTP password / Brevo SMTP key.</summary>
    public const string SmtpPassword = "EMAIL_PASSWORD";

    /// <summary>Every variable, for the administration hint.</summary>
    public static readonly string[] All = { ApiKey, Sender, SmtpHost, SmtpPort, SmtpUser, SmtpPassword };
}

/// <summary>How notification e-mails are delivered.</summary>
public enum EmailMethod
{
    /// <summary>A classic SMTP server (System.Net.Mail).</summary>
    Smtp,

    /// <summary>An HTTP API endpoint (SendGrid / Mailgun / internal service).</summary>
    Http,

    /// <summary>Brevo transactional e-mail API over HTTPS (preferred by FOS Dashboard).</summary>
    BrevoApi,
}

/// <summary>
/// Configuration for the e-mail notifications sent when a profile finishes.
/// Stored as JSON so the user only enters it once.
/// </summary>
public sealed class EmailSettings
{
    public const string DefaultRecipients = "mmucka@sylex.sk; tsalat@sylex.sk; mplevka@sylex.sk";

    /// <summary>Master switch for sending notification e-mails.</summary>
    public bool Enabled { get; set; }

    /// <summary>Delivery mechanism.</summary>
    public EmailMethod Method { get; set; } = EmailMethod.BrevoApi;

    /// <summary>Recipient address that receives the notifications.</summary>
    public string Recipient { get; set; } = DefaultRecipients;

    /// <summary>
    /// Sender ("from") address. Empty by default so a fresh installation picks it up from
    /// <see cref="EmailEnvironment.Sender"/> – it has to match a sender verified in Brevo,
    /// and a wrong hard-coded default is rejected even with a valid API key.
    /// </summary>
    public string From { get; set; } = string.Empty;

    // --- SMTP ---
    public string SmtpHost { get; set; } = "smtp-relay.brevo.com";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;
    public string SmtpUser { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;

    // --- HTTP API ---
    /// <summary>POST endpoint that accepts the e-mail (JSON body).</summary>
    public string HttpEndpoint { get; set; } = "https://api.brevo.com/v3/smtp/email";

    /// <summary>Optional bearer API key sent as the Authorization header.</summary>
    public string HttpApiKey { get; set; } = string.Empty;

    /// <summary>Kept for callers written against the older single-variable API.</summary>
    public const string ApiKeyEnvironmentVariable = EmailEnvironment.ApiKey;

    /// <summary>The API key actually used to send (see <see cref="EmailEnvironment"/>).</summary>
    public string ResolveApiKey() => Resolve(HttpApiKey, EmailEnvironment.ApiKey);

    /// <summary>The sender address actually used (see <see cref="EmailEnvironment"/>).</summary>
    public string ResolveFrom() => Resolve(From, EmailEnvironment.Sender);

    /// <summary>The SMTP server actually used (see <see cref="EmailEnvironment"/>).</summary>
    public string ResolveSmtpHost() => Resolve(SmtpHost, EmailEnvironment.SmtpHost);

    /// <summary>The SMTP login actually used (see <see cref="EmailEnvironment"/>).</summary>
    public string ResolveSmtpUser() => Resolve(SmtpUser, EmailEnvironment.SmtpUser);

    /// <summary>The SMTP password actually used (see <see cref="EmailEnvironment"/>).</summary>
    public string ResolveSmtpPassword() => Resolve(SmtpPassword, EmailEnvironment.SmtpPassword);

    /// <summary>The SMTP port actually used; a non-numeric environment value is ignored.</summary>
    public int ResolveSmtpPort()
    {
        if (SmtpPort > 0)
        {
            return SmtpPort;
        }

        return int.TryParse(Resolve(string.Empty, EmailEnvironment.SmtpPort), out int port) && port > 0
            ? port
            : 0;
    }

    /// <summary>An explicitly entered value always wins; an empty field falls back to the
    /// environment variable, so a secret never has to be typed into the application.</summary>
    private static string Resolve(string? entered, string variable) =>
        !string.IsNullOrWhiteSpace(entered)
            ? entered.Trim()
            : Environment.GetEnvironmentVariable(variable)?.Trim() ?? string.Empty;

    /// <summary>
    /// Which values are currently coming from the environment rather than from the fields,
    /// so an empty box on the panel does not read as "not configured".
    /// </summary>
    public string DescribeEnvironmentSources()
    {
        var fromEnvironment = new List<string>();
        void Check(string? entered, string variable, string label)
        {
            if (string.IsNullOrWhiteSpace(entered) &&
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
            {
                fromEnvironment.Add(label);
            }
        }

        Check(From, EmailEnvironment.Sender, "odosielateľ");
        if (Method is EmailMethod.BrevoApi or EmailMethod.Http)
        {
            Check(HttpApiKey, EmailEnvironment.ApiKey, "API kľúč");
        }
        else
        {
            Check(SmtpHost, EmailEnvironment.SmtpHost, "SMTP host");
            Check(SmtpUser, EmailEnvironment.SmtpUser, "SMTP používateľ");
            Check(SmtpPassword, EmailEnvironment.SmtpPassword, "SMTP heslo");
        }

        return fromEnvironment.Count == 0 ? string.Empty : string.Join(", ", fromEnvironment);
    }

    /// <summary>
    /// What still has to be filled in before a notification can be sent, or an empty string
    /// when the settings are complete. Only the fields the chosen <see cref="Method"/>
    /// actually uses are checked – in <see cref="EmailMethod.BrevoApi"/> mode the SMTP user
    /// and password are not used at all.
    /// </summary>
    public string DescribeMissing()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(Recipient)) missing.Add("adresát");
        if (string.IsNullOrWhiteSpace(ResolveFrom())) missing.Add("odosielateľ (from)");

        switch (Method)
        {
            case EmailMethod.BrevoApi:
            case EmailMethod.Http:
                if (string.IsNullOrWhiteSpace(HttpEndpoint)) missing.Add("endpoint URL");
                if (string.IsNullOrWhiteSpace(ResolveApiKey())) missing.Add("API kľúč");
                break;
            default:
                if (string.IsNullOrWhiteSpace(ResolveSmtpHost())) missing.Add("SMTP host");
                if (ResolveSmtpPort() <= 0) missing.Add("SMTP port");
                if (string.IsNullOrWhiteSpace(ResolveSmtpUser())) missing.Add("SMTP používateľ");
                if (string.IsNullOrWhiteSpace(ResolveSmtpPassword())) missing.Add("SMTP heslo");
                break;
        }

        return missing.Count == 0 ? string.Empty : string.Join(", ", missing);
    }

    public EmailSettings Clone() => (EmailSettings)MemberwiseClone();
}
