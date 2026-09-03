using System.Globalization;
using System.Runtime.CompilerServices;
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
    private sealed class Holder
    {
        public string SylexSerialNumber { get; set; } = string.Empty;
        public string FbgType { get; set; } = string.Empty;
    }

    private static readonly ConditionalWeakTable<CalibrationPeakRowViewModel, Holder> Values = new();

    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z0-9]{6}/[A-Za-z0-9]{4}(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex SylexSerialRegex();

    public static string ParseSerialNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string trimmed = raw.Trim();
        Match match = SylexSerialRegex().Match(trimmed);
        return match.Success ? match.Value.ToUpperInvariant() : trimmed;
    }

    public static void SetParsedSerial(CalibrationPeakRowViewModel row, string? raw)
    {
        Values.GetOrCreateValue(row).SylexSerialNumber = ParseSerialNumber(raw);
    }

    public static void SetApiMetadata(CalibrationPeakRowViewModel row, string? serialNumber, string? fbgType)
    {
        Holder holder = Values.GetOrCreateValue(row);
        if (!string.IsNullOrWhiteSpace(serialNumber))
            holder.SylexSerialNumber = ParseSerialNumber(serialNumber);
        holder.FbgType = fbgType?.Trim() ?? string.Empty;
    }

    public static string GetSerialNumber(CalibrationPeakRowViewModel? row) =>
        row is not null && Values.TryGetValue(row, out Holder? holder) ? holder.SylexSerialNumber : string.Empty;

    public static string GetFbgType(CalibrationPeakRowViewModel? row) =>
        row is not null && Values.TryGetValue(row, out Holder? holder) ? holder.FbgType : string.Empty;

    public static void Remove(CalibrationPeakRowViewModel row) => Values.Remove(row);
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
