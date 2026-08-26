using System.Globalization;
using System.Text;

namespace VotschVc3.Core.Profiles;

/// <summary>
/// Every parameter the quick profile builder ("Rýchly vytvárač profilov") needs in
/// order to describe the profile it is currently building – in both modes (parametric
/// sweep and typed temperature sequence), including the shared lead-in / safety-hold /
/// cycling options. Platform-independent so the naming rules can be tested.
/// </summary>
public sealed record QuickProfileParameters
{
    /// <summary><c>true</c> for the typed temperature sequence, <c>false</c> for the parametric sweep.</summary>
    public bool IsSequence { get; init; }

    /// <summary>
    /// Setpoints of the profile body – the sequence points in visit order, or (parametric
    /// mode) the ascending sweep temperatures including both endpoints. Never includes the
    /// lead-in ramp or the closing safety hold.
    /// </summary>
    public IReadOnlyList<double> Temperatures { get; init; } = Array.Empty<double>();

    /// <summary>Hold length (min) of each setpoint, in the same order as <see cref="Temperatures"/>.
    /// May be shorter than the temperature list – missing entries are simply not described.</summary>
    public IReadOnlyList<double> PlateauMinutes { get; init; } = Array.Empty<double>();

    /// <summary>Ramp length (min) shared by the transitions between two setpoints.</summary>
    public double RampMinutes { get; init; }

    /// <summary>Lowest sweep temperature (parametric mode).</summary>
    public double LowTemperature { get; init; }

    /// <summary>Highest sweep temperature (parametric mode).</summary>
    public double HighTemperature { get; init; }

    /// <summary>Temperature difference between two consecutive setpoints (parametric mode).</summary>
    public double TemperatureStep { get; init; }

    /// <summary>The sweep also runs back down to the low temperature (parametric mode).</summary>
    public bool IncludeDescending { get; init; }

    /// <summary>The peak is split into two by a lower notch (parametric mode).</summary>
    public bool DoublePeak { get; init; }

    /// <summary>How much lower (°C) the notch between the two peaks is (parametric mode).</summary>
    public double PeakDipCelsius { get; init; }

    /// <summary>The profile opens with a ramp from <see cref="LeadInFrom"/> to the first setpoint.</summary>
    public bool HasLeadIn { get; init; }

    /// <summary>Temperature the lead-in ramp starts from.</summary>
    public double LeadInFrom { get; init; }

    /// <summary>Length (min) of the lead-in ramp.</summary>
    public double LeadInMinutes { get; init; }

    /// <summary>The profile closes with a ramp to a safe temperature plus a long hold.</summary>
    public bool HasEndHold { get; init; }

    /// <summary>Temperature (°C) of the closing safety hold.</summary>
    public double EndTemperature { get; init; }

    /// <summary>Length (min) of the closing safety hold.</summary>
    public double EndHoldMinutes { get; init; }

    /// <summary>How many times the profile (or just its body) repeats; 1 = no cycling.</summary>
    public int Cycles { get; init; } = 1;

    /// <summary>When cycling, only the body repeats – lead-in and closing hold run once.</summary>
    public bool CycleBodyOnly { get; init; } = true;

    /// <summary>Total length of the whole run (all cycles included), in minutes.</summary>
    public double TotalMinutes { get; init; }
}

/// <summary>
/// Builds the automatic profile <see cref="Name"/> and the human-readable
/// <see cref="Description"/> of a quick-built profile.
/// <para>
/// Both are assembled from the same parts, so the name of a profile and the sentence
/// shown above its preview always agree – whichever mode the builder is in, and
/// whichever of the optional stages (lead-in ramp, closing safety hold, cycling) are
/// switched on.
/// </para>
/// <para>
/// Temperatures are joined with <c>→</c> rather than <c>-</c>: a sequence of negative
/// setpoints used to come out as <c>-20--10-0-20</c>, which is unreadable and cannot
/// be parsed back by eye. Long sequences are elided in the middle instead of pasting
/// dozens of values into the name.
/// </para>
/// </summary>
public static class QuickProfileNaming
{
    /// <summary>Sequences up to this many points are listed value by value in the name.</summary>
    private const int MaxNamedPoints = 7;

    /// <summary>Sequences up to this many points are listed value by value in the description.</summary>
    private const int MaxDescribedPoints = 10;

    /// <summary>Plateau lengths further apart than this (min) count as "not uniform".</summary>
    private const double MinutesEpsilon = 0.01;

