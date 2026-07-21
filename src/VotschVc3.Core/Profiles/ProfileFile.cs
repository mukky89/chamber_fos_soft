using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Profiles;

/// <summary>
/// Reads / writes a list of <see cref="TestProfile"/>s as a JSON array – the same
/// shape the <see cref="ProfileStore"/> persists, so an exported file can be
/// imported back or bundled with the build as seed profiles.
/// </summary>
public static class ProfileFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(IEnumerable<TestProfile> profiles) =>
        JsonSerializer.Serialize(profiles.ToList(), Options);

    public static List<TestProfile> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<TestProfile>();
        }

        return JsonSerializer.Deserialize<List<TestProfile>>(json, Options) ?? new List<TestProfile>();
    }

    public static void Write(string path, IEnumerable<TestProfile> profiles) =>
        File.WriteAllText(path, Serialize(profiles));

    public static List<TestProfile> Read(string path) =>
        Deserialize(File.ReadAllText(path));
}
