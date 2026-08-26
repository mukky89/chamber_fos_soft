namespace VotschVc3.Core.Notifications;

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

    /// <summary>Sender ("from") address.</summary>
    public string From { get; set; } = "no-reply@sylex.sk";

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

    /// <summary>
    /// Environment variable read when <see cref="HttpApiKey"/> is empty, so the key can live
    /// outside the settings file (and outside the repository) on a shared lab PC.
    /// </summary>
    public const string ApiKeyEnvironmentVariable = "BREVO_API_KEY";

    /// <summary>
    /// The API key actually used to send: the one entered in the administration, or – when
    /// that is empty – the <see cref="ApiKeyEnvironmentVariable"/> environment variable.
    /// </summary>
    public string ResolveApiKey() =>
        !string.IsNullOrWhiteSpace(HttpApiKey)
            ? HttpApiKey.Trim()
            : Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable)?.Trim() ?? string.Empty;

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
        if (string.IsNullOrWhiteSpace(From)) missing.Add("odosielateľ (from)");

        switch (Method)
        {
            case EmailMethod.BrevoApi:
            case EmailMethod.Http:
                if (string.IsNullOrWhiteSpace(HttpEndpoint)) missing.Add("endpoint URL");
                if (string.IsNullOrWhiteSpace(ResolveApiKey())) missing.Add("API kľúč");
                break;
            default:
                if (string.IsNullOrWhiteSpace(SmtpHost)) missing.Add("SMTP host");
                if (SmtpPort <= 0) missing.Add("SMTP port");
                break;
        }

        return missing.Count == 0 ? string.Empty : string.Join(", ", missing);
    }

    public EmailSettings Clone() => (EmailSettings)MemberwiseClone();
}
