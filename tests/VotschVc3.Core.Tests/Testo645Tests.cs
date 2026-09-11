using System.Net;
using System.Text;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Recording;
using VotschVc3.Core.Thermometers;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class Testo645Tests
{
    private static byte[] Frame(int rh = 567, int temp = 234)
    {
        var bytes = new byte[29]; bytes[0] = 0x21; bytes[4] = 1;
        bytes[14] = (byte)(rh >> 8); bytes[15] = (byte)rh;
        bytes[19] = (byte)(temp >> 8); bytes[20] = (byte)temp;
        return bytes;
    }
    [Fact] public void EveryPossibleSplitIncludingHeaderIsParsed()
    {
        for (int split = 1; split < 29; split++)
        {
            var parser = new Testo645Parser();
            Assert.Empty(parser.Feed(Frame().AsSpan(0, split)));
            var reading = Assert.Single(parser.Feed(Frame().AsSpan(split)));
            Assert.Equal(56.7, reading.HumidityPercent); Assert.Equal(23.4, reading.TemperatureC);
        }
    }
    [Fact] public void NoiseMultipleResponsesAndBoundedBuffer()
    {
        var parser = new Testo645Parser();
        Assert.Empty(parser.Feed(Enumerable.Repeat((byte)0x21, 100000).ToArray()));
        Assert.InRange(parser.BufferedBytes, 0, 4);
        var readings = parser.Feed(new byte[] { 77, 44 }.Concat(Frame()).Concat(Frame(5, 0)).ToArray());
        Assert.Equal(2, readings.Count); Assert.Equal(0.5, readings[1].HumidityPercent);
        Assert.Equal(0, readings[1].TemperatureC);
    }
    [Theory]
    [InlineData(1001, 234, false, true)]
    [InlineData(567, 65535, true, false)]
    [InlineData(65535, 65535, false, false)]
    [InlineData(1000, 2000, true, true)]
    public void UnsupportedValuesAreNotClamped(int rh, int temp, bool validRh, bool validT)
    {
        var value = Assert.Single(new Testo645Parser().Feed(Frame(rh, temp)));
        Assert.Equal(validRh, value.HumidityPercent.HasValue); Assert.Equal(validT, value.TemperatureC.HasValue);
    }
    [Fact] public async Task QueryUsesExactRequestAndPartialReads()
    {
        var transport = new FakeTransport(Frame().Select(x => new[] { x }).ToArray());
        await using var session = new Testo645Session(transport);
        var result = await session.ReadAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.True(result.IsValid);
        Assert.Equal(Convert.FromHexString("12000000010155D1B700"), transport.Request);
    }
    [Fact] public async Task TimeoutDoesNotBecomeAZeroReading()
    {
        var transport = new FakeTransport([]) { Timeout = true };
        await using var session = new Testo645Session(transport);
        await Assert.ThrowsAsync<TimeoutException>(() => session.ReadAsync(TimeSpan.FromMilliseconds(20), CancellationToken.None));
    }
    [Fact] public async Task DisconnectIsReportedAndDisposalIdempotent()
    {
        var transport = new FakeTransport([]);
        var session = new Testo645Session(transport);
        await Assert.ThrowsAsync<IOException>(() => session.ReadAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
        await session.DisposeAsync(); await session.DisposeAsync();
        Assert.Equal(1, transport.Disposals);
    }
    [Fact] public async Task CancellationWaitsForReadBeforeClosing()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var transport = new FakeTransport([Frame()]) { BeforeRead = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(2)); } };
        var session = new Testo645Session(transport);
        using var stop = new CancellationTokenSource();
        var read = session.ReadAsync(TimeSpan.FromSeconds(3), stop.Token);
        Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(2))));
        stop.Cancel(); var close = session.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted); Assert.Equal(0, transport.Disposals);
        release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read); await close;
        Assert.Equal(1, transport.Disposals);
    }
    [Fact] public async Task CancelBeforeOpenDoesNotOpenPort()
    {
        var transport = new FakeTransport([]); await using var session = new Testo645Session(transport);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ReadAsync(TimeSpan.FromSeconds(1), new CancellationToken(true)));
        Assert.Equal(0, transport.Opens);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task AppendPreservesEveryExistingByteAndCreatesSessionHeader(bool unicode)
    {
        string path = Path.GetTempFileName();
        try
        {
            Encoding encoding = unicode ? Encoding.Unicode : new UTF8Encoding(false);
            byte[] original = encoding.GetPreamble().Concat(encoding.GetBytes("pôvodné údaje\t123\r\nbez konca")).ToArray();
            await File.WriteAllBytesAsync(path, original);
            await using (var log = await ExternalHumidityLog.OpenAsync(path, true, Guid.NewGuid(), "COM_TEST", "OFF", [], TimeSpan.FromSeconds(5)))
                await log.AppendAsync(new(DateTimeOffset.Now, null, null, [], null, "TESTO_TIMEOUT"));
            byte[] current = await File.ReadAllBytesAsync(path);
            Assert.Equal(original, current.Take(original.Length).ToArray());
            string text = encoding.GetString(current);
            Assert.Contains("# Testo645 session", text); Assert.Contains("TESTO_TIMEOUT", text);
        }
        finally { File.Delete(path); }
    }
    [Fact] public async Task StablePeakIdentityMissingAndStaleValues()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            await using var log = await ExternalHumidityLog.OpenAsync(path, false, Guid.NewGuid(), "COM_TEST", "host:123", [new("A", "1.1", 2), new("B", "1.1", 2)], TimeSpan.FromSeconds(5));
            var now = DateTimeOffset.Now;
            var sample = new ExternalHumiditySample(now, now, new(50, 25, "OK"), [new(now, "A", "1.1", "P2", 2, 1533)], now, "");
            var row = log.FormatRow(sample).Split('\t');
            Assert.Equal("1533", row[3]); Assert.Equal("NA", row[4]); Assert.Contains("MISSING", row[6]);
            row = log.FormatRow(sample with { Peaks = [new(now, "A", "1.1", "P2", 2, 1599)] }).Split('\t');
            Assert.Equal("1599", row[3]); // wavelength changed, same source identity
            row = log.FormatRow(sample with { RecordedAt = now.AddSeconds(10) }).Split('\t');
            Assert.Equal("NA", row[1]); Assert.Equal("NA", row[3]); Assert.Contains("STALE", row[6]);
            row = log.FormatRow(sample with { Peaks = [], ApiReceivedAt = null, Status = "API_ERROR" }).Split('\t');
            Assert.Equal("50", row[1]); Assert.Contains("API_ERROR", row[6]);
        }
        finally { File.Delete(path); }
    }
    [Fact] public async Task NewFileNeverOverwritesAndWriteFailuresPropagate()
    {
        string path = Path.GetTempFileName();
        try { await Assert.ThrowsAsync<IOException>(() => ExternalHumidityLog.OpenAsync(path, false, Guid.NewGuid(), "", "", [], TimeSpan.FromSeconds(1))); }
        finally { File.Delete(path); }
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "x.txt");
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => ExternalHumidityLog.OpenAsync(missing, false, Guid.NewGuid(), "", "", [], TimeSpan.FromSeconds(1)));
    }
    [Fact] public void SettingsArePerChamberAndCorruptSourceIsPreserved()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var store = new ExternalHumiditySettingsStore(directory); var id = Guid.NewGuid();
            Assert.False(store.Load(id).Enabled);
            store.Save(id, new() { Enabled = true, Port = "COM_TEST" });
            Assert.True(new ExternalHumiditySettingsStore(directory).Load(id).Enabled);
            Assert.False(store.Load(Guid.NewGuid()).Enabled);
            string path = Directory.GetFiles(directory, "*.json").Single(); File.WriteAllText(path, "broken");
            Assert.Throws<System.Text.Json.JsonException>(() => store.Save(id, new()));
            Assert.Equal("broken", File.ReadAllText(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact] public async Task MultipleApiPortsRemainIndependentWithFallback()
    {
        using var handler = new ApiHandler(); using var http = new HttpClient(handler);
        await using var first = new PeakLoggerApiClient(http); await using var second = new PeakLoggerApiClient(http);
        await first.ConnectAsync(new() { Host = "localhost", Port = 12001, UseSimulator = false });
        await second.ConnectAsync(new() { Host = "localhost", Port = 12002, UseSimulator = false });
        Assert.Equal("D12001", Assert.Single(await first.ReadMeasurementsAsync()).SerialNumber);
        Assert.Equal("D12002", Assert.Single(await second.ReadMeasurementsAsync()).SerialNumber);
        Assert.Contains(handler.Requests, x => x.Port == 12002 && x.AbsolutePath == "/peaks");
    }
    [Fact] public async Task StrictIdentityRejectsMissingDeviceSerialAndMissingIndex()
    {
        using var handler = new InvalidIdentityHandler(); using var http = new HttpClient(handler);
        await using var client = new PeakLoggerApiClient(http) { RequireStableIdentity = true };
        await client.ConnectAsync(new() { Host = "simulation", Port = 12345, UseSimulator = false });
        var reading = Assert.Single(await client.ReadMeasurementsAsync());
        Assert.Equal("REAL_ID", reading.SerialNumber); Assert.Equal(3, reading.PeakIndex);
    }
    [Fact] public async Task AppendTwiceHasTwoSessionsAndStableColumnsAndSkewStatus()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            await using (var log = await ExternalHumidityLog.OpenAsync(path, false, Guid.NewGuid(), "COM_TEST", "OFF", [], TimeSpan.FromSeconds(5))) { }
            byte[] first = await File.ReadAllBytesAsync(path);
            await using (var log = await ExternalHumidityLog.OpenAsync(path, true, Guid.NewGuid(), "COM_TEST", "api", [new("Z", "1", 2), new("A", "1", 1)], TimeSpan.FromSeconds(5)))
            {
                var now = DateTimeOffset.Now;
                string row = log.FormatRow(new(now, now.AddSeconds(-6), new(50, 25, "OK"), [], now, ""));
                Assert.Contains("RECEIPT_SKEW_EXCEEDED", row);
                Assert.DoesNotContain("\t0\t0\t", row);
            }
            byte[] all = await File.ReadAllBytesAsync(path);
            Assert.Equal(first, all.Take(first.Length));
            string text = Encoding.UTF8.GetString(all);
            Assert.Equal(2, text.Split("# Testo645 session").Length - 1);
            Assert.True(text.IndexOf("A/1/P1 [nm]", StringComparison.Ordinal) < text.IndexOf("Z/1/P2 [nm]", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }
    [Fact] public void DisabledProfileLogRetainsOldColumnsAndEnabledKeepsSourcesSeparate()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            using var old = new ProfileTemperatureLog(directory, "old", "test", true, DateTime.Now);
            old.Log(DateTime.Now, 20, 21, 40, 41);
            old.Dispose();
            Assert.DoesNotContain("Testo", File.ReadAllText(old.FilePath));
            using var enabled = new ProfileTemperatureLog(directory, "new", "test", false, DateTime.Now, true);
            enabled.Log(DateTime.Now, 20, 21, external: new(DateTimeOffset.Now, 55, 22, "OK"));
            var sample = Assert.Single(enabled.GetSamples());
            Assert.Null(sample.MeasuredHumidity); Assert.Equal(55, sample.ExternalHumidity!.HumidityPercent);
            enabled.Dispose();
            Assert.Contains("Testo vlhkosť", File.ReadAllText(enabled.FilePath));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class FakeTransport(byte[][] fragments) : ITesto645Transport
    {
        private readonly Queue<byte[]> _fragments = new(fragments);
        public int Opens, Disposals; public bool Timeout; public Action? BeforeRead; public byte[]? Request;
        public void Open() => Opens++;
        public void ClearInput() { }
        public void Write(byte[] request) => Request = request;
        public int Read(byte[] buffer)
        {
            BeforeRead?.Invoke();
            if (Timeout) throw new TimeoutException();
            if (_fragments.Count == 0) return 0;
            byte[] fragment = _fragments.Dequeue(); fragment.CopyTo(buffer, 0); return fragment.Length;
        }
        public void Dispose() => Disposals++;
    }
    private sealed class ApiHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!; Requests.Add(uri);
            if (uri.Port == 12002 && uri.AbsolutePath == "/api/v1/peaks") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"[{{\"index\":2,\"channel\":\"1.1\",\"wavelength\":1530,\"device\":{{\"deviceSN\":\"D{uri.Port}\"}}}}]") });
        }
    }
    private sealed class InvalidIdentityHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                [{"index":1,"channel":"1","wavelength":1501,"device":{}},
                 {"channel":"1","wavelength":1502,"device":{"deviceSN":"REAL_ID"}},
                 {"index":3,"channel":"1","wavelength":1503,"device":{"deviceSN":"REAL_ID"}}]
                """) });
    }
}
