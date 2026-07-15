using System.Text.Json;

namespace VotschVc3.Core.Protocol;

/// <summary>
/// URL building and JSON parsing for the SIKA TP Premium REST-API (HTTP, port
/// 8081, path prefix "ajax/"). Pure helpers so they can be unit tested without
/// a live device; <see cref="Communication.Sika.SikaTpClient"/> does the actual
/// HTTP I/O.
/// </summary>
public static class SikaRestApiProtocol
{
    /// <summary>Fixed REST-API port documented for TP Premium devices.</summary>
    public const int DefaultPort = 8081;

    /// <summary>Register name for the currently used reference temperature (°C).</summary>
    public const string MeasuredRegister = "TRset_TR";

    /// <summary>Register name for the currently stored set point (°C).</summary>
    public const string SetpointRegister = "TRset_SP";

    /// <summary>Builds the base "http://host:port/" the ajax/ commands hang off.</summary>
    public static string BuildBaseUrl(string host, int port) => $"http://{host}:{port}/";

    /// <summary>
    /// Builds a full ajax command URL. <paramref name="command"/> is the part
    /// after "ajax/", e.g. "getRegister?register=TRset_TR" or "setSP?value=25".
    /// The "ajax/" prefix is added automatically if the caller omitted it (so a
    /// raw terminal command can be typed either way).
    /// </summary>
    public static string BuildCommandUrl(string host, int port, string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        string trimmed = command.TrimStart('/');
        if (!trimmed.StartsWith("ajax/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "ajax/" + trimmed;
        }

        return BuildBaseUrl(host, port) + trimmed;
    }

    /// <summary>Builds the URL for reading a named register (<see cref="MeasuredRegister"/> / <see cref="SetpointRegister"/>).</summary>
    public static string BuildGetRegisterUrl(string host, int port, string register) =>
        BuildCommandUrl(host, port, $"getRegister?register={Uri.EscapeDataString(register)}");

    /// <summary>Builds the URL that sets and moves to a new set point.</summary>
    public static string BuildSetSpUrl(string host, int port, double celsius) =>
        BuildCommandUrl(host, port, $"setSP?value={celsius.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}");

    /// <summary>Builds the URL for the device information report.</summary>
    public static string BuildInfoReportUrl(string host, int port) => BuildCommandUrl(host, port, "getInfoReport");

    /// <summary>Builds the URL for the current calibration status.</summary>
    public static string BuildCalibrationStatusUrl(string host, int port) => BuildCommandUrl(host, port, "getCalibrationStatus");

    /// <summary>
    /// Parses a <c>getRegister</c> response
    /// (<c>{"register":"TRset_TR","values":[{"value":28.9,"times":...}]}</c>)
    /// and returns the first value, or <c>null</c> if the response is empty / malformed.
    /// </summary>
    public static double? ParseRegisterValue(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("values", out JsonElement values) &&
                values.ValueKind == JsonValueKind.Array &&
                values.GetArrayLength() > 0 &&
                values[0].TryGetProperty("value", out JsonElement value))
            {
                return value.GetDouble();
            }
        }
        catch (JsonException)
        {
            // Malformed / non-JSON response (e.g. an HTML error page) – treat as unknown.
        }

        return null;
    }

    /// <summary>
    /// Parses a <c>setSP</c> response (<c>{"value":"success","info":"25.500000"}</c>).
    /// Returns the set point that was applied. Throws <see cref="InvalidOperationException"/>
    /// when the device reports anything other than success (e.g. "Set point outside valid range").
    /// </summary>
    public static double ParseSetSpResponse(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            string? status = doc.RootElement.TryGetProperty("value", out JsonElement v) ? v.GetString() : null;
            string? info = doc.RootElement.TryGetProperty("info", out JsonElement i) ? i.GetString() : null;

            if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(info, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }

            throw new InvalidOperationException($"SIKA setSP odmietnutý: {status ?? "?"} ({info ?? "bez detailu"}).");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Neplatná odpoveď na setSP: {json}", ex);
        }
    }
}
