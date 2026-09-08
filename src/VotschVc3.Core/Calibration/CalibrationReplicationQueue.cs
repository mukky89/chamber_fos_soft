using System.Collections.Concurrent;
using System.Text.Json;

namespace VotschVc3.Core.Calibration;

/// <summary>Local durable work list. Network I/O is performed by one background worker, never by the measurement writer.</summary>
public sealed class CalibrationReplicationQueue
{
    public sealed class Job
    {
        public Guid RunId { get; set; }
        public string Source { get; set; } = "";
        public string Destination { get; set; } = "";
        public DateTimeOffset? LastSuccess { get; set; }
        public string? Error { get; set; }
        public bool Pending { get; set; } = true;
        [System.Text.Json.Serialization.JsonIgnore] public long Revision { get; set; }
    }

    private readonly string _directory;
    private readonly Dictionary<Guid, Job> _jobs = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _processing = new(1, 1);
    private Task? _worker;
    public CalibrationReplicationQueue(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                Job? job = JsonSerializer.Deserialize<Job>(File.ReadAllText(file));
                if (job is null) continue;
                // Recheck every known local run once after restart, including data flushed just before a crash.
                job.Pending = job.Destination.Length > 0;
                _jobs[job.RunId] = job;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            { Diagnostics.AppLog.Warn("Záloha kalibrácie", $"Nemožno načítať front {file}: {ex.Message}"); }
        }
    }

    public void Enqueue(Guid runId, string source, string? destination)
    {
        source = Path.GetFullPath(source);
        destination = destination is null ? string.Empty : Path.GetFullPath(destination);
        if (destination.Length > 0 && (CalibrationStorageSettings.IsWithin(source, destination) || CalibrationStorageSettings.IsWithin(destination, source)))
            throw new ArgumentException("Zdroj zálohy a cieľ musia byť oddelené.");
        lock (_gate)
        {
            if (!_jobs.TryGetValue(runId, out Job? job))
            {
                job = new Job { RunId = runId, Source = source, Destination = destination };
                Persist(job); // Must be durable before the run starts.
                _jobs.Add(runId, job);
            }
            else if (!string.Equals(job.Source, source, StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(job.Destination, destination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Rozbehnutý beh už má uložené iné cesty zálohy.");
            job.Revision++;
            job.Pending = destination.Length > 0;
        }
    }

    public void RequestAll()
    {
        lock (_gate) foreach (Job job in _jobs.Values) { job.Pending = true; job.Revision++; }
    }

    public string? SourceDirectory(Guid runId)
    {
        lock (_gate) return _jobs.TryGetValue(runId, out Job? job) ? job.Source : null;
    }

    public string[] SourceDirectories
    {
        get { lock (_gate) return _jobs.Values.Select(job => job.Source).ToArray(); }
    }

    public string Status(Guid? runId = null)
    {
        lock (_gate)
        {
            var jobs = _jobs.Values.Where(job => runId is null || job.RunId == runId).ToArray();
            if (jobs.Length == 0) return "Sieťová kópia: zatiaľ bez behov.";
            jobs = jobs.Where(job => job.Destination.Length > 0).ToArray();
            if (jobs.Length == 0) return "Lokálne uložené · sieťová kópia pre tieto behy je vypnutá.";
            int failed = jobs.Count(job => job.Error is not null);
            int pending = jobs.Count(job => job.Pending);
            if (failed > 0) return $"⚠ Lokálne dáta zostávajú uložené. Sieťová kópia čaká: {failed} behov. {jobs.First(job => job.Error is not null).Error}";
            if (pending > 0) return $"Lokálne uložené · na synchronizáciu čaká {pending} behov.";
            DateTimeOffset? last = jobs.Max(job => job.LastSuccess);
            return $"✓ Lokálna aj sieťová kópia synchronizovaná · {last:dd.MM.yyyy HH:mm:ss}";
        }
    }

    public void Start(Func<int> intervalSeconds)
    {
        lock (_gate)
        {
            _worker ??= Task.Run(async () =>
            {
                while (true)
                {
                    try { await ProcessPendingAsync().ConfigureAwait(false); }
                    catch (Exception ex) { Diagnostics.AppLog.Warn("Záloha kalibrácie", ex.Message); }
                    int seconds;
                    try { seconds = Math.Clamp(intervalSeconds(), 1, 3600); } catch { seconds = 10; }
                    await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
                }
            });
        }
    }

    public async Task ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        await _processing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            (Guid Id, string Source, string Destination, long Revision)[] work;
            lock (_gate) work = _jobs.Values.Where(job => job.Pending && job.Destination.Length > 0)
                .Select(job => (job.RunId, job.Source, job.Destination, job.Revision)).ToArray();
            foreach (var item in work)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? error = null;
                try
                {
                    await Task.Run(() => CopyRun(item.Source, item.Destination, cancellationToken), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { error = ex.Message; }
                lock (_gate)
                {
                    Job job = _jobs[item.Id];
                    job.Error = error;
                    job.Pending = error is not null || job.Revision != item.Revision;
                    if (error is null) job.LastSuccess = DateTimeOffset.Now;
                    Persist(job);
                }
            }
        }
        finally { _processing.Release(); }
    }

    private static void CopyRun(string source, string destination, CancellationToken token)
    {
        if (!Directory.Exists(source)) throw new IOException($"Lokálny priečinok nie je dostupný: {source}");
        foreach (string file in Directory.EnumerateFiles(source, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
        {
            token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            var before = new FileInfo(file);
            if (File.Exists(target))
            {
                var existing = new FileInfo(target);
                if (existing.Length == before.Length && existing.LastWriteTimeUtc == before.LastWriteTimeUtc) continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temporary = target + ".sync-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                // Copy a bounded snapshot; ongoing acquisition can keep appending locally.
                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    long remaining = input.Length;
                    byte[] buffer = new byte[81920];
                    while (remaining > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        int count = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                        if (count == 0) throw new IOException("Zdrojový súbor sa počas kopírovania zmenil.");
                        output.Write(buffer, 0, count);
                        remaining -= count;
                    }
                    output.Flush();
                }
                File.SetLastWriteTimeUtc(temporary, before.LastWriteTimeUtc);
                File.Move(temporary, target, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private void Persist(Job job)
    {
        string path = Path.Combine(_directory, job.RunId.ToString("N") + ".json");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(job));
        File.Move(temporary, path, overwrite: true);
    }
}
