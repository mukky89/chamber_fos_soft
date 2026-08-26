using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using VotschVc3.Core.Settings;
using VotschVc3.Core.Profiles;

namespace VotschVc3.Agent;

public sealed class BridgeClient : IAsyncDisposable
{
    private static readonly HashSet<string> BlockedWriteExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".exe", ".dll", ".com", ".msi", ".ps1", ".bat", ".cmd", ".vbs", ".js", ".lnk", ".scr" };
    private readonly BridgeOptions _options;
    private readonly DeviceManager _devices;
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
        _http = new HttpClient { BaseAddress = new Uri(options.DashboardUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.Add("X-Lab-Agent-Key", options.AgentKey);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LabControlBridge/1.63.0");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine($"Lab Control Bridge 1.63.0 → {_http.BaseAddress}");
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { _lastError = ex.Message; WriteStatus(false, ex.Message); Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] {ex.Message}"); }
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
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.63.0",
            LastError = error,
        });

    private async Task HeartbeatAsync(DeviceSnapshot[] devices, FileSnapshot[]? files, CancellationToken ct)
    {
        var request = new HeartbeatRequest(
            Environment.MachineName,
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.63.0",
            new[] { "ascii2", "simserv", "sika-rest", "asl-f100", "profiles", "files-read", "files-write" },
            _options.Folders.Select(f => new FolderSnapshot(f.Alias, f.Writable)).ToArray(),
            devices, _profiles.LoadAll().Take(2000).ToArray(), files, _lastError);
        using HttpResponseMessage response = await _http.PostAsJsonAsync("api/lab-agent/heartbeat", request, BridgeOptions.Json, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task ProcessCommandsAsync(CancellationToken ct)
    {
        AgentCommand[] commands = await _http.GetFromJsonAsync<AgentCommand[]>("api/lab-agent/commands", BridgeOptions.Json, ct) ?? Array.Empty<AgentCommand>();
        foreach (AgentCommand command in commands)
        {
            try
            {
                await PostJsonAsync($"api/lab-agent/commands/{command.Id}/running", new { }, ct);
                if (command.Type == "file.read") { await UploadRequestedFileAsync(command, ct); continue; }
                if (command.Type == "file.write") { await DownloadIncomingFileAsync(command, ct); continue; }
                if (command.Type is "refresh" or "scan.files") { await CompleteAsync(command.Id, new { refreshed=true, files=ScanFiles().Length }, ct); continue; }
                if (command.Type == "thermometer.scan") { await CompleteAsync(command.Id, new { ports=System.IO.Ports.SerialPort.GetPortNames().Order().ToArray() }, ct); continue; }
                object? result = await _devices.ExecuteAsync(command, ct);
                await CompleteAsync(command.Id, result, ct);
            }
            catch (Exception ex)
            {
                await PostJsonAsync($"api/lab-agent/commands/{command.Id}/result", new { ok=false, error=ex.Message }, ct);
            }
        }
    }

    private async Task UploadRequestedFileAsync(AgentCommand command, CancellationToken ct)
    {
        string alias = GetString(command.Payload, "rootAlias"), relative = GetString(command.Payload, "relativePath");
        string full = _options.RequireFolder(alias).Resolve(relative);
        if (!File.Exists(full)) throw new FileNotFoundException("Lokálny súbor neexistuje.", relative);
        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var form = new MultipartFormDataContent();
        using var content = new StreamContent(stream); content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", Path.GetFileName(full));
        using HttpResponseMessage response = await _http.PostAsync($"api/lab-agent/commands/{command.Id}/file", form, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task DownloadIncomingFileAsync(AgentCommand command, CancellationToken ct)
    {
        if (!command.HasInputFile) throw new InvalidOperationException("Príkaz neobsahuje vstupný súbor.");
        string alias = GetString(command.Payload, "rootAlias"), relative = GetString(command.Payload, "relativePath");
        string target = _options.RequireFolder(alias, write:true).Resolve(relative);
        if (BlockedWriteExtensions.Contains(Path.GetExtension(target))) throw new UnauthorizedAccessException("Tento typ súboru sa z webu nesmie zapisovať na lokálny počítač.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temp = target + ".bridge-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using HttpResponseMessage response = await _http.GetAsync($"api/lab-agent/commands/{command.Id}/file", HttpCompletionOption.ResponseHeadersRead, ct);
            await EnsureSuccessAsync(response, ct);
            await using Stream input = await response.Content.ReadAsStreamAsync(ct);
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, FileOptions.Asynchronous)) await input.CopyToAsync(output, ct);
            File.Move(temp, target, overwrite:true);
            await CompleteAsync(command.Id, new { written=true, rootAlias=alias, relativePath=relative, size=new FileInfo(target).Length }, ct);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private FileSnapshot[] ScanFiles()
    {
        var result = new List<FileSnapshot>();
        foreach (FolderOptions folder in _options.Folders)
        {
            try
            {
                var pending = new Stack<string>(); pending.Push(folder.FullPath);
                while (pending.Count > 0 && result.Count < _options.MaxIndexedFiles)
                {
                    string dir = pending.Pop();
                    foreach (string childDir in Directory.EnumerateDirectories(dir))
                    {
                        var info = new DirectoryInfo(childDir); if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        string rel = Path.GetRelativePath(folder.FullPath, childDir).Replace('\\','/');
                        result.Add(new(folder.Alias, rel, info.Name, true, 0, info.LastWriteTimeUtc)); pending.Push(childDir);
                        if (result.Count >= _options.MaxIndexedFiles) break;
                    }
                    if (result.Count >= _options.MaxIndexedFiles) break;
                    foreach (string file in Directory.EnumerateFiles(dir))
                    {
                        var info = new FileInfo(file); string rel = Path.GetRelativePath(folder.FullPath, file).Replace('\\','/');
                        result.Add(new(folder.Alias, rel, info.Name, false, info.Length, info.LastWriteTimeUtc));
                        if (result.Count >= _options.MaxIndexedFiles) break;
                    }
                }
            }
            catch (Exception ex) { Console.Error.WriteLine($"Index {folder.Alias}: {ex.Message}"); }
            if (result.Count >= _options.MaxIndexedFiles) break;
        }
        return result.ToArray();
    }

    private Task CompleteAsync(string id, object? result, CancellationToken ct) => PostJsonAsync($"api/lab-agent/commands/{id}/result", new { ok=true, result }, ct);
    private async Task PostJsonAsync(string url, object body, CancellationToken ct) { using HttpResponseMessage r = await _http.PostAsJsonAsync(url, body, BridgeOptions.Json, ct); await EnsureSuccessAsync(r, ct); }
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string detail = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"Dashboard {(int)response.StatusCode}: {detail[..Math.Min(detail.Length, 500)]}");
    }
    private static string GetString(JsonElement e, string key) => e.TryGetProperty(key, out JsonElement v) ? v.GetString() ?? "" : "";
    public async ValueTask DisposeAsync() { await _devices.DisposeAsync(); _http.Dispose(); }
}
