namespace VotschVc3.Core.Notifications;

/// <summary>Outcome of an attempt to send a notification.</summary>
public sealed record EmailResult(bool Sent, bool Skipped, string? Error)
{
    public static EmailResult Ok() => new(true, false, null);
    public static EmailResult Fail(string error) => new(false, false, error);
    public static readonly EmailResult SkippedResult = new(false, true, null);
}

/// <summary>
/// Facade that sends notification e-mails using the configured method. Never
/// throws: failures are reported through <see cref="EmailResult"/> so a delivery
/// problem cannot disrupt the profile run that triggered it.
/// </summary>
public sealed class EmailNotifier
{
    /// <summary>The live settings (edited through the UI, persisted separately).</summary>
    public EmailSettings Settings { get; set; } = new();

    /// <summary><c>true</c> when notifications are enabled and a recipient is set.</summary>
    public bool CanSend => Settings.Enabled && !string.IsNullOrWhiteSpace(Settings.Recipient);

    /// <summary>Sends a notification, honouring the enabled flag.</summary>
    public Task<EmailResult> SendAsync(
        string subject,
        string body,
        string? htmlBody = null,
        IReadOnlyList<EmailAttachment>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanSend)
        {
            return Task.FromResult(EmailResult.SkippedResult);
        }

        return DeliverAsync(
            Settings.Recipient,
            RenameLegacyReferenceThermometer(subject),
            RenameLegacyReferenceThermometer(body),
            htmlBody is null ? null : RenameLegacyReferenceThermometer(htmlBody),
            attachments,
            cancellationToken);
    }

    /// <summary>Sends a test message, ignoring the enabled flag (recipient still required).</summary>
    public Task<EmailResult> SendTestAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Settings.Recipient))
        {
            return Task.FromResult(EmailResult.Fail("Chýba adresát."));
        }

        return DeliverAsync(
            Settings.Recipient,
            "Test – Vötsch riadenie komôr",
            "Typ: Test e-mailu\nZdroj: Lab Control\n\nToto je testovací e-mail z aplikácie na riadenie laboratórnych zariadení.",
            null, null, cancellationToken);
    }

    private static string RenameLegacyReferenceThermometer(string text) =>
        text.Replace("F100", "WIKA CTH7000 Temp. reference", StringComparison.OrdinalIgnoreCase);

    private async Task<EmailResult> DeliverAsync(
        string to, string subject, string body, string? htmlBody,
        IReadOnlyList<EmailAttachment>? attachments, CancellationToken cancellationToken)
    {
        try
        {
            IEmailSender sender = Settings.Method switch
            {
                EmailMethod.BrevoApi => new BrevoEmailSender(Settings),
                EmailMethod.Http => new HttpEmailSender(Settings),
                _ => new SmtpEmailSender(Settings),
            };

            // Any notification that did not provide a dedicated HTML body gets the shared
            // Lab Control template automatically. This keeps every plain warning/error/info
            // email visually consistent without requiring individual call sites to know HTML.
            string effectiveHtml = string.IsNullOrWhiteSpace(htmlBody)
                ? LabControlEmailTemplate.Create(subject, body)
                : htmlBody;

            await sender.SendAsync(new EmailMessage(to, subject, body, effectiveHtml, attachments), cancellationToken).ConfigureAwait(false);
            return EmailResult.Ok();
        }
        catch (Exception ex)
        {
            return EmailResult.Fail(ex.Message);
        }
    }
}