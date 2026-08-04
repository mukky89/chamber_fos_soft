using System.Globalization;
using System.Linq;

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
    private static string? _directory;

    /// <summary>Raised whenever an entry is logged (may be off the UI thread).</summary>
    public static event EventHandler<AppLogEntry>? EntryLogged;

    /// <summary>
    /// Sets the log folder. Call once at startup. Entries are written to one file
    /// per day, grouped by month: <c>&lt;dir&gt;\yyyy-MM\yyyy-MM-dd.log</c>. The file
    /// rolls over automatically at midnight, so no single log ever grows unbounded.
    /// </summary>
    public static void Configure(string directory)
    {
        lock (Sync)
        {
            _directory = Path.GetFullPath(directory);
        }
    }

    /// <summary>Path of the daily log file for <paramref name="localTime"/>, or null if unconfigured.</summary>
    private static string? DailyFilePath(DateTime localTime)
    {
        if (_directory is null)
        {
            return null;
        }

        string month = localTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        string day = localTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Path.Combine(_directory, month, day + ".log");
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
                string? file = DailyFilePath(entry.Timestamp.LocalDateTime);
                if (file is not null)
                {
                    string? directory = Path.GetDirectoryName(file);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.AppendAllText(file,
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

    /// <summary>
    /// Loads up to <paramref name="max"/> most recent entries (newest first),
    /// reading back across the daily log files from the newest day until enough
    /// entries are gathered.
    /// </summary>
    public static List<AppLogEntry> LoadRecent(int max = 1000)
    {
        lock (Sync)
        {
            var result = new List<AppLogEntry>();
            if (_directory is null || !Directory.Exists(_directory))
            {
                return result;
            }

            try
            {
                // Daily files embed the date in both the folder (yyyy-MM) and the
                // name (yyyy-MM-dd.log), so a descending path sort is newest-first.
                List<string> files = Directory
                    .GetFiles(_directory, "*.log", SearchOption.AllDirectories)
                    .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (string file in files)
                {
                    if (result.Count >= max)
                    {
                        break;
                    }

                    string[] lines = File.ReadAllLines(file);
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
