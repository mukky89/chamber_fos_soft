using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Profiles;

/// <summary>
/// Imports test profiles authored in other tools into a <see cref="TestProfile"/>.
/// <para>
/// The proprietary SIMPATI program/database format is not publicly documented,
/// so the importer targets the formats that are realistically exchangeable:
/// </para>
/// <list type="bullet">
///   <item>the application's own <c>.json</c> profiles (round trip);</item>
///   <item>delimited text exported from SIMPATI / Excel (CSV with <c>;</c>,
///   tab or <c>,</c> separators), in either of two shapes:
///     <list type="bullet">
///       <item><b>Segment table</b> – one row per step with a duration and the
///       target temperature/humidity (and an optional ramp/hold flag);</item>
///       <item><b>Setpoint timeline</b> – one row per point with a cumulative
///       time and the setpoint, converted into ramp segments.</item>
///     </list>
///   </item>
/// </list>
/// German number (comma decimal) and <c>hh:mm:ss</c> time formats are handled.
/// </summary>
public static class ProfileImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly string[] DurationKeys = { "dauer", "duration", "segment", "step time", "dwell" };
    private static readonly string[] TimeKeys = { "zeit", "time", "uhrzeit", "elapsed", "cumulative" };
    private static readonly string[] TemperatureKeys = { "temp", "temperatur", "temperature", "°c", "soll t", "channel 1", "kanal 1" };
    private static readonly string[] HumidityKeys = { "feuchte", "humidity", "rf", "r.h", "rh", "% r", "channel 2", "kanal 2" };
    private static readonly string[] RampKeys = { "ramp", "rampe", "gradient", "slope", "art", "mode", "typ", "type" };
    private static readonly string[] HoldWords = { "hold", "halten", "halt", "step", "sprung", "konstant", "constant", "soak", "dwell" };

    /// <summary>Imports a profile from a file, choosing the parser by content.</summary>
    public static ProfileImportResult ImportFile(string path, ChamberKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string text = File.ReadAllText(path);
        ProfileImportResult result = Import(text, kind);
        if (string.IsNullOrWhiteSpace(result.Profile.Name) || result.Profile.Name == "New profile")
        {
            result.Profile.Name = Path.GetFileNameWithoutExtension(path);
        }

        return result;
    }

    /// <summary>Imports a profile from raw text content.</summary>
    public static ProfileImportResult Import(string text, ChamberKind kind)
    {
        ArgumentNullException.ThrowIfNull(text);
        string trimmed = text.TrimStart('﻿', ' ', '\r', '\n', '\t');

        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return ImportJson(trimmed, kind);
        }

        return ImportDelimited(text, kind);
    }

    private static ProfileImportResult ImportJson(string text, ChamberKind kind)
    {
        TestProfile? profile = text.StartsWith('[')
            ? JsonSerializer.Deserialize<List<TestProfile>>(text, JsonOptions)?.FirstOrDefault()
            : JsonSerializer.Deserialize<TestProfile>(text, JsonOptions);

        if (profile is null || profile.Segments.Count == 0)
        {
            throw new FormatException("The JSON file does not contain a valid profile.");
        }

        var warnings = new List<string>();
        profile.Id = Guid.NewGuid();
        AdaptToKind(profile, kind, warnings);
        return new ProfileImportResult(profile, "JSON profil", warnings);
    }

    private static ProfileImportResult ImportDelimited(string text, ChamberKind kind)
    {
        var warnings = new List<string>();
        List<string> lines = text
            .Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
            .ToList();

        if (lines.Count == 0)
        {
            throw new FormatException("The file is empty.");
        }

        char delimiter = DetectDelimiter(lines);
        string[][] rows = lines.Select(l => l.Split(delimiter)).ToArray();

        bool hasHeader = rows[0].Any(c => c.Any(char.IsLetter) && !LooksNumeric(c, delimiter));
        var columns = new ColumnMap();
        int firstDataRow = 0;

        if (hasHeader)
        {
            columns = MapColumns(rows[0]);
            firstDataRow = 1;
        }
        else
        {
            // No header: assume duration, temperature, humidity by position.
            columns.Duration = 0;
            columns.Temperature = 1;
            columns.Humidity = 2;
            columns.IsTimeline = false;
            warnings.Add("Bez hlavičky – predpokladám stĺpce: trvanie, teplota, vlhkosť.");
        }

        if (columns.Temperature < 0)
        {
            throw new FormatException("Nepodarilo sa nájsť stĺpec teploty.");
        }

        var profile = new TestProfile { Kind = kind, Name = "Importovaný profil" };
        var points = new List<(double time, double temp, double? hum, bool? ramp)>();

        for (int r = firstDataRow; r < rows.Length; r++)
        {
            string[] row = rows[r];
            if (!TryGetDouble(row, columns.Temperature, delimiter, out double temp))
            {
                warnings.Add($"Riadok {r + 1} preskočený (chýba teplota).");
                continue;
            }

            double? hum = null;
            if (columns.Humidity >= 0 && TryGetDouble(row, columns.Humidity, delimiter, out double h))
            {
                hum = h;
            }

            double time = 0;
            int timeColumn = columns.IsTimeline ? columns.Time : columns.Duration;
            if (timeColumn >= 0 && timeColumn < row.Length)
            {
                time = ParseDurationMinutes(row[timeColumn], columns.TimeUnit);
            }

            bool? ramp = null;
            if (columns.Ramp >= 0 && columns.Ramp < row.Length)
            {
                ramp = !HoldWords.Any(w => row[columns.Ramp].ToLowerInvariant().Contains(w));
            }

            points.Add((time, temp, hum, ramp));
        }

        if (points.Count == 0)
        {
            throw new FormatException("Nenašli sa žiadne platné dátové riadky.");
        }

        BuildSegments(profile, points, columns.IsTimeline, warnings);
        AdaptToKind(profile, kind, warnings);

        string shape = columns.IsTimeline ? "časová os setpointov" : "tabuľka segmentov";
        string desc = $"Vötsch/SIMPATI export ({shape}, oddeľovač '{DescribeDelimiter(delimiter)}')";
        return new ProfileImportResult(profile, desc, warnings);
    }

    private static void BuildSegments(
        TestProfile profile,
        List<(double time, double temp, double? hum, bool? ramp)> points,
        bool timeline,
        List<string> warnings)
    {
        if (timeline)
        {
            // Points are cumulative times; each pair of points is a ramp segment.
            for (int i = 1; i < points.Count; i++)
            {
                double durationMinutes = points[i].time - points[i - 1].time;
                if (durationMinutes <= 0)
                {
                    warnings.Add($"Bod {i + 1}: nekladné trvanie, použijem 1 min.");
                    durationMinutes = 1;
                }

                profile.Segments.Add(new ProfileSegment
                {
                    Name = $"Segment {i}",
                    TargetTemperature = points[i].temp,
                    TargetHumidity = points[i].hum,
                    Duration = TimeSpan.FromMinutes(durationMinutes),
                    IsRamp = points[i].ramp ?? true,
                });
            }

            if (profile.Segments.Count == 0)
            {
                warnings.Add("Časová os mala iba jeden bod – vytvorený jeden plato segment.");
                profile.Segments.Add(new ProfileSegment
                {
                    Name = "Plato",
                    TargetTemperature = points[0].temp,
                    TargetHumidity = points[0].hum,
                    Duration = TimeSpan.FromMinutes(10),
                    IsRamp = false,
                });
            }
        }
        else
        {
            int index = 1;
            foreach ((double time, double temp, double? hum, bool? ramp) in points)
            {
                double durationMinutes = time > 0 ? time : 10;
                profile.Segments.Add(new ProfileSegment
                {
                    Name = $"Segment {index++}",
                    TargetTemperature = temp,
                    TargetHumidity = hum,
                    Duration = TimeSpan.FromMinutes(durationMinutes),
                    IsRamp = ramp ?? true,
                });
            }
        }
    }

    private static void AdaptToKind(TestProfile profile, ChamberKind kind, List<string> warnings)
    {
        profile.Kind = kind;
        if (kind == ChamberKind.TemperatureOnly && profile.Segments.Any(s => s.TargetHumidity is not null))
        {
            foreach (ProfileSegment segment in profile.Segments)
            {
                segment.TargetHumidity = null;
            }

            warnings.Add("Komora je len teplotná – vlhkostné hodnoty boli ignorované.");
        }
    }

    private static ColumnMap MapColumns(string[] header)
    {
        var map = new ColumnMap();
        for (int i = 0; i < header.Length; i++)
        {
            string h = header[i].Trim().ToLowerInvariant();
            if (h.Length == 0)
            {
                continue;
            }

            if (map.Temperature < 0 && TemperatureKeys.Any(h.Contains))
            {
                map.Temperature = i;
                map.TimeUnit = DetectUnit(h, map.TimeUnit);
            }
            else if (map.Humidity < 0 && HumidityKeys.Any(h.Contains))
            {
                map.Humidity = i;
            }
            else if (map.Duration < 0 && DurationKeys.Any(h.Contains))
            {
                map.Duration = i;
                map.TimeUnit = DetectUnit(h, map.TimeUnit);
            }
            else if (map.Time < 0 && TimeKeys.Any(h.Contains))
            {
                map.Time = i;
                map.TimeUnit = DetectUnit(h, map.TimeUnit);
            }
            else if (map.Ramp < 0 && RampKeys.Any(h.Contains))
            {
                map.Ramp = i;
            }
        }

        // Prefer an explicit per-segment duration; fall back to a cumulative timeline.
        map.IsTimeline = map.Duration < 0 && map.Time >= 0;
        return map;
    }

    private static TimeUnit DetectUnit(string header, TimeUnit current)
    {
        if (header.Contains("[s]") || header.Contains("sec") || header.Contains("sek")) return TimeUnit.Seconds;
        if (header.Contains("[h]") || header.Contains("hour") || header.Contains("std")) return TimeUnit.Hours;
        if (header.Contains("[min]") || header.Contains("min")) return TimeUnit.Minutes;
        return current;
    }

    private static char DetectDelimiter(List<string> lines)
    {
        string sample = lines[0];
        int semicolons = sample.Count(c => c == ';');
        int tabs = sample.Count(c => c == '\t');
        int commas = sample.Count(c => c == ',');

        if (semicolons > 0 && semicolons >= tabs) return ';';
        if (tabs > 0) return '\t';
        if (commas > 0) return ',';
        return ';';
    }

    private static string DescribeDelimiter(char d) => d switch { '\t' => "tab", _ => d.ToString() };

    private static bool TryGetDouble(string[] row, int column, char delimiter, out double value)
    {
        value = 0;
        if (column < 0 || column >= row.Length)
        {
            return false;
        }

        return TryParseDouble(row[column], delimiter, out value);
    }

    private static bool LooksNumeric(string cell, char delimiter) =>
        TryParseDouble(cell, delimiter, out _);

    private static bool TryParseDouble(string raw, char delimiter, out double value)
    {
        string s = raw.Trim().Trim('"');
        if (s.Length == 0)
        {
            value = 0;
            return false;
        }

        // German style "1.234,5" or "23,5": when comma is the decimal mark.
        if (delimiter != ',' && s.Contains(','))
        {
            s = s.Replace(".", string.Empty).Replace(',', '.');
        }

        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static double ParseDurationMinutes(string raw, TimeUnit unit)
    {
        string s = raw.Trim().Trim('"');
        if (s.Contains(':'))
        {
            string[] parts = s.Split(':');
            double[] v = parts.Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : 0).ToArray();
            return v.Length switch
            {
                3 => v[0] * 60 + v[1] + v[2] / 60.0,   // h:m:s
                2 => v[0] * 60 + v[1],                  // h:m
                _ => v.Length > 0 ? v[0] : 0,
            };
        }

        if (!TryParseDouble(s, ';', out double number))
        {
            return 0;
        }

        return unit switch
        {
            TimeUnit.Seconds => number / 60.0,
            TimeUnit.Hours => number * 60.0,
            _ => number,
        };
    }

    private enum TimeUnit
    {
        Minutes,
        Seconds,
        Hours,
    }

    private sealed class ColumnMap
    {
        public int Duration { get; set; } = -1;
        public int Time { get; set; } = -1;
        public int Temperature { get; set; } = -1;
        public int Humidity { get; set; } = -1;
        public int Ramp { get; set; } = -1;
        public bool IsTimeline { get; set; }
        public TimeUnit TimeUnit { get; set; } = TimeUnit.Minutes;
    }
}
