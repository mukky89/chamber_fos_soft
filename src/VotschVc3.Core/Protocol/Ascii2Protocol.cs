using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VotschVc3.Core.Protocol;

/// <summary>
/// Stateless encoder / decoder for the Vötsch &amp; Weiss Technik <c>ASCII-2</c>
/// interface protocol used by the S!MPAC / SIMPAC controllers found in the
/// VC3 / VT3 climate chamber family (and the closely related CTS protocol).
/// <para>
/// Frame layout of a request:
/// <code>
///   $ dd C &lt;payload&gt; &lt;terminator&gt;
///   |  |  |    |          |
///   |  |  |    |          +-- terminator, carriage return (\r) by default
///   |  |  |    +------------- space separated parameters (command specific)
///   |  |  +------------------ single command letter (I = read, E = enter set points)
///   |  +--------------------- 2 digit chamber address (e.g. "01")
///   +------------------------ start delimiter '$'
/// </code>
/// </para>
/// <para>
/// Example write frame (temperature 50.0&#160;°C, humidity 0&#160;%, start bit set):
/// <code>
///   $01E 0050.0 0000.0 0000.0 0000.0 0000.0 0000.0 01000000000000000000000000000000\r
/// </code>
/// </para>
/// </summary>
public static class Ascii2Protocol
{
    /// <summary>Start delimiter of every frame.</summary>
    public const char StartDelimiter = '$';

    /// <summary>Command letter that requests the current measured / set values.</summary>
    public const char ReadCommand = 'I';

    /// <summary>Command letter that writes new set points and digital channels.</summary>
    public const char WriteCommand = 'E';

    /// <summary>Default frame terminator. Some firmware variants expect "\r\n".</summary>
    public const string DefaultTerminator = "\r";

    /// <summary>
    /// Number of analog set point fields written by <see cref="BuildWriteCommand"/>
    /// when the caller does not specify otherwise. Six is the value used by the
    /// stock VC3 examples; adjust through the overload if your chamber expects a
    /// different number of channels.
    /// </summary>
    public const int DefaultAnalogChannelCount = 6;

    // A signed decimal number such as 0050.0, -040.0 or 23.4
    private static readonly Regex DecimalToken =
        new(@"[-+]?\d+\.\d+", RegexOptions.Compiled);

    // A run of at least 8 binary digits – the digital channel block.
    private static readonly Regex BinaryToken =
        new(@"[01]{8,}", RegexOptions.Compiled);

    /// <summary>Formats the 2 digit chamber address (clamped to 0..99).</summary>
    public static string FormatAddress(int address)
    {
        if (address is < 0 or > 99)
        {
            throw new ArgumentOutOfRangeException(
                nameof(address), address, "Chamber address must be in the range 0..99.");
        }

        return address.ToString("D2", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a single analog value the way the controller expects it:
    /// four integer digits, a dot and one decimal place. Negative numbers keep
    /// the same total width by consuming a leading zero, e.g.
    /// <c>50.0 -&gt; "0050.0"</c> and <c>-40.0 -&gt; "-040.0"</c>.
    /// </summary>
    public static string FormatValue(double value)
    {
        double magnitude = Math.Abs(value);
        // Three integer digits + one decimal keeps a 6 character field once the
        // sign (or a leading zero) is added in front.
        string body = magnitude.ToString("000.0", CultureInfo.InvariantCulture);
        return value < 0 ? "-" + body : "0" + body;
    }

    /// <summary>Builds the read command, e.g. <c>"$01I\r"</c>.</summary>
    public static string BuildReadCommand(int address, string terminator = DefaultTerminator) =>
        $"{StartDelimiter}{FormatAddress(address)}{ReadCommand}{terminator}";

    /// <summary>
    /// Builds a write command that sends the supplied analog set points and the
    /// digital channel block.
    /// </summary>
    /// <param name="address">2 digit chamber address.</param>
    /// <param name="setpoints">
    /// Analog set point values in channel order (channel 1 = temperature,
    /// channel 2 = humidity, …). The list is padded with zeros up to
    /// <paramref name="analogChannelCount"/>.
    /// </param>
    /// <param name="digital">The 32 digital channels to transmit.</param>
    /// <param name="analogChannelCount">Number of analog fields to emit.</param>
    /// <param name="terminator">Frame terminator.</param>
    public static string BuildWriteCommand(
        int address,
        IReadOnlyList<double> setpoints,
        DigitalChannels digital,
        int analogChannelCount = DefaultAnalogChannelCount,
        string terminator = DefaultTerminator)
    {
        ArgumentNullException.ThrowIfNull(setpoints);
        ArgumentNullException.ThrowIfNull(digital);
        if (analogChannelCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(analogChannelCount), analogChannelCount, "At least one analog channel is required.");
        }

        var sb = new StringBuilder();
        sb.Append(StartDelimiter).Append(FormatAddress(address)).Append(WriteCommand);

        for (int i = 0; i < analogChannelCount; i++)
        {
            double value = i < setpoints.Count ? setpoints[i] : 0d;
            sb.Append(' ').Append(FormatValue(value));
        }

        sb.Append(' ').Append(digital.ToProtocolString());
        sb.Append(terminator);
        return sb.ToString();
    }

    /// <summary>
    /// Builds an arbitrary command frame, e.g. <c>BuildRawCommand(1, 'P', "0001")</c>
    /// to start stored program 1. Use this for vendor commands that are not
    /// modelled explicitly (program control, real time clock, …).
    /// </summary>
    public static string BuildRawCommand(
        int address, char command, string? payload = null, string terminator = DefaultTerminator)
    {
        var sb = new StringBuilder();
        sb.Append(StartDelimiter).Append(FormatAddress(address)).Append(command);
        if (!string.IsNullOrEmpty(payload))
        {
            if (payload[0] != ' ')
            {
                sb.Append(' ');
            }

            sb.Append(payload);
        }

        sb.Append(terminator);
        return sb.ToString();
    }

    /// <summary>
    /// Decodes a read response into a <see cref="ChamberReading"/>. The decoder
    /// is intentionally tolerant: it extracts every decimal number and the
    /// digital channel block regardless of an echoed address, leading status
    /// characters or whitespace differences between firmware versions.
    /// </summary>
    /// <param name="raw">The frame received from the chamber (terminator already stripped).</param>
    /// <param name="startChannelIndex">Index of the start channel for the decoded <see cref="DigitalChannels"/>.</param>
    public static ChamberReading ParseReading(string raw, int startChannelIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(raw);

        string trimmed = raw.Trim().TrimEnd('\r', '\n');

        // The digital block is matched first so its binary digits are not also
        // interpreted as part of an analog value.
        string? digitalText = null;
        Match binary = BinaryToken.Match(trimmed);
        if (binary.Success)
        {
            digitalText = binary.Value;
            // Remove the matched block so the decimal pass cannot see it.
            trimmed = trimmed.Remove(binary.Index, binary.Length);
        }

        var values = new List<double>();
        foreach (Match m in DecimalToken.Matches(trimmed))
        {
            if (double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                values.Add(v);
            }
        }

        var digital = DigitalChannels.Parse(digitalText, startChannelIndex);
        return new ChamberReading(DateTimeOffset.Now, raw, values, digital);
    }
}
