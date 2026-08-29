using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Calibration;

public sealed class CalibrationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public CalibrationStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
        SetupsDirectory = Path.Combine(RootDirectory, "Setups");
        RunsDirectory = Path.Combine(RootDirectory, "Runs");
        CheckpointsDirectory = Path.Combine(RootDirectory, "Checkpoints");
        Directory.CreateDirectory(SetupsDirectory);
        Directory.CreateDirectory(RunsDirectory);
        Directory.CreateDirectory(CheckpointsDirectory);
    }

    public string RootDirectory { get; }
    public string SetupsDirectory { get; }
    public string RunsDirectory { get; }
    public string CheckpointsDirectory { get; }

    public void SaveSetup(CalibrationSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        File.WriteAllText(SetupPath(setup.ProfileId, setup.ChamberId), JsonSerializer.Serialize(setup, JsonOptions));
    }

    public CalibrationSetup? LoadSetup(Guid profileId)
    {
        string path = SetupPath(profileId, Guid.Empty);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<CalibrationSetup>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public CalibrationSetup? LoadSetup(Guid profileId, Guid chamberId)
    {
        string path = SetupPath(profileId, chamberId);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<CalibrationSetup>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public CalibrationRunWriter CreateRunWriter(CalibrationRunRecord run) => new(this, run);

    public void SaveRun(CalibrationRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        string dir = RunDirectory(run.RunId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "summary.json"), JsonSerializer.Serialize(run, JsonOptions));
        ExportSummaryCsv(run, Path.Combine(dir, "summary.csv"));
    }

    public List<CalibrationRunRecord> LoadHistory()
    {
        if (!Directory.Exists(RunsDirectory)) return new();
        var result = new List<CalibrationRunRecord>();
        foreach (string file in Directory.EnumerateFiles(RunsDirectory, "summary.json", SearchOption.AllDirectories))
        {
            try
            {
                CalibrationRunRecord? run = JsonSerializer.Deserialize<CalibrationRunRecord>(File.ReadAllText(file), JsonOptions);
                if (run is not null) result.Add(run);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // A partially written historical run must not make the history screen fail.
            }
        }

        return result.OrderByDescending(x => x.StartedAt).ToList();
    }

    public void SaveCheckpoint(CalibrationCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        checkpoint.SavedAt = DateTimeOffset.Now;
        File.WriteAllText(CheckpointPath(checkpoint.ChamberId), JsonSerializer.Serialize(checkpoint, JsonOptions));
    }

    public CalibrationCheckpoint? LoadCheckpoint(Guid chamberId)
    {
        string path = CheckpointPath(chamberId);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<CalibrationCheckpoint>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    public void DeleteCheckpoint(Guid chamberId)
    {
        string path = CheckpointPath(chamberId);
        if (File.Exists(path)) File.Delete(path);
    }

    public static void ExportSummaryCsv(CalibrationRunRecord run, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Profile;RunId;ReferenceF100Port;ReferenceF100Serial;ReferenceF100Channel;Plateau;TargetTemperatureC;ActualTemperatureC;ReferenceTemperatureC;SensorSerialNumber;PeakLoggerDeviceSN;Channel;PeakId;PeakIndex;MeanWavelengthNm;MedianWavelengthNm;StdDevPm;MinNm;MaxNm;RangePm;DriftPmPerMinute;StabilizationSeconds;Status;Problem");
        foreach (CalibrationPlateauResult plateau in run.Plateaus)
        {
            foreach (CalibrationMeasurementResult target in plateau.Targets)
            {
                sb.Append(E(run.ProfileName)).Append(';')
                  .Append(run.RunId).Append(';')
                  .Append(E(run.ReferenceThermometerPort)).Append(';')
                  .Append(E(run.ReferenceThermometerSerialNumber)).Append(';')
                  .Append(E(run.ReferenceThermometerChannel)).Append(';')
                  .Append(plateau.PlateauIndex).Append(';')
                  .Append(F(plateau.TargetTemperatureC)).Append(';')
                  .Append(F(plateau.ActualTemperatureC)).Append(';')
                  .Append(plateau.ReferenceTemperatureC is { } rt ? F(rt) : string.Empty).Append(';')
                  .Append(E(target.SerialNumber)).Append(';')
                  .Append(E(target.PeakLoggerDeviceSerialNumber)).Append(';')
                  .Append(E(target.Channel)).Append(';')
                  .Append(E(target.PeakId)).Append(';')
                  .Append(target.PeakIndex).Append(';')
                  .Append(F(target.MeanWavelengthNm)).Append(';')
                  .Append(F(target.MedianWavelengthNm)).Append(';')
                  .Append(F(target.StandardDeviationPm)).Append(';')
                  .Append(F(target.MinWavelengthNm)).Append(';')
                  .Append(F(target.MaxWavelengthNm)).Append(';')
                  .Append(F(target.RangePm)).Append(';')
                  .Append(F(target.DriftPmPerMinute)).Append(';')
                  .Append(F(target.StabilizationTime.TotalSeconds)).Append(';')
                  .Append(target.Status).Append(';')
                  .Append(E(target.Problem ?? string.Empty)).AppendLine();
            }
        }

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    internal string RunDirectory(Guid runId) => Path.Combine(RunsDirectory, runId.ToString("N"));
    private string SetupPath(Guid profileId, Guid chamberId) => chamberId == Guid.Empty
        ? Path.Combine(SetupsDirectory, $"{profileId:N}.json")
        : Path.Combine(SetupsDirectory, $"{chamberId:N}-{profileId:N}.json");
    private string CheckpointPath(Guid chamberId) => Path.Combine(CheckpointsDirectory, $"{chamberId:N}.json");

    private static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    private static string E(string value) => value.Replace(";", ",").Replace("\r", " ").Replace("\n", " ");
}

public sealed class CalibrationRunWriter : IAsyncDisposable
{
    private readonly CalibrationStore _store;
    private readonly CalibrationRunRecord _run;
    private readonly StreamWriter _rawWriter;
    private readonly StreamWriter _wavelengthWriter;
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal CalibrationRunWriter(CalibrationStore store, CalibrationRunRecord run)
    {
        _store = store;
        _run = run;
        string dir = store.RunDirectory(run.RunId);
        Directory.CreateDirectory(dir);
        _rawWriter = new StreamWriter(Path.Combine(dir, "raw-samples.csv"), append: false, Encoding.UTF8);
        _rawWriter.WriteLine("RunId;ProfileId;Plateau;TargetTemperatureC;ActualTemperatureC;ReferenceTemperatureC;Timestamp;SensorSerialNumber;PeakLoggerDeviceSN;Channel;PeakId;PeakIndex;WavelengthNm;Intensity");
        _rawWriter.Flush();

        _wavelengthWriter = new StreamWriter(Path.Combine(dir, "wavelength-trace.csv"), append: false, Encoding.UTF8);
        _wavelengthWriter.WriteLine("RunId;Timestamp;SensorSerialNumber;PeakLoggerDeviceSN;Channel;PeakId;PeakIndex;WavelengthNm;Intensity;ChamberTemperatureC;ReferenceTemperatureC");
        _wavelengthWriter.Flush();
    }

    public async Task AppendAsync(IEnumerable<CalibrationRawSample> samples, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (CalibrationRawSample s in samples)
            {
                string line = string.Join(";", new[]
                {
                    s.RunId.ToString(),
                    s.ProfileId.ToString(),
                    s.PlateauIndex.ToString(CultureInfo.InvariantCulture),
                    F(s.TargetTemperatureC),
                    F(s.ActualTemperatureC),
                    s.ReferenceTemperatureC is { } rt ? F(rt) : string.Empty,
                    s.Timestamp.ToString("O"),
                    E(s.SerialNumber),
                    E(s.PeakLoggerDeviceSerialNumber),
                    E(s.Channel),
                    E(s.PeakId),
                    s.PeakIndex.ToString(CultureInfo.InvariantCulture),
                    F(s.WavelengthNm),
                    s.Intensity is { } intensity ? F(intensity) : string.Empty,
                });
                await _rawWriter.WriteLineAsync(line).ConfigureAwait(false);
            }
            await _rawWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Appends the continuous whole-run trace for every selected FBG peak.</summary>
    public async Task AppendWavelengthTraceAsync(IEnumerable<CalibrationWavelengthTraceSample> samples, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (CalibrationWavelengthTraceSample s in samples)
            {
                string line = string.Join(";", new[]
                {
                    s.RunId.ToString(),
                    s.Timestamp.ToString("O"),
                    E(s.SerialNumber),
                    E(s.PeakLoggerDeviceSerialNumber),
                    E(s.Channel),
                    E(s.PeakId),
                    s.PeakIndex.ToString(CultureInfo.InvariantCulture),
                    F(s.WavelengthNm),
                    s.Intensity is { } intensity ? F(intensity) : string.Empty,
                    s.ChamberTemperatureC is { } chamber ? F(chamber) : string.Empty,
                    s.ReferenceTemperatureC is { } reference ? F(reference) : string.Empty,
                });
                await _wavelengthWriter.WriteLineAsync(line).ConfigureAwait(false);
            }
            await _wavelengthWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void SaveSummary() => _store.SaveRun(_run);

    private static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    private static string E(string value) => value.Replace(";", ",").Replace("\r", " ").Replace("\n", " ");

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _rawWriter.FlushAsync().ConfigureAwait(false);
            await _wavelengthWriter.FlushAsync().ConfigureAwait(false);
            _rawWriter.Dispose();
            _wavelengthWriter.Dispose();
            _store.SaveRun(_run);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
