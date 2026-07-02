using System.Buffers.Binary;
using System.Text;

namespace VotschVc3.Core.Profiles;

/// <summary>
/// Importer for the native Weiss / Vötsch <b>BEdit</b> program files
/// (<c>.b01</c>, <c>.b02</c>, …) written by the S!MPAC / SIMPATI program editor.
/// <para>
/// The format is a proprietary binary and is not publicly documented, so this
/// importer was reverse engineered from real files. A file carries one block per
/// channel ("Temperature", "Humidity", digital channels, …); each program block
/// is a stream of rows on a 4-byte lattice holding IEEE-754 doubles:
/// </para>
/// <list type="bullet">
///   <item><b>ramp row</b> – target value followed 8 bytes later by a duration
///   in seconds;</item>
///   <item><b>hold row</b> – the duration appears alone (the plateau keeps the
///   previous target), or the target is repeated with the duration 32 bytes
///   later;</item>
///   <item><b>tolerance pair</b> – an adjacent <c>-x</c>/<c>+x</c> pair
///   (alarm band), skipped.</item>
/// </list>
/// The decoder is deliberately tolerant: values that are not plausible for the
/// channel (junk from unaligned reads, denormals, header fields) are ignored.
/// Always review the imported profile before running it.
/// </summary>
public static class BEditImporter
{
    private const int MinProgramRegionBytes = 300;
    private const double MinDurationSeconds = 260;
    private const double MaxDurationSeconds = 1_000_000;

    private static readonly string[] BlockLabels =
    {
        "Temperature", "Humidity", "Option", "Start", "Custom", "Cond.protect", "Clarificat",
    };

    /// <summary>One decoded program row of a single channel.</summary>
    private readonly record struct ChannelSegment(double Target, double Seconds, bool IsRamp);

    /// <summary>True when the data starts with the BEdit editor signature.</summary>
    public static bool LooksLikeBEdit(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 64)
        {
            return false;
        }

