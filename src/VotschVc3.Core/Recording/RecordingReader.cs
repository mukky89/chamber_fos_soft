using System.Globalization;

namespace VotschVc3.Core.Recording;

/// <summary>One numeric column of a recording, with summary statistics.</summary>
public sealed class RecordingSeries
{
    public RecordingSeries(string name, IReadOnlyList<double?> values)
    {
        Name = name;
        Values = values;

        double[] present = values.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        Count = present.Length;
        if (Count > 0)
        {
            Min = present.Min();
            Max = present.Max();
            Mean = present.Average();
        }
    }

    public string Name { get; }
    public IReadOnlyList<double?> Values { get; }
    public int Count { get; }
    public double? Min { get; }
    public double? Max { get; }
    public double? Mean { get; }
}

/// <summary>A parsed recording: a time axis plus one or more numeric series.</summary>
public sealed class RecordingData
{
    public RecordingData(IReadOnlyList<DateTimeOffset> timestamps, IReadOnlyList<RecordingSeries> series)
    {
        Timestamps = timestamps;
        Series = series;
    }

    public IReadOnlyList<DateTimeOffset> Timestamps { get; }
    public IReadOnlyList<RecordingSeries> Series { get; }

    public int RowCount => Timestamps.Count;
}

/// <summary>
/// Reads a recording CSV produced by <see cref="CsvRecorder"/> or
/// <see cref="ThermometerCsvRecorder"/> back into a <see cref="RecordingData"/>
/// for plotting and statistics. Text columns (Digital, Raw, Unit) are ignored.
/// </summary>
public static class RecordingReader
{
    private static readonly HashSet<string> TextColumns =
        new(StringComparer.OrdinalIgnoreCase) { "Timestamp", "Digital", "Raw", "Unit" };

    public static RecordingData Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string[] lines = File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        if (lines.Length < 2)
        {
            throw new FormatException("Súbor neobsahuje žiadne dáta.");
        }

        char delimiter = lines[0].Contains(';') ? ';' : ',';
        string[] header = lines[0].Split(delimiter);
        int timestampIndex = Array.FindIndex(header, h => h.Trim().Equals("Timestamp", StringComparison.OrdinalIgnoreCase));
        if (timestampIndex < 0)
        {
            timestampIndex = 0;
        }

        var timestamps = new List<DateTimeOffset>();
        var columns = new List<double?>[header.Length];
        for (int i = 0; i < columns.Length; i++)
        {
            columns[i] = new List<double?>();
        }

        for (int r = 1; r < lines.Length; r++)
        {
            string[] cells = lines[r].Split(delimiter);
            timestamps.Add(ParseTimestamp(cells.ElementAtOrDefault(timestampIndex)));

            for (int c = 0; c < header.Length; c++)
            {
                columns[c].Add(ParseDouble(cells.ElementAtOrDefault(c)));
            }
        }

        var series = new List<RecordingSeries>();
        for (int c = 0; c < header.Length; c++)
        {
            string name = header[c].Trim();
            if (c == timestampIndex || TextColumns.Contains(name))
            {
                continue;
            }

            if (columns[c].Any(v => v.HasValue))
            {
                series.Add(new RecordingSeries(name, columns[c]));
            }
        }

        return new RecordingData(timestamps, series);
    }

    private static DateTimeOffset ParseTimestamp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DateTimeOffset.MinValue;
        }

        if (DateTimeOffset.TryParseExact(text.Trim(), "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset exact))
        {
            return exact;
        }

        return DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset any)
            ? any
            : DateTimeOffset.MinValue;
    }

    private static double? ParseDouble(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string s = text.Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
    }
}
