using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationMonthlyStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "calibration-monthly-" + Guid.NewGuid().ToString("N"));
    private string Local => Path.Combine(_root, "local");
    private string Production => Path.Combine(_root, "production");

    [Theory]
    [InlineData(1, "01_Januar")]
    [InlineData(2, "02_Februar")]
    [InlineData(3, "03_Marec")]
    [InlineData(4, "04_April")]
    [InlineData(5, "05_Maj")]
    [InlineData(6, "06_Jun")]
    [InlineData(7, "07_Jul")]
    [InlineData(8, "08_August")]
    [InlineData(9, "09_September")]
    [InlineData(10, "10_Oktober")]
    [InlineData(11, "11_November")]
    [InlineData(12, "12_December")]
    public void UsesStartYearAndSlovakMonthWithoutExtraRunsFolder(int month, string folder)
    {
        var store = new CalibrationStore(Local, Production);
        var run = new CalibrationRunRecord { StartedAt = new DateTimeOffset(2026, month, 1, 10, 0, 0, TimeSpan.FromHours(1)) };
        Assert.Equal(Path.Combine(Production, "2026", folder, run.RunId.ToString("N")), store.GetRunDirectory(run));
        Assert.Equal(Path.Combine(Local, "Setups"), store.SetupsDirectory);
        Assert.Equal(Path.Combine(Local, "Checkpoints"), store.CheckpointsDirectory);
        Assert.False(Directory.Exists(Production)); // Opening the UI does not require the share.
    }

    [Fact]
    public async Task ReloadAndResumeKeepStartMonthAndAppendLogsAcrossYearBoundary()
    {
        var run = new CalibrationRunRecord
        {
            HumanRunId = "01-2026-12-31",
            StartedAt = new DateTimeOffset(2026, 12, 31, 23, 50, 0, TimeSpan.FromHours(1)),
            CompletedAt = new DateTimeOffset(2027, 1, 1, 3, 0, 0, TimeSpan.FromHours(1)),
        };
        var firstStore = new CalibrationStore(Local, Production);
        await using (var writer = firstStore.CreateRunWriter(run))
            writer.WriteDiagnostic("INFO", "BEFORE_RESTART", "Saved");
        string expected = Path.Combine(Production, "2026", "12_December", "01-2026-12-31__" + run.RunId.ToString("N"));

        var reloadedStore = new CalibrationStore(Local, Production);
        var loaded = Assert.IsType<CalibrationRunRecord>(reloadedStore.LoadRun(run.RunId));
        Assert.Equal(expected, reloadedStore.GetRunDirectory(loaded));
        await using (var writer = reloadedStore.CreateRunWriter(loaded, append: true))
            writer.WriteDiagnostic("INFO", "AFTER_RESTART", "Resumed");
        string log = File.ReadAllText(Path.Combine(expected, "diagnostics.log"));
        Assert.Contains("BEFORE_RESTART", log);
        Assert.Contains("AFTER_RESTART", log);
        Assert.True(File.Exists(Path.Combine(expected, "raw-samples.csv")));
        Assert.True(File.Exists(Path.Combine(expected, "wavelength-trace.csv")));
        Assert.True(File.Exists(Path.Combine(expected, "summary.json")));
        Assert.Single(new CalibrationStore(Local, Production).LoadHistory());
        Assert.False(Directory.Exists(Path.Combine(Production, "2027")));
    }

    [Fact]
    public async Task HistoryCombinesLocalAndProductionAndDoesNotMoveLegacyRuns()
    {
        var legacyStore = new CalibrationStore(Local);
        var oldRun = new CalibrationRunRecord { HumanRunId = "01-2025-01-01" };
        await using (legacyStore.CreateRunWriter(oldRun)) { }
        string oldDirectory = legacyStore.GetRunDirectory(oldRun);
        var newStore = new CalibrationStore(Local, Production);
        var newRun = new CalibrationRunRecord { HumanRunId = "01-2026-02-01", StartedAt = new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.FromHours(1)) };
        await using (newStore.CreateRunWriter(newRun)) { }

        var historyStore = new CalibrationStore(Local, Production);
        Assert.Equal(2, historyStore.LoadHistory().Count);
        Assert.Equal(oldDirectory, historyStore.GetRunDirectory(oldRun));
        Assert.Equal(oldDirectory, new CalibrationStore(Local, Production).GetRunDirectory(oldRun));
        Assert.NotNull(historyStore.LoadRun(oldRun.RunId));
        Assert.NotNull(historyStore.LoadRun(newRun.RunId));
        Assert.True(Directory.Exists(oldDirectory));
    }

    [Fact]
    public void UnwritableDestinationFailsInsteadOfCreatingLocalRun()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Production, "A file blocks the destination directory");
        var store = new CalibrationStore(Local, Production);
        var run = new CalibrationRunRecord();
        var error = Assert.Throws<IOException>(() => store.CreateRunWriter(run));
        Assert.Contains("Kalibrácia sa nespustí", error.Message);
        Assert.Empty(Directory.EnumerateDirectories(store.LegacyRunsDirectory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
