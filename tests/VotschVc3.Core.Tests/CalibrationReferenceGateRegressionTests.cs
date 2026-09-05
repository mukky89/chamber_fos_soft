using VotschVc3.Core.Calibration;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationReferenceGateRegressionTests
{
    [Fact]
    public async Task Runner_DoesNotRequireChamberActualTemperatureToBeStable_WhenReferenceIsStable()
    {
        string root = Path.Combine(Path.GetTempPath(), "VotschVc3-reference-gate-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var peakLogger = new FakePeakLoggerClient();
            await peakLogger.ConnectAsync(new PeakLoggerSettings());
            await using var chamber = new OffsetFakeChamber(actualTemperature: 7.0);
            await chamber.ConnectAsync(new ChamberConnectionSettings());

            var profile = new TestProfile
            {
                Name = "Reference only gate",
                ExecutionMode = ProfileExecutionMode.TemperatureCalibration,
                Segments =
                {
                    new ProfileSegment
                    {
                        Name = "20 C",
                        IsRamp = false,
                        IsCalibrationPoint = true,
                        TargetTemperature = 20,
                        Duration = TimeSpan.FromMilliseconds(1),
                    },
                },
            };

            var setup = new CalibrationSetup
            {
                ProfileId = profile.Id,
                Settings = new CalibrationProfileSettings
                {
                    EnableSetpointRamp = false,
                    RequiredStableSamples = 2,
                    RequiredMeasurementSamples = 2,
                    MaxWavelengthRangePm = 0,
                    MaxWavelengthStdDevPm = 0,
                    MaxWavelengthDriftPmPerMinute = 0,
                    ChamberToleranceC = 0.5,
                    ChamberStableDuration = TimeSpan.Zero,
                    MaxChamberDriftCPerMinute = 0,
                    ChamberStabilityTimeout = TimeSpan.FromSeconds(2),
                    FinalConditioningDuration = TimeSpan.Zero,
                    DefaultSensorStabilizationTimeout = TimeSpan.FromSeconds(5),
                    SensorTimeoutPolicy = CalibrationFailurePolicy.AbortCalibration,
                    PeakLostPolicy = CalibrationFailurePolicy.AbortCalibration,
                    PeakLoggerDisconnectPolicy = CalibrationFailurePolicy.AbortCalibration,
                },
                Mappings =
                {
                    new CalibrationSensorMapping
                    {
                        SerialNumber = "123456/0001",
                        PeakLoggerDeviceSerialNumber = "242805A000004",
                        Channel = "3.2",
                        PeakId = "P4",
                        PeakIndex = 4,
                        Selected = true,
                    },
                },
            };
            setup.CalibrationSegmentIndices.Add(0);

            var store = new CalibrationStore(root);
            var run = new CalibrationRunRecord
            {
                ProfileId = profile.Id,
                ProfileName = profile.Name,
                ChamberId = Guid.NewGuid(),
            };
            await using CalibrationRunWriter writer = store.CreateRunWriter(run);
            var runner = new CalibrationProfileRunner(chamber, new CalibrationOrchestrator(peakLogger), store);

            // WIKA sits exactly on the requested plateau while the chamber's own actual value
            // deliberately remains far away. The run must proceed because WIKA is authoritative.
            await runner.RunAsync(
                profile,
                setup,
                run,
                writer,
                7.0,
                null,
                _ => Task.FromResult<double?>(20.0));

            CalibrationPlateauResult plateau = Assert.Single(run.Plateaus);
            Assert.Equal(7.0, plateau.ActualTemperatureC, 6);
            Assert.Equal(20.0, Assert.IsType<double>(plateau.ReferenceTemperatureC), 6);
            Assert.Equal(CalibrationTargetState.Stable, Assert.Single(plateau.Targets).Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class OffsetFakeChamber : IChamberDevice
    {
        private readonly double _actualTemperature;
        private double _setpoint;

        public OffsetFakeChamber(double actualTemperature)
        {
            _actualTemperature = actualTemperature;
            _setpoint = actualTemperature;
        }

        public bool IsConnected { get; private set; }
        public ChamberConnectionSettings Settings { get; private set; } = new();
        public event EventHandler<FrameExchangedEventArgs>? FrameExchanged;

        public Task ConnectAsync(ChamberConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            Settings = settings.Clone();
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<ChamberReading> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ChamberReading(
                DateTimeOffset.Now,
                "offset-fake",
                new[] { _actualTemperature, _setpoint },
                new DigitalChannels { StartChannelIndex = Settings.StartChannelIndex }));
        }

        public Task WriteSetpointsAsync(
            IReadOnlyList<double> setpoints,
            DigitalChannels digital,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (setpoints.Count > 0) _setpoint = setpoints[0];
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> SendRawAsync(string frame, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
