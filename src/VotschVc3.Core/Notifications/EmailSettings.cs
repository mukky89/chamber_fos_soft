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

    public EmailSettings Clone() => (EmailSettings)MemberwiseClone();
}
