using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace VotschVc3.App.Calibration;

/// <summary>
/// Optional PeakLogger API features used by the production workspace. The normal calibration
/// client remains the source of peak measurements; this helper only watches topology and reads
/// a spectrum on explicit operator request.
/// </summary>
public sealed class PeakLoggerExtendedApiClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public async Task<IReadOnlySet<string>?> ReadTopologyAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        foreach (string path in new[] { "/api/v1/peaks", "/peaks" })
        {
            try
            {
                using HttpResponseMessage response = await _http.GetAsync(BuildUri(host, port, path), cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) continue;
                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectPeakIdentities(document.RootElement, identities, null, null);
                return identities;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (HttpRequestException) { }
            catch (JsonException) { }
        }
        return null;
    }

    public async Task<IReadOnlyList<PeakLoggerSpectrumPoint>> ReadSpectrumAsync(
        string host,
        int port,
        string channel,
        string? deviceSerialNumber,
        CancellationToken cancellationToken = default)
    {
        string ch = Uri.EscapeDataString(channel ?? string.Empty);
        string serial = Uri.EscapeDataString(deviceSerialNumber ?? string.Empty);
        string suffix = string.IsNullOrWhiteSpace(serial) ? $"channel={ch}" : $"channel={ch}&serialNumber={serial}";
        string[] candidates =
        {
            $"/api/v1/spectrum?{suffix}",
            $"/api/v1/spectra?{suffix}",
            $"/api/v1/channels/{ch}/spectrum" + (string.IsNullOrWhiteSpace(serial) ? string.Empty : $"?serialNumber={serial}"),
            $"/spectrum?{suffix}",
        };

        var errors = new List<string>();
        foreach (string path in candidates)
        {
            try
            {
                using HttpResponseMessage response = await _http.GetAsync(BuildUri(host, port, path), cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    errors.Add($"{path}: 404");
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    errors.Add($"{path}: HTTP {(int)response.StatusCode}");
                    continue;
                }

                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                IReadOnlyList<PeakLoggerSpectrumPoint> points = ParseSpectrum(document.RootElement);
                if (points.Count >= 2) return points;
                errors.Add($"{path}: odpoveď neobsahuje rozpoznateľné spektrum");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                errors.Add($"{path}: timeout");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                errors.Add($"{path}: {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            "PeakLogger API neposkytol rozpoznateľný spectrum endpoint pre tento kanál. " +
            string.Join(" | ", errors));
    }

    private static Uri BuildUri(string host, int port, string path)
    {
        string normalizedHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        string prefix = normalizedHost.Contains("://", StringComparison.Ordinal)
            ? normalizedHost.TrimEnd('/')
            : $"http://{normalizedHost.TrimEnd('/')}";
        return new Uri($"{prefix}:{port}{path}", UriKind.Absolute);
    }

    private static void CollectPeakIdentities(
        JsonElement element,
        ISet<string> result,
        string? inheritedSerial,
        string? inheritedChannel)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
                CollectPeakIdentities(child, result, inheritedSerial, inheritedChannel);
            return;
        }
        if (element.ValueKind != JsonValueKind.Object) return;

        string? serial = ReadString(element, "serialNumber", "deviceSerialNumber", "deviceSn", "sourceSerialNumber") ?? inheritedSerial;
        string? channel = ReadString(element, "channel", "channelName", "input", "inputChannel") ?? inheritedChannel;
        string? peak = ReadString(element, "peakId", "peakID", "id", "peakIndex", "index");
        if (!string.IsNullOrWhiteSpace(channel) && !string.IsNullOrWhiteSpace(peak))
            result.Add($"{serial}|{channel}|{peak}");

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                CollectPeakIdentities(property.Value, result, serial, channel);
        }
    }

    private static IReadOnlyList<PeakLoggerSpectrumPoint> ParseSpectrum(JsonElement root)
    {
        if (TryParallelArrays(root, out List<PeakLoggerSpectrumPoint>? parallel)) return parallel;
        if (TryPointArray(root, out List<PeakLoggerSpectrumPoint>? points)) return points;

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (string name in new[] { "spectrum", "data", "points", "samples", "result" })
            {
                if (TryGetPropertyIgnoreCase(root, name, out JsonElement nested))
                {
                    if (TryParallelArrays(nested, out parallel)) return parallel;
                    if (TryPointArray(nested, out points)) return points;
                }
            }
        }
        return Array.Empty<PeakLoggerSpectrumPoint>();
    }

    private static bool TryParallelArrays(JsonElement element, out List<PeakLoggerSpectrumPoint> points)
    {
        points = new();
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (!TryGetAny(element, out JsonElement wavelengths, "wavelengths", "wavelengthNm", "x") || wavelengths.ValueKind != JsonValueKind.Array)
            return false;
        if (!TryGetAny(element, out JsonElement intensities, "intensities", "intensity", "power", "y") || intensities.ValueKind != JsonValueKind.Array)
            return false;

        JsonElement.ArrayEnumerator wx = wavelengths.EnumerateArray();
        JsonElement.ArrayEnumerator iy = intensities.EnumerateArray();
        while (wx.MoveNext() && iy.MoveNext())
        {
            if (TryDouble(wx.Current, out double w) && TryDouble(iy.Current, out double intensity))
                points.Add(new(w, intensity));
        }
        return points.Count >= 2;
    }

    private static bool TryPointArray(JsonElement element, out List<PeakLoggerSpectrumPoint> points)
    {
        points = new();
        if (element.ValueKind != JsonValueKind.Array) return false;
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() >= 2)
            {
                JsonElement[] pair = item.EnumerateArray().Take(2).ToArray();
                if (TryDouble(pair[0], out double w) && TryDouble(pair[1], out double intensity)) points.Add(new(w, intensity));
                continue;
            }
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!TryGetAny(item, out JsonElement wavelength, "wavelengthNm", "wavelength", "lambda", "x")) continue;
            if (!TryGetAny(item, out JsonElement intensityElement, "intensity", "power", "level", "y")) continue;
            if (TryDouble(wavelength, out double ww) && TryDouble(intensityElement, out double ii)) points.Add(new(ww, ii));
        }
        return points.Count >= 2;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (!TryGetPropertyIgnoreCase(element, name, out JsonElement value)) continue;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            };
        }
        return null;
    }

    private static bool TryGetAny(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (string name in names)
            if (TryGetPropertyIgnoreCase(element, name, out value)) return true;
        value = default;
        return false;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static bool TryDouble(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number) return element.TryGetDouble(out value);
        if (element.ValueKind == JsonValueKind.String)
            return double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        value = 0;
        return false;
    }

    public void Dispose() => _http.Dispose();
}

public sealed record PeakLoggerSpectrumPoint(double WavelengthNm, double Intensity);
