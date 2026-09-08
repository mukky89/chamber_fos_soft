using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VotschVc3.App.Views;

/// <summary>Highlight only a preset matching the confirmed chamber setpoint.</summary>
public sealed class QuickPresetSelectedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 2 && values[0] is double preset && values[1] is double setpoint
           && Math.Abs(preset - setpoint) < 0.001;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}