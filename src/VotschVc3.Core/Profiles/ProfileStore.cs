using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Profiles;

/// <summary>
/// Simple JSON backed store for the profile history. Persists a flat list of
/// <see cref="TestProfile"/>s to a single file and is safe to share between the
/// chambers (all access is serialised with a monitor lock).
/// </summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _sync = new();

    public ProfileStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    /// <summary>Absolute path of the backing file.</summary>
    public string FilePath { get; }

    /// <summary>Loads every stored profile (newest first).</summary>
    public List<TestProfile> LoadAll()
    {
        lock (_sync)
        {
            return LoadAllNoLock();
        }
    }

    /// <summary>Inserts or updates a profile (matched by <see cref="TestProfile.Id"/>).</summary>
    public void Save(TestProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (_sync)
        {
            List<TestProfile> all = LoadAllNoLock();
            all.RemoveAll(p => p.Id == profile.Id);
            all.Insert(0, profile);
            WriteNoLock(all);
        }
    }

    /// <summary>Removes a profile by id. Returns <c>true</c> when something was deleted.</summary>
    public bool Delete(Guid id)
    {
        lock (_sync)
        {
            List<TestProfile> all = LoadAllNoLock();
            int removed = all.RemoveAll(p => p.Id == id);
            if (removed > 0)
            {
                WriteNoLock(all);
            }

            return removed > 0;
        }
    }

    private List<TestProfile> LoadAllNoLock()
    {
        if (!File.Exists(FilePath))
        {
            return new List<TestProfile>();
        }

        try
        {
            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<TestProfile>>(json, Options) ?? new List<TestProfile>();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A corrupt or partially written file should not crash the app.
            return new List<TestProfile>();
        }
    }

    private void WriteNoLock(List<TestProfile> profiles)
    {
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(profiles, Options);
        File.WriteAllText(FilePath, json);
    }
}