    /// <summary>
    /// Compact, technical profile name, e.g.
    /// <c>-20…60 °C · 9 krokov · krok 10 °C · ↕ · plato 30 min · Σ 1 d 2 h</c> for a sweep or
    /// <c>-20→0→40→0→-20 °C · plato 90 min · rampa 30 min · Σ 8 h 30 min</c> for a sequence.
    /// </summary>
    /// <param name="parameters">The builder's current parameters.</param>
    /// <param name="prefix">Optional user prefix placed in front of the generated name.</param>
    /// <param name="culture">Number formatting; defaults to the current culture.</param>
    public static string Name(QuickProfileParameters parameters, string? prefix = null, IFormatProvider? culture = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        culture ??= CultureInfo.CurrentCulture;

        var parts = new List<string>();
        if (parameters.IsSequence)
        {
            parts.Add(SequenceTemperatures(parameters, MaxNamedPoints, culture) + " °C");
        }
        else
        {
            parts.Add($"{Number(parameters.LowTemperature, culture)}…{Number(parameters.HighTemperature, culture)} °C");
            parts.Add($"{parameters.Temperatures.Count} {StepWord(parameters.Temperatures.Count)}");
            if (parameters.TemperatureStep > 0)
            {
                parts.Add($"krok {Number(parameters.TemperatureStep, culture)} °C");
            }

            if (parameters.IncludeDescending)
            {
                parts.Add("↕");
            }

            if (parameters.DoublePeak)
            {
                parts.Add("2 vrcholy");
            }
        }

        if (PlateauText(parameters, culture) is { Length: > 0 } plateau)
        {
            parts.Add("plato " + plateau);
        }

        parts.Add(parameters.RampMinutes > 0
            ? "rampa " + Minutes(parameters.RampMinutes, culture)
            : "bez rampy");

        if (parameters.HasLeadIn)
        {
            parts.Add("nábeh " + Minutes(parameters.LeadInMinutes, culture));
        }

        if (parameters.HasEndHold)
        {
            parts.Add($"koniec {Number(parameters.EndTemperature, culture)} °C");
        }

        if (parameters.Cycles > 1)
        {
            parts.Add($"×{parameters.Cycles}");
        }

        parts.Add("Σ " + Duration(parameters.TotalMinutes, culture));

        string core = string.Join(" · ", parts);
        string head = prefix?.Trim() ?? string.Empty;
        return head.Length > 0 ? $"{head} {core}" : core;
    }

