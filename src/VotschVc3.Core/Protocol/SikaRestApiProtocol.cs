using System.Text.Json;
using System.Text.RegularExpressions;

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

    /// <summary>
    /// Register holding the EasyMode (single-step) task set point list. The device's
    /// own web UI writes this alongside <see cref="SetpointRegister"/> when the user
    /// changes the temperature, so a running single-step task tracks the new value.
    /// </summary>
    public const string TaskSetPointListRegister = "Task_SetPointList";

    /// <summary>Register for the controller on/off state (0 = off, 1 = on).</summary>
    public const string ControllerOnOffRegister = "System_ReglerOnOff";

    /// <summary>
    /// Register that reports whether external (remote) writes are enabled
    /// (1 = enabled). Set point writes are only accepted while this is 1.
    /// </summary>
    public const string ExternWriteFlagRegister = "Com_ExternWriteFlag";

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

    /// <summary>Formats a numeric value the way the SIKA REST-API expects it (invariant, no thousands separators).</summary>
    private static string FormatValue(double value) =>
        value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds a <c>setRegister?register=…&amp;value=…</c> URL. This is how the device's
    /// own web UI writes a set point (<c>register=TRset_SP</c>) – observed on a real
    /// TP3M165E.2 – rather than the older <c>setSP</c> command.
    /// </summary>
    public static string BuildSetRegisterUrl(string host, int port, string register, double value)
    {
        ArgumentNullException.ThrowIfNull(register);
        return BuildCommandUrl(host, port, $"setRegister?register={Uri.EscapeDataString(register)}&value={FormatValue(value)}");
    }

    /// <summary>Builds the URL for the device information report.</summary>
    public static string BuildInfoReportUrl(string host, int port) => BuildCommandUrl(host, port, "getInfoReport");

    /// <summary>
    /// Builds the URL for the combined status snapshot. Newer SIKA chambers
    /// (e.g. TP37200E.2) answer <c>getGradientInfo</c> with the reference
    /// temperature (<c>TR</c>), the set point (<c>SP</c>), stability and heating
    /// state in a single call – one round-trip instead of two per-register reads.
    /// </summary>
    public static string BuildGradientInfoUrl(string host, int port) => BuildCommandUrl(host, port, "getGradientInfo");

    /// <summary>Builds the URL for the current calibration status.</summary>
    public static string BuildCalibrationStatusUrl(string host, int port) => BuildCommandUrl(host, port, "getCalibrationStatus");

    /// <summary>
    /// Builds the URL that starts the current task. Verified on a real TP3M165E.2:
    /// the web UI's START issues <c>startCurrentTask</c> and then writes
    /// <see cref="ControllerOnOffRegister"/> = 1. A set point only takes effect once
    /// the task is running.
    /// </summary>
    public static string BuildStartCurrentTaskUrl(string host, int port) => BuildCommandUrl(host, port, "startCurrentTask");

    /// <summary>
    /// Builds the URL that stops the current task. The web UI's STOP issues
    /// <c>stopCurrentTask</c> and then writes <see cref="ControllerOnOffRegister"/> = 0.
    /// </summary>
    public static string BuildStopCurrentTaskUrl(string host, int port) => BuildCommandUrl(host, port, "stopCurrentTask");

    public static string BuildTaskLogIndexUrl(string host, int port) => BuildCommandUrl(host, port, "getTaskLog");

    public static string BuildTaskLogDataUrl(string host, int port, int taskId) =>
        BuildCommandUrl(host, port, $"getTaskLogs?taskid={taskId}");

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
    /// The fields we read out of a <c>getGradientInfo</c> response. All nullable so a
    /// partial / older-firmware payload still parses; <see cref="ReferenceTemperature"/>
    /// being non-null is the signal that the device actually supports this endpoint.
    /// </summary>
    public readonly record struct SikaGradientInfo(
        double? ReferenceTemperature,
        double? Setpoint,
        bool? HeatingOn,
        int? SystemState);

    /// <summary>
    /// Parses a <c>getGradientInfo</c> response
    /// (<c>{"TR":-19.99,"SP":-20.0,"Stable":2,"heatingON":1,"systemState":2,...}</c>).
    /// Returns <c>null</c> when the body is not a JSON object carrying <c>TR</c> – i.e.
    /// the device / firmware does not expose this endpoint – so the caller can fall
    /// back to per-register reads.
    /// </summary>
    public static SikaGradientInfo? ParseGradientInfo(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("TR", out _))
            {
                return null;
            }

            double? tr = TryGetNumber(root, "TR");
            if (tr is null)
            {
                return null;
            }

            bool? heating = TryGetNumber(root, "heatingON") is { } h ? h != 0 : null;
            int? systemState = TryGetNumber(root, "systemState") is { } s ? (int)s : null;
            return new SikaGradientInfo(tr, TryGetNumber(root, "SP"), heating, systemState);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads a numeric property, or <c>null</c> when it is missing / not a number.</summary>
    private static double? TryGetNumber(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number
            ? e.GetDouble()
            : null;

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

    /// <summary>
    /// Parses a <c>setRegister</c> response
    /// (<c>{"value":"success","info":"value 40.000000 wrote to register TRset_SP"}</c>).
    /// Returns the numeric value the device confirmed writing. Throws
    /// <see cref="InvalidOperationException"/> on a non-success status or when the
    /// success message carries no number.
    /// </summary>
    public static double ParseSetRegisterResponse(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            string? status = doc.RootElement.TryGetProperty("value", out JsonElement v) ? v.GetString() : null;
            string? info = doc.RootElement.TryGetProperty("info", out JsonElement i) ? i.GetString() : null;

            if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"SIKA setRegister odmietnutý: {status ?? "?"} ({info ?? "bez detailu"}).");
            }

            Match number = Regex.Match(info ?? string.Empty, @"-?\d+(\.\d+)?");
            if (number.Success &&
                double.TryParse(number.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }

            throw new InvalidOperationException($"SIKA setRegister: úspech bez rozpoznateľnej hodnoty ({info ?? "bez detailu"}).");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Neplatná odpoveď na setRegister: {json}", ex);
        }
    }

    /// <summary>
    /// Verifies a command response reports success (<c>{"value":"success",...}</c>),
    /// used for verb commands like <c>startCurrentTask</c> / <c>stopCurrentTask</c>
    /// that carry no numeric payload. Throws <see cref="InvalidOperationException"/>
    /// otherwise. <paramref name="action"/> names the command for the error text.
    /// </summary>
    public static void EnsureCommandSucceeded(string json, string action)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            string? status = doc.RootElement.TryGetProperty("value", out JsonElement v) ? v.GetString() : null;
            string? info = doc.RootElement.TryGetProperty("info", out JsonElement i) ? i.GetString() : null;

            if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"SIKA {action} odmietnutý: {status ?? "?"} ({info ?? "bez detailu"}).");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Neplatná odpoveď na {action}: {json}", ex);
        }
    }
}
