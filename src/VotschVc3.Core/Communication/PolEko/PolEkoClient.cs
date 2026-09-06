using System.Text.Json;
using System.Text.Json.Serialization;
using Eneter.Messaging.DataProcessing.Serializing;
using Eneter.Messaging.EndPoints.TypedMessages;
using Eneter.Messaging.MessagingSystems.Composites.MonitoredMessagingComposit;
using Eneter.Messaging.MessagingSystems.MessagingSystemBase;
using Eneter.Messaging.MessagingSystems.TcpMessagingSystem;
using VotschVc3.Core.Protocol;

namespace VotschVc3.Core.Communication.PolEko;

/// <summary>POL-EKO LabDesk RPC over an AES-encrypted Eneter duplex TCP channel.</summary>
public sealed class PolEkoClient : IChamberDevice
{
    public const int DefaultPort = 56506;
    private static readonly TimeSpan MinimumLabDeskResponseTimeout = TimeSpan.FromSeconds(20);
    /// <summary>Existing LabDesk profile reserved for FOS quick/manual control.</summary>
    public const long ManualProgramId = 11;
    public const string ManualProgramName = "FOS LAB";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ISyncDuplexTypedMessageSender<string, string>? _sender;
    private IDuplexOutputChannel? _channel;
    private long? _activeProgramId;
    private double? _lastSetpoint;
    private double _manualProtectionUnderC = 0;
    private double _manualProtectionOverC = 300;

    public ChamberConnectionSettings Settings { get; private set; } = new() { Port = DefaultPort };
    public bool IsConnected => _channel?.IsConnected == true;
    public event EventHandler<FrameExchangedEventArgs>? FrameExchanged;

    /// <summary>Uses the operator's active safety limits when rebuilding FOS LAB.</summary>
    public void ConfigureManualProgramProtection(double underTemperatureC, double overTemperatureC)
    {
        if (!double.IsFinite(underTemperatureC) || !double.IsFinite(overTemperatureC) ||
            underTemperatureC >= overTemperatureC)
            throw new ArgumentOutOfRangeException(nameof(underTemperatureC),
                "Dolná ochrana programu FOS LAB musí byť menšia ako horná ochrana.");

        _manualProtectionUnderC = underTemperatureC;
        _manualProtectionOverC = overTemperatureC;
    }

    public async Task ConnectAsync(ChamberConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DisconnectCore();
            Settings = settings.Clone();
            if (Settings.Port is <= 0 or 502) Settings.Port = DefaultPort;
            await Task.Run(() =>
            {
                var tcp = new TcpMessagingSystemFactory { ConnectTimeout = Settings.ConnectTimeout, ReceiveTimeout = Settings.ReadTimeout, SendTimeout = Settings.ReadTimeout };
                var monitored = new MonitoredMessagingFactory(tcp, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(7));
                TimeSpan responseTimeout = Settings.ReadTimeout > MinimumLabDeskResponseTimeout
                    ? Settings.ReadTimeout
                    : MinimumLabDeskResponseTimeout;
                var factory = new DuplexTypedMessagesFactory(new AesSerializer("poleko"))
                {
                    // The real SLN 115 can acknowledge LAUNCH_BY_ID after roughly 15 s.
                    // A five-second timeout leaves that valid response queued and shifts it
                    // onto the following GET_STATUS request.
                    SyncResponseReceiveTimeout = responseTimeout,
                };
                var sender = factory.CreateSyncDuplexTypedMessageSender<string, string>();
                var channel = monitored.CreateDuplexOutputChannel($"tcp://{Settings.Host}:{Settings.Port}/");
                sender.AttachDuplexOutputChannel(channel);
                _sender = sender;
                _channel = channel;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch { DisconnectCore(); throw; }
        finally { _gate.Release(); }
    }

    public async Task DisconnectAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { DisconnectCore(); } finally { _gate.Release(); }
    }

    public async Task<ChamberReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        PolEkoRpcResponse response = await SendAsync("GET_STATUS", "version-2", false, cancellationToken).ConfigureAwait(false);
        using JsonDocument status = ParseDataObject(response, "GET_STATUS");
        if (!PolEkoLabDeskProtocol.TryReadMainTemperature(status.RootElement, out double measured))
            throw new InvalidDataException("POL-EKO GET_STATUS neobsahuje platnú hlavnú teplotu.");
        double? setpoint = TryFindNumber(status.RootElement, out double sp, "TEMPERATURE_SET", "SET_TEMPERATURE", "temperatureSetpoint", "setpoint") ? sp : _lastSetpoint;
        bool running = TryFindBoolean(status.RootElement, out bool active, "IS_RUNNING") && active;
        var digital = new DigitalChannels { StartChannelIndex = 0, Start = running };
        IReadOnlyList<double> analog = setpoint is { } target ? new[] { measured, target } : new[] { measured };
        string raw = $"POL-EKO LabDesk · T={measured:0.0} °C" + (setpoint is { } s ? $" · SP={s:0.0} °C" : "") + $" · {(running ? "RUNNING" : "STOPPED")}";
        return new ChamberReading(DateTimeOffset.Now, raw, analog, digital);
    }

