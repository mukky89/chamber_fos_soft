using System.Text.RegularExpressions;
namespace VotschVc3.Core.Calibration;
public static partial class FbgSerialParser
{
    [GeneratedRegex(@"(?<![A-Za-z0-9])([0-9]{6})[A-Za-z]00([0-9]{4})(?![A-Za-z0-9])")]
    private static partial Regex Barcode();
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z0-9]{6}/[A-Za-z0-9]{4}(?![A-Za-z0-9])")]
    private static partial Regex Serial();
    public static string Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string text = raw.Trim();
        var serial = Serial().Match(text);
        if (serial.Success) return serial.Value.ToUpperInvariant();
        var barcode = Barcode().Match(text);
        return barcode.Success ? $"{barcode.Groups[1].Value}/{barcode.Groups[2].Value}" : text;
    }
}
