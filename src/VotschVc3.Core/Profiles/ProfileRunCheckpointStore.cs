using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Profiles;

/// <summary>
/// Persists one <see cref="ProfileRunCheckpoint"/> per chamber, one JSON file each
/// (named by chamber id) under a dedicated folder. Writes happen on every profile
/// set-point update, so they go through a temp-file-then-move so a crash mid-write
/// never leaves a half-written, unreadable checkpoint behind – exactly the moment a
/// power outage is most likely to hit.
/// </summary>
public sealed class ProfileRunCheckpointStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _sync = new();

    public ProfileRunCheckpointStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = Path.GetFullPath(directory);
    }

    /// <summary>Absolute path of the backing folder.</summary>
    public string Directory { get; }

    private string PathFor(Guid chamberId) => Path.Combine(Directory, $"{chamberId:N}.json");

    /// <summary>Loads the checkpoint for a chamber, or <c>null</c> if none is stored (or it is unreadable).</summary>
    public ProfileRunCheckpoint? TryLoad(Guid chamberId)
    {
        lock (_sync)
        {
            string path = PathFor(chamberId);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<ProfileRunCheckpoint>(File.ReadAllText(path), Options);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // A partially written or corrupt file must never block a connect/recovery flow.
                return null;
            }
        }
    }

    /// <summary>
    /// Writes (overwrites) the checkpoint for <see cref="ProfileRunCheckpoint.ChamberId"/>.
    /// Atomic: written to a temp file first, then moved into place, so a crash during the
    /// write leaves either the old checkpoint or the new one, never a corrupt hybrid.
    /// </summary>
    public void Save(ProfileRunCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        lock (_sync)
        {
            System.IO.Directory.CreateDirectory(Directory);
            string path = PathFor(checkpoint.ChamberId);
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(checkpoint, Options));
            File.Move(tempPath, path, overwrite: true);
        }
    }

    /// <summary>Removes the checkpoint for a chamber (normal completion or an explicit Stop). Safe if none exists.</summary>
    public void Delete(Guid chamberId)
    {
        lock (_sync)
        {
            try
            {
                File.Delete(PathFor(chamberId));
            }
            catch (IOException)
            {
                // Best effort – a leftover file is harmless, it is just re-checked (and
                // overwritten or ignored) on the next run.
            }
        }
    }
}
