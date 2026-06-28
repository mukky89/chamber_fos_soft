using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace VotschVc3.App.Converters;

/// <summary>Formats a nullable number, showing an em dash when no value is present.</summary>
public sealed class ValueOrDashConverter : IValueConverter
{
    public string Format { get; set; } = "0.0";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return "—";
        }

        string format = parameter as string ?? Format;
        return value is IFormattable formattable
            ? formattable.ToString(format, CultureInfo.CurrentCulture)
            : value.ToString() ?? "—";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Maps a boolean (connected) flag to a status brush.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));

    public Brush FalseBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TrueBrush : FalseBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Maps an <see cref="VotschVc3.Core.Diagnostics.AppLogLevel"/> to a brush.</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        VotschVc3.Core.Diagnostics.AppLogLevel.Error => new SolidColorBrush(Color.FromRgb(0xE2, 0x55, 0x5B)),
        VotschVc3.Core.Diagnostics.AppLogLevel.Warning => new SolidColorBrush(Color.FromRgb(0xE2, 0xC5, 0x55)),
        _ => new SolidColorBrush(Color.FromRgb(0x96, 0x9B, 0xB5)),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Inverts a boolean.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>
/// Maps a boolean to <see cref="Visibility"/>. Pass <c>Invert</c> as the
/// converter parameter to collapse when the value is <c>true</c>.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is true;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
