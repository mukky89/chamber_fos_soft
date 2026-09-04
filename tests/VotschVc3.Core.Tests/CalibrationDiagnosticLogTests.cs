using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationDiagnosticLogTests
{
    [Fact]
    public async Task RunWriter_CreatesAndFlushesTerminalFriendlyDiagnosticLog()
    {
        string root = Path.Combine(Path.GetTempPath(), "votsch-diagnostic-" + Guid.NewGuid().ToString("N"));
        try
        {
            var run = new CalibrationRunRecord
            {
                HumanRunId = "CAL-TEST-001",
                ProfileCode = "PROFILE",
                ProfileName = "Diagnostic test",
                ChamberName = "Komora 1",
                Operator = "tester",
            };
            var store = new CalibrationStore(root);
            string path;
            await using (CalibrationRunWriter writer = store.CreateRunWriter(run))
            {
                path = writer.DiagnosticFilePath;
                writer.WriteDiagnostic("INFO", "PROGRESS", "wikaC=-40.004; peak=P1; stdDevPm=0.12");
                await using var liveStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var liveReader = new StreamReader(liveStream);
                string live = await liveReader.ReadToEndAsync();
                Assert.Contains("RUN_LOG_CREATED", live);
                Assert.Contains("wikaC=-40.004", live);
                Assert.Contains(run.RunId.ToString("N"), live);
            }

            string completed = await File.ReadAllTextAsync(path);
            Assert.Contains("RUN_LOG_CLOSED", completed);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
