using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VotschVc3.Core.Calibration;

public sealed class SylexFosApiSettings
{
    public const string DefaultBaseUrl = "http://syx260421n01:5080";
    public const string DefaultApiKeyEnvironmentVariable = "SYLEX_FOS_API_KEY";

    public string BaseUrl { get; set; } = DefaultBaseUrl;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiKeyEnvironmentVariable { get; set; } = DefaultApiKeyEnvironmentVariable;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public string? ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey)) return ApiKey.Trim();
        return string.IsNullOrWhiteSpace(ApiKeyEnvironmentVariable)
            ? null
            : Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable)?.Trim();
    }
}

public sealed record SylexFosApiHealth(bool IsReachable, string Status, DateTimeOffset CheckedAtUtc, string? Detail = null);

public sealed record SylexFbgCalibrationContext(
    string SerialNumber,
    string ProductId,
    string? ProductDescription,
    string? SensorName,
    string? CustomerId,
    string? CustomerName,
    string? CustomerCode,
    string? OrderNumber,
    string Source,
    DateTimeOffset RetrievedAtUtc);

public interface ISylexFosApiClient
{
    Task<SylexFosApiHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
    Task<SylexFbgCalibrationContext?> GetFbgCalibrationContextAsync(string serialNumber, CancellationToken cancellationToken = default);
}

public sealed class SylexFosApiClient : ISylexFosApiClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;
    private readonly SylexFosApiSettings _settings;
    private readonly bool _ownsHttpClient;

    public SylexFosApiClient(SylexFosApiSettings settings, HttpClient? httpClient = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        if (!Uri.TryCreate(settings.BaseUrl?.TrimEnd('/'), UriKind.Absolute, out Uri? baseUri))
            throw new ArgumentException("Sylex FOS API BaseUrl must be an absolute URL.", nameof(settings));
        _httpClient.BaseAddress = baseUri;
        _httpClient.Timeout = settings.RequestTimeout > TimeSpan.Zero ? settings.RequestTimeout : TimeSpan.FromSeconds(10);
    }

    public async Task<SylexFosApiHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync("/health", cancellationToken).ConfigureAwait(false);
            string status = response.IsSuccessStatusCode ? "healthy" : $"http_{(int)response.StatusCode}";
            return new(response.IsSuccessStatusCode, status, DateTimeOffset.UtcNow, response.IsSuccessStatusCode ? null : response.ReasonPhrase);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "timeout", DateTimeOffset.UtcNow, "Sylex FOS API request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return new(false, "unreachable", DateTimeOffset.UtcNow, ex.Message);
        }
    }

    public async Task<SylexFbgCalibrationContext?> GetFbgCalibrationContextAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return null;
        string? apiKey = _settings.ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"Sylex FOS API key is not configured. Set environment variable '{_settings.ApiKeyEnvironmentVariable}'.");

        string encodedSerial = Uri.EscapeDataString(serialNumber.Trim());
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/calibrations/fbg/context?serialNumber={encodedSerial}");
        request.Headers.Add("X-API-Key", apiKey);
        request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString("N"));
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new InvalidOperationException("Sylex FOS API rejected the configured API key (401 Unauthorized).");
        if (response.StatusCode == HttpStatusCode.Forbidden) throw new InvalidOperationException("Sylex FOS API key is missing the calibrations.read scope (403 Forbidden).");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SylexFbgCalibrationContext>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() { if (_ownsHttpClient) _httpClient.Dispose(); }
}

public sealed class SylexFosApiProductionMetadataProvider : IProductionMetadataProvider, IDisposable
{
    private readonly ISylexFosApiClient _client;
    private readonly IDisposable? _ownedClient;
    private readonly Dictionary<string, ProductionMetadata?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheSync = new();

    public SylexFosApiProductionMetadataProvider(ISylexFosApiClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ownedClient = client as IDisposable;
    }

    public async Task<ProductionMetadata?> FindAsync(string serialNumber, string channel, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return null;
        string key = serialNumber.Trim();
        lock (_cacheSync) if (_cache.TryGetValue(key, out ProductionMetadata? cached)) return cached;
        SylexFbgCalibrationContext? context = await _client.GetFbgCalibrationContextAsync(key, cancellationToken).ConfigureAwait(false);
        ProductionMetadata? metadata = context is null ? null : new ProductionMetadata(
            context.ProductDescription ?? context.ProductId,
            context.SensorName ?? string.Empty,
            context.OrderNumber ?? string.Empty,
            context.CustomerName,
            $"Sylex FOS API · {context.Source}");
        lock (_cacheSync) _cache[key] = metadata;
        return metadata;
    }

    public void Dispose() => _ownedClient?.Dispose();
}
