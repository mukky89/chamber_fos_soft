using System.Globalization;
using System.Text;
using System.Text.Json;

namespace VotschVc3.Core.Calibration;

/// <summary>Legacy PeakLogger WLN wire format. Tabs and CRLF are part of the contract.</summary>
public static class CalibrationWlFormat
{
    private static readonly CultureInfo Sk = CultureInfo.GetCultureInfo("sk-SK");
    public static readonly string[] Channels = Enumerable.Range(1, 4)
        .SelectMany(bank => Enumerable.Range(1, 4).Select(channel => $"{bank}.{channel}")).ToArray();

    public static string Header(string interrogator, string reference, IReadOnlyList<string> channelSerials, IReadOnlyList<string> peakSerials)
    {
        if (channelSerials.Count != 16) throw new ArgumentException("WLN requires 16 channels.");
        foreach (string field in channelSerials.Concat(peakSerials).Append(interrogator).Append(reference))
            ValidateField(field);
        return $"Interrogator SN:\t{interrogator}\tTemplog SN:\t{reference}\tLog type:\tWLN\r\n" +
            "Timestamp\tTemperature\t" + string.Join('\t', Channels.Select(c => "# CH " + c)) + "\t\r\n" +
            "SN\t\t" + string.Join('\t', channelSerials.Concat(peakSerials)) + "\t\r\n";
    }

    public static string Row(DateTimeOffset timestamp, double reference, IReadOnlyList<int> counts, IReadOnlyList<double> wavelengths)
    {
        if (counts.Count != 16 || counts.Any(c => c < 0) || counts.Sum() != wavelengths.Count ||
            !double.IsFinite(reference) || wavelengths.Any(w => !double.IsFinite(w) || w <= 0))
            throw new ArgumentException("Incomplete WLN sample.");
        return timestamp.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss.fffff", CultureInfo.InvariantCulture) + "\t" +
            reference.ToString("0.000", Sk) + "\t" + string.Join('\t', counts) + "\t" +
            string.Join('\t', wavelengths.Select(w => w.ToString("0.00000", Sk))) + "\t\r\n";
    }

    private static void ValidateField(string value)
    {
        if (value.IndexOfAny(['\t', '\r', '\n']) >= 0)
            throw new ArgumentException("WLN identity contains a tab or newline.");
    }
}

