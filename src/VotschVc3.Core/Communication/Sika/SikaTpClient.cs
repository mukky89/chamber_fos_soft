using VotschVc3.Core.Protocol;

namespace VotschVc3.Core.Communication.Sika;

/// <summary>
/// <see cref="IChamberDevice"/> for a SIKA TP Premium calibration bath / dry
/// block over its HTTP REST-API (port 8081, documented commands under
/// <c>ajax/</c>). Reads the reference temperature and set point via
/// <c>getRegister</c>, writes a set point via <c>setSP</c>. Temperature only –
/// no humidity channel, and the API exposes no remote power on/off, so
/// <see cref="StopAsync"/> cannot switch the bath off (it runs continuously).
/// </summary>
public sealed class SikaTpClient : IChamberDevice
{
    private readonly SemaphoreSlim _gate = new(1, 1);
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
        string measuredUrl = SikaRestApiProtocol.BuildGetRegisterUrl(Settings.Host, Settings.Port, SikaRestApiProtocol.MeasuredRegister);
        string measuredJson = await GetAsync(measuredUrl, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {measuredUrl}", measuredJson);
        double? measured = SikaRestApiProtocol.ParseRegisterValue(measuredJson);

        string setpointUrl = SikaRestApiProtocol.BuildGetRegisterUrl(Settings.Host, Settings.Port, SikaRestApiProtocol.SetpointRegister);
        string setpointJson = await GetAsync(setpointUrl, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {setpointUrl}", setpointJson);
        double? setpoint = SikaRestApiProtocol.ParseRegisterValue(setpointJson);

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
        string url = SikaRestApiProtocol.BuildSetSpUrl(Settings.Host, Settings.Port, temperature);
        string response = await GetAsync(url, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"GET {url}", response);
        double applied = SikaRestApiProtocol.ParseSetSpResponse(response);
        RaiseFrame("SET", $"{applied:0.0} °C aplikovaných.");
    }

    /// <summary>
    /// The SIKA REST-API has no documented command to remotely switch the bath
    /// off – it circulates / conditions continuously. Always throws so the UI
    /// shows the real limitation instead of a silent no-op.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "SIKA TP Premium REST-API neposkytuje príkaz na vzdialené vypnutie – kúpeľ / blok beží nepretržite. " +
            "Zastav ho manuálne na paneli zariadenia, prípadne nastav bezpečnú (izbovú) teplotu.");

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

    private async Task<string> GetAsync(string url, CancellationToken cancellationToken)
    {
        HttpClient http = _http ?? throw new InvalidOperationException("Not connected to the SIKA device.");
        using HttpResponseMessage response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
        return ValueTask.CompletedTask;
    }
}
