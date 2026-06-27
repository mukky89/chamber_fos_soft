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
                return JsonSerializer.Deserialize<EmailSettings>(File.ReadAllText(FilePath), Options) ?? new EmailSettings();
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
