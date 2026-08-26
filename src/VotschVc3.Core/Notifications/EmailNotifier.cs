using VotschVc3.Core.Diagnostics;

namespace VotschVc3.Core.Notifications;

/// <summary>Outcome of an attempt to send a notification.</summary>
/// <param name="Sent">The transport accepted the message.</param>
/// <param name="Skipped">Nothing was attempted (notifications off, no recipient).</param>
/// <param name="Error">Why it failed, or why it was skipped.</param>
/// <param name="Detail">What the transport reported – status code, message id, relay host.</param>
public sealed record EmailResult(bool Sent, bool Skipped, string? Error, string? Detail = null)
{
    public static EmailResult Ok(string? detail = null) => new(true, false, null, detail);

    public static EmailResult Fail(string error) => new(false, false, error);

    /// <summary>Nothing was sent, and why – so a silent skip still leaves a trace.</summary>
    public static EmailResult SkippedFor(string reason) => new(false, true, reason);

    public static readonly EmailResult SkippedResult = new(false, true, "notifikácie sú vypnuté alebo chýba adresát");
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

    /// <summary>Log source used for every e-mail entry, so the App log can be filtered.</summary>
    public const string LogSource = "E-mail";

    /// <summary>
    /// The effective configuration on one line, with the API key reduced to a fingerprint.
    /// Written to the application log before every attempt: when a notification does not
    /// arrive, the first question is always which sender, which key and which recipients
    /// were actually used – not what the boxes on the panel look like.
    /// </summary>
    public string Describe()
    {
        var parts = new List<string>
        {
            $"spôsob {Settings.Method}",
            $"odosielateľ {Or(Settings.ResolveFrom(), "(chýba)")}",
            $"adresáti {DescribeRecipients()}",
            $"zapnuté {(Settings.Enabled ? "áno" : "NIE")}",
        };

        if (Settings.Method is EmailMethod.BrevoApi or EmailMethod.Http)
        {
            parts.Add($"endpoint {Or(Settings.HttpEndpoint, "(chýba)")}");
            parts.Add($"API kľúč {Fingerprint(Settings.ResolveApiKey())}");
        }
        else
        {
            parts.Add($"SMTP {Or(Settings.ResolveSmtpHost(), "(chýba)")}:{Settings.ResolveSmtpPort()}");
            parts.Add($"používateľ {Or(Settings.ResolveSmtpUser(), "(chýba)")}");
            parts.Add($"heslo {Fingerprint(Settings.ResolveSmtpPassword())}");
        }

        if (Settings.DescribeEnvironmentSources() is { Length: > 0 } environment)
        {
            parts.Add($"z premenných prostredia: {environment}");
        }

        if (Settings.DescribeMissing() is { Length: > 0 } missing)
        {
            parts.Add($"CHÝBA: {missing}");
        }

        return string.Join(" · ", parts);
    }

    private string DescribeRecipients()
    {
        try
        {
            IReadOnlyList<string> parsed = EmailAddressParser.Parse(Settings.Recipient);
            return parsed.Count == 0 ? "(žiadni)" : $"{parsed.Count}: {string.Join(", ", parsed)}";
        }
        catch (FormatException ex)
        {
            // A malformed address makes the whole send fail with nothing sent – name it.
            return $"(neplatná adresa v zozname „{Settings.Recipient}“: {ex.Message})";
        }
    }

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    /// <summary>Enough of a secret to recognise it, never enough to use it.</summary>
    private static string Fingerprint(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return "(chýba)";
        }

        string trimmed = secret.Trim();
        string head = trimmed[..Math.Min(8, trimmed.Length)];
        return $"{head}… ({trimmed.Length} znakov)";
    }

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
            // Silently doing nothing is what made "poslal som e-mail a nič" impossible to
            // diagnose – say in the log which of the two switches was off.
            string reason = !Settings.Enabled
                ? "notifikácie sú vypnuté (prepínač v Administrácii)"
                : "nie je nastavený žiadny adresát";
            AppLog.Warn(LogSource, $"Notifikácia „{subject}“ NEODOSLANÁ – {reason}. {Describe()}");
            return Task.FromResult(EmailResult.SkippedFor(reason));
        }

        return DeliverAsync(Settings.Recipient, subject, body, htmlBody, attachments, cancellationToken);
    }

    /// <summary>Sends a test message, ignoring the enabled flag (recipient still required).</summary>
    public Task<EmailResult> SendTestAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Settings.Recipient))
        {
            AppLog.Warn(LogSource, $"Testovací e-mail NEODOSLANÝ – chýba adresát. {Describe()}");
            return Task.FromResult(EmailResult.Fail("Chýba adresát."));
        }

        return DeliverAsync(
            Settings.Recipient,
            "Test – Vötsch riadenie komôr",
            "Toto je testovací e-mail z aplikácie na riadenie laboratórnych zariadení.",
            null, null, cancellationToken);
    }

    private async Task<EmailResult> DeliverAsync(
        string to, string subject, string body, string? htmlBody,
        IReadOnlyList<EmailAttachment>? attachments, CancellationToken cancellationToken)
    {
        int attachmentCount = attachments?.Count ?? 0;
        AppLog.Info(LogSource, $"Odosielam „{subject}“ ({attachmentCount} príloh). {Describe()}");

        var started = DateTimeOffset.Now;
        try
        {
            IEmailSender sender = Settings.Method switch
            {
                EmailMethod.BrevoApi => new BrevoEmailSender(Settings),
                EmailMethod.Http => new HttpEmailSender(Settings),
                _ => new SmtpEmailSender(Settings),
            };

            string detail = await sender
                .SendAsync(new EmailMessage(to, subject, body, htmlBody, attachments), cancellationToken)
                .ConfigureAwait(false);

            double seconds = (DateTimeOffset.Now - started).TotalSeconds;
            AppLog.Info(LogSource, $"✔ Odoslané za {seconds:0.0} s · {detail}");
            return EmailResult.Ok(detail);
        }
        catch (Exception ex)
        {
            // The whole exception chain: an SMTP/HTTP failure hides the useful sentence in
            // an inner exception more often than not.
            string detail = Flatten(ex);
            AppLog.Error(LogSource, $"✖ Odoslanie zlyhalo · {detail} · {Describe()}");
            return EmailResult.Fail(detail);
        }
    }

    /// <summary>The message of the exception and of everything it wraps.</summary>
    private static string Flatten(Exception ex)
    {
        var messages = new List<string>();
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            string message = current.Message.Trim();
            if (message.Length > 0 && !messages.Contains(message))
            {
                messages.Add(message);
            }
        }

        return string.Join(" → ", messages);
    }
}
