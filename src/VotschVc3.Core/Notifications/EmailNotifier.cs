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
    public Task<EmailResult> SendAsync(string subject, string body, CancellationToken cancellationToken = default)
    {
        if (!CanSend)
        {
            return Task.FromResult(EmailResult.SkippedResult);
        }

        return DeliverAsync(Settings.Recipient, subject, body, cancellationToken);
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
            "Toto je testovací e-mail z aplikácie na riadenie laboratórnych zariadení.",
            cancellationToken);
    }

    private async Task<EmailResult> DeliverAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        try
        {
            IEmailSender sender = Settings.Method == EmailMethod.Http
                ? new HttpEmailSender(Settings)
                : new SmtpEmailSender(Settings);

            await sender.SendAsync(new EmailMessage(to, subject, body), cancellationToken).ConfigureAwait(false);
            return EmailResult.Ok();
        }
        catch (Exception ex)
        {
            return EmailResult.Fail(ex.Message);
        }
    }
}
