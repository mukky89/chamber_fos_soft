using System.Globalization;

namespace VotschVc3.Core.Security;

/// <summary>One audit-trail entry: who did what, when.</summary>
public sealed class AuditEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public string User { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;

    public string TimestampText => Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}

/// <summary>
/// Append-only audit trail. Records operator actions to a CSV file and notifies
/// listeners. Inspired by the SIMPATI Pharma audit trail (not a certified
/// 21&#160;CFR&#160;Part&#160;11 implementation).
/// </summary>
public sealed class AuditLog
{
    private readonly object _sync = new();

    public AuditLog(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    /// <summary>Name of the user actions are attributed to.</summary>
    public string CurrentUser { get; set; } = "—";

    /// <summary>Raised after an entry is appended.</summary>
    public event EventHandler<AuditEntry>? EntryAdded;

    /// <summary>Records an action and persists it.</summary>
    public void Log(string source, string action, string detail = "")
    {
        var entry = new AuditEntry
        {
            Timestamp = DateTimeOffset.Now,
            User = CurrentUser,
            Source = source,
            Action = action,
            Detail = detail,
        };

        try
        {
            lock (_sync)
            {
                string? directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                bool isNew = !File.Exists(FilePath) || new FileInfo(FilePath).Length == 0;
                using var writer = new StreamWriter(FilePath, append: true);
                if (isNew)
                {
                    writer.WriteLine("Timestamp;User;Source;Action;Detail");
                }

                writer.WriteLine(string.Join(';',
                    entry.TimestampText, Clean(entry.User), Clean(entry.Source), Clean(entry.Action), Clean(entry.Detail)));
            }
        }
        catch
        {
            // Auditing must never crash the application.
        }

        EntryAdded?.Invoke(this, entry);
    }

    /// <summary>Loads up to <paramref name="max"/> most recent entries (newest first).</summary>
    public List<AuditEntry> LoadRecent(int max = 500)
    {
        lock (_sync)
        {
            if (!File.Exists(FilePath))
            {
                return new List<AuditEntry>();
            }

            var result = new List<AuditEntry>();
            try
            {
                string[] lines = File.ReadAllLines(FilePath);
                for (int i = lines.Length - 1; i >= 1 && result.Count < max; i--)
                {
                    string[] c = lines[i].Split(';');
                    if (c.Length < 5)
                    {
                        continue;
                    }

                    result.Add(new AuditEntry
                    {
                        Timestamp = DateTimeOffset.TryParse(c[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset ts)
                            ? ts : DateTimeOffset.MinValue,
                        User = c[1],
                        Source = c[2],
                        Action = c[3],
                        Detail = c[4],
                    });
                }
            }
            catch (IOException)
            {
                // ignore read errors
            }

            return result;
        }
    }

    private static string Clean(string s) => s.Replace(';', ',').Replace("\r", " ").Replace("\n", " ");
}
