using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.Views;

internal sealed class CalibrationProgressPhaseConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        CalibrationTargetState state = values.ElementAtOrDefault(0) is CalibrationTargetState s ? s : CalibrationTargetState.Waiting;
        return state switch
        {
            CalibrationTargetState.WaitingForTemperature => "1 · čaká na teplotu",
            CalibrationTargetState.Stabilizing => "2 · stabilizácia FBG",
            CalibrationTargetState.Live => "3 · meranie samples",
            CalibrationTargetState.Stable => "4 · hotovo",
            CalibrationTargetState.Overridden => "4 · hotovo (override)",
            CalibrationTargetState.PeakLost => "CHYBA · peak stratený",
            CalibrationTargetState.TimedOut => "CHYBA · timeout",
            CalibrationTargetState.Disconnected => "CHYBA · odpojené",
            CalibrationTargetState.Failed => "CHYBA",
            _ => state.ToString(),
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}

internal sealed class CalibrationProgressSampleLabelConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        CalibrationTargetState state = values.ElementAtOrDefault(0) is CalibrationTargetState s ? s : CalibrationTargetState.Waiting;
        int current = ToInt(values.ElementAtOrDefault(1));
        int required = Math.Max(0, ToInt(values.ElementAtOrDefault(2)));
        bool measurement = string.Equals(parameter?.ToString(), "measurement", StringComparison.OrdinalIgnoreCase);

        if (required <= 0) return "—";
        if (measurement)
        {
            if (state is CalibrationTargetState.Stable or CalibrationTargetState.Overridden) return $"{required}/{required} ✓";
            if (state == CalibrationTargetState.Live) return $"{Math.Min(current, required)}/{required}";
            return $"0/{required}";
        }

        if (state is CalibrationTargetState.Live or CalibrationTargetState.Stable or CalibrationTargetState.Overridden)
            return $"{required}/{required} ✓";
        if (state == CalibrationTargetState.WaitingForTemperature) return $"0/{required}";
        return $"{Math.Min(current, required)}/{required}";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();

    private static int ToInt(object? value) => value is int i ? i : 0;
}

internal sealed class CalibrationProgressRemainingConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        CalibrationTargetState state = values.ElementAtOrDefault(0) is CalibrationTargetState s ? s : CalibrationTargetState.Waiting;
        int current = values.ElementAtOrDefault(1) is int i ? i : 0;
        int required = values.ElementAtOrDefault(2) is int r ? Math.Max(0, r) : 0;
        bool measurement = string.Equals(parameter?.ToString(), "measurement", StringComparison.OrdinalIgnoreCase);

        if (required <= 0) return "—";
        if (measurement)
        {
            if (state is CalibrationTargetState.Stable or CalibrationTargetState.Overridden) return "0";
            if (state != CalibrationTargetState.Live) return required.ToString(CultureInfo.InvariantCulture);
            return Math.Max(0, required - current).ToString(CultureInfo.InvariantCulture);
        }

        if (state is CalibrationTargetState.Live or CalibrationTargetState.Stable or CalibrationTargetState.Overridden) return "0";
        if (state == CalibrationTargetState.WaitingForTemperature) return required.ToString(CultureInfo.InvariantCulture);
        return Math.Max(0, required - current).ToString(CultureInfo.InvariantCulture);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}

internal sealed class CalibrationProgressTimeoutRemainingConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        TimeSpan elapsed = values.ElementAtOrDefault(0) is TimeSpan e ? e : TimeSpan.Zero;
        TimeSpan timeout = values.ElementAtOrDefault(1) is TimeSpan t ? t : TimeSpan.Zero;
        if (timeout <= TimeSpan.Zero) return "bez limitu";
        TimeSpan left = timeout - elapsed;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        return Format(left);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();

    private static string Format(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}"
        : $"{(int)value.TotalMinutes:00}:{value.Seconds:00}";
}

internal sealed class CalibrationProgressBlockReasonConverter : IMultiValueConverter
{
    private static readonly Regex FailedCriterion = new(@"(?<item>[^·]+×)", RegexOptions.Compiled);

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        CalibrationTargetState state = values.ElementAtOrDefault(0) is CalibrationTargetState s ? s : CalibrationTargetState.Waiting;
        int current = values.ElementAtOrDefault(1) is int i ? i : 0;
        int required = values.ElementAtOrDefault(2) is int r ? r : 0;
        string detail = values.ElementAtOrDefault(3)?.ToString() ?? string.Empty;

        if (state == CalibrationTargetState.WaitingForTemperature)
            return "Čaká na stabilnú WIKA / internú teplotu.";
        if (state == CalibrationTargetState.Live)
            return $"Stabilita OK. Zbiera finálne meracie samples; chýba {Math.Max(0, required - current)}.";
        if (state is CalibrationTargetState.Stable or CalibrationTargetState.Overridden)
            return "Hotovo – tento FBG už neblokuje ďalší krok.";
        if (state is CalibrationTargetState.PeakLost or CalibrationTargetState.TimedOut or CalibrationTargetState.Disconnected or CalibrationTargetState.Failed)
            return string.IsNullOrWhiteSpace(detail) ? state.ToString() : detail;

        MatchCollection failed = FailedCriterion.Matches(detail);
        if (failed.Count > 0)
            return "Blokuje: " + string.Join("; ", failed.Select(match => match.Groups["item"].Value.Trim()));
        if (required > current)
            return $"Blokuje: chýba {required - current} stabilizačných samples.";
        return string.IsNullOrWhiteSpace(detail) ? "Vyhodnocujem stabilitu…" : detail;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}

internal sealed class CalibrationProgressCriteriaConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string detail = value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(detail)) return "—";
        return detail.Replace(" · ", "\n", StringComparison.Ordinal);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
