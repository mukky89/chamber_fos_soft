using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Calibration;

public interface IPeakLoggerClient : IAsyncDisposable
{
    bool IsConnected { get; }
    DateTimeOffset? LastDataTimestamp { get; }

    Task ConnectAsync(PeakLoggerSettings settings, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<IReadOnlyList<PeakLoggerSensor>> DiscoverSensorsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PeakLoggerMeasurement>> ReadMeasurementsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional hook implemented by the simulator so a calibration runner can feed it
/// the actual chamber temperature. Production PeakLogger adapters do not implement it.
/// </summary>
public interface IPeakLoggerSimulationControl
{
    double SimulatedTemperatureC { get; set; }
}

public enum FakePeakLoggerScenario
{
    Normal,
    OneNonResponsivePeak,
    OneNoisySlowPeak,
    OneNeverStablePeak,
    DisconnectAfterSamples,
    PeakDisappears,
}

/// <summary>
/// Deterministic PeakLogger simulator used for development and unit tests. A sensor
/// identity is SerialNumber + Channel + PeakId; wavelength itself is deliberately not
/// used as identity because it changes with temperature.
/// </summary>
public sealed class FakePeakLoggerClient : IPeakLoggerClient, IPeakLoggerSimulationControl
{
    private readonly Random _random;
    private readonly List<PeakLoggerSensor> _sensors;
    private PeakLoggerSettings _settings = new();
    private int _readCount;

    public FakePeakLoggerClient(FakePeakLoggerScenario scenario = FakePeakLoggerScenario.Normal, int randomSeed = 12345)
    {
        Scenario = scenario;
        _random = new Random(randomSeed);
        _sensors = BuildSensors();
    }

    public FakePeakLoggerScenario Scenario { get; set; }
    public bool IsConnected { get; private set; }
    public DateTimeOffset? LastDataTimestamp { get; private set; }
    public double SimulatedTemperatureC { get; set; } = 20.0;

    public Task ConnectAsync(PeakLoggerSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        IsConnected = true;
        _readCount = 0;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PeakLoggerSensor>> DiscoverSensorsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureConnected();
        return Task.FromResult<IReadOnlyList<PeakLoggerSensor>>(_sensors);
    }

    public Task<IReadOnlyList<PeakLoggerMeasurement>> ReadMeasurementsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureConnected();
        _readCount++;

        if (Scenario == FakePeakLoggerScenario.DisconnectAfterSamples && _readCount > 80)
        {
            IsConnected = false;
            throw new IOException("Simulovaná strata spojenia s PeakLoggerom.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var result = new List<PeakLoggerMeasurement>();

        foreach (PeakLoggerSensor sensor in _sensors)
        {
            foreach (PeakLoggerPeak peak in sensor.Peaks)
            {
                if (Scenario == FakePeakLoggerScenario.PeakDisappears &&
                    sensor.SerialNumber == "242805A000005" && peak.PeakIndex == 2 && _readCount > 30)
                {
                    continue;
                }

                bool nonResponsive = Scenario == FakePeakLoggerScenario.OneNonResponsivePeak &&
                                     sensor.SerialNumber == "242805A000003" && peak.PeakIndex == 1;
                bool noisySlow = Scenario == FakePeakLoggerScenario.OneNoisySlowPeak &&
                                 sensor.SerialNumber == "242805A000005" && peak.PeakIndex == 2;
                bool neverStable = Scenario == FakePeakLoggerScenario.OneNeverStablePeak &&
                                   sensor.SerialNumber == "242805A000005" && peak.PeakIndex == 2;

                double tempResponseNm = nonResponsive ? 0 : (SimulatedTemperatureC - 20.0) * 0.010;
                double noisePm = neverStable
                    ? (_random.NextDouble() - 0.5) * 30.0
                    : noisySlow && _readCount < 90
                        ? (_random.NextDouble() - 0.5) * 12.0
                        : (_random.NextDouble() - 0.5) * 0.8;
                double wavelength = peak.WavelengthNm + tempResponseNm + noisePm / 1000.0;
                double intensity = (peak.Intensity ?? -20) + (_random.NextDouble() - 0.5) * 0.4;

                result.Add(new PeakLoggerMeasurement(
                    now,
                    sensor.SerialNumber,
                    sensor.Channel,
                    peak.PeakId,
                    peak.PeakIndex,
                    wavelength,
                    intensity));
            }
        }

        LastDataTimestamp = now;
        return Task.FromResult<IReadOnlyList<PeakLoggerMeasurement>>(result);
    }

    private static List<PeakLoggerSensor> BuildSensors()
    {
        static PeakLoggerPeak Peak(int i, double nm) => new($"P{i}", i, nm, -20 - i);

        return new List<PeakLoggerSensor>
        {
            new("242805A000004", "3.2", Enumerable.Range(1, 10).Select(i => Peak(i, 1510 + i * 4.2)).ToArray()),
            new("242805A000003", "3.3", new[] { Peak(1, 1531.20), Peak(2, 1540.10), Peak(3, 1550.40) }),
            new("242805A000001", "3.4", new[] { Peak(1, 1522.70), Peak(2, 1561.20) }),
            new("242805A000005", "4.3", new[] { Peak(1, 1534.90), Peak(2, 1558.90), Peak(3, 1570.10) }),
            new("242805A000002", "4.4", new[] { Peak(1, 1542.40), Peak(2, 1564.80) }),
        };
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("PeakLogger nie je pripojený.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Production adapter for the local PeakLogger REST API used by the existing
/// Auto_calibrator_Pali application. The established contract is:
/// <c>GET /api/v1/peaks</c> (current) or <c>GET /peaks?</c> (legacy) for all
/// currently detected peaks. PeakLogger normally listens
/// on localhost:43122. A peak response contains index, channel, wavelength,
/// intensity and device.deviceSN/deviceType/connector.
/// </summary>
public sealed class PeakLoggerApiClient : IPeakLoggerClient
{
    public const int DefaultPort = 43122;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private PeakLoggerSettings _settings = new();
    private Uri? _baseUri;
    private string _peaksPath = "api/v1/peaks";

    public string PeaksPath => _peaksPath;

    public PeakLoggerApiClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    public bool IsConnected { get; private set; }
    public DateTimeOffset? LastDataTimestamp { get; private set; }

    public async Task ConnectAsync(PeakLoggerSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        _settings = settings;
        _baseUri = BuildBaseUri(settings);
        IsConnected = false;
        LastDataTimestamp = null;

        HttpStatusCode? lastStatus = null;
        foreach (string candidate in new[] { "api/v1/peaks", "peaks?" })
        {
            using HttpResponseMessage response = await SendGetAsync(candidate, cancellationToken).ConfigureAwait(false);
            lastStatus = response.StatusCode;
            if (!response.IsSuccessStatusCode) continue;

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array) continue;
            }
            catch (JsonException)
            {
                continue;
            }

            _peaksPath = candidate;
            IsConnected = true;
            return;
        }

        throw new HttpRequestException(
            $"PeakLogger API nie je dostupné na {_baseUri}. Skúšané /api/v1/peaks aj /peaks " +
            $"(posledný stav HTTP {(int?)lastStatus}).",
            null,
            lastStatus);
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        LastDataTimestamp = null;
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<PeakLoggerSensor>> DiscoverSensorsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        IReadOnlyList<PeakLoggerApiPeakDto> peaks = await FetchPeaksAsync(cancellationToken).ConfigureAwait(false);

        return peaks
            .Where(IsUsablePeak)
            .GroupBy(p => new { Serial = GetDeviceSerial(p), p.Channel })
            .OrderBy(g => g.Key.Serial, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.Channel, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PeakLoggerSensor(
                g.Key.Serial,
                g.Key.Channel,
                g.OrderBy(p => p.Index)
                    .Select(p => new PeakLoggerPeak(
                        PeakId(p.Index),
                        p.Index,
                        p.Wavelength,
                        p.Intensity))
                    .ToArray()))
            .ToArray();
    }

    public async Task<IReadOnlyList<PeakLoggerMeasurement>> ReadMeasurementsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        IReadOnlyList<PeakLoggerApiPeakDto> peaks = await FetchPeaksAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        PeakLoggerMeasurement[] measurements = peaks
            .Where(IsUsablePeak)
            .OrderBy(p => GetDeviceSerial(p), StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Channel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Index)
            .Select(p => new PeakLoggerMeasurement(
                timestamp,
                GetDeviceSerial(p),
                p.Channel,
                PeakId(p.Index),
                p.Index,
                p.Wavelength,
                p.Intensity))
            .ToArray();

        LastDataTimestamp = timestamp;
        return measurements;
    }

    private async Task<IReadOnlyList<PeakLoggerApiPeakDto>> FetchPeaksAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await SendGetAsync(_peaksPath, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Array.Empty<PeakLoggerApiPeakDto>();
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"PeakLogger /{_peaksPath.TrimEnd('?')} zlyhal (HTTP {(int)response.StatusCode} {response.ReasonPhrase}).",
                    null,
                    response.StatusCode);
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            List<PeakLoggerApiPeakDto>? result = JsonSerializer.Deserialize<List<PeakLoggerApiPeakDto>>(json, JsonOptions);
            return result is null ? Array.Empty<PeakLoggerApiPeakDto>() : result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            IsConnected = false;
            throw new TimeoutException($"PeakLogger API neodpovedalo do {_settings.RequestTimeout}.");
        }
        catch (HttpRequestException)
        {
            IsConnected = false;
            throw;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"PeakLogger /{_peaksPath.TrimEnd('?')} vrátil neplatný JSON.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendGetAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (_baseUri is null)
        {
            throw new InvalidOperationException("PeakLogger klient ešte nemá nastavenú adresu.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_settings.RequestTimeout > TimeSpan.Zero && _settings.RequestTimeout != Timeout.InfiniteTimeSpan)
        {
            timeoutCts.CancelAfter(_settings.RequestTimeout);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, relativePath));
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
    }

    private static Uri BuildBaseUri(PeakLoggerSettings settings)
    {
        string host = string.IsNullOrWhiteSpace(settings.Host) ? "localhost" : settings.Host.Trim();
        int port = settings.Port > 0 ? settings.Port : DefaultPort;

        if (Uri.TryCreate(host, UriKind.Absolute, out Uri? absolute) &&
            (absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            var builder = new UriBuilder(absolute);
            if (settings.Port > 0)
            {
                builder.Port = port;
            }
            else if (absolute.IsDefaultPort)
            {
                builder.Port = DefaultPort;
            }
            builder.Path = "/";
            builder.Query = string.Empty;
            return builder.Uri;
        }

        return new UriBuilder(Uri.UriSchemeHttp, host, port, "/").Uri;
    }

    private static bool IsUsablePeak(PeakLoggerApiPeakDto peak) =>
        peak.Index >= 0 && !string.IsNullOrWhiteSpace(peak.Channel) && double.IsFinite(peak.Wavelength);

    private static string PeakId(int index) => $"P{index}";

    private static string GetDeviceSerial(PeakLoggerApiPeakDto peak)
    {
        if (!string.IsNullOrWhiteSpace(peak.Device?.DeviceSN))
        {
            return peak.Device.DeviceSN.Trim();
        }

        string type = string.IsNullOrWhiteSpace(peak.Device?.DeviceType) ? "PeakLogger" : peak.Device.DeviceType.Trim();
        return $"{type}@{peak.Channel}";
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("PeakLogger nie je pripojený.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private sealed class PeakLoggerApiPeakDto
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("channel")]
        public string Channel { get; set; } = string.Empty;

        [JsonPropertyName("wavelength")]
        public double Wavelength { get; set; }

        [JsonPropertyName("cog")]
        public double? Cog { get; set; }

        [JsonPropertyName("intensity")]
        public double? Intensity { get; set; }

        [JsonPropertyName("returnLoss")]
        public double? ReturnLoss { get; set; }

        [JsonPropertyName("slsr")]
        public double? Slsr { get; set; }

        [JsonPropertyName("width")]
        public double? Width { get; set; }

        [JsonPropertyName("asymmetry")]
        public double? Asymmetry { get; set; }

        [JsonPropertyName("device")]
        public PeakLoggerApiDeviceDto? Device { get; set; }

        [JsonPropertyName("fos4x")]
        public JsonElement? Fos4x { get; set; }
    }

    private sealed class PeakLoggerApiDeviceDto
    {
        [JsonPropertyName("deviceType")]
        public string DeviceType { get; set; } = string.Empty;

        [JsonPropertyName("deviceSN")]
        public string DeviceSN { get; set; } = string.Empty;

        [JsonPropertyName("connector")]
        public int? Connector { get; set; }
    }

    public sealed record DiscoveredInstance(string Host, int Port, string ApiPath, int PeakCount, int DeviceCount)
    {
        public string Display => $"{Host}:{Port} · {DeviceCount} interrogátorov · {PeakCount} peakov · /{ApiPath.TrimEnd('?')}";
    }

    public sealed record DiscoveryReport(IReadOnlyList<DiscoveredInstance> Instances, int ScannedPortCount);

    /// <summary>
    /// Finds concurrently running PeakLogger API instances. For localhost it probes every
    /// active TCP listener reported by the OS, plus a fallback consecutive range. Remote
    /// hosts cannot expose their listener table, so only the fallback range is available.
    /// </summary>
    public static async Task<DiscoveryReport> DiscoverInstancesAsync(
        string host,
        int firstPort = DefaultPort,
        int portCount = 64,
        CancellationToken cancellationToken = default)
    {
        string normalizedHost = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
        int start = firstPort > 0 ? firstPort : DefaultPort;
        int count = Math.Clamp(portCount, 1, 512);
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(900) };

        var candidatePorts = new HashSet<int>(Enumerable.Range(start, count));
        if (IsLocalHost(normalizedHost))
        {
            try
            {
                foreach (IPEndPoint listener in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                {
                    candidatePorts.Add(listener.Port);
                }
            }
            catch (NetworkInformationException)
            {
                // The fallback range is still useful if the OS listener table is unavailable.
            }
        }

        Task<DiscoveredInstance?>[] probes = candidatePorts.OrderBy(port => port)
            .Select(port => ProbeInstanceAsync(http, normalizedHost, port, cancellationToken))
            .ToArray();
        DiscoveredInstance?[] results = await Task.WhenAll(probes).ConfigureAwait(false);
        DiscoveredInstance[] instances = results.Where(x => x is not null).Cast<DiscoveredInstance>().OrderBy(x => x.Port).ToArray();
        return new DiscoveryReport(instances, candidatePorts.Count);
    }

    private static bool IsLocalHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
        host.Equals(".", StringComparison.OrdinalIgnoreCase) ||
        host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);

    private static async Task<DiscoveredInstance?> ProbeInstanceAsync(
        HttpClient http,
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        foreach (string path in new[] { "api/v1/peaks", "peaks?" })
        {
            try
            {
                var uri = new UriBuilder(Uri.UriSchemeHttp, host, port, path).Uri;
                using HttpResponseMessage response = await http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) continue;
                using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                if (json.RootElement.ValueKind != JsonValueKind.Array) continue;

                int peaks = json.RootElement.GetArrayLength();
                var devices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonElement peak in json.RootElement.EnumerateArray())
                {
                    if (peak.TryGetProperty("device", out JsonElement device) &&
                        device.TryGetProperty("deviceSN", out JsonElement serial) &&
                        !string.IsNullOrWhiteSpace(serial.GetString()))
                    {
                        devices.Add(serial.GetString()!);
                    }
                }
                return new DiscoveredInstance(host, port, path, peaks, devices.Count);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                // Closed/non-PeakLogger port: continue with the next candidate.
            }
        }
        return null;
    }
}
