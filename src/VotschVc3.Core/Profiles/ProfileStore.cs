using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Profiles;

/// <summary>
/// JSON backed store for the profile library: <b>one file per profile</b>, named after the
/// profile, in a single folder. A library kept in one big file was hard to look at, back up
/// or hand over – a single profile could not be copied out or e-mailed without exporting it
/// first, and every save rewrote megabytes. All access is serialised with a monitor lock so
/// the chambers can share one instance.
/// </summary>
/// <remarks>
/// The old single-file library (<c>profiles.json</c>) is migrated on first use: every profile
/// in it is written out as its own file and the original is kept as
/// <c>profiles.json.migrated</c> – nothing is deleted, the operator's data stays where they
/// can see it.
/// </remarks>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Suffix the migrated single-file library keeps, so it is never read again.</summary>
    public const string MigratedSuffix = ".migrated";

    private readonly object _sync = new();

    // Parsed-folder cache: the dashboard reloads the library for every chamber on each
    // navigation home, so re-reading and re-deserialising unchanged files over and over made
    // that transition noticeably slow. The signature below (names, sizes and write times of
    // every file) changes whenever any file is added, removed or edited – also by another
    // process – so an outside edit is still picked up.
    private List<TestProfile>? _cache;
    private Dictionary<Guid, string> _cachePaths = new();
    private string? _cacheSignature;

    /// <param name="path">
    /// The library folder, or – for compatibility with the single-file layout – the path of
    /// the old <c>profiles.json</c>, whose folder is then used and whose content is migrated.
    /// </param>
    public ProfileStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);

        bool looksLikeFile = string.Equals(Path.GetExtension(full), ".json", StringComparison.OrdinalIgnoreCase);
        Directory = looksLikeFile ? Path.GetDirectoryName(full) ?? full : full;
        LegacyFilePath = looksLikeFile ? full : Path.Combine(Directory, "profiles.json");
    }

    /// <summary>Folder holding one JSON file per profile.</summary>
    public string Directory { get; }

    /// <summary>The old single-file library that is migrated away on first use.</summary>
    public string LegacyFilePath { get; }

    /// <summary>Loads every stored profile, most recently changed first.</summary>
    public List<TestProfile> LoadAll()
    {
        lock (_sync)
        {
            return LoadAllNoLock();
        }
    }

    /// <summary>
    /// Inserts or updates a profile (matched by <see cref="TestProfile.Id"/>) and stamps
    /// <see cref="TestProfile.UpdatedAt"/>, so the picker can offer a "Najnovšie" group.
    /// A renamed profile is written under the new file name and the old file is removed, so
    /// the folder never fills up with the previous names of one profile.
    /// </summary>
    public void Save(TestProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.UpdatedAt = DateTimeOffset.Now;
        lock (_sync)
        {
            MigrateLegacyNoLock();
            List<TestProfile> all = LoadAllNoLock();
            string? previous = FindFileNoLock(profile.Id);
            string target = WriteOneNoLock(profile, all.Where(p => p.Id != profile.Id));

            if (previous is not null && !PathsEqual(previous, target))
            {
                TryDelete(previous);
            }

            InvalidateNoLock();
        }
    }

    /// <summary>
    /// Adds every profile that is not already present (matched by id or, case-insensitively,
    /// by name). Returns the number actually added.
    /// </summary>
    public int AddMissing(IEnumerable<TestProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        lock (_sync)
        {
            MigrateLegacyNoLock();
            List<TestProfile> all = LoadAllNoLock();
            var ids = new HashSet<Guid>(all.Select(p => p.Id));
            var names = new HashSet<string>(all.Select(p => p.Name.Trim()), StringComparer.OrdinalIgnoreCase);

            int added = 0;
            foreach (TestProfile profile in profiles)
            {
                if (ids.Contains(profile.Id) || names.Contains(profile.Name.Trim()))
                {
                    continue;
                }

                WriteOneNoLock(profile, all);
                all.Add(profile);
                ids.Add(profile.Id);
                names.Add(profile.Name.Trim());
                added++;
            }

            if (added > 0)
            {
                InvalidateNoLock();
            }

            return added;
        }
    }

    /// <summary>Removes every profile. Returns the number deleted.</summary>
    public int Clear()
    {
        lock (_sync)
        {
            MigrateLegacyNoLock();
            int count = 0;
            foreach (string file in ProfileFilesNoLock())
            {
                if (TryDelete(file))
                {
                    count++;
                }
            }

            InvalidateNoLock();
            return count;
        }
    }

    /// <summary>Removes a profile by id. Returns <c>true</c> when something was deleted.</summary>
    public bool Delete(Guid id)
    {
        lock (_sync)
        {
            MigrateLegacyNoLock();
            string? file = FindFileNoLock(id);
            if (file is null || !TryDelete(file))
            {
                return false;
            }

            InvalidateNoLock();
            return true;
        }
    }

    /// <summary>Path of the file a profile is stored in, or <c>null</c> when it is not saved.</summary>
    public string? FileOf(Guid id)
    {
        lock (_sync)
        {
            return FindFileNoLock(id);
        }
    }

    private List<TestProfile> LoadAllNoLock()
    {
        MigrateLegacyNoLock();

        string signature = SignatureNoLock();
        if (_cache is not null && signature == _cacheSignature)
        {
            return new List<TestProfile>(_cache);
        }

        var parsed = new List<TestProfile>();
        var paths = new Dictionary<Guid, string>();
        foreach (string file in ProfileFilesNoLock())
        {
            if (ReadOne(file) is not { } profile)
            {
                continue;
            }

            parsed.Add(profile);
            paths[profile.Id] = file; // also the map Save/Delete look the file up in
        }

        // Newest change first – the order the pickers and the library tree expect.
        parsed = parsed.OrderByDescending(p => p.LastChangedAt).ToList();

        _cache = parsed;
        _cachePaths = paths;
        _cacheSignature = signature;
        return new List<TestProfile>(parsed);
    }

    /// <summary>Reads one profile file; a corrupt file is skipped instead of losing the library.</summary>
    private static TestProfile? ReadOne(string file)
    {
        try
        {
            string json = File.ReadAllText(file);

            // Any JSON object deserialises into a TestProfile full of defaults, so a stray
            // file in the folder would show up as a profile called "New profile". A profile
            // file always carries its identity, so require it before trusting the file.
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object || !HasId(document.RootElement))
                {
                    return null;
                }
            }

            return JsonSerializer.Deserialize<TestProfile>(json, Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool HasId(JsonElement root)
    {
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Writes one profile to its own file and returns the path. The file is named after the
    /// profile; when another profile already uses that name the id is appended, so two
    /// profiles that happen to share a name never overwrite each other.
    /// </summary>
    private string WriteOneNoLock(TestProfile profile, IEnumerable<TestProfile> others)
    {
        System.IO.Directory.CreateDirectory(Directory);

        string baseName = FileNameFor(profile.Name);
        string path = Path.Combine(Directory, baseName + ".json");

        bool taken = others.Any(p => string.Equals(FileNameFor(p.Name), baseName, StringComparison.OrdinalIgnoreCase));
        if (taken)
        {
            path = Path.Combine(Directory, $"{baseName} ({profile.Id.ToString("N")[..8]}).json");
        }

        File.WriteAllText(path, JsonSerializer.Serialize(profile, Options));
        return path;
    }

    /// <summary>Turns a profile name into a file name Windows accepts, without losing the name.</summary>
    public static string FileNameFor(string? profileName)
    {
        string name = (profileName ?? string.Empty).Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        // A trailing dot or space is legal in the string but not in a Windows file name.
        name = name.TrimEnd('.', ' ');

        // Leave room for the extension and a disambiguating suffix inside MAX_PATH.
        if (name.Length > 120)
        {
            name = name[..120].TrimEnd('.', ' ');
        }

        return name.Length == 0 ? "profil" : name;
    }

    /// <summary>The file a profile lives in, from the map the folder scan already built.</summary>
    private string? FindFileNoLock(Guid id)
    {
        LoadAllNoLock();
        return _cachePaths.TryGetValue(id, out string? file) && File.Exists(file) ? file : null;
    }

    private IEnumerable<string> ProfileFilesNoLock()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return Array.Empty<string>();
        }

        try
        {
            return System.IO.Directory
                .EnumerateFiles(Directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Cheap fingerprint of the folder: any add, delete or edit changes it.</summary>
    private string SignatureNoLock()
    {
        var parts = new List<string>();
        foreach (string file in ProfileFilesNoLock())
        {
            try
            {
                var info = new FileInfo(file);
                parts.Add($"{info.Name}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
            }
            catch (IOException)
            {
                parts.Add(file);
            }
        }

        return string.Join(";", parts);
    }

    private void InvalidateNoLock()
    {
        _cache = null;
        _cachePaths = new Dictionary<Guid, string>();
        _cacheSignature = null;
    }

    /// <summary>
    /// Splits an old single-file library into one file per profile, once. The original is
    /// renamed (never deleted) so the operator keeps a full backup of what was there.
    /// </summary>
    private void MigrateLegacyNoLock()
    {
        if (!File.Exists(LegacyFilePath))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(LegacyFilePath);
            List<TestProfile> legacy =
                JsonSerializer.Deserialize<List<TestProfile>>(json, Options) ?? new List<TestProfile>();

            System.IO.Directory.CreateDirectory(Directory);
            var written = new List<TestProfile>();
            var seenIds = new HashSet<Guid>();
            foreach (TestProfile profile in legacy)
            {
                // The old file was a plain list and could carry the same id twice; keep the
                // first, which is the newest, and drop the stale duplicate.
                if (!seenIds.Add(profile.Id))
                {
                    continue;
                }

                WriteOneNoLock(profile, written);
                written.Add(profile);
            }

            File.Move(LegacyFilePath, UniqueBackupPath(), overwrite: false);
            InvalidateNoLock();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Leave the old file exactly as it is; the library then simply stays empty and the
            // operator still has every profile in the file they can see.
        }
    }

    private string UniqueBackupPath()
    {
        string candidate = LegacyFilePath + MigratedSuffix;
        int n = 2;
        while (File.Exists(candidate))
        {
            candidate = $"{LegacyFilePath}{MigratedSuffix}{n++}";
        }

        return candidate;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static bool TryDelete(string file)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
