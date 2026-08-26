namespace VotschVc3.App.ViewModels;

internal static class StringComparisonExtensions
{
    public static bool EndsWith(this string value, char suffix, StringComparison comparison) =>
        value.EndsWith(suffix.ToString(), comparison);
}
