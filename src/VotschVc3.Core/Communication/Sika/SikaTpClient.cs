using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using VotschVc3.Core.Protocol;

namespace VotschVc3.Core.Communication.Sika;

/// <summary>
/// <see cref="IChamberDevice"/> for a SIKA TP Premium calibration bath / dry
/// block over its HTTP REST-API (commands under <c>ajax/</c>). Reads the
/// reference temperature and set point (via <c>getGradientInfo</c>, falling back
/// to <c>getRegister</c>), and writes a set point via <c>setRegister</c>
/// (<c>Task_SetPointList</c> + <c>TRset_SP</c>). Temperature only – no humidity
/// channel. A set point only takes effect while the task is running, so
/// <see cref="WriteSetpointsAsync"/> starts the device the same way the web UI's
/// START does (<c>startCurrentTask</c> + <c>System_ReglerOnOff</c> = 1) when the
/// controller is still off, and <see cref="StopAsync"/> stops it
/// (<c>stopCurrentTask</c> + <c>System_ReglerOnOff</c> = 0).
/// All HTTP requests are serialised through <see cref="_ioGate"/> (live
/// polling, the manual terminal and a set point write never interleave on
/// the wire) – the device's embedded web server answered concurrent requests
/// with sporadic 404s / stale bodies otherwise, which made the write look
/// like it had no effect on the real bath.
///
/// <para>Robustness notes learned on the real baths:</para>
/// <list type="bullet">
/// <item>The lab devices sit on the local network – the system HTTP proxy (if
/// any is configured on the PC) must be bypassed, otherwise every request dies
/// in the proxy while the raw-TCP devices keep working.</item>
/// <item>The embedded web server occasionally answers a single request with a
/// sporadic 404 / stale body, so one-shot commands are retried instead of
/// failing the whole connect / write.</item>
/// <item><c>getInfoReport</c> generates a full report and can take longer than
/// the connect timeout – the connect probe therefore uses the cheap
/// <c>getRegister</c> instead.</item>
/// <item>A write can be acknowledged and still ignored (manual mode, running
/// calibration, remote control disabled), so the new set point is read back
/// from <c>TRset_SP</c> and a mismatch is reported instead of looking fine
/// while nothing happens.</item>
/// </list>
/// </summary>
public sealed class SikaTpClient : IChamberDevice
{
    /// <summary>Attempts for one logical command before giving up (embedded server hiccups).</summary>
    private const int RetryAttempts = 3;

    /// <summary>Pause between retry attempts.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(350);

    /// <summary>How closely the read-back set point must match the written one (°C).</summary>
    private const double SetpointVerifyTolerance = 0.1;

    /// <summary>Read-back attempts while waiting for the device to store the new set point.</summary>
    private const int SetpointVerifyAttempts = 4;

    /// <summary>Pause between set point read-back attempts.</summary>
    private static readonly TimeSpan SetpointVerifyDelay = TimeSpan.FromMilliseconds(400);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private readonly Func<ChamberConnectionSettings, HttpClient> _httpFactory;

    private HttpClient? _http;

    /// <summary>Creates a client that opens a real <see cref="HttpClient"/> per connection.</summary>
    public SikaTpClient()
        : this(CreateHttpClient)
    {
    }

    /// <summary>Creates a client with a custom <see cref="HttpClient"/> factory (used for tests).</summary>
    public SikaTpClient(Func<ChamberConnectionSettings, HttpClient> httpFactory)
    {
        _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
        Settings = new ChamberConnectionSettings { Port = SikaRestApiProtocol.DefaultPort };
    }

