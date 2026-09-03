using System.Globalization;
using System.Text.RegularExpressions;

namespace VotschVc3.Core.Thermometers;

/// <summary>
/// Encoder / decoder for the WIKA CTH7000 USB-serial interface.
/// <para>
/// WIKA documents 9600 baud, 8 data bits, no parity, 1 stop bit and no flow control.
/// The manual recommends a 1–2 ms inter-character gap. A production CTH7000 V1.0 unit,
/// however, was validated on 2026-09-03 with the historical AutoOptical/Pali timing of
/// 25 ms between transmitted characters. The production clients therefore use that
/// compatibility timing and a 1 s REMOTE settle delay before the first query.
/// </para>
/// </summary>
public static class F100Protocol
{
    public const int DefaultBaudRate = 9600;
    public const string Terminator = "\r";

    /// <summary>Timing stated in the WIKA documentation.</summary>
    public const int DocumentedInterCharacterDelayMs = 2;

    /// <summary>
    /// Production-compatible pacing validated against the installed CTH7000 V1.0.
    /// The previous 2 ms production path produced fresh-open zero-byte timeouts.
    /// </summary>
    public const int InterCharacterDelayMs = 25;

    /// <summary>
    /// Minimum pause after SYSTEM:REMOTE before the first query in the validated Pali sequence.
    /// </summary>
    public const int RemoteSettleDelayMs = 1000;

    // Commands explicitly documented by WIKA CTH7000 operating instructions, chapter 11.
    public const string IdentifyCommand = "*IDN?";
    public const string RemoteCommand = "SYSTEM:REMOTE";
    public const string LocalCommand = "SYSTEM:LOCAL";

    // Kept as a compatibility value for older callers. It is NOT a documented CTH7000 command
    // and the active CTH7000 path never sends it.
    [Obsolete("READ? is not documented by WIKA for CTH7000 and is not used by the active protocol path.")]
    public const string DefaultReadCommand = "READ?";

    public static IReadOnlyList<string> Units { get; } = new[] { "C", "F", "K", "R" };
    public static IReadOnlyList<string> Channels { get; } = new[] { "A", "B", "A-B" };
    public static IReadOnlyList<string> ProbeChannels { get; } = new[] { "A", "B" };

    // WIKA documents only one USB interface speed for CTH7000.
    public static IReadOnlyList<int> BaudRates { get; } = new[] { DefaultBaudRate };

    private static readonly Regex NumberToken =
        new(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.Compiled);

    private static readonly Regex Cth7000Measurement =
        new(@"^\s*[12]\s*,\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*,", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Cth7000Identification =
        new(@"^\s*(?:WIKA|ASL)\s*,\s*CTH7000\s*,", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TalkOnlyChannel =
        new(@"^\s*(?:(?:CH|CHANNEL)\s*)?(?<channel>A|B|1|2)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Not part of the documented USB command set; retained only for source compatibility
    // with legacy callers. The active CTH7000 query path does not call this method.
    [Obsolete("CONFIGURE:CHANNEL is not documented in the WIKA CTH7000 USB command set and is not used.")]
    public static string BuildConfigureChannelCommand(string channel) =>
        $"CONFIGURE:CHANNEL {NormalizeChannel(channel)}";

    /// <summary>
    /// Builds the documented WIKA CTH7000 measurement command.
    /// A = channel 1, B = channel 2, A-B = differential mode (-).
    /// </summary>
    public static string BuildMeasureChannelCommand(string channel) =>
        $"MEASURE:CHANNEL? {ChannelNumber(channel)}";

    public static string ChannelNumber(string channel) => NormalizeChannel(channel) switch
    {
        "A" => "1",
        "B" => "2",
        "A-B" => "-",
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };

    public static string NormalizeChannel(string? channel)
    {
        string value = (channel ?? "A").Trim().ToUpperInvariant().Replace(" ", string.Empty);
        return value switch
        {
            "A" => "A",
            "B" => "B",
            "A-B" or "A−B" => "A-B",
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "CTH7000 channel must be A, B or A-B."),
        };
    }

    public static string Frame(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.EndsWith(Terminator, StringComparison.Ordinal) ? command : command + Terminator;
    }

    public static bool IsErrorResponse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string text = raw.Trim();
        if (text.Contains("ERR CMD", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("NOPROBE", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("NO PROBE", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("OVER RANGE", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("UNDER RANGE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (Regex.IsMatch(text, @"^E\d+\b", RegexOptions.IgnoreCase)) return true;
        return Regex.IsMatch(text, @"^-2\d{2}\b");
    }

    /// <summary>
    /// Decodes a CTH7000 measurement response. Identification frames are explicitly rejected
    /// so the date token in the identification response can never become a fake temperature.
    /// </summary>
    public static ThermometerReading ParseReading(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        string trimmed = raw.Trim().TrimEnd('\r', '\n');

        if (Cth7000Identification.IsMatch(trimmed))
        {
            return new ThermometerReading(DateTimeOffset.Now, null, string.Empty, raw);
        }

        if (IsErrorResponse(trimmed))
        {
            return new ThermometerReading(DateTimeOffset.Now, null, string.Empty, raw);
        }

        // WIKA CTH7000 returns: <channel>,<measurement>,<units>
        // e.g. 2,24.332,"CEL"
        Match cth7000 = Cth7000Measurement.Match(trimmed);
        if (cth7000.Success)
        {
            string number = cth7000.Groups["value"].Value;
            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return new ThermometerReading(DateTimeOffset.Now, value, DetectUnit(trimmed), raw);
            }
        }

        // Keep the generic numeric fallback for older source-compatible protocol variants,
        // but never use it for anything that looks like a WIKA/CTH7000 identification frame.
        if (trimmed.Contains("WIKA", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("CTH7000", StringComparison.OrdinalIgnoreCase))
        {
            return new ThermometerReading(DateTimeOffset.Now, null, string.Empty, raw);
        }

        MatchCollection matches = NumberToken.Matches(trimmed);
        if (matches.Count > 0)
        {
            string number = matches[^1].Value.Replace(',', '.');
            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return new ThermometerReading(DateTimeOffset.Now, value, DetectUnit(trimmed), raw);
            }
        }

        return new ThermometerReading(DateTimeOffset.Now, null, DetectUnit(trimmed), raw);
    }

    /// <summary>Source-compatibility parser for legacy talk-only frames; not used by the CTH7000 UI.</summary>
    [Obsolete("Legacy talk-only thermometer protocol is retired; the CTH7000 uses documented SCPI query commands.")]
    public static string? DetectTalkOnlyChannel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        Match match = TalkOnlyChannel.Match(raw);
        if (!match.Success) return null;
        return match.Groups["channel"].Value.ToUpperInvariant() switch
        {
            "A" or "1" => "A",
            "B" or "2" => "B",
            _ => null,
        };
    }

    private static string DetectUnit(string text)
    {
        string upper = text.ToUpperInvariant();
        if (upper.Contains("OHM") || upper.Contains("\"R\"") || text.Contains('Ω')) return "Ω";
        if (upper.EndsWith('C') || upper.Contains(" C") || upper.Contains("DEG C") || upper.Contains("CEL")) return "°C";
        if (upper.EndsWith('F') || upper.Contains(" F") || upper.Contains("DEG F") || upper.Contains("FAR")) return "°F";
        if (upper.EndsWith('K') || upper.Contains(" K")) return "K";
        return string.Empty;
    }
}
