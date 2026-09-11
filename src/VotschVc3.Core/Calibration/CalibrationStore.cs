using System.Globalization;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Calibration;

public sealed class CalibrationStore
{
    private static readonly CultureInfo SlovakCulture = CultureInfo.GetCultureInfo("sk-SK");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ConcurrentDictionary<Guid, string> _runDirectories = new();
    private readonly bool _partitionRunsByMonth;
    private readonly string? _replicaDirectory;
    private readonly CalibrationReplicationQueue? _replicationQueue;
    private readonly string[] _additionalHistoryRoots;
    private static readonly string[] MonthFolders = { "01_Januar", "02_Februar", "03_Marec", "04_April", "05_Maj", "06_Jun", "07_Jul", "08_August", "09_September", "10_Oktober", "11_November", "12_December" };

    public CalibrationStore(string rootDirectory, string? runsDirectory = null, string? replicaDirectory = null, CalibrationReplicationQueue? replicationQueue = null, IEnumerable<string>? additionalHistoryRoots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
        SetupsDirectory = Path.Combine(RootDirectory, "Setups");
        LegacyRunsDirectory = Path.Combine(RootDirectory, "Runs");
        RunsDirectory = runsDirectory is null ? LegacyRunsDirectory : Path.GetFullPath(runsDirectory);
        _partitionRunsByMonth = runsDirectory is not null;
        _replicaDirectory = replicaDirectory is null ? null : Path.GetFullPath(replicaDirectory);
        _replicationQueue = replicationQueue;
        _additionalHistoryRoots = (additionalHistoryRoots ?? []).ToArray();
        CheckpointsDirectory = Path.Combine(RootDirectory, "Checkpoints");
        Directory.CreateDirectory(SetupsDirectory);
        Directory.CreateDirectory(LegacyRunsDirectory);
        Directory.CreateDirectory(CheckpointsDirectory);
    }

    public string RootDirectory { get; }
    public string SetupsDirectory { get; }
    public string RunsDirectory { get; }
    public string LegacyRunsDirectory { get; }
    private IEnumerable<string> HistoryRoots => new[] { RunsDirectory, LegacyRunsDirectory }
        .Concat(_replicationQueue?.SourceDirectories ?? []).Concat(_additionalHistoryRoots)
        .Distinct(StringComparer.OrdinalIgnoreCase);
    public string CheckpointsDirectory { get; }

    public void SaveSetup(CalibrationSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        setup.SavedAt = DateTimeOffset.Now;
        WriteRecoveryFile(SetupPath(setup.ProfileId, setup.ChamberId), JsonSerializer.Serialize(setup, JsonOptions));
    }

    public CalibrationSetup? LoadSetup(Guid profileId)
    {
        return ReadRecoveryFile<CalibrationSetup>(SetupPath(profileId, Guid.Empty));
    }

