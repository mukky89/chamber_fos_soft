using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Net.Mime;

namespace VotschVc3.Core.Notifications;

/// <summary>A single e-mail message.</summary>
public sealed record EmailAttachment(string FileName, byte[] Content, string MediaType, string? ContentId = null);

public sealed record EmailMessage(
    string To,
    string Subject,
    string Body,
    string? HtmlBody = null,
    IReadOnlyList<EmailAttachment>? Attachments = null);

/// <summary>Sends an <see cref="EmailMessage"/>.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Sends e-mail through an SMTP server using the built-in mail client.</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailSettings _settings;

    public SmtpEmailSender(EmailSettings settings) => _settings = settings;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.SmtpHost))
        {
            throw new InvalidOperationException("SMTP host nie je nastavený.");
        }

        using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
        {
            EnableSsl = _settings.SmtpUseSsl,
        };

        if (!string.IsNullOrEmpty(_settings.SmtpUser))
        {
            client.Credentials = new NetworkCredential(_settings.SmtpUser, _settings.SmtpPassword);
        }

        string from = !string.IsNullOrWhiteSpace(_settings.From) ? _settings.From : _settings.SmtpUser;
        using var mail = new MailMessage { From = new MailAddress(from, "Lab Control"), Subject = message.Subject, Body = message.Body };
        foreach (string recipient in EmailAddressParser.Parse(message.To))
        {
            mail.To.Add(recipient);
        }

        if (mail.To.Count == 0)
        {
            throw new InvalidOperationException("Chýba platný adresát.");
        }

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            var html = AlternateView.CreateAlternateViewFromString(message.HtmlBody, null, MediaTypeNames.Text.Html);
            foreach (EmailAttachment attachment in message.Attachments?.Where(a => a.ContentId is not null) ?? [])
            {
                var resource = new LinkedResource(new MemoryStream(attachment.Content, writable: false), attachment.MediaType)
                {
                    ContentId = attachment.ContentId!,
                    TransferEncoding = TransferEncoding.Base64,
                };
                html.LinkedResources.Add(resource);
            }
            mail.AlternateViews.Add(html);
        }

        foreach (EmailAttachment attachment in message.Attachments?.Where(a => a.ContentId is null) ?? [])
        {
            mail.Attachments.Add(new Attachment(
                new MemoryStream(attachment.Content, writable: false), attachment.FileName, attachment.MediaType));
        }
        await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Sends e-mail by POSTing JSON to an HTTP API. The default payload is
/// <c>{ "to", "from", "subject", "text" }</c> with an optional bearer token –
/// adjust to match your service (e.g. the dbfood endpoint) if needed.
/// </summary>
public sealed class HttpEmailSender : IEmailSender
{
    private static readonly HttpClient SharedClient = new();
    private readonly EmailSettings _settings;
    private readonly HttpClient _http;

    public HttpEmailSender(EmailSettings settings, HttpClient? http = null)
    {
        _settings = settings;
        _http = http ?? SharedClient;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.HttpEndpoint))
        {
            throw new InvalidOperationException("HTTP endpoint nie je nastavený.");
        }

        var payload = new
        {
            to = message.To,
            from = _settings.From,
            subject = message.Subject,
            text = message.Body,
            html = message.HtmlBody,
            attachments = message.Attachments?.Select(a => new
            {
                filename = a.FileName,
                contentType = a.MediaType,
                content = Convert.ToBase64String(a.Content),
                contentId = a.ContentId,
            }),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _settings.HttpEndpoint)
        {
            Content = JsonContent.Create(payload),
        };

        if (!string.IsNullOrWhiteSpace(_settings.HttpApiKey))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_settings.HttpApiKey}");
        }

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}

public static class EmailAddressParser
{
    public static IReadOnlyList<string> Parse(string value) =>
        (value ?? string.Empty)
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(address => new MailAddress(address).Address)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
