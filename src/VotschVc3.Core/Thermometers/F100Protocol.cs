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
/// 1–2&#160;ms gap between transmitted characters. Because the exact value query
/// differs slightly between firmware revisions, the read command is configurable
/// (see <see cref="DefaultReadCommand"/>) and a raw terminal is provided for
/// calibration.
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

    /// <summary>Default command that requests the current reading.</summary>
    public const string DefaultReadCommand = "READ?";

    /// <summary>Supported measurement units.</summary>
    public static IReadOnlyList<string> Units { get; } = new[] { "C", "F", "K", "Ohms" };

    /// <summary>Selectable channels.</summary>
    public static IReadOnlyList<string> Channels { get; } = new[] { "A", "B", "A-B" };

    /// <summary>Supported baud rates.</summary>
    public static IReadOnlyList<int> BaudRates { get; } = new[] { 4800, 9600, 19200 };

    private static readonly Regex NumberToken =
        new(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.Compiled);

    /// <summary>Appends the terminator to a command if it is missing.</summary>
    public static string Frame(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.EndsWith(Terminator, StringComparison.Ordinal) ? command : command + Terminator;
    }

    /// <summary>Decodes a response line into a <see cref="ThermometerReading"/>.</summary>
    public static ThermometerReading ParseReading(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        string trimmed = raw.Trim().TrimEnd('\r', '\n');

        double? value = null;
        Match m = NumberToken.Match(trimmed);
        if (m.Success)
        {
            string number = m.Value.Replace(',', '.');
            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                value = v;
            }
        }

        return new ThermometerReading(DateTimeOffset.Now, value, DetectUnit(trimmed), raw);
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
