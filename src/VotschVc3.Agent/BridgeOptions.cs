using System.Text.Json;
using System.Text.Json.Serialization;
using VotschVc3.Core.Profiles;

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
    public string ChambersFile { get; set; } = Path.Combine(LabRoot(), "chambers.json");

    [JsonIgnore]
    public string ConfigFilePath { get; private set; } = "";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static BridgeOptions Load(string path)
    {
        path = Path.GetFullPath(path);
        BridgeOptions options;
        if (!File.Exists(path))
        {
            options = new BridgeOptions();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            options.ConfigFilePath = path;
            options.Save();
            return options;
        }

        options = JsonSerializer.Deserialize<BridgeOptions>(File.ReadAllText(path), Json) ?? new BridgeOptions();
        options.ConfigFilePath = path;
        return options;
    }

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(ConfigFilePath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath)!);
        string temp = ConfigFilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, ConfigFilePath, overwrite: true);
    }

    public void Validate()
    {
        if (!Uri.TryCreate(DashboardUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("DashboardUrl musí byť platná HTTPS adresa.");
        if (!AgentKey.StartsWith("lab_", StringComparison.Ordinal) || AgentKey.Length < 44)
            throw new InvalidOperationException("V bridge.json chýba pairing AgentKey z administrácie Dashboardu.");

        PollSeconds = Math.Clamp(PollSeconds, 2, 60);
        FileScanEveryCycles = Math.Clamp(FileScanEveryCycles, 1, 10_000);
        MaxIndexedFiles = Math.Clamp(MaxIndexedFiles, 100, 20_000);
        SyncDesktopConfiguration();
        foreach (FolderOptions folder in Folders) folder.Validate();
    }

    /// <summary>
    /// Makes the desktop ChamberConfig library the source of truth for connection settings.
    /// Existing bridge ids and AllowControl flags are retained, newly added desktop chambers
    /// are automatically discovered, and entries that were previously sourced from a deleted
    /// ChamberConfig are removed. Manual bridge-only devices remain supported.
    /// </summary>
    public void SyncDesktopConfiguration()
    {
        string file = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ChambersFile));
        List<ChamberConfig> sources = new ChamberConfigStore(file).LoadAll()
            .Where(c => c.Protocol != ChamberProtocol.PolEkoModbus)
            .ToList();

        var existing = Devices ?? new List<DeviceOptions>();
        var result = new List<DeviceOptions>();
        var used = new HashSet<DeviceOptions>();

        foreach (ChamberConfig source in sources)
        {
            DeviceOptions? target = existing.FirstOrDefault(d => d.SourceConfigId == source.Id);
            target ??= existing.FirstOrDefault(d => !used.Contains(d) && !string.IsNullOrWhiteSpace(d.Host)
                && string.Equals(d.Host, source.Host, StringComparison.OrdinalIgnoreCase));
            target ??= existing.FirstOrDefault(d => !used.Contains(d)
                && (Normalize(d.Name).Contains(Normalize(source.Name)) || Normalize(source.Name).Contains(Normalize(d.Name))));
            target ??= new DeviceOptions
            {
                Id = "chamber-" + source.Id.ToString("N")[..12],
                AllowControl = false,
                Enabled = true,
            };

            target.SourceConfigId = source.Id;
            CopyFromDesktop(target, source);
            used.Add(target);
            result.Add(target);
        }

        // Keep truly manual bridge entries, but do not resurrect a deleted desktop-backed entry.
        foreach (DeviceOptions manual in existing.Where(d => !used.Contains(d) && d.SourceConfigId is null))
            result.Add(manual);

        Devices = result
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    public IReadOnlyList<ChamberConfigSnapshot> GetChamberSnapshots()
    {
        string file = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ChambersFile));
        List<ChamberConfig> configs = new ChamberConfigStore(file).LoadAll()
            .Where(c => c.Protocol != ChamberProtocol.PolEkoModbus)
            .ToList();

        var snapshots = new List<ChamberConfigSnapshot>();
        foreach (ChamberConfig c in configs)
        {
            DeviceOptions? d = Devices.FirstOrDefault(x => x.SourceConfigId == c.Id)
                ?? Devices.FirstOrDefault(x => string.Equals(x.Host, c.Host, StringComparison.OrdinalIgnoreCase));
            snapshots.Add(ToSnapshot(d?.Id ?? "chamber-" + c.Id.ToString("N")[..12], c, d));
        }

        // Also publish bridge-only devices so Dashboard can diagnose/configure legacy installs.
        foreach (DeviceOptions d in Devices.Where(x => x.SourceConfigId is null))
        {
            var c = new ChamberConfig
            {
                Id = Guid.Empty,
                Name = d.Name,
                Kind = d.HasHumidity ? ChamberKind.TemperatureHumidity : ChamberKind.Temperature,
                Protocol = d.Protocol,
                Host = d.Host,
                Port = d.Port,
                Address = d.Address,
                AnalogChannelCount = d.AnalogChannelCount,
                StartChannelIndex = d.StartChannelIndex,
                PollIntervalSeconds = PollSeconds,
            };
            snapshots.Add(ToSnapshot(d.Id, c, d));
        }
        return snapshots;
    }

    /// <summary>Persists a Dashboard edit into the same chambers.json used by the desktop app.</summary>
    public ChamberConfigSnapshot UpsertChamber(ChamberConfig incoming, string deviceId, bool? allowControl)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        string file = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ChambersFile));
        var store = new ChamberConfigStore(file);
        List<ChamberConfig> all = store.LoadAll();

        if (incoming.Id == Guid.Empty)
            incoming.Id = Guid.NewGuid();

        ValidateChamber(incoming);
        ChamberConfig? previous = all.FirstOrDefault(c => c.Id == incoming.Id);
        if (previous is not null && string.IsNullOrWhiteSpace(incoming.LockPasswordHash))
            incoming.LockPasswordHash = previous.LockPasswordHash;

        int index = all.FindIndex(c => c.Id == incoming.Id);
        if (index >= 0) all[index] = incoming; else all.Add(incoming);
        store.SaveAll(all);

        SyncDesktopConfiguration();
        DeviceOptions? device = Devices.FirstOrDefault(d => d.SourceConfigId == incoming.Id);
        if (device is not null)
        {
            if (!string.IsNullOrWhiteSpace(deviceId) && !Devices.Any(d => !ReferenceEquals(d, device) && string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase)))
                device.Id = deviceId;
            if (allowControl.HasValue) device.AllowControl = allowControl.Value;
        }
        Save();
        return ToSnapshot(device?.Id ?? deviceId, incoming, device);
    }

    public bool DeleteChamber(Guid configId)
    {
        if (configId == Guid.Empty) return false;
        string file = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ChambersFile));
        var store = new ChamberConfigStore(file);
        List<ChamberConfig> all = store.LoadAll();
        int removed = all.RemoveAll(c => c.Id == configId);
        if (removed == 0) return false;
        store.SaveAll(all);
        SyncDesktopConfiguration();
        Save();
        return true;
    }

    private static void ValidateChamber(ChamberConfig c)
    {
        c.Name = (c.Name ?? "").Trim();
        c.Host = (c.Host ?? "").Trim();
        c.Terminator = string.IsNullOrWhiteSpace(c.Terminator) ? "CR (\\r)" : c.Terminator.Trim();
        if (string.IsNullOrWhiteSpace(c.Name)) throw new InvalidOperationException("Názov komory nesmie byť prázdny.");
        if (c.Name.Length > 160) c.Name = c.Name[..160];
        if (string.IsNullOrWhiteSpace(c.Host) || c.Host.Length > 255) throw new InvalidOperationException("Host/IP komory je neplatný.");
        c.Port = Math.Clamp(c.Port, 1, 65535);
        c.Address = Math.Clamp(c.Address, 0, 255);
        c.AnalogChannelCount = Math.Clamp(c.AnalogChannelCount, 1, 64);
        c.StartChannelIndex = Math.Clamp(c.StartChannelIndex, 0, 64);
        c.PollIntervalSeconds = Math.Clamp(c.PollIntervalSeconds, 0.25, 300);
        c.TempMin = Math.Clamp(c.TempMin, -273.15, 1000);
        c.TempMax = Math.Clamp(c.TempMax, c.TempMin, 1000);
        c.HumMin = Math.Clamp(c.HumMin, 0, 100);
        c.HumMax = Math.Clamp(c.HumMax, c.HumMin, 100);
        c.QuickPresets = c.QuickPresets?.Where(double.IsFinite).Select(v => Math.Clamp(v, c.TempMin, c.TempMax)).Distinct().Take(20).ToList();
    }

    private static void CopyFromDesktop(DeviceOptions target, ChamberConfig source)
    {
        target.Name = source.Name;
        target.Host = source.Host;
        target.Port = source.Port;
        target.Address = source.Address;
        target.AnalogChannelCount = source.AnalogChannelCount;
        target.StartChannelIndex = source.StartChannelIndex;
        target.HasHumidity = source.Kind == ChamberKind.TemperatureHumidity;
        target.Protocol = source.Protocol;
        target.Kind = source.Protocol == ChamberProtocol.SikaRestApi ? DeviceKind.Sika : DeviceKind.Chamber;
    }

    private static ChamberConfigSnapshot ToSnapshot(string deviceId, ChamberConfig c, DeviceOptions? d) => new(
        deviceId,
        c.Id,
        c.Name,
        c.Kind,
        c.Protocol,
        c.Host,
        c.Port,
        c.Address,
        c.AnalogChannelCount,
        c.StartChannelIndex,
        c.Terminator,
        c.PollIntervalSeconds,
        c.AlarmsEnabled,
        c.TempMin,
        c.TempMax,
        c.HumMin,
        c.HumMax,
        c.AutoStopOnAlarm,
        c.AutoReconnect,
        c.AutoRecoverProfile,
        c.QuickPresets?.ToArray() ?? Array.Empty<double>(),
        c.IsLocked,
        d?.Enabled ?? true,
        d?.AllowControl ?? false);

    private static string Normalize(string value) => new((value ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

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
        new() { Id="vc3", Name="Klimatická komora VC3", Kind=DeviceKind.Chamber, Protocol=ChamberProtocol.VotschAscii2, Host="10.88.5.181", Port=1080, HasHumidity=true },
        new() { Id="vt3", Name="Teplotná komora VT3", Kind=DeviceKind.Chamber, Protocol=ChamberProtocol.VotschAscii2, Host="10.88.5.182", Port=1080 },
        new() { Id="komora3", Name="Komora 3 - FOI", Kind=DeviceKind.Chamber, Protocol=ChamberProtocol.VotschAscii2, Host="10.88.5.233", Port=2049, HasHumidity=true },
        new() { Id="sika-sylex", Name="SIKA Sylex", Kind=DeviceKind.Sika, Protocol=ChamberProtocol.SikaRestApi, Host="10.88.5.226", Port=8081 },
        new() { Id="sika-polytech", Name="SIKA PolyTech", Kind=DeviceKind.Sika, Protocol=ChamberProtocol.SikaRestApi, Host="10.88.6.28", Port=80 }
    };
}

public enum DeviceKind { Chamber, Sika }
public sealed class DeviceOptions
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Guid? SourceConfigId { get; set; }
    public string Name { get; set; } = "Zariadenie";
    public DeviceKind Kind { get; set; }
    public ChamberProtocol Protocol { get; set; } = ChamberProtocol.VotschAscii2;
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
    [JsonIgnore]
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
