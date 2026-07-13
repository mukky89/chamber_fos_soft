using System.Globalization;
using System.Text;

namespace VotschVc3.Core.Protocol;

/// <summary>
/// Encoder / decoder for the Weiss / Vötsch <c>SIMSERV</c> protocol used by the
/// Simpac controllers (Simpati TCP/IP interface). Unlike the older ASCII-2
/// <c>$ddE</c> frames, control here is done with numbered function commands.
/// <para>
/// Frame make-up (see the Simpati manual, "Function commands – structure"):
/// <code>
///   FunctionNo ¶ Simpati-ID ¶ Argument1 ¶ Argument2 … CR
/// </code>
/// where the separator is <c>'¶'</c> (ASCII&#160;182) and every frame ends with a
/// carriage return. The Simpati-ID is the chamber number (1..99). For control
/// variables an additional index selects the channel (1&#160;=&#160;temperature,
/// 2&#160;=&#160;humidity, …).
/// </para>
/// <para>
/// A response starts with a status token: <c>"1"</c> on success (followed by any
/// return values, again <c>¶</c>-separated) or a negative error code
/// (e.g. <c>-4</c> = test system not available, <c>-5</c> = unknown command).
/// </para>
/// </summary>
public static class SimservProtocol
{
    /// <summary>Field separator, <c>'¶'</c> (ASCII 182 / 0xB6).</summary>
    public const char Separator = '¶';

    /// <summary>Frame terminator (carriage return).</summary>
    public const string Terminator = "\r";

    // Function numbers taken from the Simpati SIMSERV command list.
    public const int GetChamberType = 10017;       // -> 33333 SimCon, 44444 Simpac
    public const int GetOperatingMode = 10010;     // -> 0x01 logging, 0x02 MANUAL, 0x04 automatic, …
    public const int GetOperatingStatus = 10012;   // -> 0x1 available, 0x2 run, 0x4 warning, 0x8 error
    public const int GetControlVariableCount = 11018;
    public const int SetNominalValue = 11001;      // args: index, value[, user]
    public const int GetNominalValue = 11002;      // args: index -> value
    public const int GetActualValue = 11004;       // args: index -> value
    public const int SetDigitalOut = 14001;        // args: index, 1/0[, user]
    public const int GetDigitalOut = 14003;        // args: index -> 0/1

    /// <summary>
    /// Builds a SIMSERV frame: <c>FunctionNo ¶ Simpati-ID ¶ args… CR</c>.
    /// </summary>
    public static string Build(int functionNo, int simpatiId, params string[] args)
    {
        var sb = new StringBuilder();
        sb.Append(functionNo.ToString(CultureInfo.InvariantCulture));
        sb.Append(Separator).Append(simpatiId.ToString(CultureInfo.InvariantCulture));
        if (args is not null)
        {
            foreach (string a in args)
            {
                sb.Append(Separator).Append(a);
            }
        }

        sb.Append(Terminator);
        return sb.ToString();
    }

    /// <summary>Formats an analog value the SIMSERV way (invariant decimal point, one decimal).</summary>
    public static string Number(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Idx(int index) => index.ToString(CultureInfo.InvariantCulture);

    /// <summary>Read the measured value of a control variable (1 = temperature).</summary>
    public static string BuildGetActualValue(int simpatiId, int index) =>
        Build(GetActualValue, simpatiId, Idx(index));

    /// <summary>Read the active set point of a control variable (1 = temperature).</summary>
    public static string BuildGetNominalValue(int simpatiId, int index) =>
        Build(GetNominalValue, simpatiId, Idx(index));

    /// <summary>Set the set point of a control variable (1 = temperature).</summary>
    public static string BuildSetNominalValue(int simpatiId, int index, double value) =>
        Build(SetNominalValue, simpatiId, Idx(index), Number(value));

    /// <summary>Switch a digital output channel on/off (channel 1 = start / "system on").</summary>
    public static string BuildSetDigitalOut(int simpatiId, int index, bool on) =>
        Build(SetDigitalOut, simpatiId, Idx(index), on ? "1" : "0");

    /// <summary>Ask the controller for its type (33333 = SimCon, 44444 = Simpac).</summary>
    public static string BuildGetChamberType(int simpatiId) => Build(GetChamberType, simpatiId);

    /// <summary>Ask the controller for its operating mode (0x02 = MANUAL).</summary>
    public static string BuildGetOperatingMode(int simpatiId) => Build(GetOperatingMode, simpatiId);

    /// <summary>Splits a SIMSERV response into its ¶-separated tokens (terminator stripped).</summary>
    public static string[] ParseResponse(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return Array.Empty<string>();
        }

        return raw.TrimEnd('\r', '\n').Split(Separator);
    }

    /// <summary>True when the response's first token is the success flag <c>"1"</c>.</summary>
    public static bool IsSuccess(string? raw)
    {
        string[] tokens = ParseResponse(raw);
        return tokens.Length > 0 && tokens[0] == "1";
    }

    /// <summary>
    /// Returns the first numeric return value of a successful response, or
    /// <c>null</c> if the response was an error / carried no number.
    /// </summary>
    public static double? FirstValue(string? raw)
    {
        string[] tokens = ParseResponse(raw);
        for (int i = 1; i < tokens.Length; i++)
        {
            if (double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                return v;
            }
        }

        return null;
    }
}
