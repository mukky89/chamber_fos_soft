using System.Globalization;

namespace VotschVc3.Core.Recording;

/// <summary>
/// Shared number formatting for the app's CSV exports. The files are opened in a
/// Slovak Excel, where the column (list) separator is ';' and the decimal separator
/// is ','. Writing numbers with an invariant '.' made Excel read a value like
/// "25.4" as the date 25. apríl; writing "25,4" instead makes Excel treat it as a
/// number. The <see cref="RecordingReader"/> reads both forms back (it normalises
/// ',' to '.'), so this change is backward compatible with older files.
/// </summary>
public static class CsvFormat
{
    /// <summary>Comma decimal separator, no thousands grouping – pairs with the ';' delimiter.</summary>
    public static readonly NumberFormatInfo Number = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = string.Empty,
    };

    /// <summary>
    /// Default format for a measured chamber value. The S!MPAC controller reports the actual
    /// temperature to four decimals (SIMPATI shows e.g. 40,0213 °C) and the protocol parser
    /// keeps every digit, so the recording keeps them too – rounding to 0,1 °C on the way to
    /// the file threw away resolution the device had already measured. Trailing zeros are
    /// dropped, so a controller that really only sends 40,0 still writes "40,0".
    /// </summary>
    public const string MeasuredValue = "0.0###";

    /// <summary>Formats a nullable value with the given numeric format (empty string when null).</summary>
    public static string Fmt(double? value, string format = MeasuredValue) =>
        value?.ToString(format, Number) ?? string.Empty;
}
