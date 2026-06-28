using System.Globalization;

namespace VotschVc3.Core.Diagnostics;

public enum AppLogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>One diagnostic log entry.</summary>
public sealed class AppLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public AppLogLevel Level { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public string TimestampText => Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    public string LevelText => Level.ToString().ToUpperInvariant();
}

/// <summary>
/// Global application diagnostic log: starts, errors, calibration events and any
/// other technical detail. Writes to a file and raises an event so a viewer can
/// show entries live. Static so it can be called from anywhere without plumbing.
/// </summary>
public static class AppLog
{
    private static readonly object Sync = new();
    private static string? _filePath;

    /// <summary>Raised whenever an entry is logged (may be off the UI thread).</summary>
    public static event EventHandler<AppLogEntry>? EntryLogged;

    /// <summary>Sets the backing file. Call once at startup.</summary>
    public static void Configure(string filePath)
    {
        lock (Sync)
        {
            _filePath = Path.GetFullPath(filePath);
        }
    }

    public static void Info(string source, string message) => Log(AppLogLevel.Info, source, message);

    public static void Warn(string source, string message) => Log(AppLogLevel.Warning, source, message);

    public static void Error(string source, string message) => Log(AppLogLevel.Error, source, message);

    public static void Error(string source, Exception ex) =>
        Log(AppLogLevel.Error, source, $"{ex.GetType().Name}: {ex.Message}");

    public static void Log(AppLogLevel level, string source, string message)
    {
        var entry = new AppLogEntry { Level = level, Source = source, Message = message };

        try
        {
            lock (Sync)
            {
                if (_filePath is not null)
                {
                    string? directory = Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.AppendAllText(_filePath,
                        $"{entry.TimestampText}\t{entry.LevelText}\t{Clean(source)}\t{Clean(message)}{Environment.NewLine}");
                }
            }
        }
        catch
        {
            // Logging must never throw.
        }

        EntryLogged?.Invoke(null, entry);
    }

    /// <summary>Loads up to <paramref name="max"/> most recent entries (newest first).</summary>
    public static List<AppLogEntry> LoadRecent(int max = 1000)
    {
        lock (Sync)
        {
            var result = new List<AppLogEntry>();
            if (_filePath is null || !File.Exists(_filePath))
            {
                return result;
            }

            try
            {
                string[] lines = File.ReadAllLines(_filePath);
                for (int i = lines.Length - 1; i >= 0 && result.Count < max; i--)
                {
                    string[] c = lines[i].Split('\t');
                    if (c.Length < 4)
                    {
                        continue;
                    }

                    result.Add(new AppLogEntry
                    {
                        Timestamp = DateTimeOffset.TryParse(c[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset ts) ? ts : DateTimeOffset.MinValue,
                        Level = Enum.TryParse(c[1], ignoreCase: true, out AppLogLevel lvl) ? lvl : AppLogLevel.Info,
                        Source = c[2],
                        Message = c[3],
                    });
                }
            }
            catch (IOException)
            {
                // ignore
            }

            return result;
        }
    }

    private static string Clean(string s) => s.Replace('\t', ' ').Replace("\r", " ").Replace("\n", " ");
}
