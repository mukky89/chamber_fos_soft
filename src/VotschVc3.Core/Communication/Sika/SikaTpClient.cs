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
/// polling, the manual terminal and a <c>setSP</c> write never interleave on
/// the wire) – the device's embedded web server answered concurrent requests
/// with sporadic 404s / stale bodies otherwise, which made <c>setSP</c> look
/// like it had no effect on the real bath.
/// </summary>
public sealed class SikaTpClient : IChamberDevice
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private readonly Func<ChamberConnectionSettings, HttpClient> _httpFactory;

    private HttpClient? _http;

    /// <summary>Creates a client that opens a real <see cref="HttpClient"/> per connection.</summary>
    public SikaTpClient()
        : this(static s => new HttpClient { Timeout = s.ReadTimeout })
    {
    }

    /// <summary>Creates a client with a custom <see cref="HttpClient"/> factory (used for tests).</summary>
    public SikaTpClient(Func<ChamberConnectionSettings, HttpClient> httpFactory)
    {
        _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
        Settings = new ChamberConnectionSettings { Port = SikaRestApiProtocol.DefaultPort };
    }

    public ChamberConnectionSettings Settings { get; private set; }

    public bool IsConnected { get; private set; }

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
            // answers before reporting "connected".
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(Settings.ConnectTimeout);
            string url = SikaRestApiProtocol.BuildInfoReportUrl(Settings.Host, Settings.Port);
            string response = await GetAsync(url, linked.Token).ConfigureAwait(false);
            RaiseFrame($"GET {url}", response);
            IsConnected = true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            DisposeHttp();
            throw new TimeoutException($"Timed out connecting to {settings.Host}:{settings.Port}.");
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

        double temperature = setpoints.Count > 0 ? setpoints[0] : 0d;

        // Mirror the device's own web UI (verified on a real TP3M165E.2): a set point
        // change writes the EasyMode task set point list first, then the live set point
        // register, both via setRegister – not the older setSP command.
        string listUrl = SikaRestApiProtocol.BuildSetRegisterUrl(
            Settings.Host, Settings.Port, SikaRestApiProtocol.TaskSetPointListRegister, temperature);
        string listResponse = await GetAsync(listUrl, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {listUrl}", listResponse);
        SikaRestApiProtocol.ParseSetRegisterResponse(listResponse);

        string setpointUrl = SikaRestApiProtocol.BuildSetRegisterUrl(
            Settings.Host, Settings.Port, SikaRestApiProtocol.SetpointRegister, temperature);
        string setpointResponse = await GetAsync(setpointUrl, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {setpointUrl}", setpointResponse);
        double applied = SikaRestApiProtocol.ParseSetRegisterResponse(setpointResponse);

        // A written set point only takes effect while the task is running. When the
        // caller requests "system on" (digital start channel) and the controller is
        // still off, run the same START the web UI does – startCurrentTask then
        // System_ReglerOnOff=1. If it is already running, the set point write above
        // is enough (verified against the device's own capture).
        if (digital.Start && !await IsControllerOnAsync(cancellationToken).ConfigureAwait(false))
        {
            await StartCurrentTaskAsync(cancellationToken).ConfigureAwait(false);
        }

        RaiseFrame("SET", $"{applied:0.0} °C aplikovaných.");
    }

    /// <summary>Reads <c>System_ReglerOnOff</c>; <c>true</c> when the controller is on (value ≥ 0.5).</summary>
    private async Task<bool> IsControllerOnAsync(CancellationToken cancellationToken)
    {
        string url = SikaRestApiProtocol.BuildGetRegisterUrl(Settings.Host, Settings.Port, SikaRestApiProtocol.ControllerOnOffRegister);
        string json = await GetAsync(url, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {url}", json);
        return SikaRestApiProtocol.ParseRegisterValue(json) is >= 0.5;
    }

    /// <summary>Runs the verified START: <c>startCurrentTask</c> then <c>System_ReglerOnOff</c> = 1.</summary>
    private async Task StartCurrentTaskAsync(CancellationToken cancellationToken)
    {
        string startUrl = SikaRestApiProtocol.BuildStartCurrentTaskUrl(Settings.Host, Settings.Port);
        string startResponse = await GetAsync(startUrl, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {startUrl}", startResponse);
        SikaRestApiProtocol.EnsureCommandSucceeded(startResponse, "startCurrentTask");

        string onUrl = SikaRestApiProtocol.BuildSetRegisterUrl(
            Settings.Host, Settings.Port, SikaRestApiProtocol.ControllerOnOffRegister, 1);
        string onResponse = await GetAsync(onUrl, cancellationToken).ConfigureAwait(false);
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
        string stopUrl = SikaRestApiProtocol.BuildStopCurrentTaskUrl(Settings.Host, Settings.Port);
        string stopResponse = await GetAsync(stopUrl, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {stopUrl}", stopResponse);
        SikaRestApiProtocol.EnsureCommandSucceeded(stopResponse, "stopCurrentTask");

        string offUrl = SikaRestApiProtocol.BuildSetRegisterUrl(
            Settings.Host, Settings.Port, SikaRestApiProtocol.ControllerOnOffRegister, 0);
        string offResponse = await GetAsync(offUrl, cancellationToken).ConfigureAwait(false);
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
        string url = SikaRestApiProtocol.BuildCommandUrl(Settings.Host, Settings.Port, frame);
        string response = await GetAsync(url, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {url}", response);
        return response;
    }

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
    }

    public ValueTask DisposeAsync()
    {
        DisposeHttp();
        _gate.Dispose();
        _ioGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
