using System.Globalization;
using System.Text;
using VotschVc3.Core.Protocol;

namespace VotschVc3.Core.Recording;

/// <summary>
/// Appends chamber readings to a CSV file for long term recording. Each call to
/// <see cref="Record"/> writes one timestamped row; the header is written once
/// when the file is created. Thread safe.
/// </summary>
public sealed class CsvRecorder : IDisposable
{
    private const string Header =
        "Timestamp;Temperature;TemperatureSetpoint;Humidity;HumiditySetpoint;Digital;Raw";

    private readonly object _sync = new();
    private readonly StreamWriter _writer;

    /// <summary>Opens (or creates) the target file for appending.</summary>
    public CsvRecorder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FilePath = Path.GetFullPath(path);

        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        bool isNew = !File.Exists(FilePath) || new FileInfo(FilePath).Length == 0;
        _writer = new StreamWriter(FilePath, append: true, Encoding.UTF8) { AutoFlush = true };
        if (isNew)
        {
            _writer.WriteLine(Header);
        }
    }

    /// <summary>Absolute path of the file being written.</summary>
    public string FilePath { get; }

    /// <summary>Number of data rows written by this instance.</summary>
    public long RowCount { get; private set; }

    /// <summary>Appends one reading as a CSV row.</summary>
    public void Record(ChamberReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        string row = string.Join(';',
            reading.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            Format(reading.Temperature),
            Format(reading.TemperatureSetpoint),
            Format(reading.Humidity),
            Format(reading.HumiditySetpoint),
            reading.DigitalChannels.ToProtocolString(),
            Escape(reading.Raw));

        lock (_sync)
        {
            _writer.WriteLine(row);
            RowCount++;
        }
    }

    private static string Format(double? value) =>
        value?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Escape(string raw)
    {
        // Replace the field separator so a stray ';' in a raw frame cannot shift columns.
        string sanitized = raw.Replace(';', ',').Replace("\r", string.Empty).Replace("\n", string.Empty);
        return sanitized;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            _writer.Flush();
            _writer.Dispose();
        }
    }
}