    /// <summary>
    /// HTTP client tuned for the bath's embedded web server: no system proxy
    /// (the bath is a local-network device – a configured corporate proxy would
    /// swallow the requests), an explicit TCP connect timeout, and
    /// "Connection: close" so every request gets a fresh connection (keep-alive
    /// against the embedded server produced stale bodies).
    /// </summary>
    private static HttpClient CreateHttpClient(ChamberConnectionSettings settings)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = settings.ConnectTimeout,
        };
        var http = new HttpClient(handler) { Timeout = settings.ReadTimeout };
        http.DefaultRequestHeaders.ConnectionClose = true;
        return http;
    }

    public ChamberConnectionSettings Settings { get; private set; }

    public bool IsConnected { get; private set; }

    public bool? RemoteControlEnabled { get; private set; }

    public event EventHandler<FrameExchangedEventArgs>? FrameExchanged;

    public async Task ConnectAsync(ChamberConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DisposeHttp();
            Settings = settings.Clone();
            _http = _httpFactory(Settings);

            // No persistent connection with HTTP – confirm the device actually
            // answers before reporting "connected". The probe reads a register
            // rather than getInfoReport: the report is generated on the device and
            // took longer than the connect timeout on real baths, while getRegister
            // is cheap and exists on every TP software version. The whole probe
            // (retries included) is bounded so an unreachable bath still fails fast.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(Settings.ConnectTimeout + Settings.ReadTimeout);
            string url = SikaRestApiProtocol.BuildGetRegisterUrl(
                Settings.Host, Settings.Port, SikaRestApiProtocol.MeasuredRegister);
            string response = await GetWithRetryAsync(url, linked.Token).ConfigureAwait(false);
            RaiseFrame($"GET {url}", response);

            if (SikaRestApiProtocol.ParseRegisterValue(response) is null)
            {
                throw new InvalidOperationException(
                    $"Zariadenie na {Settings.Host}:{Settings.Port} odpovedá, ale nie ako SIKA REST-API " +
                    $"(getRegister nevrátil hodnotu). Skontroluj, či je zadaný REST-API port " +
                    $"({SikaRestApiProtocol.DefaultPort}), nie port webovej aplikácie, a či je na prístroji " +
                    $"povolené vzdialené REST-API ovládanie. Odpoveď: {Truncate(response)}");
            }

            IsConnected = true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            DisposeHttp();
            throw new TimeoutException(
                $"Časový limit pri pripájaní na SIKA {settings.Host}:{settings.Port}. " +
                "Skontroluj, či je kúpeľ zapnutý a dostupný na sieti (ping na IP).");
        }
        catch (HttpRequestException ex)
        {
            DisposeHttp();
            throw new InvalidOperationException(BuildConnectErrorMessage(settings, ex), ex);
        }
        catch
        {
            DisposeHttp();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Turns a low-level HTTP failure into an actionable Slovak message.</summary>
    private static string BuildConnectErrorMessage(ChamberConnectionSettings settings, HttpRequestException ex)
    {
        string endpoint = $"{settings.Host}:{settings.Port}";
        if (ex.StatusCode is { } status)
        {
            return $"SIKA {endpoint} odpovedala HTTP {(int)status} ({status}). " +
                   $"Skontroluj REST-API port ({SikaRestApiProtocol.DefaultPort}) a či je REST-API na prístroji povolené.";
        }

        if (ex.InnerException is SocketException se)
        {
            string reason = se.SocketErrorCode switch
            {
                SocketError.ConnectionRefused =>
                    "spojenie odmietnuté – prístroj beží, ale na tomto porte REST-API nepočúva " +
                    $"(skontroluj port {SikaRestApiProtocol.DefaultPort} a povolenie REST-API)",
                SocketError.TimedOut or SocketError.HostUnreachable or SocketError.NetworkUnreachable =>
                    "prístroj je nedostupný na sieti (skontroluj napájanie, kábel a IP adresu)",
                SocketError.HostNotFound => "adresa sa nedá preložiť (skontroluj IP/hostname)",
                _ => se.Message,
            };
            return $"Nepodarilo sa pripojiť na SIKA {endpoint}: {reason}.";
        }

        return $"Nepodarilo sa pripojiť na SIKA {endpoint}: {ex.Message}";
    }

    public Task DisconnectAsync()
    {
        DisposeHttp();
        return Task.CompletedTask;
    }

    public async Task<ChamberReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        double? measured = null;
        double? setpoint = null;

        // Prefer the single getGradientInfo call – newer chambers (e.g. TP37200E.2
        // on port 80) return TR (reference temperature) and SP together, so it is
        // one round-trip instead of two. Older TP Premium firmware does not serve
        // the endpoint (HTTP 404); fall back to the per-register reads below.
        try
        {
            string gradientUrl = SikaRestApiProtocol.BuildGradientInfoUrl(Settings.Host, Settings.Port);
            string gradientJson = await GetAsync(gradientUrl, cancellationToken).ConfigureAwait(false);
            RaiseFrame($"GET {gradientUrl}", gradientJson);
            if (SikaRestApiProtocol.ParseGradientInfo(gradientJson) is { ReferenceTemperature: not null } gradient)
            {
                measured = gradient.ReferenceTemperature;
                setpoint = gradient.Setpoint;
            }
        }
        catch (HttpRequestException)
        {
            // getGradientInfo not available on this device/firmware – fall through.
        }

        if (measured is null)
        {
            string measuredUrl = SikaRestApiProtocol.BuildGetRegisterUrl(Settings.Host, Settings.Port, SikaRestApiProtocol.MeasuredRegister);
            string measuredJson = await GetAsync(measuredUrl, cancellationToken).ConfigureAwait(false);
            RaiseFrame($"GET {measuredUrl}", measuredJson);
            measured = SikaRestApiProtocol.ParseRegisterValue(measuredJson);

            string setpointUrl = SikaRestApiProtocol.BuildGetRegisterUrl(Settings.Host, Settings.Port, SikaRestApiProtocol.SetpointRegister);
            string setpointJson = await GetAsync(setpointUrl, cancellationToken).ConfigureAwait(false);
            RaiseFrame($"GET {setpointUrl}", setpointJson);
            setpoint = SikaRestApiProtocol.ParseRegisterValue(setpointJson);
        }

        try
        {
            RemoteControlEnabled = await ReadRemoteControlEnabledAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            RemoteControlEnabled = null;
        }

        var analog = new List<double>();
        if (measured is { } m) analog.Add(m);
        if (setpoint is { } sp) analog.Add(sp);

        string raw = "SIKA TP" +
            (measured is { } mv ? $" · T={mv:0.000} °C" : " · T=?") +
            (setpoint is { } spv ? $" · SP={spv:0.0} °C" : string.Empty);

        return new ChamberReading(DateTimeOffset.Now, raw, analog, new DigitalChannels());
    }

    public async Task WriteSetpointsAsync(
        IReadOnlyList<double> setpoints,
        DigitalChannels digital,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setpoints);
        ArgumentNullException.ThrowIfNull(digital);

        await EnsureRemoteControlEnabledAsync(cancellationToken).ConfigureAwait(false);
        double temperature = setpoints.Count > 0 ? setpoints[0] : 0d;

        // START before the set point, the same way turning on a profile does. A set
        // point only sticks while the task is running, and startCurrentTask *reloads*
        // the task – so writing the set point first would be discarded. When the caller
        // requests "system on" (digital start channel) and the controller is still off,
        // run the verified START (startCurrentTask + System_ReglerOnOff=1) first; if it
        // is already running, skip straight to the set point write.
        if (digital.Start && !await IsControllerOnAsync(cancellationToken).ConfigureAwait(false))
        {
            await StartCurrentTaskAsync(cancellationToken).ConfigureAwait(false);
        }

        // Write the set point the way the device's own web UI does (verified on a real
        // TP3M165E.2): the EasyMode task set point list first, then the live set point
        // register, both via setRegister – not the older setSP command.
        string listUrl = SikaRestApiProtocol.BuildSetRegisterUrl(
            Settings.Host, Settings.Port, SikaRestApiProtocol.TaskSetPointListRegister, temperature);
        string listResponse = await GetWithRetryAsync(listUrl, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {listUrl}", listResponse);
        SikaRestApiProtocol.ParseSetRegisterResponse(listResponse);

        string setpointUrl = SikaRestApiProtocol.BuildSetRegisterUrl(
            Settings.Host, Settings.Port, SikaRestApiProtocol.SetpointRegister, temperature);
        string setpointResponse = await GetWithRetryAsync(setpointUrl, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {setpointUrl}", setpointResponse);
        double applied = SikaRestApiProtocol.ParseSetRegisterResponse(setpointResponse);

        // The device acknowledged the write – now check it really stored the value.
        // Manual mode, a running calibration or disabled remote control make it
        // acknowledge and ignore, which the operator only sees as "the bath never
        // starts heating". Surface that instead of reporting success.
        double? stored = await VerifySetpointAsync(applied, cancellationToken).ConfigureAwait(false);
        if (stored is { } sp && Math.Abs(sp - applied) > SetpointVerifyTolerance)
        {
            throw new InvalidOperationException(
                $"SIKA potvrdila zápis {applied:0.0} °C, ale v prístroji zostal setpoint {sp:0.0} °C – " +
                "zápis sa nevykonal. Skontroluj, či prístroj nie je v ručnom režime alebo v prebiehajúcej " +
                "kalibrácii a či má povolené vzdialené ovládanie.");
        }

        RaiseFrame("SET", stored is { } ok
            ? $"{ok:0.0} °C aplikovaných a overených (TRset_SP)."
            : $"{applied:0.0} °C aplikovaných (overenie čítaním sa nepodarilo, pokračujem).");
    }

    /// <summary>
    /// Reads <see cref="SikaRestApiProtocol.SetpointRegister"/> back until it matches
    /// <paramref name="expected"/> or the attempts run out. Returns the last value read,
    /// or <c>null</c> when the read-back itself never succeeded – no false alarm then,
    /// the write was already acknowledged by the device.
    /// </summary>
    private async Task<double?> VerifySetpointAsync(double expected, CancellationToken cancellationToken)
    {
        string url = SikaRestApiProtocol.BuildGetRegisterUrl(
            Settings.Host, Settings.Port, SikaRestApiProtocol.SetpointRegister);
        double? last = null;
        for (int attempt = 1; attempt <= SetpointVerifyAttempts; attempt++)
        {
            try
            {
                string json = await GetAsync(url, cancellationToken).ConfigureAwait(false);
                RaiseFrame($"GET {url}", json);
                last = SikaRestApiProtocol.ParseRegisterValue(json) ?? last;
                if (last is { } v && Math.Abs(v - expected) <= SetpointVerifyTolerance)
                {
                    return v;
                }
            }
            catch (Exception ex) when (IsTransient(ex) && !cancellationToken.IsCancellationRequested)
            {
                // Read-back is best effort – keep trying, the write itself succeeded.
            }

            if (attempt < SetpointVerifyAttempts)
            {
                await Task.Delay(SetpointVerifyDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        return last;
    }

    /// <summary>Reads <c>System_ReglerOnOff</c>; <c>true</c> when the controller is on (value ≥ 0.5).</summary>
    private async Task<bool> IsControllerOnAsync(CancellationToken cancellationToken)
    {
        string url = SikaRestApiProtocol.BuildGetRegisterUrl(Settings.Host, Settings.Port, SikaRestApiProtocol.ControllerOnOffRegister);
        string json = await GetWithRetryAsync(url, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {url}", json);
        return SikaRestApiProtocol.ParseRegisterValue(json) is >= 0.5;
    }

    /// <summary>Runs the verified START: <c>startCurrentTask</c> then <c>System_ReglerOnOff</c> = 1.</summary>
    private async Task StartCurrentTaskAsync(CancellationToken cancellationToken)
    {
        string startUrl = SikaRestApiProtocol.BuildStartCurrentTaskUrl(Settings.Host, Settings.Port);
        string startResponse = await GetWithRetryAsync(startUrl, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {startUrl}", startResponse);
        SikaRestApiProtocol.EnsureCommandSucceeded(startResponse, "startCurrentTask");

        string onUrl = SikaRestApiProtocol.BuildSetRegisterUrl(
            Settings.Host, Settings.Port, SikaRestApiProtocol.ControllerOnOffRegister, 1);
        string onResponse = await GetWithRetryAsync(onUrl, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {onUrl}", onResponse);
        SikaRestApiProtocol.ParseSetRegisterResponse(onResponse);
        RaiseFrame("START", "Regulátor zapnutý (startCurrentTask + System_ReglerOnOff=1).");
    }

    /// <summary>
    /// Stops the device the same way the web UI's STOP does (verified on a real
    /// TP3M165E.2): <c>stopCurrentTask</c> then <c>System_ReglerOnOff</c> = 0.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await EnsureRemoteControlEnabledAsync(cancellationToken).ConfigureAwait(false);
        string stopUrl = SikaRestApiProtocol.BuildStopCurrentTaskUrl(Settings.Host, Settings.Port);
        string stopResponse = await GetWithRetryAsync(stopUrl, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {stopUrl}", stopResponse);
        SikaRestApiProtocol.EnsureCommandSucceeded(stopResponse, "stopCurrentTask");

        string offUrl = SikaRestApiProtocol.BuildSetRegisterUrl(
            Settings.Host, Settings.Port, SikaRestApiProtocol.ControllerOnOffRegister, 0);
        string offResponse = await GetWithRetryAsync(offUrl, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {offUrl}", offResponse);
        SikaRestApiProtocol.ParseSetRegisterResponse(offResponse);
        RaiseFrame("STOP", "Regulátor vypnutý (stopCurrentTask + System_ReglerOnOff=0).");
    }

    /// <summary>
    /// Sends an ad-hoc ajax/ command (e.g. "getInfoReport" or
    /// "getRegister?register=TRset_TR") and returns the raw JSON response.
    /// </summary>
    public async Task<string> SendRawAsync(string frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        string command = frame.TrimStart('/');
        if (command.StartsWith("ajax/", StringComparison.OrdinalIgnoreCase)) command = command[5..];
        if (command.StartsWith("set", StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith("start", StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith("stop", StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith("delete", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureRemoteControlEnabledAsync(cancellationToken).ConfigureAwait(false);
        }
        string url = SikaRestApiProtocol.BuildCommandUrl(Settings.Host, Settings.Port, frame);
        string response = await GetAsync(url, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {url}", response);
        return response;
    }

    /// <summary>
    /// Reads the device's "Remote Control" (extern write) flag. Called on every poll,
    /// so a single failure is not retried here – the next poll asks again anyway.
    /// </summary>
    public Task<bool> ReadRemoteControlEnabledAsync(CancellationToken cancellationToken = default) =>
        ReadRemoteControlEnabledAsync(retry: false, cancellationToken);

    private async Task<bool> ReadRemoteControlEnabledAsync(bool retry, CancellationToken cancellationToken)
    {
        string url = SikaRestApiProtocol.BuildGetRegisterUrl(
            Settings.Host, Settings.Port, SikaRestApiProtocol.ExternWriteFlagRegister);
        string json = retry
            ? await GetWithRetryAsync(url, cancellationToken).ConfigureAwait(false)
            : await GetAsync(url, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {url}", json);
        double? value = SikaRestApiProtocol.ParseRegisterValue(json);
        if (value is null) throw new InvalidOperationException("SIKA nevrátila stav Remote Control.");
        RemoteControlEnabled = value >= 0.5;
        return RemoteControlEnabled.Value;
    }

    /// <summary>Lists the measurement logs stored on the device itself.</summary>
    public async Task<IReadOnlyList<SikaTaskLogSummary>> GetTaskLogsAsync(CancellationToken cancellationToken = default)
    {
        string url = SikaRestApiProtocol.BuildTaskLogIndexUrl(Settings.Host, Settings.Port);
        string json = await GetWithRetryAsync(url, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {url}", $"Zoznam SIKA logov ({json.Length} znakov)");
        return SikaTaskLogParser.ParseIndex(json);
    }

    /// <summary>Downloads the samples of one log stored on the device.</summary>
    public async Task<SikaTaskLogData> GetTaskLogDataAsync(int taskId, CancellationToken cancellationToken = default)
    {
        if (taskId <= 0) throw new ArgumentOutOfRangeException(nameof(taskId));
        string url = SikaRestApiProtocol.BuildTaskLogDataUrl(Settings.Host, Settings.Port, taskId);
        string json = await GetWithRetryAsync(url, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {url}", $"SIKA log {taskId} ({json.Length} znakov)");
        return SikaTaskLogParser.ParseData(json);
    }

    /// <summary>
    /// Gate in front of every command that changes the device: with "Remote Control"
    /// off the bath silently ignores writes, so refuse up front with a clear message
    /// instead of pretending the command landed. Retried, because this guards
    /// one-shot commands and must not fail them on a single server hiccup.
    /// </summary>
    private async Task EnsureRemoteControlEnabledAsync(CancellationToken cancellationToken)
    {
        bool enabled = await ReadRemoteControlEnabledAsync(retry: true, cancellationToken).ConfigureAwait(false);
        if (!enabled)
        {
            throw new InvalidOperationException(
                "Remote Control je na zariadení SIKA vypnutý. Zapni ho na displeji zariadenia.");
        }
    }

    /// <summary>
    /// Issues one GET with retries on transient failures (the embedded server's
    /// sporadic 404s / resets). Used for one-shot commands – connect, writes,
    /// START/STOP – where a single hiccup must not fail the whole operation; the
    /// live poll keeps using <see cref="GetAsync"/> because it simply repeats.
    /// </summary>
    private async Task<string> GetWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (int attempt = 1; attempt <= RetryAttempts; attempt++)
        {
            try
            {
                return await GetAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransient(ex) && !cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
                if (attempt < RetryAttempts)
                {
                    await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        throw lastError!;
    }

    /// <summary>Failures worth a retry against the flaky embedded web server.</summary>
    private static bool IsTransient(Exception ex) => ex
        is HttpRequestException
        or IOException
        or TaskCanceledException; // HttpClient request timeout (caller cancellation is filtered out above)

    private static string Truncate(string text) =>
        text.Length <= 120 ? text : text[..120] + "…";

    /// <summary>
    /// Issues one GET, serialised behind <see cref="_ioGate"/> so it never overlaps
    /// another request on the wire (see the class summary for why that matters).
    /// </summary>
    private async Task<string> GetAsync(string url, CancellationToken cancellationToken)
    {
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            HttpClient http = _http ?? throw new InvalidOperationException("Not connected to the SIKA device.");
            using HttpResponseMessage response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private void RaiseFrame(string request, string response) =>
        FrameExchanged?.Invoke(this, new FrameExchangedEventArgs(request, response));

    private void DisposeHttp()
    {
        _http?.Dispose();
        _http = null;
        IsConnected = false;
        RemoteControlEnabled = null;
    }

    public ValueTask DisposeAsync()
    {
        DisposeHttp();
        _gate.Dispose();
        _ioGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