    public string? SaveImportedSetup(CalibrationSetup setup)
    {
        lock (RecoveryFileSync)
        {
            string path = SetupPath(setup.ProfileId, setup.ChamberId);
            string? backup = null;
            if (File.Exists(path))
            {
                string directory = Path.Combine(SetupsDirectory, "ImportBackups");
                Directory.CreateDirectory(directory);
                backup = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(path)}-pred-importom-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
                File.Copy(path, backup, overwrite: false);
            }
            SaveSetup(setup);
            return backup;
        }
    }

    public CalibrationSetup? LoadSetup(Guid profileId, Guid chamberId)
    {
        return ReadRecoveryFile<CalibrationSetup>(SetupPath(profileId, chamberId));
    }

    public CalibrationRunWriter CreateRunWriter(CalibrationRunRecord run, bool append = false)
    {
        try
        {
            PrepareLocalRun(run, append);
            return new(this, run, append);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Nie je možné zapisovať kalibračný beh do '{GetRunDirectory(run)}'. " +
                "Skontrolujte dostupnosť disku a oprávnenie na zápis. Kalibrácia sa nespustí bez uloženia dát.", ex);
        }
    }

    private string MonthlyPath(string root, CalibrationRunRecord run)
    {
        string readable = string.Concat((run.HumanRunId ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string name = string.IsNullOrWhiteSpace(readable) ? run.RunId.ToString("N") : readable + "__" + run.RunId.ToString("N");
        return Path.Combine(root, run.StartedAt.Year.ToString("D4", CultureInfo.InvariantCulture), MonthFolders[run.StartedAt.Month - 1], name);
    }

    private void PrepareLocalRun(CalibrationRunRecord run, bool append)
    {
        if (_replicationQueue is null) return;
        string source = GetRunDirectory(run);
        // Old network-only runs are imported before resuming; never acquire directly to the network.
        if (source.StartsWith(@"\\", StringComparison.Ordinal) ||
            new DriveInfo(Path.GetPathRoot(source)!).DriveType == DriveType.Network ||
            _additionalHistoryRoots.Any(root => CalibrationStorageSettings.IsWithin(source, root)))
        {
            string local = run.LocalRunDirectory ?? MonthlyPath(RunsDirectory, run);
            if (append && !Directory.Exists(local))
            {
                string staging = local + ".import-" + Guid.NewGuid().ToString("N");
                try
                {
                    Directory.CreateDirectory(staging);
                    foreach (string file in Directory.EnumerateFiles(source, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
                    {
                        string target = Path.Combine(staging, Path.GetRelativePath(source, file));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(file, target);
                    }
                    Directory.Move(staging, local);
                }
                finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            }
            source = local;
            _runDirectories[run.RunId] = source;
        }
        bool firstStorageSnapshot = run.LocalRunDirectory is null;
        run.LocalRunDirectory = source;
        if (firstStorageSnapshot && run.ReplicaRunDirectory is null && _replicaDirectory is not null)
            run.ReplicaRunDirectory = MonthlyPath(_replicaDirectory, run);
        Directory.CreateDirectory(source);
        RequestReplication(run);
    }

    public void RequestReplication(CalibrationRunRecord run)
    {
        if (_replicationQueue is not null && run.LocalRunDirectory is { } local)
            _replicationQueue.Enqueue(run.RunId, local, run.ReplicaRunDirectory);
    }

    public string ReplicationStatus(Guid? runId = null) => _replicationQueue?.Status(runId) ?? "Lokálne ukladanie.";
    public CalibrationRunRecord? LoadRun(Guid runId)
    {
        return ReadRecoveryFile<CalibrationRunRecord>(Path.Combine(GetRunDirectory(runId), "summary.json"));
    }

    internal void SaveSettlingProgress(CalibrationRunRecord run)
    {
        string dir = GetRunDirectory(run);
        Directory.CreateDirectory(dir);
        WriteRecoveryFile(Path.Combine(dir, "summary.json"), JsonSerializer.Serialize(run, JsonOptions));
        RequestReplication(run);
    }

    public void SaveRun(CalibrationRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        string dir = GetRunDirectory(run);
        Directory.CreateDirectory(dir);
        run.CalibrationResults = TemperatureCalibrationAnalyzer.Analyze(run);
        WriteRecoveryFile(Path.Combine(dir, "summary.json"), JsonSerializer.Serialize(run, JsonOptions));
        ExportSummaryCsv(run, Path.Combine(dir, "summary.csv"));
        try
        {
            if (run.State is CalibrationRunState.Completed or CalibrationRunState.CompletedWithWarnings)
                TemperatureCalibrationAnalyzer.Export(run, dir);
            CalibrationPointReportExporter.ExportCompletedPlateaus(run, dir, LoadSetup(run.ProfileId, run.ChamberId)?.Settings);
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            // A report is a secondary artifact. Its failure must never interrupt or invalidate calibration data.
            try
            {
                File.WriteAllText(Path.Combine(dir, "report-generation-error.txt"), $"{DateTimeOffset.Now:O}\r\n{ex}", Encoding.UTF8);
            }
            catch (Exception writeEx) when (writeEx is not StackOverflowException and not OutOfMemoryException)
            {
                // Even an unwritable diagnostic must not affect the primary JSON/CSV calibration result.
            }
        }
        RequestReplication(run);
    }

    public List<CalibrationRunRecord> LoadHistory()
    {

        var result = new List<CalibrationRunRecord>();
        var seen = new HashSet<Guid>();
        foreach (string file in HistoryRoots.Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "summary.json", SearchOption.AllDirectories)))
        {
            try
            {
                CalibrationRunRecord? run = ReadRecoveryFile<CalibrationRunRecord>(file);
                if (run is not null && seen.Add(run.RunId))
                {
                    run.CalibrationResults = TemperatureCalibrationAnalyzer.Analyze(run);
                    string directory = Path.GetDirectoryName(file)!;
                    _runDirectories.TryAdd(run.RunId, directory);
                    string coefficientsCsv = Path.Combine(directory, "calibration-coefficients.csv");
                    bool currentFormat = File.Exists(coefficientsCsv) && File.ReadLines(coefficientsCsv).FirstOrDefault()?.Contains("CalibrationType", StringComparison.Ordinal) == true;
                    if ((run.State is CalibrationRunState.Completed or CalibrationRunState.CompletedWithWarnings) &&
                        (!currentFormat || !File.Exists(Path.Combine(directory, "calibration-coefficients.xlsx"))))
                        TemperatureCalibrationAnalyzer.Export(run, directory);
                    result.Add(run);
                }
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
        WriteRecoveryFile(CheckpointPath(checkpoint.ChamberId), JsonSerializer.Serialize(checkpoint, JsonOptions));
    }

    public CalibrationCheckpoint? LoadCheckpoint(Guid chamberId)
    {
        var checkpoint = ReadRecoveryFile<CalibrationCheckpoint>(CheckpointPath(chamberId));
        return checkpoint?.ChamberId == chamberId ? checkpoint : null;
    }

    public void DeleteCheckpoint(Guid chamberId)
    {
        lock (RecoveryFileSync)
        {
            string path = CheckpointPath(chamberId);
            // Remove the backup first so deliberate deletion cannot revive an old run.
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static readonly object RecoveryFileSync = new();
    private static void WriteRecoveryFile(string path, string json)
    {
        lock (RecoveryFileSync)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private static T? ReadRecoveryFile<T>(string path) where T : class
    {
        lock (RecoveryFileSync)
        {
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                try
                {
                    if (File.Exists(candidate) && JsonSerializer.Deserialize<T>(File.ReadAllText(candidate), JsonOptions) is { } value)
                        return value;
                }
                catch (Exception ex) when (ex is IOException or JsonException) { }
            }
            return null;
        }
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

    public string GetRunDirectory(CalibrationRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (_runDirectories.TryGetValue(run.RunId, out string? known)) return known;
        string readableId = string.Concat((run.HumanRunId ?? string.Empty).Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        string folder = string.IsNullOrWhiteSpace(readableId) ? run.RunId.ToString("N") : readableId + "__" + run.RunId.ToString("N");
        string preferred = _partitionRunsByMonth
            ? Path.Combine(RunsDirectory, run.StartedAt.Year.ToString("D4", CultureInfo.InvariantCulture), MonthFolders[run.StartedAt.Month - 1], folder)
            : Path.Combine(RunsDirectory, folder);
        // Keep an existing run in its original directory when resuming after an upgrade.
        string? existing = new[] { preferred, Path.Combine(LegacyRunsDirectory, run.RunId.ToString("N")),
            Path.Combine(LegacyRunsDirectory, folder), Path.Combine(RunsDirectory, run.RunId.ToString("N")),
            Path.Combine(RunsDirectory, folder) }.FirstOrDefault(Directory.Exists);
        return _runDirectories.GetOrAdd(run.RunId, existing ?? preferred);
    }

    public string GetRunDirectory(Guid runId)
    {
        string? local = _replicationQueue?.SourceDirectory(runId);
        if (local is not null && File.Exists(Path.Combine(local, "summary.json")))
        {
            _runDirectories[runId] = local;
            return local;
        }
        if (_runDirectories.TryGetValue(runId, out string? known)) return known;
        string id = runId.ToString("N");
        foreach (string root in HistoryRoots.Where(Directory.Exists))
        {
            if (Path.GetFileName(root) == id || Path.GetFileName(root).EndsWith("__" + id, StringComparison.OrdinalIgnoreCase))
                return _runDirectories.GetOrAdd(runId, root);
            string? directory = Directory.EnumerateDirectories(root, "*" + id, SearchOption.AllDirectories)
                .FirstOrDefault(path => Path.GetFileName(path) == id || Path.GetFileName(path).EndsWith("__" + id, StringComparison.OrdinalIgnoreCase));
            if (directory is not null) return _runDirectories.GetOrAdd(runId, directory);
        }
        return Path.Combine(RunsDirectory, id);
    }
    private string SetupPath(Guid profileId, Guid chamberId) => chamberId == Guid.Empty
        ? Path.Combine(SetupsDirectory, $"{profileId:N}.json")
        : Path.Combine(SetupsDirectory, $"{chamberId:N}-{profileId:N}.json");
    private string CheckpointPath(Guid chamberId) => Path.Combine(CheckpointsDirectory, $"{chamberId:N}.json");

    private static string F(double value) => value.ToString("G17", SlovakCulture);
    private static string E(string value) => value.Replace(";", ",").Replace("\r", " ").Replace("\n", " ");
}

public sealed class CalibrationRunWriter : IAsyncDisposable
{
    private static readonly CultureInfo SlovakCulture = CultureInfo.GetCultureInfo("sk-SK");
    private readonly CalibrationStore _store;
    private readonly CalibrationRunRecord _run;
    private readonly StreamWriter _rawWriter;
    private readonly StreamWriter _wavelengthWriter;
    private readonly StreamWriter _diagnosticWriter;
    private readonly object _diagnosticSync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event Action<string>? DiagnosticWritten;

    internal CalibrationRunWriter(CalibrationStore store, CalibrationRunRecord run, bool append)
    {
        _store = store;
        _run = run;
        string dir = store.GetRunDirectory(run);
        Directory.CreateDirectory(dir);
        string rawPath = Path.Combine(dir, "raw-samples.csv");
        bool rawHasContent = append && File.Exists(rawPath) && new FileInfo(rawPath).Length > 0;
        _rawWriter = new StreamWriter(rawPath, append, Encoding.UTF8);
        if (!rawHasContent)
            _rawWriter.WriteLine("RunId;ProfileId;Plateau;TargetTemperatureC;ActualTemperatureC;ReferenceTemperatureC;Timestamp;SensorSerialNumber;PeakLoggerDeviceSN;Channel;PeakId;PeakIndex;WavelengthNm;Intensity");
        _rawWriter.Flush();

        string wavelengthPath = Path.Combine(dir, "wavelength-trace.csv");
        bool wavelengthHasContent = append && File.Exists(wavelengthPath) && new FileInfo(wavelengthPath).Length > 0;
        _wavelengthWriter = new StreamWriter(wavelengthPath, append, Encoding.UTF8);
        if (!wavelengthHasContent)
            _wavelengthWriter.WriteLine("RunId;Timestamp;SensorSerialNumber;PeakLoggerDeviceSN;Channel;PeakId;PeakIndex;WavelengthNm;Intensity;ChamberTemperatureC;ReferenceTemperatureC");
        _wavelengthWriter.Flush();

        DiagnosticFilePath = Path.Combine(dir, "diagnostics.log");
        var diagnosticStream = new FileStream(
            DiagnosticFilePath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _diagnosticWriter = new StreamWriter(diagnosticStream, Encoding.UTF8) { AutoFlush = true };
        if (!append || diagnosticStream.Length == 0)
            _diagnosticWriter.WriteLine("Timestamp\tLevel\tRunId\tHumanRunId\tEvent\tDetails");
        WriteDiagnostic("INFO", append ? "RUN_LOG_RESUMED" : "RUN_LOG_CREATED",
            $"profile={run.ProfileCode}|{run.ProfileName}; chamber={run.ChamberName}; operator={run.Operator}");
    }

    public string DiagnosticFilePath { get; }

    public void WriteDiagnostic(string level, string eventName, string details)
    {
        string line = string.Join('\t', new[]
        {
            DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture), E(level),
            _run.RunId.ToString("N"), E(_run.DisplayRunId), E(eventName), E(details),
        });
        lock (_diagnosticSync)
        {
            _diagnosticWriter.WriteLine(line);
        }
        _store.RequestReplication(_run);
        DiagnosticWritten?.Invoke(line);
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
            _store.RequestReplication(_run);
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
            _store.RequestReplication(_run);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void SaveSummary() => _store.SaveRun(_run);
    public void SaveSettlingProgress() => _store.SaveSettlingProgress(_run);

    private static string F(double value) => value.ToString("G17", SlovakCulture);
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
            lock (_diagnosticSync)
            {
                _diagnosticWriter.WriteLine($"{DateTimeOffset.Now:O}\tINFO\t{_run.RunId:N}\t{E(_run.DisplayRunId)}\tRUN_LOG_CLOSED\tstate={_run.State}");
                _diagnosticWriter.Dispose();
            }
            _store.SaveRun(_run);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
