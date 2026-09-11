using System.Globalization;
using System.Text;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.Core.Recording;

public sealed record ExternalPeakKey(string Device, string Channel, int Index)
{
    public bool Matches(PeakLoggerMeasurement sample) =>
        string.Equals(Device, sample.SerialNumber, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Channel, sample.Channel, StringComparison.OrdinalIgnoreCase) && Index == sample.PeakIndex;
    public override string ToString() => $"{Device}/{Channel}/P{Index}";
}

public sealed record ExternalHumiditySample(DateTimeOffset RecordedAt, DateTimeOffset? TestoReceivedAt,
    Testo645Reading? Testo, IReadOnlyList<PeakLoggerMeasurement> Peaks,
    DateTimeOffset? ApiReceivedAt, string Status);

/// <summary>
/// Append-only, session-scoped TSV. Receipt timestamps are software timestamps, not instrument
/// acquisition times. The existing PeakLogger adapter exposes no instrument source timestamp.
/// </summary>
public sealed class ExternalHumidityLog : IAsyncDisposable
{
    private readonly StreamWriter _writer;
    private readonly ExternalPeakKey[] _peaks;
    private readonly TimeSpan _maxAge;
    public string LastDataStatus { get; private set; } = "";
    private ExternalHumidityLog(StreamWriter writer, ExternalPeakKey[] peaks, TimeSpan maxAge)
        => (_writer, _peaks, _maxAge) = (writer, peaks, maxAge);

    public static async Task<ExternalHumidityLog> OpenAsync(string path, bool append, Guid chamberId,
        string port, string api, IEnumerable<ExternalPeakKey> peaks, TimeSpan maxAge)
    {
        if (maxAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxAge));
        var selected = peaks.Distinct().OrderBy(x => x.Device, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Channel, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Index).ToArray();
        var stream = new FileStream(path, append ? FileMode.Open : FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.Asynchronous);
        StreamWriter? writer = null;
        try
        {
            Encoding encoding = new UTF8Encoding(false);
            bool hasBytes = stream.Length > 0;
            if (hasBytes)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true);
                await reader.ReadAsync(new char[1]);
                encoding = reader.CurrentEncoding;
            }
            stream.Seek(0, SeekOrigin.End);
            writer = new StreamWriter(stream, encoding) { AutoFlush = true };
            if (hasBytes) await writer.WriteLineAsync();
            await writer.WriteLineAsync($"# Testo645 session {Guid.NewGuid():N}; {DateTimeOffset.Now:O}; chamber={chamberId}; COM={Clean(port)}; API={Clean(api)}");
            await writer.WriteLineAsync($"# UTF/BOM-detected append; software receipt times; hardware source times unavailable; NOT synchronized; max_age_and_skew_s={F(maxAge.TotalSeconds)}; signed temperatures/error codes/checksum unverified; accepted T=0..200 C, RH=0..100 %RH");
            var columns = new List<string> { "Testo received [ISO8601]", "Testo humidity [%RH]", "Testo temperature [C]" };
            columns.AddRange(selected.Select(x => Clean(x.ToString()) + " [nm]"));
            columns.AddRange(["API received [ISO8601]", "Data status", "Row time [ISO8601]", "Testo age [s]", "API age [s]", "Receipt skew [s]", "Testo source time", "API source time"]);
            await writer.WriteLineAsync(string.Join('\t', columns));
            return new ExternalHumidityLog(writer, selected, maxAge);
        }
        catch { if (writer is not null) await writer.DisposeAsync(); else await stream.DisposeAsync(); throw; }
    }

    public string FormatRow(ExternalHumiditySample sample)
    {
        double? testoAge = Age(sample.RecordedAt, sample.TestoReceivedAt);
        double? apiAge = Age(sample.RecordedAt, sample.ApiReceivedAt);
        double? skew = sample.TestoReceivedAt.HasValue && sample.ApiReceivedAt.HasValue
            ? Math.Abs((sample.TestoReceivedAt.Value - sample.ApiReceivedAt.Value).TotalSeconds) : null;
        bool freshTesto = testoAge is >= 0 && testoAge <= _maxAge.TotalSeconds;
        bool freshApi = apiAge is >= 0 && apiAge <= _maxAge.TotalSeconds;
        var states = new List<string> { sample.Status };
        if (sample.Testo is null) states.Add("TESTO_UNAVAILABLE");
        else states.Add(sample.Testo.Status);
        if (!freshTesto) states.Add("TESTO_STALE_OR_MISSING");
        if (_peaks.Length > 0 && !freshApi) states.Add("API_STALE_OR_MISSING");
        if (skew > _maxAge.TotalSeconds) states.Add("RECEIPT_SKEW_EXCEEDED");
        var values = new List<string> { T(sample.TestoReceivedAt), F(freshTesto ? sample.Testo?.HumidityPercent : null), F(freshTesto ? sample.Testo?.TemperatureC : null) };
        foreach (var peak in _peaks)
        {
            var matches = sample.Peaks.Where(peak.Matches).ToArray();
            var found = matches.Length == 1 ? matches[0] : null;
            bool valid = freshApi && found is not null && double.IsFinite(found.WavelengthNm) && found.WavelengthNm > 0 &&
                Age(sample.RecordedAt, found.Timestamp) is double age && age >= 0 && age <= _maxAge.TotalSeconds;
            values.Add(F(valid ? found!.WavelengthNm : null));
            if (!valid) states.Add("MISSING_INVALID_OR_STALE_PEAK:" + peak);
        }
        LastDataStatus = Clean(string.Join(";", states.Where(s => !string.IsNullOrWhiteSpace(s))));
        values.AddRange([T(sample.ApiReceivedAt), LastDataStatus,
            T(sample.RecordedAt), F(testoAge), F(apiAge), F(skew), "NA", "NA"]);
        return string.Join('\t', values);
    }

    public Task AppendAsync(ExternalHumiditySample sample) => _writer.WriteLineAsync(FormatRow(sample));
    private static double? Age(DateTimeOffset now, DateTimeOffset? time) => time.HasValue ? (now - time.Value).TotalSeconds : null;
    private static string T(DateTimeOffset? time) => time?.ToString("O", CultureInfo.InvariantCulture) ?? "NA";
    private static string F(double? value) => value.HasValue && double.IsFinite(value.Value) ? value.Value.ToString("0.#########", CultureInfo.InvariantCulture) : "NA";
    private static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' ');
    public ValueTask DisposeAsync() => _writer.DisposeAsync();
}