        // "BEdit" sits right after a 4 byte header in every known file.
        ReadOnlySpan<byte> head = data.AsSpan(0, 32);
        return head.IndexOf("BEdit"u8) >= 0;
    }

    /// <summary>Imports a BEdit program file into a <see cref="TestProfile"/>.</summary>
    public static ProfileImportResult Import(byte[] data, ChamberKind kind)
    {
        ArgumentNullException.ThrowIfNull(data);
        var warnings = new List<string>();

        List<ChannelSegment> temp = ParseBestChannel(data, "Temperature", -90, 250);
        List<ChannelSegment> hum = ParseBestChannel(data, "Humidity", 0, 100);

        if (temp.Count == 0)
        {
            throw new FormatException("V BEdit súbore sa nepodarilo nájsť teplotný program.");
        }

        bool useHumidity = hum.Count > 0 && kind == ChamberKind.TemperatureHumidity;
        if (hum.Count > 0 && kind == ChamberKind.TemperatureOnly)
        {
            warnings.Add("Komora je len teplotná – vlhkostný kanál bol ignorovaný.");
        }

        var profile = new TestProfile
        {
            Name = "BEdit profil",
            Kind = kind,
            Cycles = 1,
        };

        profile.Segments = useHumidity
            ? MergeChannels(temp, hum, warnings)
            : temp.Select((s, i) => new ProfileSegment
            {
                Name = SegmentName(s, i),
                TargetTemperature = s.Target,
                Duration = TimeSpan.FromSeconds(s.Seconds),
                IsRamp = s.IsRamp,
            }).ToList();

        warnings.Add("BEdit je reverzne dekódovaný formát – skontroluj profil pred spustením.");
        return new ProfileImportResult(profile, "Weiss/Vötsch BEdit program (binárny)", warnings);
    }

    private static string SegmentName(ChannelSegment s, int index) =>
        s.IsRamp ? $"Nábeh {s.Target:0.#} °C" : $"Plato {s.Target:0.#} °C";

    /// <summary>
    /// Finds every program region for the given channel label and returns the
    /// longest decoded segment list (short summary blocks decode to nothing).
    /// </summary>
    private static List<ChannelSegment> ParseBestChannel(byte[] data, string label, double loValue, double hiValue)
    {
        List<int> marks = FindBlockMarks(data);
        byte[] labelBytes = Encoding.ASCII.GetBytes(label);
        var best = new List<ChannelSegment>();

        foreach (int mark in marks)
        {
            if (!MatchesAt(data, mark, labelBytes))
            {
                continue;
            }

            int end = marks.FirstOrDefault(m => m > mark, data.Length);
            if (end - mark < MinProgramRegionBytes)
            {
                continue;
            }

            List<ChannelSegment> segments = ParseRegion(data, mark, end, loValue, hiValue);
            if (segments.Count > best.Count)
            {
                best = segments;
            }
        }

        return best;
    }

    private static List<int> FindBlockMarks(byte[] data)
    {
        var marks = new List<int>();
        foreach (string label in BlockLabels)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(label);
            for (int i = 0; i <= data.Length - bytes.Length; i++)
            {
                if (MatchesAt(data, i, bytes))
                {
                    marks.Add(i);
                }
            }
        }

        marks.Sort();
        return marks;
    }

    private static bool MatchesAt(byte[] data, int offset, byte[] pattern)
    {
        if (offset + pattern.Length > data.Length)
        {
            return false;
        }

        for (int i = 0; i < pattern.Length; i++)
        {
            if (data[offset + i] != pattern[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Event-stream decoder for one channel program region. Walks the 4-byte
    /// lattice and recognises ramp rows, hold rows / lone durations and
    /// tolerance pairs; everything else is skipped.
    /// </summary>
    private static List<ChannelSegment> ParseRegion(byte[] data, int lo, int hi, double loValue, double hiValue)
    {
        var segments = new List<ChannelSegment>();
        double? current = null;
        (double Value, int Offset)? pending = null;
        int lastRowEnd = -1;

        bool IsSane(double v) => !double.IsNaN(v) && (v == 0.0 || (Math.Abs(v) >= 0.01 && Math.Abs(v) <= MaxDurationSeconds));
        bool IsTarget(double v) => IsSane(v) && v >= loValue && v <= hiValue;
        bool IsDuration(double v) => IsSane(v) && v >= MinDurationSeconds && v <= MaxDurationSeconds;

        int o = lo + (((3 - lo) % 4) + 4) % 4;
        while (o + 16 <= hi)
        {
            double v = ReadDouble(data, o);
            double v8 = ReadDouble(data, o + 8);

            // Tolerance pair (-x / +x alarm band around the set point).
            if (IsSane(v) && v < 0 && v >= -60 && IsSane(v8) && v8 > 0 && Math.Abs(v8 + v) < 1e-9)
            {
                o += 16;
                continue;
            }

            // Ramp row: target immediately followed by its duration. A zero read
            // just before a hold-row duration belongs to the pending target.
            if (IsTarget(v) && IsDuration(v8))
            {
                double target = v == 0.0 && pending is { } p && o - p.Offset <= 40 ? p.Value : v;
                pending = null;
                bool isRamp = current is not { } c || Math.Abs(target - c) > 1e-9;
                segments.Add(new ChannelSegment(target, v8, isRamp));
                current = target;
                lastRowEnd = o + 16;
                o += 16;
                continue;
            }

            // Lone target: remember it, a later duration may belong to it.
            if (IsTarget(v) && v != 0.0 && !IsDuration(v))
            {
                pending = (v, o);
                o += 8;
                continue;
            }

            // Lone duration close after the previous row: a plateau at the
            // current level (or a ramp to the pending target).
            if (IsDuration(v) && current is { } level && lastRowEnd >= 0 && o - lastRowEnd <= 64)
            {
                if (pending is { } pt && Math.Abs(pt.Value - level) > 1e-9)
                {
                    segments.Add(new ChannelSegment(pt.Value, v, true));
                    current = pt.Value;
                }
                else
                {
                    segments.Add(new ChannelSegment(level, v, false));
                }

                pending = null;
                lastRowEnd = o + 8;
                o += 8;
                continue;
            }

            o += 4;
        }

        TrimTrailingJunkHold(segments);
        return segments;
    }

    /// <summary>
    /// Drops a trailing "plateau" that is really a program-summary field (its
    /// duration is far longer than any real plateau in the program).
    /// </summary>
    private static void TrimTrailingJunkHold(List<ChannelSegment> segments)
    {
        if (segments.Count < 3 || segments[^1].IsRamp)
        {
            return;
        }

        double maxOther = segments.Take(segments.Count - 1)
            .Where(s => !s.IsRamp)
            .Select(s => s.Seconds)
            .DefaultIfEmpty(0)
            .Max();

        if (maxOther > 0 && segments[^1].Seconds > 3 * maxOther)
        {
            segments.RemoveAt(segments.Count - 1);
        }
    }

    /// <summary>
    /// Merges independent temperature and humidity channel programs into one
    /// segment list by taking the union of their breakpoints; within every
    /// interval both values ramp linearly to their interval-end values.
    /// </summary>
    private static List<ProfileSegment> MergeChannels(
        List<ChannelSegment> temp, List<ChannelSegment> hum, List<string> warnings)
    {
        List<(double Time, double Value)> tempLine = BuildPolyline(temp);
        List<(double Time, double Value)> humLine = BuildPolyline(hum);

        double tempTotal = tempLine[^1].Time;
        double humTotal = humLine[^1].Time;
        if (Math.Abs(tempTotal - humTotal) > 1)
        {
            warnings.Add(
                $"Teplotný ({FormatHours(tempTotal)}) a vlhkostný ({FormatHours(humTotal)}) program majú rôznu dĺžku – " +
                "kratší kanál drží poslednú hodnotu.");
        }

        SortedSet<double> times = new() { 0 };
        foreach ((double t, _) in tempLine) times.Add(Math.Round(t));
        foreach ((double t, _) in humLine) times.Add(Math.Round(t));

        var result = new List<ProfileSegment>();
        double prev = 0;
        double prevTemp = ValueAt(tempLine, 0);
        double prevHum = ValueAt(humLine, 0);
        int index = 1;

        foreach (double t in times.Where(t => t > 0.5))
        {
            double tEnd = ValueAt(tempLine, t);
            double hEnd = ValueAt(humLine, t);
            bool changes = Math.Abs(tEnd - prevTemp) > 1e-6 || Math.Abs(hEnd - prevHum) > 1e-6;

            result.Add(new ProfileSegment
            {
                Name = $"Krok {index++} · {tEnd:0.#} °C / {hEnd:0.#} %",
                TargetTemperature = tEnd,
                TargetHumidity = hEnd,
                Duration = TimeSpan.FromSeconds(t - prev),
                IsRamp = changes,
            });

            prev = t;
            prevTemp = tEnd;
            prevHum = hEnd;
        }

        return result;
    }

    /// <summary>Turns channel segments into a cumulative (time, value) polyline.</summary>
    private static List<(double Time, double Value)> BuildPolyline(List<ChannelSegment> segments)
    {
        var line = new List<(double, double)> { (0, segments[0].Target) };
        double t = 0;
        foreach (ChannelSegment s in segments)
        {
            t += s.Seconds;
            line.Add((t, s.Target));
        }

        return line;
    }

    private static double ValueAt(List<(double Time, double Value)> line, double time)
    {
        if (time <= line[0].Time)
        {
            return line[0].Value;
        }

        for (int i = 1; i < line.Count; i++)
        {
            if (time <= line[i].Time)
            {
                (double t0, double v0) = line[i - 1];
                (double t1, double v1) = line[i];
                return t1 <= t0 ? v1 : v0 + (v1 - v0) * (time - t0) / (t1 - t0);
            }
        }

        return line[^1].Value;
    }

    private static string FormatHours(double seconds) => $"{seconds / 3600:0.#} h";

    private static double ReadDouble(byte[] data, int offset) =>
        BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(offset, 8));
}
