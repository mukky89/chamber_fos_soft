using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Security;

/// <summary>Persists the user list to JSON and seeds a default admin on first run.</summary>
public sealed class UserStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _sync = new();

    public UserStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    /// <summary>Loads all users, seeding defaults (admin/admin, operator/operator) on first run.</summary>
    public List<User> LoadAll()
    {
        lock (_sync)
        {
            List<User>? users = null;
            if (File.Exists(FilePath))
            {
                try
                {
                    users = JsonSerializer.Deserialize<List<User>>(File.ReadAllText(FilePath), Options);
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    users = null;
                }
            }

            if (users is null || users.Count == 0)
            {
                users = new List<User>
                {
                    new() { Name = "admin", Role = UserRole.Admin, PasswordHash = User.Hash("admin") },
                    new() { Name = "operator", Role = UserRole.Operator, PasswordHash = User.Hash("operator") },
                };
                SaveNoLock(users);
            }

            return users;
        }
    }

    public void SaveAll(IEnumerable<User> users)
    {
        ArgumentNullException.ThrowIfNull(users);
        lock (_sync)
        {
            SaveNoLock(users.ToList());
        }
    }

    /// <summary>Returns the user with the given name, or <c>null</c>.</summary>
    public User? Find(string name) =>
        LoadAll().FirstOrDefault(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));

    private void SaveNoLock(List<User> users)
    {
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(FilePath, JsonSerializer.Serialize(users, Options));
    }
}
