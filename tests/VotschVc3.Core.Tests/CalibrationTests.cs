using System.Net;
using System.Text;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationTests
{
    [Fact]
    public void RollingStability_RequiresExactlyConfiguredSampleCount()
    {
        var detector = new RollingStabilityDetector(50, maxRangePm: 2, maxStdDevPm: 1, maxDriftPmPerMinute: 1);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        StabilityMetrics metrics = default!;
        for (int i = 0; i < 49; i++)
        {
            metrics = detector.Add(t0.AddSeconds(i), 1550.0000);
        }
        Assert.False(metrics.IsStable);
        Assert.Equal(49, metrics.Count);

        metrics = detector.Add(t0.AddSeconds(49), 1550.0000);
        Assert.True(metrics.IsStable);
        Assert.Equal(50, metrics.Count);
    }

    [Fact]
    public void RollingStability_RejectsNoisyWindow()
    {
        var detector = new RollingStabilityDetector(50, maxRangePm: 5, maxStdDevPm: 2, maxDriftPmPerMinute: 0);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;
        StabilityMetrics metrics = default!;

        for (int i = 0; i < 50; i++)
        {
            double value = 1550 + (i % 2 == 0 ? 0.010 : -0.010);
            metrics = detector.Add(t0.AddSeconds(i), value);
        }

        Assert.False(metrics.IsStable);
        Assert.True(metrics.Range > 5);
    }

    [Fact]
    public void RollingStability_SlidingWindowCanRecoverAfterNoise()
    {
        var detector = new RollingStabilityDetector(50, maxRangePm: 2, maxStdDevPm: 1, maxDriftPmPerMinute: 1);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        for (int i = 0; i < 10; i++)
        {
            detector.Add(t0.AddSeconds(i), 1550 + i * 0.005);
        }

        StabilityMetrics metrics = default!;
        for (int i = 0; i < 50; i++)
        {
            metrics = detector.Add(t0.AddSeconds(10 + i), 1550.1234);
        }

        Assert.True(metrics.IsStable);
        Assert.Equal(50, detector.Count);
    }

    [Fact]
    public void RollingStability_RejectsExcessiveDrift()
    {
        var detector = new RollingStabilityDetector(50, maxRangePm: 0, maxStdDevPm: 0, maxDriftPmPerMinute: 0.5);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;
        StabilityMetrics metrics = default!;

        for (int i = 0; i < 50; i++)
        {
            // +1 pm/minute linear drift.
            double value = 1550 + (i / 60.0) * 0.001;
            metrics = detector.Add(t0.AddSeconds(i), value);
        }

        Assert.False(metrics.IsStable);
        Assert.True(Math.Abs(metrics.SlopePerMinute) > 0.5);
    }

    [Fact]
    public async Task FakePeakLogger_DiscoversMultipleSensorsAndTenPeaksOnOneSn()
    {
        await using var peakLogger = new FakePeakLoggerClient();
        await peakLogger.ConnectAsync(new PeakLoggerSettings());
        IReadOnlyList<PeakLoggerSensor> sensors = await peakLogger.DiscoverSensorsAsync();

        Assert.True(sensors.Count >= 4);
        PeakLoggerSensor sensor = Assert.Single(sensors, s => s.SerialNumber == "242805A000004");
        Assert.Equal(10, sensor.Peaks.Count);
        Assert.Equal(10, sensor.Peaks.Select(p => p.PeakId).Distinct().Count());
    }

    [Fact]
    public async Task PeakLoggerApi_UsesLocal43122AndMapsDocumentedPeakSchema()
    {
        const string json = """
            [
              {
                "index": 1,
                "channel": "4.1",
                "wavelength": 1512.9482421875,
                "cog": 1512.95203956298,
                "intensity": -24.5599994659424,
                "returnLoss": -53,
                "slsr": 0,
                "width": 0.2,
                "asymmetry": 0.995,
                "device": {
                  "deviceType": "Hyperion",
                  "deviceSN": "HIAER3",
                  "connector": 4
                },
                "fos4x": null
              },
              {
                "index": 2,
                "channel": "4.1",
                "wavelength": 1516.57470703125,
                "cog": 1516.5737324377,
                "intensity": -24.25,
                "returnLoss": -53,
                "slsr": 0,
                "width": 0.184,
                "asymmetry": 0.942,
                "device": {
                  "deviceType": "Hyperion",
                  "deviceSN": "HIAER3",
                  "connector": 4
                },
                "fos4x": null
              }
            ]
            """;

        var handler = new PeakLoggerHttpHandler(json);
        using var http = new HttpClient(handler);
        await using var peakLogger = new PeakLoggerApiClient(http);

        await peakLogger.ConnectAsync(new PeakLoggerSettings { Host = "localhost", Port = 0 });
        IReadOnlyList<PeakLoggerSensor> sensors = await peakLogger.DiscoverSensorsAsync();
        IReadOnlyList<PeakLoggerMeasurement> measurements = await peakLogger.ReadMeasurementsAsync();

        PeakLoggerSensor sensor = Assert.Single(sensors);
        Assert.Equal("HIAER3", sensor.SerialNumber);
        Assert.Equal("4.1", sensor.Channel);
        Assert.Collection(sensor.Peaks,
            p =>
            {
                Assert.Equal("P1", p.PeakId);
                Assert.Equal(1, p.PeakIndex);
                Assert.Equal(1512.9482421875, p.WavelengthNm, 10);
                Assert.Equal(-24.5599994659424, p.Intensity!.Value, 10);
            },
            p =>
            {
                Assert.Equal("P2", p.PeakId);
                Assert.Equal(2, p.PeakIndex);
            });

        Assert.Equal(2, measurements.Count);
        Assert.All(measurements, m => Assert.Equal("HIAER3", m.SerialNumber));
        Assert.NotNull(peakLogger.LastDataTimestamp);
        Assert.Contains(handler.Requests, u => u.Host == "localhost" && u.Port == PeakLoggerApiClient.DefaultPort && u.AbsolutePath == "/api/v1/peaks");
        Assert.Equal("api/v1/peaks", peakLogger.PeaksPath);
    }

    [Fact]
    public async Task PeakLoggerApi_404PeaksReturnsEmptySetLikeExistingIntegration()
    {
        var handler = new PeakLoggerHttpHandler("[]", peaksStatus: HttpStatusCode.NotFound);
        using var http = new HttpClient(handler);
        await using var peakLogger = new PeakLoggerApiClient(http);

        await peakLogger.ConnectAsync(new PeakLoggerSettings());

        Assert.Empty(await peakLogger.DiscoverSensorsAsync());
        Assert.Empty(await peakLogger.ReadMeasurementsAsync());
    }

    [Fact]
    public async Task Preflight_UsesStableIdentitySerialChannelPeakId()
    {
        await using var peakLogger = new FakePeakLoggerClient();
        await peakLogger.ConnectAsync(new PeakLoggerSettings());
        var orchestrator = new CalibrationOrchestrator(peakLogger);
        var setup = new CalibrationSetup
        {
            Mappings =
            {
                new CalibrationSensorMapping
                {
                    SerialNumber = "242805A000004",
                    Channel = "3.2",
                    PeakId = "P4",
                    PeakIndex = 99,
                    Selected = true,
                },
            },
        };

        await orchestrator.PreflightAsync(setup);

        CalibrationSensorMapping mapping = Assert.Single(setup.Mappings);
        Assert.Equal(4, mapping.PeakIndex);
        Assert.NotNull(mapping.CurrentWavelengthNm);
    }

    [Fact]
    public async Task Preflight_SeparatesProductionFbgSnFromPeakLoggerDeviceSn()
    {
        await using var peakLogger = new FakePeakLoggerClient();
        await peakLogger.ConnectAsync(new PeakLoggerSettings());
        var orchestrator = new CalibrationOrchestrator(peakLogger);
        var setup = new CalibrationSetup
        {
            Mappings =
            {
                new CalibrationSensorMapping
                {
                    SerialNumber = "FBG/PRODUCTION/0001",
                    PeakLoggerDeviceSerialNumber = "242805A000004",
                    Channel = "3.2",
                    PeakId = "P4",
                    Selected = true,
                },
            },
        };

        await orchestrator.PreflightAsync(setup);

        CalibrationSensorMapping mapping = Assert.Single(setup.Mappings);
        Assert.Equal("FBG/PRODUCTION/0001", mapping.SerialNumber);
        Assert.Equal("242805A000004", mapping.PeakLoggerDeviceSerialNumber);
        Assert.Equal("242805A000004|3.2|P4", mapping.SourceIdentity);
        Assert.Equal("FBG/PRODUCTION/0001|3.2|P4", mapping.Identity);
        Assert.Equal(4, mapping.PeakIndex);
    }

    [Fact]
    public void TemperatureResponseValidation_PassesResponsivePeak()
    {
        var orchestrator = new CalibrationOrchestrator(new FakePeakLoggerClient());
        var run = new CalibrationRunRecord();
        CalibrationPlateauResult baseline = Plateau(0, 20, 1550.000);
        CalibrationPlateauResult current = Plateau(1, 40, 1550.200);
        var settings = new CalibrationProfileSettings
        {
            ValidationMinimumDeltaTemperatureC = 5,
            ValidationMinimumWavelengthResponsePm = 10,
            ValidationFailurePolicy = CalibrationFailurePolicy.ContinueAndFlag,
        };

        Assert.True(orchestrator.ValidateTemperatureResponse(run, baseline, current, settings));
        Assert.Empty(run.Warnings);
    }

    [Fact]
    public void TemperatureResponseValidation_FlagsNonResponsivePeak()
    {
        var orchestrator = new CalibrationOrchestrator(new FakePeakLoggerClient());
        var run = new CalibrationRunRecord();
        CalibrationPlateauResult baseline = Plateau(0, 20, 1550.000);
        CalibrationPlateauResult current = Plateau(1, 40, 1550.001);
        var settings = new CalibrationProfileSettings
        {
            ValidationMinimumDeltaTemperatureC = 5,
            ValidationMinimumWavelengthResponsePm = 10,
            ValidationFailurePolicy = CalibrationFailurePolicy.ContinueAndFlag,
        };

        Assert.False(orchestrator.ValidateTemperatureResponse(run, baseline, current, settings));
        CalibrationWarning warning = Assert.Single(run.Warnings);
        Assert.Equal("NO_TEMPERATURE_RESPONSE", warning.Code);
    }

    [Fact]
    public void CalibrationStore_RoundTripsSetupRunAndCheckpoint()
    {
        string root = Path.Combine(Path.GetTempPath(), "VotschVc3-cal-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CalibrationStore(root);
            Guid profileId = Guid.NewGuid();
            Guid chamberId = Guid.NewGuid();
            var setup = new CalibrationSetup
            {
                ProfileId = profileId,
                Mappings =
                {
                    new CalibrationSensorMapping
                    {
                        SerialNumber = "SN1",
                        PeakLoggerDeviceSerialNumber = "HIAER3",
                        Channel = "1.1",
                        PeakId = "P2",
                        PeakIndex = 2,
                        Selected = true,
                    },
                },
            };
            store.SaveSetup(setup);
            CalibrationSetup loaded = Assert.IsType<CalibrationSetup>(store.LoadSetup(profileId));
            CalibrationSensorMapping loadedMapping = Assert.Single(loaded.Mappings);
            Assert.Equal("P2", loadedMapping.PeakId);
            Assert.Equal("HIAER3", loadedMapping.PeakLoggerDeviceSerialNumber);

            var run = new CalibrationRunRecord { ProfileId = profileId, ChamberId = chamberId, ProfileName = "T CAL", State = CalibrationRunState.Completed };
            run.Plateaus.Add(Plateau(0, 20, 1550));
            store.SaveRun(run);
            Assert.Contains(store.LoadHistory(), x => x.RunId == run.RunId);

            var checkpoint = new CalibrationCheckpoint { RunId = run.RunId, ProfileId = profileId, ChamberId = chamberId, CurrentPlateauIndex = 1 };
            store.SaveCheckpoint(checkpoint);
            Assert.Equal(1, store.LoadCheckpoint(chamberId)?.CurrentPlateauIndex);
            store.DeleteCheckpoint(chamberId);
            Assert.Null(store.LoadCheckpoint(chamberId));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProfileClone_PreservesCalibrationMetadata()
    {
        var profile = new TestProfile
        {
            ExecutionMode = ProfileExecutionMode.TemperatureCalibration,
            Segments = { new ProfileSegment { IsRamp = false, IsCalibrationPoint = true, TargetTemperature = 60 } },
        };

        TestProfile clone = profile.Clone();
        Assert.Equal(ProfileExecutionMode.TemperatureCalibration, clone.ExecutionMode);
        Assert.True(Assert.Single(clone.Segments).IsCalibrationPoint);
    }

    private static CalibrationPlateauResult Plateau(int index, double temp, double wavelength) => new()
    {
        PlateauIndex = index,
        TargetTemperatureC = temp,
        ActualTemperatureC = temp,
        Targets =
        {
            new CalibrationMeasurementResult
            {
                SerialNumber = "SN1",
                Channel = "1.1",
                PeakId = "P1",
                PeakIndex = 1,
                Status = CalibrationTargetState.Stable,
                MeanWavelengthNm = wavelength,
            },
        },
    };

    private sealed class PeakLoggerHttpHandler : HttpMessageHandler
    {
        private readonly string _peaksJson;
        private readonly HttpStatusCode _peaksStatus;
        private int _peakRequests;

        public PeakLoggerHttpHandler(string peaksJson, HttpStatusCode peaksStatus = HttpStatusCode.OK)
        {
            _peaksJson = peaksJson;
            _peaksStatus = peaksStatus;
        }

        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = Assert.IsType<Uri>(request.RequestUri);
            Requests.Add(uri);

            if (uri.AbsolutePath.Equals("/api/v1/peaks", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.Equals("/peaks", StringComparison.OrdinalIgnoreCase))
            {
                _peakRequests++;
                HttpStatusCode status = _peaksStatus == HttpStatusCode.NotFound && _peakRequests == 1
                    ? HttpStatusCode.OK
                    : _peaksStatus;
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(_peaksJson, Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
