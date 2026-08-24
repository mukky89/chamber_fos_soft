using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Profiles;

/// <summary>
/// JSON backed store for <see cref="ProfileRunState"/> checkpoints, one per
/// chamber, in a single shared file. Writes go through a temp file + atomic move
/// so a crash in the middle of a save never leaves a corrupt checkpoint behind
/// (the previous complete checkpoint survives). All access is serialised with a
/// monitor lock, mirroring <see cref="ProfileStore"/>.
/// </summary>
public sealed class ProfileRunStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _sync = new();

    public ProfileRunStateStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    /// <summary>Absolute path of the backing file.</summary>
    public string FilePath { get; }

    /// <summary>Returns the saved checkpoint for a chamber, or <c>null</c> when none exists.</summary>
    public ProfileRunState? Load(Guid chamberId)
    {
        lock (_sync)
        {
            return LoadAllNoLock().FirstOrDefault(s => s.ChamberId == chamberId);
        }
    }

    /// <summary>Inserts or replaces the checkpoint of the state's chamber.</summary>
    public void Save(ProfileRunState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_sync)
        {
            List<ProfileRunState> all = LoadAllNoLock();
            all.RemoveAll(s => s.ChamberId == state.ChamberId);
            all.Add(state);
            WriteNoLock(all);
        }
    }

    /// <summary>Removes a chamber's checkpoint. Returns <c>true</c> when something was deleted.</summary>
    public bool Delete(Guid chamberId)
    {
        lock (_sync)
        {
            List<ProfileRunState> all = LoadAllNoLock();
            int removed = all.RemoveAll(s => s.ChamberId == chamberId);
            if (removed > 0)
            {
                WriteNoLock(all);
            }

            return removed > 0;
        }
    }

    private List<ProfileRunState> LoadAllNoLock()
    {
        if (!File.Exists(FilePath))
        {
            return new List<ProfileRunState>();
        }

        try
        {
            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<ProfileRunState>>(json, Options) ?? new List<ProfileRunState>();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A corrupt or partially written file should not crash the app.
            return new List<ProfileRunState>();
        }
    }

    private void WriteNoLock(List<ProfileRunState> states)
    {
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Atomic replace: a crash mid-write must not destroy the last checkpoint.
        string tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(states, Options));
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
