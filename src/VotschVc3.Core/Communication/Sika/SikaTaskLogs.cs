using System.Globalization;
using System.Text;
using System.Text.Json;

namespace VotschVc3.Core.Communication.Sika;

public sealed record SikaTaskLogSummary(
    int Id, string Name, string TaskName, string Type, string Version,
    DateTimeOffset Started, DateTimeOffset Finished)
{
    public TimeSpan Duration => Finished - Started;
    public string DisplayText => $"{Started.LocalDateTime:dd.MM.yyyy HH:mm} · {TaskName} · {Name} · {FormatDuration(Duration)}";

    private static string FormatDuration(TimeSpan value) => value.TotalDays >= 1
        ? $"{(int)value.TotalDays} d {value.Hours} h {value.Minutes} min"
        : $"{(int)value.TotalHours} h {value.Minutes} min";
}

public sealed record SikaTaskLogPoint(long Seconds, double Value);

public sealed record SikaTaskLogData(
    IReadOnlyList<SikaTaskLogPoint> Setpoints,
    IReadOnlyList<SikaTaskLogPoint> Temperatures)
{
    public async Task WriteCsvAsync(string path, DateTimeOffset started, CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await writer.WriteLineAsync("Čas;Sekundy;Setpoint °C;Teplota SIKA °C").ConfigureAwait(false);
        int sp = 0, tr = 0;
        while (sp < Setpoints.Count || tr < Temperatures.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long seconds = Math.Min(
                sp < Setpoints.Count ? Setpoints[sp].Seconds : long.MaxValue,
                tr < Temperatures.Count ? Temperatures[tr].Seconds : long.MaxValue);
            double? setpoint = sp < Setpoints.Count && Setpoints[sp].Seconds == seconds ? Setpoints[sp++].Value : null;
            double? temperature = tr < Temperatures.Count && Temperatures[tr].Seconds == seconds ? Temperatures[tr++].Value : null;
            DateTimeOffset timestamp = started.AddSeconds(seconds);
            string line = string.Join(';',
                timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                seconds.ToString(CultureInfo.InvariantCulture), Fmt(setpoint), Fmt(temperature));
            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }
    }

    private static string Fmt(double? value) => value?.ToString("0.######", CultureInfo.InvariantCulture) ?? string.Empty;
}

public static class SikaTaskLogParser
{
    public static IReadOnlyList<SikaTaskLogSummary> ParseIndex(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("values", out JsonElement values) || values.ValueKind != JsonValueKind.Array)
            return [];
        var result = new List<SikaTaskLogSummary>();
        foreach (JsonElement item in values.EnumerateArray())
        {
            if (!TryInt(item, "ID", out int id) || !TryLong(item, "Start", out long start) || !TryLong(item, "End", out long end))
                continue;
            JsonElement task = item.TryGetProperty("Task", out JsonElement taskValue) ? taskValue : default;
            result.Add(new SikaTaskLogSummary(id, Text(item, "Name"), Text(task, "Name"), Text(item, "Type"),
                Text(item, "Version"), DateTimeOffset.FromUnixTimeSeconds(start), DateTimeOffset.FromUnixTimeSeconds(end)));
        }
        return result.OrderByDescending(x => x.Started).ToArray();
    }

    public static SikaTaskLogData ParseData(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        var setpoints = new List<SikaTaskLogPoint>();
        var temperatures = new List<SikaTaskLogPoint>();
        if (!doc.RootElement.TryGetProperty("values", out JsonElement values) || values.ValueKind != JsonValueKind.Array)
            return new(setpoints, temperatures);
        foreach (JsonElement series in values.EnumerateArray())
        {
            string name = Text(series, "n");
            List<SikaTaskLogPoint>? target = name switch
            {
                "TRset_SP" => setpoints,
                "TRset_TR" => temperatures,
                _ => null,
            };
            if (target is null || !series.TryGetProperty("l", out JsonElement points) || points.ValueKind != JsonValueKind.Array)
                continue;
            foreach (JsonElement point in points.EnumerateArray())
            {
                if (point.TryGetProperty("t", out JsonElement t) && point.TryGetProperty("v", out JsonElement v) &&
                    t.TryGetInt64(out long seconds) && v.TryGetDouble(out double value))
                    target.Add(new(seconds, value));
            }
        }
        return new(setpoints, temperatures);
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value)
            ? value.ToString() : string.Empty;

    private static bool TryInt(JsonElement element, string name, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out JsonElement item)) return false;
        return item.ValueKind == JsonValueKind.Number
            ? item.TryGetInt32(out value)
            : int.TryParse(item.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryLong(JsonElement element, string name, out long value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out JsonElement item)) return false;
        return item.ValueKind == JsonValueKind.Number
            ? item.TryGetInt64(out value)
            : long.TryParse(item.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
