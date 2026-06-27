using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Profiles;

/// <summary>Persists the list of <see cref="ChamberConfig"/> to a JSON file.</summary>
public sealed class ChamberConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _sync = new();

    public ChamberConfigStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public List<ChamberConfig> LoadAll()
    {
        lock (_sync)
        {
            if (!File.Exists(FilePath))
            {
                return new List<ChamberConfig>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<ChamberConfig>>(File.ReadAllText(FilePath), Options)
                    ?? new List<ChamberConfig>();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return new List<ChamberConfig>();
            }
        }
    }

    public void SaveAll(IEnumerable<ChamberConfig> configs)
    {
        ArgumentNullException.ThrowIfNull(configs);
        lock (_sync)
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(configs.ToList(), Options));
        }
    }
}
