using System.Globalization;
using System.Text;

namespace VotschVc3.Core.Recording;

/// <summary>
/// Per-profile temperature log: one CSV file per profile run capturing the set point
/// temperature and the measured chamber temperature (plus humidity for climate
/// chambers) over time. Files are written to a folder under the user's Documents so
/// every run leaves an auditable record. Best-effort and thread-safe; logging must
/// never break chamber control, so write failures are swallowed.
/// </summary>
public sealed class ProfileTemperatureLog : IDisposable
{
    private readonly object _sync = new();
    private StreamWriter? _writer;

    /// <summary>
    /// Creates the log file <c>yyyyMMdd_HHmmss_&lt;profil&gt;.csv</c> in <paramref name="directory"/>
    /// and writes the header. On any I/O error the instance stays usable but inert.
    /// </summary>
    public ProfileTemperatureLog(string directory, string profileName, string chamberName, bool humidity, DateTime start)
    {
        Humidity = humidity;
        try
        {
            Directory.CreateDirectory(directory);
            string safeProfile = Sanitize(profileName);
            FilePath = Path.Combine(directory, $"{start:yyyyMMdd_HHmmss}_{safeProfile}.csv");

            _writer = new StreamWriter(FilePath, append: false, Encoding.UTF8) { AutoFlush = true };
            _writer.WriteLine($"# Profil: {profileName}");
            _writer.WriteLine($"# Zariadenie: {chamberName}");
            _writer.WriteLine($"# Spustené: {start:yyyy-MM-dd HH:mm:ss}");
            _writer.WriteLine(humidity
                ? "Čas;Setpoint °C;Teplota komory °C;Setpoint %;Vlhkosť %"
                : "Čas;Setpoint °C;Teplota komory °C");
        }
        catch
        {
            _writer = null;
        }
    }

    /// <summary>Absolute path of the log file (empty if it could not be created).</summary>
    public string FilePath { get; } = string.Empty;

    /// <summary>Whether humidity columns are logged.</summary>
    public bool Humidity { get; }

    /// <summary>Rows written so far.</summary>
    public long RowCount { get; private set; }

    /// <summary>Appends one timestamped row with the set point and measured values.</summary>
    public void Log(DateTime timestamp, double setpoint, double? measured, double? humiditySetpoint = null, double? measuredHumidity = null)
    {
        if (_writer is null)
        {
            return;
        }

        string row = Humidity
            ? string.Join(';',
                timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Fmt(setpoint), Fmt(measured), Fmt(humiditySetpoint), Fmt(measuredHumidity))
            : string.Join(';',
                timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Fmt(setpoint), Fmt(measured));

        try
        {
            lock (_sync)
            {
                _writer.WriteLine(row);
                RowCount++;
            }
        }
        catch
        {
            // Never let a logging failure disturb the running profile.
        }
    }

    private static string Fmt(double? value) => value?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        name = name.Trim();
        return string.IsNullOrEmpty(name) ? "profil" : (name.Length > 80 ? name[..80] : name);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch
            {
                // ignore
            }

            _writer = null;
        }
    }
}
