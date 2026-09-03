using System.IO;
using System.Media;
using System.Text.Json;

namespace VotschVc3.App.Notifications;

/// <summary>Audible operator warning for bad/mismatched FBG identification. Disabled state persists.</summary>
public static class OperatorAlertSoundService
{
    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(AppPaths.SettingsDir, "operator-alert-sound.json");
    private static readonly Dictionary<string, DateTimeOffset> LastPlayed = new(StringComparer.OrdinalIgnoreCase);
    private static bool _enabled = LoadEnabled();

    public static event EventHandler? EnabledChanged;

    public static bool Enabled
    {
        get { lock (Gate) return _enabled; }
        set
        {
            bool changed;
            lock (Gate)
            {
                changed = _enabled != value;
                _enabled = value;
                if (changed) SaveEnabled(value);
            }
            if (changed) EnabledChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static void PlayWarning(string key)
    {
        lock (Gate)
        {
            if (!_enabled) return;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (LastPlayed.TryGetValue(key, out DateTimeOffset previous) && now - previous < TimeSpan.FromSeconds(3)) return;
            LastPlayed[key] = now;
        }

        try { SystemSounds.Exclamation.Play(); }
        catch { /* sound is optional and must never affect calibration */ }
    }

    private static bool LoadEnabled()
    {
        try
        {
            if (!File.Exists(FilePath)) return true;
            AlertSoundSettings? value = JsonSerializer.Deserialize<AlertSoundSettings>(File.ReadAllText(FilePath));
            return value?.Enabled ?? true;
        }
        catch { return true; }
    }

    private static void SaveEnabled(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new AlertSoundSettings(enabled), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private sealed record AlertSoundSettings(bool Enabled);
}
