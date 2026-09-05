using System.Collections.Concurrent;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Communication.Sika;
using VotschVc3.Core.Communication.PolEko;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;

namespace VotschVc3.Agent;

public sealed class DeviceManager : IAsyncDisposable
{
    private readonly BridgeOptions _options;
    private readonly ConcurrentDictionary<string, DeviceRuntime> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ThermometerRuntime> _thermometers = new(StringComparer.OrdinalIgnoreCase);

    public DeviceManager(BridgeOptions options)
    {
        _options = options;
        foreach (DeviceOptions config in options.Devices.Where(d => d.Enabled)) _devices[config.Id] = new DeviceRuntime(config);
        foreach (ThermometerOptions config in options.Thermometers.Where(d => d.Enabled)) _thermometers[config.Id] = new ThermometerRuntime(config);
    }

    public async Task<DeviceSnapshot[]> ReadAllAsync(CancellationToken ct)
    {
        var tasks = _devices.Values.Select(d => d.ReadAsync(ct)).Concat(_thermometers.Values.Select(d => d.ReadAsync(ct)));
        return await Task.WhenAll(tasks);
    }

    public async Task<object?> ExecuteAsync(AgentCommand command, CancellationToken ct)
    {
        string deviceId = GetString(command.Payload, "deviceId");
        if (!_devices.TryGetValue(deviceId, out DeviceRuntime? device)) throw new InvalidOperationException($"Zariadenie {deviceId} neexistuje.");
        return command.Type switch
        {
            "device.setpoint" => await device.SetpointAsync(GetDouble(command.Payload, "value"), null, ct),
            "device.humidity" => await device.SetpointAsync(null, GetDouble(command.Payload, "value"), ct),
            "device.start" => await device.StartAsync(ct),
            "device.stop" => await device.StopAsync(ct),
            "raw.command" => await device.RawAsync(GetString(command.Payload, "command"), ct),
            "profile.start" => await device.StartProfileAsync(LoadProfile(GetString(command.Payload, "profileId")), ct),
            "profile.pause" => device.PauseProfile(),
            "profile.resume" => device.ResumeProfile(),
            "profile.stop" => await device.StopProfileAsync(ct),
            _ => throw new NotSupportedException($"Príkaz {command.Type} nie je zariadenový príkaz.")
        };
    }