    public async Task WriteSetpointsAsync(IReadOnlyList<double> setpoints, DigitalChannels digital, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setpoints);
        ArgumentNullException.ThrowIfNull(digital);
        if (!digital.Start) { await StopAsync(cancellationToken).ConfigureAwait(false); return; }
        if (setpoints.Count == 0 || !double.IsFinite(setpoints[0])) throw new ArgumentException("POL-EKO vyžaduje platnú cieľovú teplotu.", nameof(setpoints));

        double temperature = setpoints[0];

        // This controller has no direct SET_TEMPERATURE command. Keep one stable,
        // reserved LabDesk profile instead of asking older firmware for the unsupported
        // GET_NEXT_PROGRAM_ID command or continuously creating/deleting profiles.
        await SendAllowingAsync("STOP", null, true, cancellationToken, "NO_PROGRAM_IS_RUNNING").ConfigureAwait(false);
        _activeProgramId = null;
        await WaitForProgramStoppedAsync(cancellationToken).ConfigureAwait(false);

        string programJson = PolEkoLabDeskProtocol.BuildSingleSetpointProgram(
            ManualProgramId, temperature, _manualProtectionUnderC, _manualProtectionOverC);
        PolEkoRpcResponse update = await UpdateManualProgramAsync(programJson, cancellationToken).ConfigureAwait(false);
        if (!update.ResponseStatus.Equals("OK", StringComparison.OrdinalIgnoreCase))
        {
            PolEkoRpcResponse save = await SendAllowingAsync(
                "SAVE_PROGRAM", programJson, true, cancellationToken, "PROGRAM_ALREADY_EXIST").ConfigureAwait(false);
            if (save.ResponseStatus.Equals("PROGRAM_ALREADY_EXIST", StringComparison.OrdinalIgnoreCase))
                await SendAsync("UPDATE_PROGRAM", programJson, true, cancellationToken).ConfigureAwait(false);
        }

