using System.Text.Json;

namespace VotschVc3.Core.Calibration;

/// <summary>Persists the Chamber client's central FOS API connection settings.</summary>
public sealed class SylexFosApiSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public SylexFosApiSettingsStore(string filePath) => FilePath = Path.GetFullPath(filePath);

    public string FilePath { get; }

    public SylexFosApiSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return FromEnvironment();
            SylexFosApiSettings settings = JsonSerializer.Deserialize<SylexFosApiSettings>(File.ReadAllText(FilePath), Options)
                ?? FromEnvironment();
            settings.BaseUrl = NormalizeBaseUrl(settings.BaseUrl);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return FromEnvironment();
        }
    }

    public void Save(SylexFosApiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.BaseUrl = NormalizeBaseUrl(settings.BaseUrl);
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
    }

    public static string NormalizeBaseUrl(string? value)
    {
        string candidate = string.IsNullOrWhiteSpace(value) ? SylexFosApiSettings.DefaultBaseUrl : value.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal)) candidate = "http://" + candidate;
        if (!Uri.TryCreate(candidate.TrimEnd('/'), UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Zadaj platnú HTTP alebo HTTPS adresu Sylex FOS API.");
        return uri.ToString().TrimEnd('/');
    }

    private static SylexFosApiSettings FromEnvironment() => new()
    {
        BaseUrl = Environment.GetEnvironmentVariable("SYLEX_FOS_API_URL") ?? SylexFosApiSettings.DefaultBaseUrl,
    };
}