    private TestProfile LoadProfile(string profileId)
    {
        string file = Path.GetFullPath(Environment.ExpandEnvironmentVariables(_options.ProfilesFile));
        var profiles = new ProfileStore(file).LoadAll();
        return profiles.FirstOrDefault(p => p.Id.ToString().Equals(profileId, StringComparison.OrdinalIgnoreCase))
            ?? profiles.FirstOrDefault(p => p.Name.Equals(profileId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Profil {profileId} sa v {file} nenašiel.");
    }

    private static string GetString(System.Text.Json.JsonElement e, string key) => e.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
    private static double GetDouble(System.Text.Json.JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.TryGetDouble(out double n) ? n : throw new InvalidOperationException($"Chýba číselná hodnota {key}.");

    public async ValueTask DisposeAsync()
    {
        foreach (DeviceRuntime d in _devices.Values) await d.DisposeAsync();
        foreach (ThermometerRuntime d in _thermometers.Values) await d.DisposeAsync();
    }
}

internal sealed class DeviceRuntime : IAsyncDisposable
{
    private readonly DeviceOptions _config;
    private readonly IChamberDevice _client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private double? _temperature, _setpoint, _humidity, _humiditySetpoint;
    private string _error = "";
    private ProfileRunner? _runner;
    private CancellationTokenSource? _profileCts;
    private Task? _profileTask;
    private object? _profileState;

    public DeviceRuntime(DeviceOptions config)
    {
        _config = config;
        _client = config.Protocol switch
        {
            ChamberProtocol.PolEkoModbus => new PolEkoClient(),
            ChamberProtocol.SikaRestApi => new SikaTpClient(),
            _ => new ChamberClient(),
        };
    }
    private ChamberConnectionSettings Settings => new()
    {
        Host = _config.Host, Port = _config.Port, Address = _config.Address,
        AnalogChannelCount = _config.AnalogChannelCount, StartChannelIndex = _config.StartChannelIndex,
        ConnectTimeout = TimeSpan.FromSeconds(4), ReadTimeout = TimeSpan.FromSeconds(6)
    };
    private async Task EnsureConnectedAsync(CancellationToken ct) { if (!_client.IsConnected) await _client.ConnectAsync(Settings, ct); }
    public async Task<DeviceSnapshot> ReadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureConnectedAsync(ct); ChamberReading r = await _client.ReadAsync(ct);
            _temperature = r.Temperature; _setpoint = r.TemperatureSetpoint; _humidity = _config.HasHumidity ? r.Humidity : null; _humiditySetpoint = _config.HasHumidity ? r.HumiditySetpoint : null; _error = "";
            return Snapshot(true, r.Timestamp);
        }
        catch (Exception ex)
        {
            _error = ex.Message; try { await _client.DisconnectAsync(); } catch { }
            return Snapshot(false, DateTimeOffset.Now);
        }
        finally { _gate.Release(); }
    }
    private DeviceSnapshot Snapshot(bool online, DateTimeOffset at) => new(_config.Id, _config.Name, _config.Kind == DeviceKind.Sika ? "sika" : "chamber", online, !_config.AllowControl, _temperature, _setpoint, _humidity, _humiditySetpoint, "", online ? (_profileTask is { IsCompleted: false } ? "profile" : "ready") : "offline", _error, _profileState, null, at);
    private void RequireControl() { if (!_config.AllowControl) throw new UnauthorizedAccessException($"Ovládanie {_config.Name} je v bridge.json vypnuté."); }
    public async Task<object> SetpointAsync(double? temp, double? hum, CancellationToken ct)
    {
        RequireControl(); await _gate.WaitAsync(ct);
        try { await EnsureConnectedAsync(ct); double t = temp ?? _setpoint ?? _temperature ?? 20; double? h = hum ?? _humiditySetpoint; var values = _config.HasHumidity && h is not null ? new[] { t, h.Value } : new[] { t }; var digital = new DigitalChannels { StartChannelIndex = _config.StartChannelIndex, Start = true }; await _client.WriteSetpointsAsync(values, digital, ct); _setpoint=t; if(hum is not null)_humiditySetpoint=hum; return new { appliedTemperature=t, appliedHumidity=_humiditySetpoint }; }
        finally { _gate.Release(); }
    }
    public Task<object> StartAsync(CancellationToken ct) => SetpointAsync(_setpoint ?? _temperature ?? 20, _humiditySetpoint, ct);
    public async Task<object> StopAsync(CancellationToken ct) { RequireControl(); await _gate.WaitAsync(ct); try { await EnsureConnectedAsync(ct); await _client.StopAsync(ct); return new { stopped=true }; } finally { _gate.Release(); } }
    public async Task<object> RawAsync(string command, CancellationToken ct) { RequireControl(); await _gate.WaitAsync(ct); try { await EnsureConnectedAsync(ct); return new { response=await _client.SendRawAsync(command,ct) }; } finally { _gate.Release(); } }
    public async Task<object> StartProfileAsync(TestProfile profile, CancellationToken ct)
    {
        RequireControl(); if (_profileTask is { IsCompleted:false }) throw new InvalidOperationException("Profil už beží.");
        DeviceSnapshot current = await ReadAsync(ct); if (!current.Online || current.Temperature is null) throw new InvalidOperationException("Zariadenie nemá platnú teplotu.");
        _profileCts = new CancellationTokenSource(); _runner = new ProfileRunner(_client, soakAllSegments: _config.Kind == DeviceKind.Sika, defaultSoakTolerance: .3);
        _runner.Progress += (_, e) => _profileState = new { id=profile.Id, name=profile.Name, segment=e.SegmentIndex, cycle=e.Cycle, progress=e.OverallFraction, target=e.TemperatureSetpoint, paused=_runner.IsPaused };
        _profileTask = Task.Run(async () => { try { await _runner.RunAsync(profile, current.Temperature.Value, current.Humidity, _profileCts.Token); } finally { _profileState = null; } });
        return new { started=true, profile=profile.Name };
    }
    public object PauseProfile() { if (_runner is null) throw new InvalidOperationException("Profil nebeží."); _runner.Pause(); return new { paused=true }; }
    public object ResumeProfile() { if (_runner is null) throw new InvalidOperationException("Profil nebeží."); _runner.Resume(); return new { resumed=true }; }
    public async Task<object> StopProfileAsync(CancellationToken ct) { _profileCts?.Cancel(); if (_profileTask is not null) try { await _profileTask; } catch (OperationCanceledException) { } await StopAsync(ct); _runner=null; _profileTask=null; return new { stopped=true }; }
    public async ValueTask DisposeAsync() { _profileCts?.Cancel(); await _client.DisposeAsync(); _gate.Dispose(); _profileCts?.Dispose(); }
}

internal sealed class ThermometerRuntime : IAsyncDisposable
{
    private readonly ThermometerOptions _config; private F100Client? _client;
    public ThermometerRuntime(ThermometerOptions config) { _config=config; }
    public async Task<DeviceSnapshot> ReadAsync(CancellationToken ct)
    {
        try { _client ??= new F100Client(_config.PortName,_config.BaudRate); if(!_client.IsOpen)await _client.OpenAsync(); var r=await _client.ReadAsync(_config.ReadCommand,ct); return new(_config.Id,_config.Name,"thermometer",true,true,r.Temperature,null,null,null,_config.SerialNumber,r.Unit,"",null,null,r.Timestamp); }
        catch(Exception ex) { if(_client is not null){await _client.DisposeAsync();_client=null;} return new(_config.Id,_config.Name,"thermometer",false,true,null,null,null,null,_config.SerialNumber,"offline",ex.Message,null,null,DateTimeOffset.Now); }
    }
    public async ValueTask DisposeAsync(){if(_client is not null)await _client.DisposeAsync();}
}
