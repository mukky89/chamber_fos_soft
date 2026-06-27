using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Mail;

namespace VotschVc3.Core.Notifications;

/// <summary>A single e-mail message.</summary>
public sealed record EmailMessage(string To, string Subject, string Body);

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
        using var mail = new MailMessage(from, message.To, message.Subject, message.Body);
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
