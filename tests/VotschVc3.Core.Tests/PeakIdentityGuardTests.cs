using System.Text.Json;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class PeakIdentityGuardTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);
    private static PeakLoggerMeasurement Peak(string id, double wavelength, DateTimeOffset time, string channel = "1.2") =>
        new(time, "DEVICE", channel, id, int.Parse(id[1..]), wavelength, -20);
    private static List<PeakIdentityChannel> Channels() => new()
    {
        new() { Device = "DEVICE", Channel = "1.2", LastObservedAt = Start,
            Tracks = new() {
                new() { OriginalPeakId = "P1", OriginalPeakIndex = 1, ApiPeakId = "P1", WavelengthNm = 1510 },
                new() { OriginalPeakId = "P2", OriginalPeakIndex = 2, ApiPeakId = "P2", WavelengthNm = 1511 },
            } },
    };

    [Fact]
    public void ReindexingPreservesBindingAndAuditsBothAssignments()
    {
        var channels = Channels();
        var audit = new List<PeakIdentityEvent>();
        var time = Start.AddSeconds(1);
        var batch = new[] { Peak("P1", 1511.001, time), Peak("P2", 1510.001, time) };
        var accepted = PeakIdentityGuard.Observe(batch, channels, new(), time, audit.Add);
        Assert.Equal(1510.001, accepted.Single(p => p.PeakId == "P1").WavelengthNm);
        Assert.Equal(1511.001, accepted.Single(p => p.PeakId == "P2").WavelengthNm);
        Assert.Equal(2, audit.Count);
        Assert.Equal("P2", channels[0].Tracks[0].ApiPeakId);
        Assert.Equal(1511.001, batch[0].WavelengthNm); // Raw frame never modified.
        Assert.Null(channels[0].Problem);
    }

    [Theory]
    [InlineData("merge")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("close")]
    [InlineData("jump")]
    [InlineData("stale")]
    [InlineData("gap")]
    [InlineData("nan")]
    public void AmbiguityIsLatchedAcrossReturnAndSerialization(string scenario)
    {
        var channels = Channels();
        var audit = new List<PeakIdentityEvent>();
        var time = Start.AddSeconds(scenario == "gap" ? 121 : 1);
        var batch = new List<PeakLoggerMeasurement> { Peak("P1", 1510, time), Peak("P2", 1511, time) };
        switch (scenario)
        {
            case "merge": batch.RemoveAt(1); break;
            case "extra": batch.Add(Peak("P3", 1512, time)); break;
            case "duplicate": batch[1] = batch[1] with { PeakId = "P1" }; break;
            case "close": batch[1] = batch[1] with { WavelengthNm = 1510.001 }; break;
            case "jump": batch[1] = batch[1] with { WavelengthNm = 1515 }; break;
            case "stale": batch[0] = batch[0] with { Timestamp = time.AddSeconds(-11) }; break;
            case "nan": batch[0] = batch[0] with { WavelengthNm = double.NaN }; break;
        }
        Assert.Empty(PeakIdentityGuard.Observe(batch, channels, new(), time, audit.Add));
        Assert.NotNull(channels[0].Problem);
        Assert.Single(audit);
        channels = JsonSerializer.Deserialize<List<PeakIdentityChannel>>(JsonSerializer.Serialize(channels))!;
        time = time.AddSeconds(1);
        Assert.Empty(PeakIdentityGuard.Observe(new[] { Peak("P1", 1510, time), Peak("P2", 1511, time) }, channels, new(), time, audit.Add));
        Assert.Single(audit);
    }

    [Fact]
    public void MultipleGlobalAssignmentsAbstainInsteadOfChoosingNearest()
    {
        var channels = Channels();
        channels[0].Tracks[1].WavelengthNm = 1510.1;
        var time = Start.AddSeconds(60);
        Assert.Empty(PeakIdentityGuard.Observe(new[] { Peak("P1", 1510.03, time), Peak("P2", 1510.07, time) }, channels, new(), time, _ => { }));
        Assert.Contains("viac možných", channels[0].Problem);
    }

    [Fact]
    public void RepeatedTimestampCannotCreateStableSamples()
    {
        var channels = Channels();
        var time = Start.AddSeconds(1);
        var batch = new[] { Peak("P1", 1510, time), Peak("P2", 1511, time) };
        Assert.Equal(2, PeakIdentityGuard.Observe(batch, channels, new(), time, _ => { }).Count);
        Assert.Empty(PeakIdentityGuard.Observe(batch, channels, new(), time.AddSeconds(1), _ => { }));
    }

    [Fact]
    public void UnaffectedChannelContinuesDuringSimultaneousLossAndReindexing()
    {
        var channels = Channels();
        channels.Add(new PeakIdentityChannel { Device = "DEVICE", Channel = "2.1", LastObservedAt = Start,
            Tracks = new() { new() { OriginalPeakId = "P1", OriginalPeakIndex = 1, ApiPeakId = "P1", WavelengthNm = 1550 } } });
        var time = Start.AddSeconds(1);
        var accepted = PeakIdentityGuard.Observe(new[] { Peak("P1", 1510.5, time), Peak("P9", 1550, time, "2.1") }, channels, new(), time, _ => { });
        Assert.Single(accepted);
        Assert.Equal("P1", accepted[0].PeakId);
        Assert.Equal("2.1", accepted[0].Channel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingPeakSkipsPointWithoutOperatorAndRemainsExcludedAtNextPoint(bool supervision)
    {
        string root = Path.Combine(Path.GetTempPath(), "fbg-identity-" + Guid.NewGuid().ToString("N"));
        try
        {
            var setup = new CalibrationSetup { Settings = new() { OperatorSupervisionEnabled = supervision,
                PeakLostPolicy = CalibrationFailurePolicy.PauseForOperator, ChamberStableDuration = TimeSpan.Zero,
                RequiredStableSamples = 2, RequiredMeasurementSamples = 2, SampleAcquisitionIntervalSeconds = 1,
                MaxWavelengthDriftPmPerMinute = 0 }, Mappings = new() {
                    new() { SerialNumber = "123456/0001", PeakLoggerDeviceSerialNumber = "DEVICE", Channel = "1.2", PeakId = "P1", PeakIndex = 1, Selected = true }
                } };
            var run = new CalibrationRunRecord();
            var store = new CalibrationStore(root);
            await using var writer = store.CreateRunWriter(run);
            await using var logger = new SequenceLogger();
            var orchestrator = new CalibrationOrchestrator(logger);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            // Three good samples: stable qualification followed by one partial final sample; then merge.
            var point = await orchestrator.WaitForPlateauAsync(run, setup, 0, 2, 20,
                _ => Task.FromResult(20d), _ => Task.FromResult<double?>(20d), writer, cancellationToken: timeout.Token);
            var target = Assert.Single(point.Targets);
            Assert.Equal(CalibrationTargetState.SkippedIdentityUncertain, target.Status);
            Assert.Equal(0, target.SampleCount);
            Assert.Empty(target.StableSamples);
            Assert.Equal(setup.Mappings[0].PhysicalFbgId, target.PhysicalFbgId);
            Assert.Contains(run.Warnings, w => w.Code == "FBG_POINT_SKIPPED_IDENTITY" && w.PlateauIndex == 0);
            // Already ambiguous: even a bad reference must not demand an operator for this skipped target.
            var next = await orchestrator.WaitForPlateauAsync(run, setup, 1, 2, 40,
                _ => throw new Exception("A skipped point must not wait on reference acquisition"), null, writer, cancellationToken: timeout.Token);
            Assert.Equal(CalibrationTargetState.SkippedIdentityUncertain, next.Targets[0].Status);
            Assert.Contains(run.Warnings, w => w.PlateauIndex == 1);
            var raw = Directory.GetFiles(root, "peak-observations.jsonl", SearchOption.AllDirectories).Single();
            using (var reader = new StreamReader(new FileStream(raw, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                Assert.Equal(4, reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            // Even a malformed imported skipped result with finite samples must never enter a fit.
            target.SampleCount = 50; target.MeanWavelengthNm = 1510;
            run.Plateaus.Add(point);
            Assert.All(TemperatureCalibrationAnalyzer.Analyze(run), r => Assert.Equal(0, r.PointCount));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RunnerAutonomouslyAdvancesAllSkippedPointsAndOnlyStopsAfterFinalReturn()
    {
        string root = Path.Combine(Path.GetTempPath(), "fbg-identity-run-" + Guid.NewGuid().ToString("N"));
        try
        {
            var profile = new TestProfile { Name = "Identity loss", ExecutionMode = ProfileExecutionMode.TemperatureCalibration,
                Segments = { new() { TargetTemperature = 20, IsCalibrationPoint = true, IsRamp = false }, new() { TargetTemperature = 40, IsCalibrationPoint = true, IsRamp = false } } };
            var setup = new CalibrationSetup { ProfileId = profile.Id, Settings = new() { EnableSetpointRamp = false,
                ChamberStableDuration = TimeSpan.Zero, MaxChamberDriftCPerMinute = 0,
                OperatorSupervisionEnabled = true, PeakLostPolicy = CalibrationFailurePolicy.AbortCalibration },
                Mappings = new() { new() { SerialNumber = "123456/0001", PeakLoggerDeviceSerialNumber = "DEVICE",
                    Channel = "1.2", PeakId = "P1", PeakIndex = 1, Selected = true } } };
            var run = new CalibrationRunRecord { ProfileId = profile.Id, ChamberId = Guid.NewGuid() };
            var store = new CalibrationStore(root);
            await using var writer = store.CreateRunWriter(run);
            await using var logger = new SequenceLogger { MergeImmediately = true };
            await using var chamber = new RecordingChamber();
            var orchestrator = new CalibrationOrchestrator(logger);
            orchestrator.OperatorAttentionRequired += _ => throw new Exception("Identity must not request an operator");
            var runner = new CalibrationProfileRunner(chamber, orchestrator, store);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await runner.RunAsync(profile, setup, run, writer, 20, null, cancellationToken: timeout.Token);
            Assert.Equal(new[] { 20d, 40d, 25d }, chamber.Setpoints);
            Assert.Equal(1, chamber.Stops); // Existing normal completion, never an identity-induced STOP.
            Assert.Equal(25, chamber.TemperatureAtStop);
            Assert.Equal(CalibrationRunState.CompletedWithWarnings, run.State);
            Assert.Equal(2, run.Plateaus.Count);
            Assert.All(run.Plateaus, p => Assert.Equal(CalibrationTargetState.SkippedIdentityUncertain, p.Targets[0].Status));
            Assert.Equal(CalibrationTargetState.SkippedIdentityUncertain, run.FinalVerification!.Targets[0].Status);
            Assert.Equal(3, run.Warnings.Count(w => w.Code == "FBG_POINT_SKIPPED_IDENTITY"));
            Assert.All(run.CalibrationResults, r => Assert.Equal(0, r.PointCount));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class RecordingChamber : IChamberDevice
    {
        public List<double> Setpoints { get; } = new();
        public int Stops { get; private set; }
        public double TemperatureAtStop { get; private set; }
        private double _lastRead;
        public bool IsConnected => true;
        public ChamberConnectionSettings Settings { get; } = new();
        public event EventHandler<FrameExchangedEventArgs>? FrameExchanged { add { } remove { } }
        public Task ConnectAsync(ChamberConnectionSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<ChamberReading> ReadAsync(CancellationToken cancellationToken = default)
        {
            _lastRead = Setpoints.LastOrDefault(20);
            return Task.FromResult(new ChamberReading(DateTimeOffset.Now, "fake", new[] { _lastRead }, new DigitalChannels()));
        }
        public Task WriteSetpointsAsync(IReadOnlyList<double> setpoints, DigitalChannels digital, CancellationToken cancellationToken = default)
        { Setpoints.Add(setpoints[0]); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken = default) { Stops++; TemperatureAtStop = _lastRead; return Task.CompletedTask; }
        public Task<string> SendRawAsync(string frame, CancellationToken cancellationToken = default) => Task.FromResult("");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SequenceLogger : IPeakLoggerClient
    {
        public bool MergeImmediately { get; init; }
        private int _reads;
        public bool IsConnected => true;
        public DateTimeOffset? LastDataTimestamp => DateTimeOffset.UtcNow;
        public Task ConnectAsync(PeakLoggerSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<IReadOnlyList<PeakLoggerSensor>> DiscoverSensorsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PeakLoggerSensor>>(new[] { new PeakLoggerSensor("DEVICE", "1.2", new[] {
                new PeakLoggerPeak("P1", 1, 1510, -20), new PeakLoggerPeak("P2", 2, 1511, -20) }) });
        public Task<IReadOnlyList<PeakLoggerMeasurement>> ReadMeasurementsAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            IReadOnlyList<PeakLoggerMeasurement> batch = ++_reads < 4 && !MergeImmediately ? new[] { Peak("P1", 1510, now), Peak("P2", 1511, now) } : new[] { Peak("P1", 1510.5, now) };
            return Task.FromResult(batch);
        }
    }
}
