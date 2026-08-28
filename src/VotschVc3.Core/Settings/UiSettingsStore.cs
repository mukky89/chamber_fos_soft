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
            UiSettings settings = ReadNoLock();

            // One-time migration: the fleet timeline is now hidden by default. A file
            // written before that still says ShowTimeline = true, so reset it once and
            // record that it was done – a choice the operator makes afterwards sticks.
            if (!settings.TimelineDefaultApplied)
            {
                settings.ShowTimeline = false;
                settings.TimelineDefaultApplied = true;
                TrySaveNoLock(settings);
            }

            return settings;
        }
    }

    private UiSettings ReadNoLock()
    {
        if (!File.Exists(FilePath))
        {
            return new UiSettings { TimelineDefaultApplied = true };
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

    /// <summary>Best-effort save used by the migration – loading settings must never throw.</summary>
    private void TrySaveNoLock(UiSettings settings)
    {
        try
        {
            SaveNoLock(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The preference simply stays at its default until a write succeeds.
        }
    }

    public void Save(UiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_sync)
        {
            SaveNoLock(settings);
        }
    }

    private void SaveNoLock(UiSettings settings)
    {
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
    }
}
