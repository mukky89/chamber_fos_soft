namespace VotschVc3.Core.Profiles;

/// <summary>
/// Generates a standardized profile name from the profile's characteristics –
/// used when importing externally authored profiles so the library keeps a
/// consistent naming scheme while the source name is preserved in
/// <see cref="TestProfile.OriginalName"/>.
/// <para>
/// Pattern: <c>[snímače · ]{min}…{max} °C · {N} segm · {trvanie}[ · tagy]</c>,
/// e.g. <c>ADXL · -40…85 °C · 6 segm · 3 h 20 min</c>.
/// </para>
/// </summary>
public static class ProfileNaming
{
    public static string StandardName(TestProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Segments.Count == 0)
        {
            return string.IsNullOrWhiteSpace(profile.Name) ? "Profil" : profile.Name;
        }

        double min = profile.Segments.Min(s => s.TargetTemperature);
        double max = profile.Segments.Max(s => s.TargetTemperature);

        string sensors = profile.Sensors is { Count: > 0 }
            ? string.Join("+", profile.Sensors) + " · "
            : string.Empty;

        string tags = profile.Tags is { Count: > 0 }
            ? " · " + string.Join(", ", profile.Tags)
            : string.Empty;

        return $"{sensors}{min:0.#}…{max:0.#} °C · {profile.Segments.Count} segm · {FormatDuration(profile.TotalDuration)}{tags}";
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalMinutes < 1)
        {
            return "< 1 min";
        }

        if (t.TotalDays >= 1)
        {
            return $"{(int)t.TotalDays} d {t.Hours} h {t.Minutes} min";
        }

        return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{t.Minutes} min";
    }
}
