using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationRedundancyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "calibration-redundancy-" + Guid.NewGuid().ToString("N"));
    private string Local => Path.Combine(_root, "local");
    private string Network => Path.Combine(_root, "network");
    private string QueuePath => Path.Combine(_root, "queue");
    private CalibrationStore Store(CalibrationReplicationQueue queue) =>
        new(Path.Combine(_root, "config"), Local, Network, queue, new[] { Network });

    [Fact]
    public async Task LiveTraceAndAllReportsAreCopiedWithoutClosingWriter()
    {
        var queue = new CalibrationReplicationQueue(QueuePath);
        var store = Store(queue);
        var run = new CalibrationRunRecord { HumanRunId = "01-2026-09-08" };
        await using (var writer = store.CreateRunWriter(run))
        {
            writer.SaveSummary();
            await writer.AppendWavelengthTraceAsync(new[] { new CalibrationWavelengthTraceSample { RunId = run.RunId, SerialNumber = "SN-1", WavelengthNm = 1550.123 } });
            string report = Path.Combine(run.LocalRunDirectory!, "reports", "point.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            File.WriteAllText(report, "point report");
            await queue.ProcessPendingAsync();
            Assert.Equal(ReadLiveText(Path.Combine(run.LocalRunDirectory!, "wavelength-trace.csv")),
                File.ReadAllText(Path.Combine(run.ReplicaRunDirectory!, "wavelength-trace.csv")));
            Assert.Equal("point report", File.ReadAllText(Path.Combine(run.ReplicaRunDirectory!, "reports", "point.txt")));
            Assert.Contains("synchronizovaná", queue.Status(run.RunId));
            await writer.AppendWavelengthTraceAsync(new[] { new CalibrationWavelengthTraceSample { RunId = run.RunId, SerialNumber = "SN-2", WavelengthNm = 1551 } });
            Assert.Contains("čaká", queue.Status(run.RunId));
        }
        await queue.ProcessPendingAsync();
        foreach (string file in Directory.EnumerateFiles(run.LocalRunDirectory!, "*", SearchOption.AllDirectories))
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(run.ReplicaRunDirectory!, Path.GetRelativePath(run.LocalRunDirectory!, file))));
    }

    [Fact]
    public async Task NetworkFailureDoesNotLoseLocalDataAndQueueRecoversAfterRestart()
    {
        var queue = new CalibrationReplicationQueue(QueuePath);
        File.WriteAllText(Network, "blocked network");
        var store = Store(queue);
        var run = new CalibrationRunRecord();
        await using (var writer = store.CreateRunWriter(run))
        {
            writer.WriteDiagnostic("INFO", "KEPT_LOCALLY", "Network offline");
            await queue.ProcessPendingAsync();
            Assert.Contains("Sieťová kópia čaká", queue.Status(run.RunId));
            writer.WriteDiagnostic("INFO", "CONTINUED", "Acquisition continues");
        }
        Assert.True(File.Exists(Path.Combine(run.LocalRunDirectory!, "summary.json")));
        File.Delete(Network);
        var restartedQueue = new CalibrationReplicationQueue(QueuePath);
        await restartedQueue.ProcessPendingAsync();
        Assert.Contains("CONTINUED", File.ReadAllText(Path.Combine(run.ReplicaRunDirectory!, "diagnostics.log")));
        Assert.Contains("synchronizovaná", restartedQueue.Status(run.RunId));
    }

    [Fact]
    public async Task StorageChangesDoNotRedirectExistingRunsAndHistoryPrefersLocalCopy()
    {
        var queue = new CalibrationReplicationQueue(QueuePath);
        var original = Store(queue);
        var run = new CalibrationRunRecord();
        await using (original.CreateRunWriter(run)) { }
        await queue.ProcessPendingAsync();
        string oldLocal = run.LocalRunDirectory!;
        string oldNetwork = run.ReplicaRunDirectory!;
        File.AppendAllText(Path.Combine(oldLocal, "diagnostics.log"), "UNSYNCED_LOCAL_DATA");

        var changed = new CalibrationStore(Path.Combine(_root, "config"), Path.Combine(_root, "new-local"),
            Path.Combine(_root, "new-network"), queue, new[] { Network });
        var loaded = Assert.IsType<CalibrationRunRecord>(changed.LoadRun(run.RunId));
        Assert.Equal(oldLocal, changed.GetRunDirectory(loaded));
        await using (changed.CreateRunWriter(loaded, append: true)) { }
        Assert.Equal(oldNetwork, loaded.ReplicaRunDirectory);
        Assert.Single(changed.LoadHistory());
        await queue.ProcessPendingAsync();
        Assert.Contains("UNSYNCED_LOCAL_DATA", File.ReadAllText(Path.Combine(oldNetwork, "diagnostics.log")));
    }

    [Fact]
    public async Task OldNetworkOnlyRunIsImportedBeforeResume()
    {
        var old = new CalibrationStore(Path.Combine(_root, "config"), Network);
        var run = new CalibrationRunRecord();
        await using (var writer = old.CreateRunWriter(run))
            writer.WriteDiagnostic("INFO", "OLD_NETWORK_DATA", "Keep");
        var queue = new CalibrationReplicationQueue(QueuePath);
        var store = Store(queue);
        var loaded = Assert.IsType<CalibrationRunRecord>(store.LoadRun(run.RunId));
        await using (var writer = store.CreateRunWriter(loaded, append: true))
            writer.WriteDiagnostic("INFO", "NEW_LOCAL_DATA", "Local");
        Assert.StartsWith(Local, loaded.LocalRunDirectory);
        Assert.Contains("OLD_NETWORK_DATA", File.ReadAllText(Path.Combine(loaded.LocalRunDirectory!, "diagnostics.log")));
        await queue.ProcessPendingAsync();
        Assert.Contains("NEW_LOCAL_DATA", File.ReadAllText(Path.Combine(loaded.ReplicaRunDirectory!, "diagnostics.log")));
    }

    [Fact]
    public async Task DisabledNetworkCopyKeepsLocalRunAndItsHistoryAfterPathChange()
    {
        var queue = new CalibrationReplicationQueue(QueuePath);
        var run = new CalibrationRunRecord();
        var localOnly = new CalibrationStore(Path.Combine(_root, "config"), Local, replicationQueue: queue);
        await using (localOnly.CreateRunWriter(run)) { }
        await queue.ProcessPendingAsync();
        Assert.Null(run.ReplicaRunDirectory);
        Assert.False(Directory.Exists(Network));
        var changed = new CalibrationStore(Path.Combine(_root, "config"), Path.Combine(_root, "new"), Network, queue);
        Assert.NotNull(changed.LoadRun(run.RunId));
        Assert.Contains("vypnutá", queue.Status(run.RunId));
    }

    [Fact]
    public void StorageSettingsPersistAndRejectOverlappingDestinations()
    {
        var settings = new CalibrationStorageSettings { LocalDirectory = Local, NetworkDirectory = Network, SynchronizationIntervalSeconds = 12 };
        var store = new CalibrationStorageSettingsStore(Path.Combine(_root, "settings.json"));
        store.Save(settings);
        Assert.Equal(Local, store.Load().LocalDirectory);
        Assert.Equal(12, store.Load().SynchronizationIntervalSeconds);
        settings.NetworkDirectory = Path.Combine(Local, "nested");
        Assert.Throws<ArgumentException>(() => store.Save(settings));
        Assert.Equal(Network, store.Load().NetworkDirectory);
    }

    [Fact]
    public async Task HistoryRefreshCannotRedirectAnOpenWriterToNetworkReplica()
    {
        var queue = new CalibrationReplicationQueue(QueuePath);
        var store = Store(queue);
        var run = new CalibrationRunRecord { ProfileName = "Before" };
        await using var writer = store.CreateRunWriter(run);
        writer.SaveSummary();
        await queue.ProcessPendingAsync();
        string local = run.LocalRunDirectory!;
        string summary = Path.Combine(local, "summary.json");
        File.WriteAllText(summary, "{"); // Simulate a transient, partially rewritten local summary.
        store.LoadHistory();
        Assert.Equal(local, store.GetRunDirectory(run));
        run.ProfileName = "After";
        writer.SaveSummary();
        Assert.Contains("After", File.ReadAllText(summary));
        Assert.DoesNotContain("After", File.ReadAllText(Path.Combine(run.ReplicaRunDirectory!, "summary.json")));
    }
    private static string ReadLiveText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