/// <summary>One fixed-layout WLN file per interrogator; uses existing acquisition batches only.</summary>
public sealed class CalibrationWlLog : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<DeviceFile> _files = [];
    private readonly Action<string> _diagnostic;
    private bool _disposed;
    private string? _lastProblem;
    private readonly DateTimeOffset _startedAt;
    public IReadOnlyList<string> Paths => _files.Select(f => f.Path).ToArray();

    public CalibrationWlLog(string directory, CalibrationRunRecord run, CalibrationSetup setup, Action<string> diagnostic)
    {
        _diagnostic = diagnostic;
        _startedAt = run.StartedAt;
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var device in setup.ActiveMappings.Where(m => m.Selected).GroupBy(m => m.SourceDeviceSerialNumber, StringComparer.OrdinalIgnoreCase))
            {
                var peaks = device.OrderBy(m => Array.IndexOf(CalibrationWlFormat.Channels, m.Channel)).ThenBy(m => m.PeakIndex).ToArray();
                if (peaks.Any(m => !CalibrationWlFormat.Channels.Contains(m.Channel)) ||
                    peaks.Select(m => m.SourceIdentity).Distinct(StringComparer.OrdinalIgnoreCase).Count() != peaks.Length ||
                    peaks.GroupBy(m => m.Channel).Any(g => g.Select(m => m.PeakIndex).Distinct().Count() != g.Count()))
                    throw new InvalidOperationException("WLN: nejednoznačné mapovanie kanálov/peakov.");
                string[] channelSerials = CalibrationWlFormat.Channels.Select(channel =>
                {
                    var channelMappings = setup.ActiveMappings.Where(m => string.Equals(m.SourceDeviceSerialNumber, device.Key, StringComparison.OrdinalIgnoreCase) && m.Channel == channel).ToArray();
                    string[] serials = channelMappings.Select(m => string.IsNullOrWhiteSpace(m.ChannelSerialNumber) ? m.SerialNumber : m.ChannelSerialNumber)
                        .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
                    return serials.Length == 1 ? serials[0] : "NOSN";
                }).ToArray();
                string reference = string.IsNullOrWhiteSpace(run.ReferenceThermometerSerialNumber) ? run.ReferenceThermometerPort : run.ReferenceThermometerSerialNumber;
                string header = CalibrationWlFormat.Header(device.Key, reference, channelSerials, peaks.Select(m => m.SerialNumber).ToArray());
                string safeDevice = string.Concat(device.Key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
                string filename = $"{run.StartedAt.LocalDateTime:ddMMyyyy_HHmmss}_{run.RunId:N}_{safeDevice}_WL.txt";
                string path = System.IO.Path.Combine(directory, filename);
                // Persist identity as well as labels: equal SNs alone cannot identify individual peaks.
                string layout = JsonSerializer.Serialize(new { Header = header, Sources = peaks.Select(m => m.SourceIdentity).ToArray(), Indices = peaks.Select(m => m.PeakIndex).ToArray() });
                string manifest = path + ".layout.json";
                if (File.Exists(path) && new FileInfo(path).Length > 0 && (!File.Exists(manifest) || File.ReadAllText(manifest) != layout))
                    throw new InvalidOperationException("WLN: uložené mapovanie sa líši od obnoveného behu; existujúci log zostal zachovaný.");
                if (!File.Exists(manifest)) File.WriteAllText(manifest, layout, new UTF8Encoding(false));
                DateTimeOffset last = RecoverAndReadLastTimestamp(path, header);
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read,
                    4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
                stream.Seek(0, SeekOrigin.End);
                if (stream.Length == 0)
                {
                    stream.Write(Encoding.UTF8.GetBytes(header));
                    stream.Flush(true);
                }
                _files.Add(new DeviceFile(path, device.Key, peaks.Select(m => m.SourceIdentity).ToArray(),
                    peaks.Select(m => m.PeakIndex).ToArray(), CalibrationWlFormat.Channels.Select(c => peaks.Count(m => m.Channel == c)).ToArray(), stream, last));
            }
        }
        catch { foreach (var file in _files) file.Stream.Dispose(); throw; }
    }

    public async Task AppendAsync(IReadOnlyList<PeakLoggerMeasurement> batch, double? reference,
        DateTimeOffset? referenceAt, int intervalSeconds, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            foreach (DeviceFile file in _files)
            {
                if (file.Faulted) continue;
                var readings = batch.Where(m => string.Equals(m.SerialNumber, file.Device, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(m => $"{m.SerialNumber}|{m.Channel}|{m.PeakId}", StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
                if (file.Sources.Any(s => !readings.TryGetValue(s, out var found) || found.Length != 1))
                { Problem("WLN_SAMPLE_SKIPPED: chýba peak alebo je zdroj duplicitný."); continue; }
                var ordered = file.Sources.Select(s => readings[s][0]).ToArray();
                DateTimeOffset timestamp = ordered.Max(m => m.Timestamp);
                if (timestamp <= file.Last || timestamp - file.Last < TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 1, 86400))) continue;
                if (reference is not double temperature || !double.IsFinite(temperature) || referenceAt is null ||
                    referenceAt < _startedAt || referenceAt <= file.LastReference || timestamp < _startedAt ||
                    // PeakLogger batches may arrive after the reference read in the
                    // same acquisition cycle. Allow that bounded transport skew;
                    // the independent absolute-age check below still rejects stale WIKA.
                    (timestamp - referenceAt.Value).Duration() > TimeSpan.FromSeconds(15) ||
                    DateTimeOffset.UtcNow - timestamp > TimeSpan.FromSeconds(10) || timestamp - DateTimeOffset.UtcNow > TimeSpan.FromSeconds(10) ||
                    ordered.Any(m => timestamp - m.Timestamp > TimeSpan.FromSeconds(1) || !double.IsFinite(m.WavelengthNm) || m.WavelengthNm <= 0) ||
                    ordered.Where((m, i) => m.PeakIndex != file.Indices[i]).Any())
                { Problem("WLN_SAMPLE_SKIPPED: neplatná/stará WIKA, nesúčasné dáta alebo zmena indexu peaku."); continue; }
                string line = CalibrationWlFormat.Row(timestamp, temperature, file.Counts, ordered.Select(m => m.WavelengthNm).ToArray());
                // Once accepted, finish the complete row even when STOP cancels the caller.
                try
                {
                    await file.Stream.WriteAsync(Encoding.UTF8.GetBytes(line), CancellationToken.None).ConfigureAwait(false);
                    await file.Stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    file.Faulted = true;
                    Problem("WLN_WRITE_FAILED: zápis zastavený, existujúce dáta zostali zachované; " + ex.Message);
                    continue;
                }
                file.Last = timestamp;
                file.LastReference = referenceAt.Value;
                _lastProblem = null;
            }
        }
        finally { _gate.Release(); }
    }

    private void Problem(string message)
    {
        if (_lastProblem == message) return;
        _lastProblem = message;
        _diagnostic(message);
    }

    private DateTimeOffset RecoverAndReadLastTimestamp(string path, string header)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0) return DateTimeOffset.MinValue;
        byte[] expected = Encoding.UTF8.GetBytes(header);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        byte[] actual = new byte[expected.Length];
        if (stream.Read(actual) != actual.Length || !actual.SequenceEqual(expected))
            throw new InvalidOperationException("WLN: hlavička existujúceho logu nie je kompatibilná.");
        long tailStart = Math.Max(expected.Length, stream.Length - 1024 * 1024);
        stream.Position = tailStart;
        byte[] tail = new byte[checked((int)(stream.Length - tailStart))];
        stream.ReadExactly(tail);
        int end = tail.Length;
        while (end > 0 && !(end >= 2 && tail[end - 2] == 13 && tail[end - 1] == 10)) end--;
        if (end == 0 && tailStart > expected.Length)
            throw new InvalidOperationException("WLN: poškodený koniec presahuje bezpečný limit obnovy; súbor zostal zachovaný.");
        if (end < tail.Length)
        {
            string backup = path + $".partial-{Guid.NewGuid():N}.bin";
            File.WriteAllBytes(backup, tail[end..]);
            stream.SetLength(tailStart + end);
            _diagnostic("WLN_TAIL_RECOVERED: neúplný posledný riadok zachovaný v " + backup);
        }
        if (end == 0) return DateTimeOffset.MinValue;
        int start = end - 3;
        while (start >= 0 && tail[start] != 10) start--;
        string lastLine = Encoding.UTF8.GetString(tail, start + 1, end - start - 1);
        if (!DateTimeOffset.TryParseExact(lastLine.Split('\t')[0], "dd.MM.yyyy HH:mm:ss.fffff", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var last)) throw new InvalidOperationException("WLN: poškodený posledný úplný riadok.");
        return last;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var file in _files) await file.Stream.DisposeAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private sealed class DeviceFile(string path, string device, string[] sources, int[] indices, int[] counts, FileStream stream, DateTimeOffset last)
    {
        public string Path = path;
        public string Device = device;
        public string[] Sources = sources;
        public int[] Indices = indices;
        public int[] Counts = counts;
        public FileStream Stream = stream;
        public DateTimeOffset Last = last;
        public DateTimeOffset LastReference = last;
        public bool Faulted;
    }
}
