using System.Text.Json;

namespace VotschVc3.Core.Settings;

/// <summary>Persists <see cref="UiSettings"/> to a JSON file.</summary>
public sealed class UiSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    private readonly object _sync = new();

    public UiSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public UiSettings Load()
    {
        lock (_sync)
        {
            if (!File.Exists(FilePath))
            {
                return new UiSettings();
            }

            try
            {
                return JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(FilePath), Options) ?? new UiSettings();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return new UiSettings();
            }
        }
    }

    public void Save(UiSettings settings)
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