        await LaunchManualProgramAsync(cancellationToken).ConfigureAwait(false);
        await VerifyManualProgramStartedAsync(cancellationToken).ConfigureAwait(false);
        await VerifyManualProgramDefinitionAsync(temperature, cancellationToken).ConfigureAwait(false);
        _activeProgramId = ManualProgramId;
        _lastSetpoint = temperature;
    }

    private async Task WaitForProgramStoppedAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            PolEkoRpcResponse response = await SendAsync("GET_STATUS", "version-2", false, cancellationToken).ConfigureAwait(false);
            using JsonDocument status = ParseDataObject(response, "GET_STATUS");
            bool running = TryFindBoolean(status.RootElement, out bool active, "IS_RUNNING") && active;
            if (!running)
            {
                // LabDesk can report STOPPED slightly before it releases the program for editing.
                await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken).ConfigureAwait(false);
                return;
            }

            if (attempt < 5)
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        throw new IOException("POL-EKO prijalo STOP, ale sušiareň do 3 sekúnd nepotvrdila zastavenie programu.");
    }

    private async Task<PolEkoRpcResponse> UpdateManualProgramAsync(string programJson, CancellationToken cancellationToken)
    {
        PolEkoRpcResponse? response = null;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            response = await SendAllowingAsync(
                "UPDATE_PROGRAM", programJson, true, cancellationToken, "NO_DATA", "GENERAL_ERROR").ConfigureAwait(false);
            if (!response.ResponseStatus.Equals("GENERAL_ERROR", StringComparison.OrdinalIgnoreCase))
                return response;

            if (attempt < 5)
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        throw new PolEkoRpcException("UPDATE_PROGRAM", response?.ResponseStatus ?? "GENERAL_ERROR", response?.Data ?? string.Empty);
    }

    private async Task LaunchManualProgramAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync("LAUNCH_BY_ID", ManualProgramId.ToString(), false, cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (TimeoutException)
        {
            // The command may have reached LabDesk even though its reply did not. SendAsync
            // closes the contaminated channel so the late response cannot be consumed by the
            // next request. Reconnect, inspect the real state and repeat LAUNCH only if needed.
            ChamberConnectionSettings reconnectSettings = Settings.Clone();
            await ConnectAsync(reconnectSettings, cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken).ConfigureAwait(false);
            if (await IsManualProgramRunningAsync(cancellationToken).ConfigureAwait(false))
                return;

            await SendAsync("LAUNCH_BY_ID", ManualProgramId.ToString(), false, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> IsManualProgramRunningAsync(CancellationToken cancellationToken)
    {
        PolEkoRpcResponse response = await SendAsync("GET_STATUS", "version-2", false, cancellationToken).ConfigureAwait(false);
        using JsonDocument status = ParseDataObject(response, "GET_STATUS");
        return TryFindBoolean(status.RootElement, out bool running, "IS_RUNNING") && running &&
               (!TryFindNumber(status.RootElement, out double programId, "PROGRAM_ID") ||
                Math.Abs(programId - ManualProgramId) < 0.5);
    }

    private async Task VerifyManualProgramDefinitionAsync(double expectedTemperatureC, CancellationToken cancellationToken)
    {
        PolEkoRpcResponse response;
        try
        {
            response = await SendAsync("GET_PROGRAMS", null, true, cancellationToken).ConfigureAwait(false);
        }
        catch (PolEkoRpcException ex) when (IsUnavailableProgramCatalog(ex.ResponseStatus))
        {
            // Some SLN 115/LabDesk versions accept UPDATE_PROGRAM and confirm the
            // running program through GET_STATUS, but their GET_PROGRAMS endpoint
            // always answers DATA_CORRUPTED. Runtime verification above is the
            // authoritative confirmation in that compatibility mode.
            return;
        }

        if (!PolEkoLabDeskProtocol.TryReadProgramTemperature(
                response.Data, ManualProgramId, out double storedTemperatureC))
            throw new InvalidDataException($"POL-EKO po spustení nevrátilo definíciu programu {ManualProgramId} ({ManualProgramName}).");
        if (Math.Abs(storedTemperatureC - expectedTemperatureC) > 0.051)
        {
            await SendAllowingAsync("STOP", null, true, CancellationToken.None, "NO_PROGRAM_IS_RUNNING").ConfigureAwait(false);
            throw new InvalidDataException(
                $"POL-EKO uložilo do programu {ManualProgramName} teplotu {storedTemperatureC:0.0} °C namiesto požadovaných {expectedTemperatureC:0.0} °C; program bol zastavený.");
        }
    }

    public static bool IsUnavailableProgramCatalog(string? responseStatus) =>
        string.Equals(responseStatus, "DATA_CORRUPTED", StringComparison.OrdinalIgnoreCase);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await SendAllowingAsync("STOP", null, true, cancellationToken, "NO_PROGRAM_IS_RUNNING").ConfigureAwait(false);
        _activeProgramId = null;
    }

    public async Task<string> SendRawAsync(string frame, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frame);
        string command = frame.Trim();
        string? data = null;
        if (command.StartsWith('{'))
        {
            using JsonDocument doc = JsonDocument.Parse(command);
            command = doc.RootElement.GetProperty("requestCommand").GetString() ?? throw new InvalidDataException("Chýba requestCommand.");
            if (doc.RootElement.TryGetProperty("data", out JsonElement value) && value.ValueKind != JsonValueKind.Null)
                data = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        }
        return JsonSerializer.Serialize(await SendAsync(command, data, true, cancellationToken).ConfigureAwait(false), Json);
    }

    /// <summary>Compatibility entry point for the existing diagnostics button.</summary>
    public async Task<string> ScanRegistersAsync(int count = 64, CancellationToken cancellationToken = default)
    {
        PolEkoRpcResponse status = await SendAsync("GET_STATUS", "version-2", false, cancellationToken).ConfigureAwait(false);
        PolEkoRpcResponse config = await SendAsync("GET_CONFIG", null, true, cancellationToken).ConfigureAwait(false);
        return "LabDesk RPC GET_STATUS:\r\n" + status.Data + "\r\n\r\nLabDesk RPC GET_CONFIG:\r\n" + config.Data;
    }

    /// <summary>Reads the complete LabDesk program catalogue without changing device state.</summary>
    public async Task<string> GetProgramsAsync(CancellationToken cancellationToken = default)
    {
        PolEkoRpcResponse response = await SendAsync("GET_PROGRAMS", null, true, cancellationToken).ConfigureAwait(false);
        return PolEkoLabDeskProtocol.FormatProgramCatalog(response.Data);
    }

    private async Task<PolEkoRpcResponse> SendAsync(string command, string? data, bool credentials, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sender = _sender ?? throw new InvalidOperationException("POL-EKO nie je pripojené.");
            (string username, string password) = PolEkoLabDeskProtocol.ResolveCredential();
            string request = PolEkoLabDeskProtocol.BuildRequest(command, data, credentials, username: username, password: password);
            string raw;
            try
            {
                raw = await Task.Run(() => sender.SendRequestMessage(request), ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (IsResponseTimeout(ex))
            {
                // A delayed reply remains associated with the old synchronous sender. Tear the
                // channel down now; otherwise the next request can receive the previous reply.
                DisconnectCore();
                throw new TimeoutException(
                    $"POL-EKO {command} neodpovedalo do {MinimumLabDeskResponseTimeout.TotalSeconds:0} sekúnd.", ex);
            }
            string diagnosticRequest = PolEkoLabDeskProtocol.BuildRequest(
                command, data, credentials, redactPassword: true, username: username, password: password);
            FrameExchanged?.Invoke(this, new FrameExchangedEventArgs(diagnosticRequest, raw));
            var response = JsonSerializer.Deserialize<PolEkoRpcResponse>(raw, Json) ?? throw new InvalidDataException("POL-EKO vrátilo prázdnu RPC odpoveď.");
            if (!response.RequestCommand.Equals(command, StringComparison.OrdinalIgnoreCase))
            {
                DisconnectCore();
                throw new InvalidDataException(
                    $"POL-EKO vrátilo odpoveď na {response.RequestCommand} namiesto očakávaného {command}; spojenie bolo ukončené kvôli oneskorenej odpovedi.");
            }
            if (!response.ResponseStatus.Equals("OK", StringComparison.OrdinalIgnoreCase)) throw new PolEkoRpcException(command, response.ResponseStatus, response.Data);
            return response;
        }
        finally { _gate.Release(); }
    }

    private static bool IsResponseTimeout(InvalidOperationException exception) =>
        exception.Message.Contains("failed to receive the response within the timeout", StringComparison.OrdinalIgnoreCase);

    private async Task<PolEkoRpcResponse> SendAllowingAsync(string command, string? data, bool credentials, CancellationToken ct, params string[] allowed)
    {
        try { return await SendAsync(command, data, credentials, ct).ConfigureAwait(false); }
        catch (PolEkoRpcException ex) when (allowed.Contains(ex.ResponseStatus, StringComparer.OrdinalIgnoreCase))
        { return new PolEkoRpcResponse { RequestCommand = command, ResponseStatus = ex.ResponseStatus, Data = ex.ResponseData }; }
    }

    private async Task VerifyManualProgramStartedAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            PolEkoRpcResponse response = await SendAsync("GET_STATUS", "version-2", false, cancellationToken).ConfigureAwait(false);
            using JsonDocument status = ParseDataObject(response, "GET_STATUS");
            if (TryFindBoolean(status.RootElement, out bool running, "IS_RUNNING") && running &&
                (!TryFindNumber(status.RootElement, out double programId, "PROGRAM_ID") ||
                 Math.Abs(programId - ManualProgramId) < 0.5))
            {
                return;
            }

            if (attempt < 5)
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        // Do not report success to the UI (and therefore do not start its timer) when
        // LabDesk accepted LAUNCH_BY_ID but the controller remained idle.
        await SendAllowingAsync("STOP", null, true, CancellationToken.None, "NO_PROGRAM_IS_RUNNING").ConfigureAwait(false);
        throw new IOException($"POL-EKO prijalo spustenie manuálneho programu {ManualProgramId}, ale sušiareň do 3 sekúnd nepotvrdila stav RUNNING.");
    }

    private void DisconnectCore()
    {
        try { _channel?.CloseConnection(); } catch { }
        try { _sender?.DetachDuplexOutputChannel(); } catch { }
        _sender = null; _channel = null; _activeProgramId = null;
    }

    private static JsonDocument ParseDataObject(PolEkoRpcResponse response, string command) =>
        string.IsNullOrWhiteSpace(response.Data) ? throw new InvalidDataException($"POL-EKO {command} vrátilo prázdne Data.") : JsonDocument.Parse(response.Data);
    internal static bool TryFindNumber(JsonElement e, out double value, params string[] names)
    {
        if (e.ValueKind == JsonValueKind.Object) foreach (JsonProperty p in e.EnumerateObject())
        {
            if (names.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out value)) return true;
                if (p.Value.ValueKind == JsonValueKind.String && double.TryParse(p.Value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)) return true;
                if (p.Value.ValueKind == JsonValueKind.Object && TryFindNumber(p.Value, out value, "value", "Value", "valueProbe")) return true;
            }
            if (TryFindNumber(p.Value, out value, names)) return true;
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (JsonElement item in e.EnumerateArray()) if (TryFindNumber(item, out value, names)) return true;
        value = 0; return false;
    }

    internal static bool TryFindElement(JsonElement e, string name, out JsonElement value)
    {
        if (e.ValueKind == JsonValueKind.Object) foreach (JsonProperty p in e.EnumerateObject())
        {
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
            if (TryFindElement(p.Value, name, out value)) return true;
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (JsonElement item in e.EnumerateArray()) if (TryFindElement(item, name, out value)) return true;
        value = default; return false;
    }

    private static bool TryFindBoolean(JsonElement e, out bool value, params string[] names)
    {
        if (e.ValueKind == JsonValueKind.Object) foreach (JsonProperty p in e.EnumerateObject())
        {
            if (names.Contains(p.Name, StringComparer.OrdinalIgnoreCase) && p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) { value = p.Value.GetBoolean(); return true; }
            if (TryFindBoolean(p.Value, out value, names)) return true;
        }
        value = false; return false;
    }

    public async ValueTask DisposeAsync() { await DisconnectAsync().ConfigureAwait(false); _gate.Dispose(); }
}

