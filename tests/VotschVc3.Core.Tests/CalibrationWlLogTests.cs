using System.Globalization;
using System.Text;
using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationWlLogTests
{
    [Theory]
    [InlineData("08092026")]
    [InlineData("04092026")]
    [InlineData("01092026")]
    [InlineData("28082026")]
    [InlineData("26082026")]
    public void MatchesProductionFixtureByteForByte(string name)
    {
        byte[] expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Wl", name + ".txt"));
        string[] lines = Encoding.UTF8.GetString(expected).Split("\r\n");
        string[] identity = lines[0].Split('\t'), sn = lines[2].Split('\t'), data = lines[3].Split('\t');
        var culture = CultureInfo.GetCultureInfo("sk-SK");
        string header = CalibrationWlFormat.Header(identity[1], identity[3], sn.Skip(2).Take(16).ToArray(), sn.Skip(18).SkipLast(1).ToArray());
        DateTimeOffset timestamp = DateTimeOffset.ParseExact(data[0], "dd.MM.yyyy HH:mm:ss.fffff", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
        string row = CalibrationWlFormat.Row(timestamp, double.Parse(data[1], culture),
            data.Skip(2).Take(16).Select(int.Parse).ToArray(), data.Skip(18).SkipLast(1).Select(v => double.Parse(v, culture)).ToArray());
        Assert.Equal(expected, Encoding.UTF8.GetBytes(header + row));
    }

    [Fact]
    public async Task StableMappingResumeAndPartialTailPreserveCompleteRows()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wl-test-" + Guid.NewGuid().ToString("N"));
        var run = new CalibrationRunRecord { StartedAt = DateTimeOffset.Now.AddMinutes(-1), ReferenceThermometerSerialNumber = "WIKA" };
        var setup = Setup();
        var diagnostics = new List<string>();
        DateTimeOffset at = DateTimeOffset.Now.AddSeconds(-5);
        string path;
        try
        {
            await using (var log = new CalibrationWlLog(dir, run, setup, diagnostics.Add))
            {
                path = Assert.Single(log.Paths);
                await log.AppendAsync(Batch(at).Reverse().ToArray(), 25.125, at, 1);
                await log.AppendAsync(Batch(at), 25.125, at, 1); // duplicate acquisition
            }
            byte[] before = File.ReadAllBytes(path);
            Assert.Equal(4, File.ReadAllLines(path).Length);
            Assert.EndsWith("\t1530,10000\t1550,20000\t\r\n", Encoding.UTF8.GetString(before));
            File.AppendAllText(path, "incomplete");
            await using (var resumed = new CalibrationWlLog(dir, run, setup, diagnostics.Add))
            {
                await resumed.AppendAsync(Batch(at), 25.125, at, 1);
                Assert.Equal(before, ReadShared(path));
                await resumed.AppendAsync(Batch(at.AddSeconds(2)), 26, at.AddSeconds(2), 1);
            }
            Assert.Equal(5, File.ReadAllLines(path).Length);
            Assert.Equal("incomplete", File.ReadAllText(Assert.Single(Directory.GetFiles(dir, "*.bin"))));
            Assert.Contains(diagnostics, d => d.Contains("WLN_TAIL_RECOVERED"));
            setup.Mappings[0].PeakId = "different";
            Assert.Throws<InvalidOperationException>(() => new CalibrationWlLog(dir, run, setup, diagnostics.Add));
            Assert.Equal(5, File.ReadAllLines(path).Length);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task MissingStaleAndChangedPeaksNeverProduceFabricatedRows()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wl-test-" + Guid.NewGuid().ToString("N"));
        var run = new CalibrationRunRecord { StartedAt = DateTimeOffset.Now.AddMinutes(-1) };
        var diagnostics = new List<string>();
        try
        {
            await using var log = new CalibrationWlLog(dir, run, Setup(), diagnostics.Add);
            DateTimeOffset at = DateTimeOffset.Now;
            await log.AppendAsync(Batch(at), null, at, 1);
            await log.AppendAsync(Batch(at), 25, at.AddSeconds(-20), 1);
            await log.AppendAsync(Batch(at).Take(1).ToArray(), 25, at, 1);
            await log.AppendAsync(Batch(at).Select(m => m with { PeakIndex = 99 }).ToArray(), 25, at, 1);
            Assert.Equal(3, Encoding.UTF8.GetString(ReadShared(log.Paths[0])).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
            await log.AppendAsync(Batch(at), 25, at, 1);
            await log.DisposeAsync();
            await log.AppendAsync(Batch(at.AddSeconds(2)), 26, at.AddSeconds(2), 1);
            Assert.Equal(4, File.ReadAllLines(log.Paths[0]).Length);
            Assert.NotEmpty(diagnostics);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task SeparateInterrogatorsAndCancelledAppendKeepFilesConsistent()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wl-test-" + Guid.NewGuid().ToString("N"));
        var run = new CalibrationRunRecord { StartedAt = DateTimeOffset.Now.AddMinutes(-1) };
        var setup = Setup();
        setup.Mappings.Add(new() { Selected = true, SerialNumber = "OTHER-SN", PeakLoggerDeviceSerialNumber = "OTHER", Channel = "1.1", PeakId = "P1", PeakIndex = 1 });
        try
        {
            await using var log = new CalibrationWlLog(dir, run, setup, _ => { });
            Assert.Equal(2, log.Paths.Count);
            DateTimeOffset at = DateTimeOffset.Now;
            var batch = Batch(at).Append(new PeakLoggerMeasurement(at, "OTHER", "1.1", "P1", 1, 1560)).ToArray();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => log.AppendAsync(batch, 25, at, 1, cancelled.Token));
            await log.AppendAsync(batch, 25, at, 1);
            await log.DisposeAsync();
            foreach (string path in log.Paths) Assert.Equal(4, File.ReadAllLines(path).Length);
            string other = File.ReadAllText(log.Paths.Single(p => p.EndsWith("_OTHER_WL.txt")));
            Assert.Contains("Interrogator SN:\tOTHER\t", other);
            Assert.DoesNotContain("SN-A", other);
            Assert.EndsWith("\t1560,00000\t\r\n", other);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private static byte[] ReadShared(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private static CalibrationSetup Setup() => new() { Mappings = [
        new() { Selected = true, SerialNumber = "SN-A", ChannelSerialNumber = "CHAIN", PeakLoggerDeviceSerialNumber = "DEVICE", Channel = "4.3", PeakId = "P1", PeakIndex = 1 },
        new() { Selected = true, SerialNumber = "SN-B", ChannelSerialNumber = "CHAIN", PeakLoggerDeviceSerialNumber = "DEVICE", Channel = "4.3", PeakId = "P2", PeakIndex = 2 },
    ] };
    private static PeakLoggerMeasurement[] Batch(DateTimeOffset at) => [
        new(at, "DEVICE", "4.3", "P1", 1, 1530.1), new(at, "DEVICE", "4.3", "P2", 2, 1550.2),
    ];
}