    /// <summary>
    /// Full sentence describing the profile, shown next to the preview – the same facts
    /// as <see cref="Name"/> but spelled out, e.g.
    /// <c>Postupnosť 5 teplôt: -20 → 0 → 40 → 0 → -20 °C · plato 90 min · rampa 30 min ·
    /// nábeh z 25 °C (60 min) · koniec na 25 °C (60 min) · cyklus ×2 (len telo) · Σ 1 d 3 h</c>.
    /// </summary>
    public static string Description(QuickProfileParameters parameters, IFormatProvider? culture = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        culture ??= CultureInfo.CurrentCulture;

        var parts = new List<string>();
        if (parameters.IsSequence)
        {
            if (parameters.Temperatures.Count == 0)
            {
                return "Pridaj aspoň dve teploty (body postupnosti).";
            }

            parts.Add($"Postupnosť {parameters.Temperatures.Count} {PointWord(parameters.Temperatures.Count)}: " +
                SequenceTemperatures(parameters, MaxDescribedPoints, culture, spaced: true) + " °C");
        }
        else
        {
            parts.Add($"Sweep {Number(parameters.LowTemperature, culture)} → {Number(parameters.HighTemperature, culture)} °C");
            parts.Add($"{parameters.Temperatures.Count} {StepWord(parameters.Temperatures.Count)}");
            if (parameters.TemperatureStep > 0)
            {
                parts.Add($"krok {Number(parameters.TemperatureStep, culture)} °C");
            }

            if (parameters.IncludeDescending)
            {
                parts.Add("aj späť dole");
            }

            if (parameters.DoublePeak)
            {
                parts.Add($"2 vrcholy (pokles {Number(parameters.PeakDipCelsius, culture)} °C)");
            }
        }

        if (PlateauText(parameters, culture) is { Length: > 0 } plateau)
        {
            parts.Add("plato " + plateau);
        }

        parts.Add(parameters.RampMinutes > 0
            ? "rampa " + Minutes(parameters.RampMinutes, culture)
            : "bez rampy (skok)");

        if (parameters.HasLeadIn)
        {
            parts.Add($"nábeh z {Number(parameters.LeadInFrom, culture)} °C ({Minutes(parameters.LeadInMinutes, culture)})");
        }

        if (parameters.HasEndHold)
        {
            parts.Add($"koniec na {Number(parameters.EndTemperature, culture)} °C ({Minutes(parameters.EndHoldMinutes, culture)})");
        }

        if (parameters.Cycles > 1)
        {
            parts.Add($"cyklus ×{parameters.Cycles}" + (parameters.CycleBodyOnly ? " (len telo)" : " (celý profil)"));
        }

        parts.Add("Σ " + Duration(parameters.TotalMinutes, culture));
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// The temperature list, joined with <c>→</c> and elided in the middle once it grows
    /// past <paramref name="maxPoints"/> values (the full span is then given as a range,
    /// so a 28-point profile still says what it covers without a wall of numbers).
    /// </summary>
    private static string SequenceTemperatures(
        QuickProfileParameters parameters, int maxPoints, IFormatProvider culture, bool spaced = false)
    {
        IReadOnlyList<double> temps = parameters.Temperatures;
        if (temps.Count == 0)
        {
            return "—";
        }

        string arrow = spaced ? " → " : "→";
        if (temps.Count <= maxPoints)
        {
            return string.Join(arrow, temps.Select(t => Number(t, culture)));
        }

        // Head … tail plus the covered range: enough to recognise the profile at a glance.
        int head = Math.Max(2, (maxPoints - 1) / 2);
        int tail = Math.Max(1, maxPoints - 1 - head);
        var sb = new StringBuilder();
        sb.Append(string.Join(arrow, temps.Take(head).Select(t => Number(t, culture))));
        sb.Append(arrow).Append('…').Append(arrow);
        sb.Append(string.Join(arrow, temps.Skip(temps.Count - tail).Select(t => Number(t, culture))));
        sb.Append(" (").Append(temps.Count).Append(' ').Append(PointWord(temps.Count)).Append(", ")
          .Append(Number(temps.Min(), culture)).Append('…').Append(Number(temps.Max(), culture)).Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// The hold length as one value when every point holds equally long, or as a range
    /// when they differ – so a sequence with per-point plateaus is described honestly
    /// instead of pretending one shared value.
    /// </summary>
    private static string PlateauText(QuickProfileParameters parameters, IFormatProvider culture)
    {
        List<double> holds = parameters.PlateauMinutes.Where(m => m >= 0).ToList();
        if (holds.Count == 0)
        {
            return string.Empty;
        }

        double min = holds.Min();
        double max = holds.Max();
        return max - min <= MinutesEpsilon
            ? Minutes(min, culture)
            : $"{Minutes(min, culture)}–{Minutes(max, culture)}";
    }

    /// <summary>
    /// A length in minutes, switching to hours from a whole or half hour upwards
    /// ("30 min", "45 min", "1 h", "1,5 h", "2 h") – how an operator says it out loud.
    /// </summary>
    private static string Minutes(double minutes, IFormatProvider culture)
    {
        if (minutes <= 0)
        {
            return "0 min";
        }

        double hours = minutes / 60;
        return minutes >= 60 && Math.Abs((hours * 2) - Math.Round(hours * 2)) < 1e-9
            ? $"{Number(hours, culture)} h"
            : $"{Number(minutes, culture)} min";
    }

    /// <summary>Total run length, e.g. <c>1 d 3 h 15 min</c>, <c>8 h 30 min</c>, <c>45 min</c>.</summary>
    public static string Duration(double minutes, IFormatProvider? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (minutes < 1)
        {
            return "< 1 min";
        }

        var ts = TimeSpan.FromMinutes(Math.Round(minutes));
        if (ts.TotalDays >= 1)
        {
            return $"{(int)ts.TotalDays} d {ts.Hours} h" + (ts.Minutes > 0 ? $" {ts.Minutes} min" : string.Empty);
        }

        if (ts.TotalHours >= 1)
        {
            return $"{ts.Hours} h" + (ts.Minutes > 0 ? $" {ts.Minutes} min" : string.Empty);
        }

        return $"{ts.Minutes} min";
    }

    private static string Number(double value, IFormatProvider culture) =>
        value.ToString("0.#", culture);

    private static string PointWord(int n) => n == 1 ? "teplota" : (n is >= 2 and <= 4 ? "teploty" : "teplôt");

    private static string StepWord(int n) => n == 1 ? "krok" : (n is >= 2 and <= 4 ? "kroky" : "krokov");
}
