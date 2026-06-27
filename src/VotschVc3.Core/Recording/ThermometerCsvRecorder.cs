using System.Globalization;
using System.Text;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.Core.Recording;

/// <summary>Appends ASL F100 thermometer readings to a CSV file. Thread safe.</summary>
public sealed class ThermometerCsvRecorder : IDisposable
{
    private const string Header = "Timestamp;Temperature;Unit;Raw";

    private readonly object _sync = new();
    private readonly StreamWriter _writer;

    public ThermometerCsvRecorder(string path)
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

    public string FilePath { get; }

    public long RowCount { get; private set; }

    public void Record(ThermometerReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        string row = string.Join(';',
            reading.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            reading.Temperature?.ToString("0.000", CultureInfo.InvariantCulture) ?? string.Empty,
            reading.Unit,
            reading.Raw.Replace(';', ',').Replace("\r", string.Empty).Replace("\n", string.Empty));

        lock (_sync)
        {
            _writer.WriteLine(row);
            RowCount++;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer.Flush();
            _writer.Dispose();
        }
    }
}
