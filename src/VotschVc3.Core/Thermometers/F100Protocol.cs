using System.Globalization;
using System.Text.RegularExpressions;

namespace VotschVc3.Core.Thermometers;

/// <summary>
/// Encoder / decoder for the ASL F100 precision thermometer's USB-serial
/// interface (also covers the F150 / F250 family).
/// <para>
/// The F100 presents itself as a USB virtual COM port. Default line settings are
/// <b>9600 baud, 8 data bits, no parity, 1 stop bit, no flow control</b>; commands
/// are SCPI style and terminated with a carriage return, and the device asks for a
/// 1–2&#160;ms gap between transmitted characters.
/// </para>
/// </summary>
public static class F100Protocol
{
    /// <summary>Default line speed of the USB-serial interface.</summary>
    public const int DefaultBaudRate = 9600;

    /// <summary>Frame terminator (carriage return).</summary>
    public const string Terminator = "\r";

    /// <summary>Recommended delay between characters, in milliseconds.</summary>
    public const int InterCharacterDelayMs = 2;

    /// <summary>Standard identification query.</summary>
    public const string IdentifyCommand = "*IDN?";

    /// <summary>Places the instrument in USB remote-control mode.</summary>
    public const string RemoteCommand = "SYSTEM:REMOTE";

    /// <summary>Returns the instrument front panel to local control.</summary>
    public const string LocalCommand = "SYSTEM:LOCAL";

    /// <summary>Default command that requests the current reading from the configured channel.</summary>
    public const string DefaultReadCommand = "READ?";

    /// <summary>Supported measurement units.</summary>
    public static IReadOnlyList<string> Units { get; } = new[] { "C", "F", "K", "Ohms" };

    /// <summary>Selectable F100 probe inputs. A-B is retained for diagnostics/differential measurements.</summary>
    public static IReadOnlyList<string> Channels { get; } = new[] { "A", "B", "A-B" };

    /// <summary>Physical probe sockets used by the calibration UI.</summary>
    public static IReadOnlyList<string> ProbeChannels { get; } = new[] { "A", "B" };

    /// <summary>Supported baud rates.</summary>
    public static IReadOnlyList<int> BaudRates { get; } = new[] { 4800, 9600, 19200 };

    private static readonly Regex NumberToken =
        new(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.Compiled);

    private static readonly Regex TalkOnlyChannel =
        new(@"^\s*(?:(?:CH|CHANNEL)\s*)?(?<channel>A|B|1|2)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// ASL SCPI-family command that selects an input without initiating a measurement.
    /// F100 firmware variants that do not expose it are handled by the client's READ? fallback.
    /// </summary>
    public static string BuildConfigureChannelCommand(string channel) =>
        $"CONFIGURE:CHANNEL {NormalizeChannel(channel)}";

    /// <summary>
    /// ASL SCPI-family immediate measurement command for a selected input. This is preferred
    /// because it makes the requested A/B channel explicit for every reference reading.
    /// </summary>
    public static string BuildMeasureChannelCommand(string channel) =>
        $"MEASURE:CHANNEL? {NormalizeChannel(channel)}";

    public static string NormalizeChannel(string? channel)
    {
        string value = (channel ?? "A").Trim().ToUpperInvariant().Replace(" ", string.Empty);
        return value switch
        {
            "A" => "A",
            "B" => "B",
            "A-B" or "A−B" => "A-B",
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "F100 channel must be A, B or A-B."),
        };
    }

    /// <summary>Appends the terminator to a command if it is missing.</summary>
    public static string Frame(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.EndsWith(Terminator, StringComparison.Ordinal) ? command : command + Terminator;
    }

    /// <summary>True for common ASL instrument error responses (E4, E5, -2xx etc.).</summary>
    public static bool IsErrorResponse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string text = raw.Trim();
        if (Regex.IsMatch(text, @"^E\d+\b", RegexOptions.IgnoreCase)) return true;
        return Regex.IsMatch(text, @"^-2\d{2}\b");
    }

    /// <summary>Decodes a response line into a <see cref="ThermometerReading"/>.</summary>
    public static ThermometerReading ParseReading(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        string trimmed = raw.Trim().TrimEnd('\r', '\n');

        if (IsErrorResponse(trimmed))
        {
            return new ThermometerReading(DateTimeOffset.Now, null, string.Empty, raw);
        }

        double? value = null;
        // Talk-only F100 frames can start with a channel number (for example
        // "2 23.456 C"). The measurement is therefore the last numeric token,
        // not necessarily the first one.
        MatchCollection matches = NumberToken.Matches(trimmed);
        if (matches.Count > 0)
        {
            Match m = matches[^1];
            string number = m.Value.Replace(',', '.');
            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                value = v;
            }
        }

        return new ThermometerReading(DateTimeOffset.Now, value, DetectUnit(trimmed), raw);
    }

    /// <summary>
    /// Extracts the input identifier from a talk-only frame. F100 variants use
    /// either A/B or 1/2 at the start of the line; frames without an identifier
    /// are valid too and return <see langword="null"/>.
    /// </summary>
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
        if (upper.Contains("OHM") || text.Contains('Ω'))
        {
            return "Ω";
        }

        // Look at a trailing unit letter (avoid matching letters inside "READ" etc.).
        if (upper.EndsWith('C') || upper.Contains(" C") || upper.Contains("DEG C"))
        {
            return "°C";
        }

        if (upper.EndsWith('F') || upper.Contains(" F") || upper.Contains("DEG F"))
        {
            return "°F";
        }

        if (upper.EndsWith('K') || upper.Contains(" K"))
        {
            return "K";
        }

        return string.Empty;
    }
}
