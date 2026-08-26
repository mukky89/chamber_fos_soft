using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Notifications;

/// <summary>Persists <see cref="EmailSettings"/> to a JSON file.</summary>
public sealed class EmailSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _sync = new();

    public EmailSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public EmailSettings Load()
    {
        lock (_sync)
        {
            if (!File.Exists(FilePath))
            {
                return new EmailSettings();
            }

            try
            {
                EmailSettings settings = JsonSerializer.Deserialize<EmailSettings>(File.ReadAllText(FilePath), Options) ?? new EmailSettings();
                // Older versions stored empty values because notifications only supported
                // one manually entered recipient and had no dashboard-compatible defaults.
                if (string.IsNullOrWhiteSpace(settings.Recipient))
                {
                    settings.Recipient = EmailSettings.DefaultRecipients;
                }
                if (string.IsNullOrWhiteSpace(settings.SmtpHost))
                {
                    settings.SmtpHost = "smtp-relay.brevo.com";
                }

                // The sender is deliberately NOT backfilled. It has to match an address
                // verified in Brevo, and stamping a fixed one over an empty field meant the
                // EMAIL_SENDER environment variable could never take effect – the field was
                // never empty by the time anything read it.
                if (string.IsNullOrWhiteSpace(settings.HttpEndpoint))
                {
                    settings.HttpEndpoint = "https://api.brevo.com/v3/smtp/email";
                }
                return settings;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return new EmailSettings();
            }
        }
    }

    public void Save(EmailSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_sync)
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
    }
}
