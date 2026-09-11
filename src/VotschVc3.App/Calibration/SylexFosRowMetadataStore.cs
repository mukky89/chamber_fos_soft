using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Calibration;

/// <summary>
/// Runtime metadata that belongs to the Sylex production record rather than PeakLogger.
/// These values are display-only in the wiring grid and are intentionally not editable.
/// </summary>
public static partial class SylexFosRowMetadataStore
{
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z0-9]{6}/[A-Za-z0-9]{4}(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex SylexSerialRegex();

    public static string ParseSerialNumber(string? raw) => VotschVc3.Core.Calibration.FbgSerialParser.Parse(raw);

    public static void SetParsedSerial(CalibrationPeakRowViewModel row, string? raw)
    {
        string parsed = ParseSerialNumber(raw);
        if (!string.Equals(row.ApiMetadata.SylexSerialNumber, parsed, StringComparison.OrdinalIgnoreCase))
            row.ApiMetadata.Clear();
        row.ApiMetadata.SylexSerialNumber = parsed;
    }

    public static void SetApiMetadata(CalibrationPeakRowViewModel row, string? serialNumber, string? fbgType)
    {
        SylexFosDisplayMetadata holder = row.ApiMetadata;
        if (!string.IsNullOrWhiteSpace(serialNumber))
            holder.SylexSerialNumber = ParseSerialNumber(serialNumber);
        holder.FbgType = fbgType?.Trim() ?? string.Empty;
    }

    public static string GetSerialNumber(CalibrationPeakRowViewModel? row) =>
        row?.ApiMetadata.SylexSerialNumber ?? string.Empty;

    public static string GetFbgType(CalibrationPeakRowViewModel? row) =>
        row?.ApiMetadata.FbgType ?? string.Empty;

    public static void Remove(CalibrationPeakRowViewModel row) => row.ApiMetadata.Clear();
}

public sealed class SylexFosSerialNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is CalibrationPeakRowViewModel row ? SylexFosRowMetadataStore.GetSerialNumber(row) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

public sealed class SylexFosFbgTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is CalibrationPeakRowViewModel row ? SylexFosRowMetadataStore.GetFbgType(row) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}
