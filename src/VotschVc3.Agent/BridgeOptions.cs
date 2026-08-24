using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Agent;

public sealed class BridgeOptions
{
    public string DashboardUrl { get; set; } = "https://YOUR-DASHBOARD.example";
    public string AgentKey { get; set; } = "";
    public int PollSeconds { get; set; } = 3;
    public int FileScanEveryCycles { get; set; } = 10;
    public int MaxIndexedFiles { get; set; } = 5000;
    public List<DeviceOptions> Devices { get; set; } = DefaultDevices();
    public List<ThermometerOptions> Thermometers { get; set; } = new();
    public List<FolderOptions> Folders { get; set; } = DefaultFolders();
    public string ProfilesFile { get; set; } = Path.Combine(LabRoot(), "Profiles", "profiles.json");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static BridgeOptions Load(string path)
    {
        if (!File.Exists(path))
        {
            var created = new BridgeOptions();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(created, Json));
            return created;
        }
        return JsonSerializer.Deserialize<BridgeOptions>(File.ReadAllText(path), Json) ?? new BridgeOptions();
    }

    public void Validate()
    {
        if (!Uri.TryCreate(DashboardUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("DashboardUrl musí byť platná HTTPS adresa.");
        if (!AgentKey.StartsWith("lab_", StringComparison.Ordinal) || AgentKey.Length < 44)
            throw new InvalidOperationException("V bridge.json chýba pairing AgentKey z administrácie Dashboardu.");
        PollSeconds = Math.Clamp(PollSeconds, 2, 60);
        MaxIndexedFiles = Math.Clamp(MaxIndexedFiles, 100, 20_000);
        foreach (FolderOptions folder in Folders) folder.Validate();
    }

    public FolderOptions RequireFolder(string alias, bool write = false)
    {
        FolderOptions? folder = Folders.FirstOrDefault(f => string.Equals(f.Alias, alias, StringComparison.OrdinalIgnoreCase));
        if (folder is null || (write && !folder.Writable)) throw new UnauthorizedAccessException($"Priečinok {alias} nie je povolený{(write ? " na zápis" : "")}.");
        return folder;
    }

    private static string LabRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Lab Control");
    private static List<FolderOptions> DefaultFolders() => new()
    {
        new("Profiles", Path.Combine(LabRoot(), "Profiles"), true),
        new("Recordings", Path.Combine(LabRoot(), "Recordings"), true),
        new("Profilelog", Path.Combine(LabRoot(), "Profilelog"), true),
        new("AppLog", Path.Combine(LabRoot(), "App log"), false),
        new("AllData", LabRoot(), false)
    };
    private static List<DeviceOptions> DefaultDevices() => new()
    {
        new() { Id="vc3", Name="Klimatická komora VC3", Kind=DeviceKind.Chamber, Host="10.88.5.181", Port=1080, HasHumidity=true },
        new() { Id="vt3", Name="Teplotná komora VT3", Kind=DeviceKind.Chamber, Host="10.88.5.182", Port=1080 },
        new() { Id="komora3", Name="Komora 3 - FOI", Kind=DeviceKind.Chamber, Host="10.88.5.233", Port=2049, HasHumidity=true },
        new() { Id="sika-sylex", Name="SIKA Sylex", Kind=DeviceKind.Sika, Host="10.88.5.226", Port=8081 },
        new() { Id="sika-polytech", Name="SIKA PolyTech", Kind=DeviceKind.Sika, Host="10.88.6.28", Port=80 }
    };
}

public enum DeviceKind { Chamber, Sika }
public sealed class DeviceOptions
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Zariadenie";
    public DeviceKind Kind { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 1080;
    public int Address { get; set; } = 1;
    public int AnalogChannelCount { get; set; } = 6;
    public int StartChannelIndex { get; set; } = 1;
    public bool HasHumidity { get; set; }
    public bool Enabled { get; set; } = true;
    public bool AllowControl { get; set; } = false;
}
public sealed class ThermometerOptions
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "ASL F100";
    public string PortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;
    public string ReadCommand { get; set; } = "READ?";
    public string SerialNumber { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
public sealed record FolderOptions(string Alias, string Path, bool Writable)
{
    public string FullPath { get; private set; } = "";
    public void Validate()
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(Alias, "^[A-Za-z0-9_-]{1,40}$")) throw new InvalidOperationException($"Neplatný alias priečinka: {Alias}");
        FullPath = System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(Path));
        Directory.CreateDirectory(FullPath);
    }
    public string Resolve(string relativePath)
    {
        string rel = (relativePath ?? "").Replace('/', System.IO.Path.DirectorySeparatorChar).TrimStart(System.IO.Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(rel)) throw new InvalidOperationException("Chýba relatívna cesta.");
        string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(FullPath, rel));
        if (!full.StartsWith(FullPath + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Cesta je mimo povoleného koreňa.");
        return full;
    }
}
