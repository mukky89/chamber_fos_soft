using System.Net;
using System.Net.Sockets;
using VotschVc3.Core.Protocol;

namespace VotschVc3.Core.Communication.Sika;

/// <summary>
/// <see cref="IChamberDevice"/> for a SIKA TP Premium calibration bath / dry
/// block over its HTTP REST-API (port 8081, documented commands under
/// <c>ajax/</c>). Reads the reference temperature and set point via
/// <c>getRegister</c>, writes a set point via <c>setSP</c>. Temperature only –
/// no humidity channel, and the API exposes no remote power on/off, so
/// <see cref="StopAsync"/> cannot switch the bath off (it runs continuously).
///
/// Robustness notes learned on the real baths:
/// <list type="bullet">
/// <item>The lab devices sit on the local network – the system HTTP proxy (if
/// any is configured on the PC) must be bypassed, otherwise every request dies
/// in the proxy while the raw-TCP devices keep working.</item>
/// <item>The embedded web server occasionally answers a single request with a
/// sporadic 404 / stale body, so transient failures are retried instead of
/// failing the whole connect / write.</item>
/// <item><c>getInfoReport</c> generates a full report and can take longer than
/// the connect timeout – the connect probe therefore uses the cheap
/// <c>getRegister</c> instead.</item>
/// <item><c>setSP</c> exists only from TP software 30.35 – on older firmware the
/// write is verified by reading back <c>TRset_SP</c> so a silently ignored set
/// point surfaces as a clear error instead of "looks OK, nothing happens".</item>
/// </list>
/// All HTTP requests are serialised through <see cref="_ioGate"/> (live
/// polling, the manual terminal and a <c>setSP</c> write never interleave on
/// the wire) – the device's embedded web server answered concurrent requests
/// with sporadic 404s / stale bodies otherwise.
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
    /// swallow the requests), explicit TCP connect timeout, and
    /// "Connection: close" so every request gets a fresh connection (keep-alive
    /// against the embedded server produced stale bodies).
    /// </summary>
    private static HttpClient CreateHttpClient(ChamberConnectionSettings s)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = s.ConnectTimeout,
        };
        var http = new HttpClient(handler) { Timeout = s.ReadTimeout };
        http.DefaultRequestHeaders.ConnectionClose = true;
        return http;
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
            // answers before reporting "connected". getRegister is used instead
            // of getInfoReport: the report generation is slow on the device and
            // used to blow the connect timeout, and getRegister exists on every
            // TP software version. The whole probe (including retries after a
            // sporadic 404) is bounded so an unreachable bath fails fast.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(Settings.ConnectTimeout + Settings.ReadTimeout);
            string url = SikaRestApiProtocol.BuildGetRegisterUrl(Settings.Host, Settings.Port, SikaRestApiProtocol.MeasuredRegister);
            string response = await GetWithRetryAsync(url, linked.Token).ConfigureAwait(false);
            RaiseFrame($"GET {url}", response);

            if (SikaRestApiProtocol.ParseRegisterValue(response) is null)
            {
                throw new InvalidOperationException(
                    $"Zariadenie na {Settings.Host}:{Settings.Port} odpovedá, ale nie ako SIKA REST-API " +
                    $"(getRegister nevrátil hodnotu). Skontroluj, či je zadaný REST-API port {SikaRestApiProtocol.DefaultPort} " +
                    "(nie port webovej aplikácie) a či je na prístroji povolené vzdialené REST-API ovládanie. " +
                    $"Odpoveď: {Truncate(response)}");
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
                    "spojenie odmietnuté – prístroj beží, ale na tomto porte REST-API nepočúva (skontroluj port 8081 a povolenie REST-API)",
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

        string response;
        try
        {
            response = await GetWithRetryAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Old TP software answers 404 for the unknown setSP command.
            throw new InvalidOperationException(
                "Prístroj nepozná príkaz setSP (HTTP 404) – vzdialený zápis setpointu vyžaduje TP software > 30.35. " +
                "Aktualizuj firmvér prístroja (SIKA servis), dovtedy nastav teplotu na paneli zariadenia.", ex);
        }

        RaiseFrame($"GET {url}", response);
        double applied = SikaRestApiProtocol.ParseSetSpResponse(response);

        // The device confirmed the command – now verify it really stored the new
        // set point (old firmware / disabled remote control can acknowledge and
        // ignore, which the user sees as "the power never starts").
        double? stored = await VerifySetpointAsync(applied, cancellationToken).ConfigureAwait(false);
        if (stored is { } sp && Math.Abs(sp - applied) > SetpointVerifyTolerance)
        {
            throw new InvalidOperationException(
                $"SIKA potvrdila setSP {applied:0.0} °C, ale v prístroji zostal setpoint {sp:0.0} °C – " +
                "zápis sa nevykonal. Skontroluj, či prístroj nie je v ručnom režime / v prebiehajúcej kalibrácii " +
                "a či TP software podporuje vzdialené ovládanie (> 30.35).");
        }

        RaiseFrame("SET", stored is { } ok
            ? $"{ok:0.0} °C aplikovaných a overených (TRset_SP)."
            : $"{applied:0.0} °C aplikovaných (overenie čítaním sa nepodarilo, pokračujem).");
    }

    /// <summary>
    /// Reads <see cref="SikaRestApiProtocol.SetpointRegister"/> back until it matches
    /// <paramref name="expected"/> or the attempts run out. Returns the last stored
    /// value, or <c>null</c> when the read-back itself failed (no false alarm then –
    /// the write was already acknowledged).
    /// </summary>
    private async Task<double?> VerifySetpointAsync(double expected, CancellationToken cancellationToken)
    {
        string url = SikaRestApiProtocol.BuildGetRegisterUrl(Settings.Host, Settings.Port, SikaRestApiProtocol.SetpointRegister);
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
                // Read-back is best effort; keep trying.
            }

            if (attempt < SetpointVerifyAttempts)
            {
                await Task.Delay(SetpointVerifyDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        return last;
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

    /// <summary>
    /// Issues one GET with retries on transient failures (the embedded server's
    /// sporadic 404s / resets). A repeated 404 is not transient and propagates
    /// after the last attempt so callers can tell "unknown command" apart.
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

    private static string Truncate(string text) =>
        text.Length <= 120 ? text : text[..120] + "…";

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
