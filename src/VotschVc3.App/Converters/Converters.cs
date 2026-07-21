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

/// <summary>
/// Maps a boolean to a <see cref="GridLength"/>: <c>true</c> collapses the
/// column/row to zero, <c>false</c> uses the width given as the parameter
/// (default 280).
/// </summary>
public sealed class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
        {
            return new GridLength(0);
        }

        double width = 280;
        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double w))
        {
            width = w;
        }

        return new GridLength(width);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// Maps a chip label (tag / sensor / customer …) to a stable colour from a fixed
/// palette, so the same text always gets the same colour. Pass <c>bg</c>, <c>border</c>
/// or <c>fg</c> (default) as the parameter to get the soft background, the border or the
/// bright text brush.
/// </summary>
public sealed class TagBrushConverter : IValueConverter
{
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x5B, 0x8D, 0xEF), // blue
        Color.FromRgb(0x4F, 0xC1, 0x7A), // green
        Color.FromRgb(0xFF, 0xB4, 0x54), // amber
        Color.FromRgb(0xA5, 0x6C, 0xF0), // purple
        Color.FromRgb(0x3F, 0xC9, 0xC0), // teal
        Color.FromRgb(0xEF, 0x6F, 0x9C), // pink
        Color.FromRgb(0xFF, 0x8A, 0x5C), // orange
        Color.FromRgb(0x4F, 0xB6, 0xFF), // cyan
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Color c = Palette[StableIndex(value?.ToString() ?? string.Empty, Palette.Length)];
        Brush brush = (parameter as string) switch
        {
            "bg" => new SolidColorBrush(Color.FromArgb(0x2E, c.R, c.G, c.B)),
            "border" => new SolidColorBrush(Color.FromArgb(0x77, c.R, c.G, c.B)),
            _ => new SolidColorBrush(c),
        };
        brush.Freeze();
        return brush;
    }

    private static int StableIndex(string s, int n)
    {
        unchecked
        {
            int h = 17;
            foreach (char ch in s)
            {
                h = h * 31 + ch;
            }

            return Math.Abs(h) % n;
        }
    }

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
