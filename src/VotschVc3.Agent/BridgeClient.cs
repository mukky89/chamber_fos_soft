using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Settings;

namespace VotschVc3.Agent;

public sealed class BridgeClient : IAsyncDisposable
{
    private static readonly HashSet<string> BlockedWriteExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".exe", ".dll", ".com", ".msi", ".ps1", ".bat", ".cmd", ".vbs", ".js", ".lnk", ".scr" };

    private readonly BridgeOptions _options;
    private DeviceManager _devices;
    private readonly HttpClient _http;
    private readonly ProfileStore _profiles;
    private int _cycle;
    private string _lastError = "";
    private readonly string _statusPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Lab Control", "bridge-status.json");

    public BridgeClient(BridgeOptions options)
    {
        _options = options;
        _devices = new DeviceManager(options);
        _profiles = new ProfileStore(options.ProfilesFile);
        _http = new HttpClient
        {
            BaseAddress = new Uri(options.DashboardUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(10)
        };
        _http.DefaultRequestHeaders.Add("X-Lab-Agent-Key", options.AgentKey);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LabControlBridge/2.0");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine($"Lab Control Bridge v2 → {_http.BaseAddress}");
        WriteStatus(false, "Agent sa pripája k Dashboardu…");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                DeviceSnapshot[] devices = await _devices.ReadAllAsync(ct);
                FileSnapshot[]? files = _cycle++ % Math.Max(1, _options.FileScanEveryCycles) == 0 ? ScanFiles() : null;
                await HeartbeatAsync(devices, files, ct);
                await ProcessCommandsAsync(ct);
                _lastError = "";
                WriteStatus(true, "");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                WriteStatus(false, ex.Message);
                Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollSeconds), ct);
        }

        WriteStatus(false, "Agent bol zastavený.", running: false);
    }

    private void WriteStatus(bool reachable, string error, bool running = true) =>
        BridgeStatusFile.Write(_statusPath, new BridgeStatus
        {
            Running = running,
            DashboardReachable = reachable,
            UpdatedUtc = DateTime.UtcNow,
            LastHeartbeatUtc = reachable ? DateTime.UtcNow : null,
            DashboardUrl = _options.DashboardUrl,
            MachineName = Environment.MachineName,
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0",
            LastError = error,
        });

    private async Task HeartbeatAsync(DeviceSnapshot[] devices, FileSnapshot[]? files, CancellationToken ct)
    {
        TestProfile[] profiles = _profiles.LoadAll().Take(2000).ToArray();
        ChamberConfigSnapshot[] chambers = _options.GetChamberSnapshots().ToArray();
        var request = new HeartbeatRequest(
            ContractVersion: 2,
            HostName: Environment.MachineName,
            Version: Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0",
            Capabilities: new[]
            {
                "bridge-v2", "ascii2", "simserv", "sika-rest", "asl-f100",
                "profiles:read", "profiles:write", "chambers:read", "chambers:write",
                "files-read", "files-write"
            },
            Folders: _options.Folders.Select(f => new FolderSnapshot(f.Alias, f.Writable)).ToArray(),
            Devices: devices,
            Profiles: profiles,
            Chambers: chambers,
            Settings: new AgentSettingsSnapshot(_options.PollSeconds, _options.FileScanEveryCycles, _options.MaxIndexedFiles),
            ProfilesRevision: DirectoryRevision(_profiles.Directory),
            ChambersRevision: FileRevision(Path.GetFullPath(Environment.ExpandEnvironmentVariables(_options.ChambersFile))),
            Files: files,
            LastError: _lastError);

        using HttpResponseMessage response = await _http.PostAsJsonAsync("api/lab-agent/heartbeat", request, BridgeOptions.Json, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task ProcessCommandsAsync(CancellationToken ct)
    {
        AgentCommand[] commands = await _http.GetFromJsonAsync<AgentCommand[]>("api/lab-agent/commands", BridgeOptions.Json, ct)
            ?? Array.Empty<AgentCommand>();

        foreach (AgentCommand command in commands)
        {
            try
            {
                await PostJsonAsync($"api/lab-agent/commands/{command.Id}/running", new { }, ct);

                switch (command.Type)
                {
                    case "file.read":
                        await UploadRequestedFileAsync(command, ct);
                        continue;
                    case "file.write":
                        await DownloadIncomingFileAsync(command, ct);
                        continue;
                    case "refresh":
                    case "scan.files":
                        await CompleteAsync(command.Id, new { refreshed = true, files = ScanFiles().Length }, ct);
                        continue;
                    case "thermometer.scan":
                        await CompleteAsync(command.Id, new { ports = System.IO.Ports.SerialPort.GetPortNames().Order().ToArray() }, ct);
                        continue;
                    case "profile.upsert":
                        await CompleteAsync(command.Id, UpsertProfile(command.Payload), ct);
                        continue;
                    case "profile.delete":
                        await CompleteAsync(command.Id, DeleteProfile(command.Payload), ct);
                        continue;
                    case "chamber.upsert":
                        await CompleteAsync(command.Id, await UpsertChamberAsync(command.Payload, ct), ct);
                        continue;
                    case "chamber.delete":
                        await CompleteAsync(command.Id, await DeleteChamberAsync(command.Payload, ct), ct);
                        continue;
                    case "agent.settings":
                        await CompleteAsync(command.Id, UpdateAgentSettings(command.Payload), ct);
                        continue;
                }

                object? result = await _devices.ExecuteAsync(command, ct);
                await CompleteAsync(command.Id, result, ct);
            }
            catch (Exception ex)
            {
                await PostJsonAsync($"api/lab-agent/commands/{command.Id}/result", new { ok = false, error = ex.Message }, ct);
            }
        }
    }

    private object UpsertProfile(JsonElement payload)
    {
        JsonElement node = RequireProperty(payload, "profile");
        TestProfile profile = node.Deserialize<TestProfile>(BridgeOptions.Json)
            ?? throw new InvalidOperationException("Profil sa nepodarilo načítať.");
        ValidateProfile(profile);
        _profiles.Save(profile);
        return new
        {
            saved = true,
            profileId = profile.Id,
            profile.Code,
            profile.Name,
            revision = DirectoryRevision(_profiles.Directory)
        };
    }

    private object DeleteProfile(JsonElement payload)
    {
        Guid id = RequireGuid(payload, "profileId");
        bool deleted = _profiles.Delete(id);
        return new { deleted, profileId = id, revision = DirectoryRevision(_profiles.Directory) };
    }

    private async Task<object> UpsertChamberAsync(JsonElement payload, CancellationToken ct)
    {
        await EnsureConfigurationCanChangeAsync(ct);
        JsonElement node = RequireProperty(payload, "config");
        ChamberConfig config = node.Deserialize<ChamberConfig>(BridgeOptions.Json)
            ?? throw new InvalidOperationException("Konfigurácia komory sa nepodarila načítať.");
        string deviceId = GetString(payload, "deviceId");
        bool? allowControl = GetNullableBool(payload, "allowControl");
        ChamberConfigSnapshot saved = _options.UpsertChamber(config, deviceId, allowControl);
        await ReloadDevicesAsync();
        return new
        {
            saved = true,
            chamber = saved,
            revision = FileRevision(Path.GetFullPath(Environment.ExpandEnvironmentVariables(_options.ChambersFile)))
        };
    }

    private async Task<object> DeleteChamberAsync(JsonElement payload, CancellationToken ct)
    {
        await EnsureConfigurationCanChangeAsync(ct);
        Guid configId = RequireGuid(payload, "configId");
        bool deleted = _options.DeleteChamber(configId);
        if (deleted) await ReloadDevicesAsync();
        return new
        {
            deleted,
            configId,
            revision = FileRevision(Path.GetFullPath(Environment.ExpandEnvironmentVariables(_options.ChambersFile)))
        };
    }

    private object UpdateAgentSettings(JsonElement payload)
    {
        if (payload.TryGetProperty("pollSeconds", out JsonElement poll) && poll.TryGetInt32(out int pollSeconds))
            _options.PollSeconds = Math.Clamp(pollSeconds, 2, 60);
        if (payload.TryGetProperty("fileScanEveryCycles", out JsonElement scan) && scan.TryGetInt32(out int scanCycles))
            _options.FileScanEveryCycles = Math.Clamp(scanCycles, 1, 10_000);
        if (payload.TryGetProperty("maxIndexedFiles", out JsonElement max) && max.TryGetInt32(out int maxFiles))
            _options.MaxIndexedFiles = Math.Clamp(maxFiles, 100, 20_000);
        _options.Save();
        return new
        {
            saved = true,
            settings = new AgentSettingsSnapshot(_options.PollSeconds, _options.FileScanEveryCycles, _options.MaxIndexedFiles)
        };
    }

    private async Task EnsureConfigurationCanChangeAsync(CancellationToken ct)
    {
        DeviceSnapshot[] current = await _devices.ReadAllAsync(ct);
        if (current.Any(d => d.Profile is not null))
            throw new InvalidOperationException("Konfiguráciu komory nemožno meniť počas bežiaceho profilu. Najprv profil zastavte.");
    }

    private async Task ReloadDevicesAsync()
    {
        DeviceManager previous = _devices;
        _devices = new DeviceManager(_options);
        await previous.DisposeAsync();
    }

    private static void ValidateProfile(TestProfile profile)
    {
        if (profile.Id == Guid.Empty) profile.Id = Guid.NewGuid();
        profile.Name = (profile.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new InvalidOperationException("Názov profilu nesmie byť prázdny.");
        if (profile.Name.Length > 200) profile.Name = profile.Name[..200];
        if (profile.Segments is null || profile.Segments.Count == 0) throw new InvalidOperationException("Profil musí obsahovať aspoň jeden segment.");
        if (profile.Segments.Count > 500) throw new InvalidOperationException("Profil môže obsahovať najviac 500 segmentov.");
        profile.Cycles = Math.Clamp(profile.Cycles, 1, 10_000);
        profile.Tags = NormalizeStrings(profile.Tags, 50, 100);
        profile.Sensors = NormalizeStrings(profile.Sensors, 100, 120);
        profile.Customer = TrimTo(profile.Customer, 160);
        profile.Project = TrimTo(profile.Project, 160);
        profile.Notes = TrimTo(profile.Notes, 4000);
        profile.Warning = TrimTo(profile.Warning, 1000);

        for (int i = 0; i < profile.Segments.Count; i++)
        {
            ProfileSegment segment = profile.Segments[i] ?? throw new InvalidOperationException($"Segment {i + 1} je neplatný.");
            segment.Name = TrimTo(segment.Name, 160);
            if (!double.IsFinite(segment.TargetTemperature) || segment.TargetTemperature is < -273.15 or > 1000)
                throw new InvalidOperationException($"Segment {i + 1}: neplatná cieľová teplota.");
            if (segment.TargetHumidity is { } h && (!double.IsFinite(h) || h is < 0 or > 100))
                throw new InvalidOperationException($"Segment {i + 1}: vlhkosť musí byť 0–100 %.");
            if (segment.Duration < TimeSpan.Zero || segment.Duration > TimeSpan.FromDays(365))
                throw new InvalidOperationException($"Segment {i + 1}: neplatné trvanie.");
            if (!double.IsFinite(segment.SoakTolerance) || segment.SoakTolerance is < 0 or > 100)
                throw new InvalidOperationException($"Segment {i + 1}: neplatná soak tolerancia.");
        }
    }

    private static List<string> NormalizeStrings(IEnumerable<string>? values, int maxItems, int maxLength) =>
        (values ?? Array.Empty<string>())
            .Select(v => TrimTo(v, maxLength))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToList();

    private static string TrimTo(string? value, int maxLength)
    {
        string v = (value ?? "").Trim();
        return v.Length <= maxLength ? v : v[..maxLength];
    }

    private async Task UploadRequestedFileAsync(AgentCommand command, CancellationToken ct)
    {
        string alias = GetString(command.Payload, "rootAlias");
        string relative = GetString(command.Payload, "relativePath");
        string full = _options.RequireFolder(alias).Resolve(relative);
        if (!File.Exists(full)) throw new FileNotFoundException("Lokálny súbor neexistuje.", relative);

        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var form = new MultipartFormDataContent();
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", Path.GetFileName(full));
        using HttpResponseMessage response = await _http.PostAsync($"api/lab-agent/commands/{command.Id}/file", form, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task DownloadIncomingFileAsync(AgentCommand command, CancellationToken ct)
    {
        if (!command.HasInputFile) throw new InvalidOperationException("Príkaz neobsahuje vstupný súbor.");
        string alias = GetString(command.Payload, "rootAlias");
        string relative = GetString(command.Payload, "relativePath");
        string target = _options.RequireFolder(alias, write: true).Resolve(relative);
        if (BlockedWriteExtensions.Contains(Path.GetExtension(target)))
            throw new UnauthorizedAccessException("Tento typ súboru sa z webu nesmie zapisovať na lokálny počítač.");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temp = target + ".bridge-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using HttpResponseMessage response = await _http.GetAsync($"api/lab-agent/commands/{command.Id}/file", HttpCompletionOption.ResponseHeadersRead, ct);
            await EnsureSuccessAsync(response, ct);
            await using Stream input = await response.Content.ReadAsStreamAsync(ct);
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, FileOptions.Asynchronous))
                await input.CopyToAsync(output, ct);
            File.Move(temp, target, overwrite: true);
            await CompleteAsync(command.Id, new
            {
                written = true,
                rootAlias = alias,
                relativePath = relative,
                size = new FileInfo(target).Length
            }, ct);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private FileSnapshot[] ScanFiles()
    {
        var result = new List<FileSnapshot>();
        foreach (FolderOptions folder in _options.Folders)
        {
            try
            {
                var pending = new Stack<string>();
                pending.Push(folder.FullPath);
                while (pending.Count > 0 && result.Count < _options.MaxIndexedFiles)
                {
                    string dir = pending.Pop();
                    foreach (string childDir in Directory.EnumerateDirectories(dir))
                    {
                        var info = new DirectoryInfo(childDir);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        string rel = Path.GetRelativePath(folder.FullPath, childDir).Replace('\\', '/');
                        result.Add(new(folder.Alias, rel, info.Name, true, 0, info.LastWriteTimeUtc));
                        pending.Push(childDir);
                        if (result.Count >= _options.MaxIndexedFiles) break;
                    }
                    if (result.Count >= _options.MaxIndexedFiles) break;

                    foreach (string file in Directory.EnumerateFiles(dir))
                    {
                        var info = new FileInfo(file);
                        string rel = Path.GetRelativePath(folder.FullPath, file).Replace('\\', '/');
                        result.Add(new(folder.Alias, rel, info.Name, false, info.Length, info.LastWriteTimeUtc));
                        if (result.Count >= _options.MaxIndexedFiles) break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Index {folder.Alias}: {ex.Message}");
            }

            if (result.Count >= _options.MaxIndexedFiles) break;
        }
        return result.ToArray();
    }

    private static string FileRevision(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? $"{file.Length:x}-{file.LastWriteTimeUtc.Ticks:x}" : "missing";
        }
        catch
        {
            return "unavailable";
        }
    }

    private static string DirectoryRevision(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return "missing";
            var sb = new StringBuilder();
            foreach (string file in Directory.EnumerateFiles(path, "*.json", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(file);
                sb.Append(info.Name).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks).Append('|');
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16].ToLowerInvariant();
        }
        catch
        {
            return "unavailable";
        }
    }

    private Task CompleteAsync(string id, object? result, CancellationToken ct) =>
        PostJsonAsync($"api/lab-agent/commands/{id}/result", new { ok = true, result }, ct);

    private async Task PostJsonAsync(string url, object body, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.PostAsJsonAsync(url, body, BridgeOptions.Json, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string detail = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"Dashboard {(int)response.StatusCode}: {detail[..Math.Min(detail.Length, 500)]}");
    }

    private static JsonElement RequireProperty(JsonElement e, string key) =>
        e.TryGetProperty(key, out JsonElement value) ? value : throw new InvalidOperationException($"Chýba pole {key}.");

    private static string GetString(JsonElement e, string key) =>
        e.TryGetProperty(key, out JsonElement value) ? value.GetString() ?? "" : "";

    private static bool? GetNullableBool(JsonElement e, string key) =>
        e.TryGetProperty(key, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static Guid RequireGuid(JsonElement e, string key)
    {
        string value = GetString(e, key);
        return Guid.TryParse(value, out Guid id) ? id : throw new InvalidOperationException($"Pole {key} musí byť platné GUID.");
    }

    public async ValueTask DisposeAsync()
    {
        await _devices.DisposeAsync();
        _http.Dispose();
    }
}
