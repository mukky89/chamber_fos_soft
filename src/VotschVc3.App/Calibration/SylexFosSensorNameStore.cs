using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Calibration;

/// <summary>
/// Runtime-only storage for sensor names returned by the central FOS API.
/// Customer and SensorName are different business fields; this store prevents
/// the API sensor name from overwriting the persisted Customer field.
/// </summary>
public static class SylexFosSensorNameStore
{
    private sealed class Holder
    {
        public string Value { get; set; } = string.Empty;
    }

    private static readonly ConditionalWeakTable<CalibrationPeakRowViewModel, Holder> Values = new();

    public static void Set(CalibrationPeakRowViewModel row, string? sensorName)
    {
        Holder holder = Values.GetOrCreateValue(row);
        holder.Value = sensorName?.Trim() ?? string.Empty;
    }

    public static string Get(CalibrationPeakRowViewModel? row) =>
        row is not null && Values.TryGetValue(row, out Holder? holder) ? holder.Value : string.Empty;

    public static void Remove(CalibrationPeakRowViewModel row) => Values.Remove(row);
}

public sealed class SylexFosSensorNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is CalibrationPeakRowViewModel row ? SylexFosSensorNameStore.Get(row) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
