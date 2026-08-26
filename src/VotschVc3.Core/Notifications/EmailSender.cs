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
        // Fields left empty fall back to the environment variables (see EmailEnvironment),
        // so the SMTP key never has to live in the settings file.
        string host = _settings.ResolveSmtpHost();
        int port = _settings.ResolveSmtpPort();
        string user = _settings.ResolveSmtpUser();
        string password = _settings.ResolveSmtpPassword();

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException(
                $"SMTP host nie je nastavený (ani v premennej {EmailEnvironment.SmtpHost}).");
        }

        using var client = new SmtpClient(host, port > 0 ? port : 587)
        {
            EnableSsl = _settings.SmtpUseSsl,
        };

        if (!string.IsNullOrEmpty(user))
        {
            client.Credentials = new NetworkCredential(user, password);
        }

        string from = !string.IsNullOrWhiteSpace(_settings.ResolveFrom()) ? _settings.ResolveFrom() : user;
        if (string.IsNullOrWhiteSpace(from))
        {
            throw new InvalidOperationException(
                $"Chýba adresa odosielateľa. Vyplň pole Odosielateľ (from) alebo premennú {EmailEnvironment.Sender}.");
        }
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Chýba SMTP používateľ alebo heslo. Pre Brevo použi SMTP login a SMTP key " +
                $"(alebo premenné {EmailEnvironment.SmtpUser} a {EmailEnvironment.SmtpPassword}), prípadne zvoľ BrevoApi.");
        }
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

/// <summary>Sends transactional e-mail using the same Brevo HTTPS API as FOS Dashboard.</summary>
public sealed class BrevoEmailSender : IEmailSender
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly EmailSettings _settings;
    private readonly HttpClient _http;

    public BrevoEmailSender(EmailSettings settings, HttpClient? http = null)
    {
        _settings = settings;
        _http = http ?? SharedClient;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        string from = _settings.ResolveFrom();
        if (string.IsNullOrWhiteSpace(from))
        {
            throw new InvalidOperationException(
                "Chýba overená Brevo adresa odosielateľa. Vyplň pole Odosielateľ (from) alebo " +
                $"premennú {EmailEnvironment.Sender}.");
        }
        string apiKey = _settings.ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Chýba Brevo API kľúč. Vlož ho do poľa „Brevo API kľúč“ v administrácii, alebo ho nastav " +
                $"do systémovej premennej {EmailSettings.ApiKeyEnvironmentVariable} (potom netreba nič zapisovať do aplikácie).");
        }

        string endpoint = string.IsNullOrWhiteSpace(_settings.HttpEndpoint)
            ? "https://api.brevo.com/v3/smtp/email"
            : _settings.HttpEndpoint;
        var payload = new
        {
            sender = new { name = "Lab Control", email = from },
            to = EmailAddressParser.Parse(message.To).Select(email => new { email }).ToArray(),
            subject = message.Subject,
            textContent = message.Body,
            htmlContent = message.HtmlBody,
            attachment = message.Attachments?.Select(a => new
            {
                name = a.FileName,
                content = Convert.ToBase64String(a.Content),
            }).ToArray(),
        };
        if (payload.to.Length == 0)
        {
            throw new InvalidOperationException("Chýba platný adresát.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(payload) };
        request.Headers.TryAddWithoutValidation("api-key", apiKey);
        request.Headers.TryAddWithoutValidation("accept", "application/json");
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Brevo API {(int)response.StatusCode}: {detail[..Math.Min(detail.Length, 300)]}");
        }
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
            from = _settings.ResolveFrom(),
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

        if (_settings.ResolveApiKey() is { Length: > 0 } bearer)
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");
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