/// <summary>Pure LabDesk JSON helpers, separated for protocol regression tests.</summary>
public static class PolEkoLabDeskProtocol
{
    public const string UsernameEnvironmentVariable = "CHAMBER_FOS_POL_EKO_USERNAME";
    public const string PasswordEnvironmentVariable = "CHAMBER_FOS_POL_EKO_PASSWORD";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static string BuildRequest(
        string command,
        string? data,
        bool credentials,
        bool redactPassword = false,
        string? username = null,
        string? password = null) => JsonSerializer.Serialize(new PolEkoRpcRequest
    {
        RequestCommand = command,
        UserCredential = credentials
            ? new PolEkoCredential
            {
                Username = string.IsNullOrWhiteSpace(username) ? "admin" : username,
                Password = redactPassword ? "***" : password ?? "admin",
            }
            : null,
        Data = data,
    }, Json);

    public static (string Username, string Password) ResolveCredential()
    {
        string username = ResolveEnvironmentVariable(UsernameEnvironmentVariable) ?? "admin";
        string password = ResolveEnvironmentVariable(PasswordEnvironmentVariable) ?? "admin";
        return (username, password);
    }

    private static string? ResolveEnvironmentVariable(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value)) return value;
        try
        {
            value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(value)) return value;
            value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Reads the main probe from both known GET_STATUS response shapes.</summary>
    public static bool TryReadMainTemperature(JsonElement status, out double value)
    {
        if (PolEkoClient.TryFindElement(status, "TEMPERATURE_MAIN", out JsonElement mainProbe))
        {
            if (mainProbe.ValueKind == JsonValueKind.Number && mainProbe.TryGetDouble(out value))
                return double.IsFinite(value);
            if (mainProbe.ValueKind == JsonValueKind.String &&
                double.TryParse(mainProbe.GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
                return double.IsFinite(value);
            return PolEkoClient.TryFindNumber(mainProbe, out value, "valueProbe", "value") && double.IsFinite(value);
        }

        return PolEkoClient.TryFindNumber(
            status, out value, "TEMPERATURE_MAIN_VALUE", "temperatureMain", "temperature") && double.IsFinite(value);
    }

    public static string BuildSingleSetpointProgram(
        long id,
        double temperatureC,
        double underTemperatureC = -100,
        double overTemperatureC = 300)
    {
        if (!double.IsFinite(temperatureC)) throw new ArgumentOutOfRangeException(nameof(temperatureC));
        if (!double.IsFinite(underTemperatureC) || !double.IsFinite(overTemperatureC) ||
            underTemperatureC >= overTemperatureC)
            throw new ArgumentOutOfRangeException(nameof(underTemperatureC));
        if (temperatureC < underTemperatureC || temperatureC > overTemperatureC)
            throw new ArgumentOutOfRangeException(nameof(temperatureC),
                $"Setpoint {temperatureC:0.###} °C je mimo ochrany programu [{underTemperatureC:0.###}; {overTemperatureC:0.###}] °C.");
        return JsonSerializer.Serialize(new PolEkoProgram
        {
            ProgramId = id,
            Name = id == PolEkoClient.ManualProgramId ? PolEkoClient.ManualProgramName : $"LabControl {temperatureC:0.0}C",
            TempProtection = new PolEkoTemperatureProtection
            {
                UnderTemperatureLimit = underTemperatureC,
                OverTemperatureLimit = overTemperatureC,
            },
            // LabDesk program fields are expressed directly in degrees Celsius.
            Segments = [new PolEkoProgramSegment { Temperature = temperatureC, IsInfinityEnabled = true }],
        }, Json);
    }

    public static bool TryReadProgramTemperature(string? data, long programId, out double temperatureC)
    {
        temperatureC = 0;
        if (string.IsNullOrWhiteSpace(data)) return false;
        using JsonDocument document = JsonDocument.Parse(data);
        IEnumerable<JsonElement> programs = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray()
            : new[] { document.RootElement };
        foreach (JsonElement program in programs)
        {
            if (!PolEkoClient.TryFindNumber(program, out double id, "programId") || Math.Abs(id - programId) >= 0.5)
                continue;
            if (!PolEkoClient.TryFindElement(program, "segments", out JsonElement segments) ||
                segments.ValueKind != JsonValueKind.Array || segments.GetArrayLength() == 0 ||
                !PolEkoClient.TryFindNumber(segments[0], out double wireTemperature, "temperature"))
                return false;
            temperatureC = wireTemperature;
            return double.IsFinite(temperatureC);
        }
        return false;
    }

    public static string FormatProgramCatalog(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return "POL-EKO nevrátilo žiadne programy.";

        using JsonDocument document = JsonDocument.Parse(data);
        int count = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.GetArrayLength()
            : 1;
        string formatted = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        return $"POL-EKO programy: {count}\r\n\r\n{formatted}";
    }
}

public sealed class PolEkoRpcException : IOException
{
    public PolEkoRpcException(string command, string? status, string? data) : base($"POL-EKO {command}: {status ?? "GENERAL_ERROR"}" + (string.IsNullOrWhiteSpace(data) ? "" : $" · {data}")) { ResponseStatus = status ?? "GENERAL_ERROR"; ResponseData = data; }
    public string ResponseStatus { get; }
    public string? ResponseData { get; }
}
public sealed class PolEkoRpcRequest { public string RequestCommand { get; set; } = ""; public PolEkoCredential? UserCredential { get; set; } public string? Data { get; set; } }
public sealed class PolEkoRpcResponse { public string RequestCommand { get; set; } = ""; public string ResponseStatus { get; set; } = ""; public string? Data { get; set; } }
public sealed class PolEkoCredential { public string Username { get; set; } = "admin"; public string Password { get; set; } = "admin"; }
public sealed class PolEkoProgram
{
    public long ProgramId { get; set; } public string Name { get; set; } = "LabControl"; public int Interval { get; set; } = 60; public int Owner { get; set; } = 1;
    public string ProgramMode { get; set; } = "Advanced"; public long ParentId { get; set; } public List<long> ChildrenProgram { get; set; } = [];
    public PolEkoLoop Loop { get; set; } = new(); public PolEkoTemperatureProtection TempProtection { get; set; } = new(); public List<PolEkoProgramSegment> Segments { get; set; } = [];
}
public sealed class PolEkoLoop { public bool Enabled { get; set; } public bool Infinity { get; set; } public int Number { get; set; } = 1; }
public sealed class PolEkoTemperatureProtection { public string ProtectionMode { get; set; } = "Class_3_1"; public double OverTemperatureLimit { get; set; } = 50; public double UnderTemperatureLimit { get; set; } = 50; }
public sealed class PolEkoProgramSegment
{
    public long Duration { get; set; } public string Priority { get; set; } = "Parameters"; public double Temperature { get; set; } public short Fan { get; set; } = 100; public short Flap { get; set; } public bool Bolt { get; set; }
    [JsonPropertyName("edge.duration")] public int EdgeDuration { get; set; }
    [JsonPropertyName("edge.fan")] public int EdgeFan { get; set; } = 100;
    [JsonPropertyName("edge.airFlap")] public short EdgeAirFlap { get; set; }
    [JsonPropertyName("edge.enable")] public bool EdgeEnable { get; set; }
    [JsonPropertyName("IsInfinityEnabled")] public bool IsInfinityEnabled { get; set; }
    public PolEkoHumidity Humidity { get; set; } = new();
}
public sealed class PolEkoHumidity { public bool Enable { get; set; } public double Parameter { get; set; } }
