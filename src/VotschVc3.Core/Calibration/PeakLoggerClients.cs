namespace VotschVc3.Core.Calibration;

public interface IPeakLoggerClient : IAsyncDisposable
{
    bool IsConnected { get; }
    DateTimeOffset? LastDataTimestamp { get; }

    Task ConnectAsync(PeakLoggerSettings settings, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<IReadOnlyList<PeakLoggerSensor>> DiscoverSensorsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PeakLoggerMeasurement>> ReadMeasurementsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional hook implemented by the simulator so a calibration runner can feed it
/// the actual chamber temperature. Production PeakLogger adapters do not implement it.
/// </summary>
public interface IPeakLoggerSimulationControl
{
    double SimulatedTemperatureC { get; set; }
}

public enum FakePeakLoggerScenario
{
    Normal,
    OneNonResponsivePeak,
    OneNoisySlowPeak,
    OneNeverStablePeak,
    DisconnectAfterSamples,
    PeakDisappears,
}

/// <summary>
/// Deterministic PeakLogger simulator used for development and unit tests. A sensor
/// identity is SerialNumber + Channel + PeakId; wavelength itself is deliberately not
/// used as identity because it changes with temperature.
/// </summary>
public sealed class FakePeakLoggerClient : IPeakLoggerClient, IPeakLoggerSimulationControl
{
    private readonly Random _random;
    private readonly List<PeakLoggerSensor> _sensors;
    private PeakLoggerSettings _settings = new();
    private int _readCount;

    public FakePeakLoggerClient(FakePeakLoggerScenario scenario = FakePeakLoggerScenario.Normal, int randomSeed = 12345)
    {
        Scenario = scenario;
        _random = new Random(randomSeed);
        _sensors = BuildSensors();
    }

    public FakePeakLoggerScenario Scenario { get; set; }
    public bool IsConnected { get; private set; }
    public DateTimeOffset? LastDataTimestamp { get; private set; }
    public double SimulatedTemperatureC { get; set; } = 20.0;

    public Task ConnectAsync(PeakLoggerSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        IsConnected = true;
        _readCount = 0;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PeakLoggerSensor>> DiscoverSensorsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureConnected();
        return Task.FromResult<IReadOnlyList<PeakLoggerSensor>>(_sensors);
    }

    public Task<IReadOnlyList<PeakLoggerMeasurement>> ReadMeasurementsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureConnected();
        _readCount++;

        if (Scenario == FakePeakLoggerScenario.DisconnectAfterSamples && _readCount > 80)
        {
            IsConnected = false;
            throw new IOException("Simulovaná strata spojenia s PeakLoggerom.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var result = new List<PeakLoggerMeasurement>();

        foreach (PeakLoggerSensor sensor in _sensors)
        {
            foreach (PeakLoggerPeak peak in sensor.Peaks)
            {
                if (Scenario == FakePeakLoggerScenario.PeakDisappears &&
                    sensor.SerialNumber == "242805A000005" && peak.PeakIndex == 2 && _readCount > 30)
                {
                    continue;
                }

                bool nonResponsive = Scenario == FakePeakLoggerScenario.OneNonResponsivePeak &&
                                     sensor.SerialNumber == "242805A000003" && peak.PeakIndex == 1;
                bool noisySlow = Scenario == FakePeakLoggerScenario.OneNoisySlowPeak &&
                                 sensor.SerialNumber == "242805A000005" && peak.PeakIndex == 2;
                bool neverStable = Scenario == FakePeakLoggerScenario.OneNeverStablePeak &&
                                   sensor.SerialNumber == "242805A000005" && peak.PeakIndex == 2;

                double tempResponseNm = nonResponsive ? 0 : (SimulatedTemperatureC - 20.0) * 0.010;
                double noisePm = neverStable
                    ? (_random.NextDouble() - 0.5) * 30.0
                    : noisySlow && _readCount < 90
                        ? (_random.NextDouble() - 0.5) * 12.0
                        : (_random.NextDouble() - 0.5) * 0.8;
                double wavelength = peak.WavelengthNm + tempResponseNm + noisePm / 1000.0;
                double intensity = (peak.Intensity ?? -20) + (_random.NextDouble() - 0.5) * 0.4;

                result.Add(new PeakLoggerMeasurement(
                    now,
                    sensor.SerialNumber,
                    sensor.Channel,
                    peak.PeakId,
                    peak.PeakIndex,
                    wavelength,
                    intensity));
            }
        }

        LastDataTimestamp = now;
        return Task.FromResult<IReadOnlyList<PeakLoggerMeasurement>>(result);
    }

    private static List<PeakLoggerSensor> BuildSensors()
    {
        static PeakLoggerPeak Peak(int i, double nm) => new($"P{i}", i, nm, -20 - i);

        return new List<PeakLoggerSensor>
        {
            new("242805A000004", "3.2", Enumerable.Range(1, 10).Select(i => Peak(i, 1510 + i * 4.2)).ToArray()),
            new("242805A000003", "3.3", new[] { Peak(1, 1531.20), Peak(2, 1540.10), Peak(3, 1550.40) }),
            new("242805A000001", "3.4", new[] { Peak(1, 1522.70), Peak(2, 1561.20) }),
            new("242805A000005", "4.3", new[] { Peak(1, 1534.90), Peak(2, 1558.90), Peak(3, 1570.10) }),
            new("242805A000002", "4.4", new[] { Peak(1, 1542.40), Peak(2, 1564.80) }),
        };
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("PeakLogger nie je pripojený.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Production adapter seam. The concrete PeakLogger REST/streaming contract was not
/// present in this repository, therefore no endpoint names or response schema are
/// guessed here. Fill this adapter once the vendor API documentation is supplied.
/// </summary>
public sealed class PeakLoggerApiClient : IPeakLoggerClient
{
    private PeakLoggerSettings? _settings;

    public bool IsConnected { get; private set; }
    public DateTimeOffset? LastDataTimestamp { get; private set; }

    public Task ConnectAsync(PeakLoggerSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        throw MissingContract();
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PeakLoggerSensor>> DiscoverSensorsAsync(CancellationToken cancellationToken = default) =>
        throw MissingContract();

    public Task<IReadOnlyList<PeakLoggerMeasurement>> ReadMeasurementsAsync(CancellationToken cancellationToken = default) =>
        throw MissingContract();

    private static NotSupportedException MissingContract() => new(
        "PeakLogger API kontrakt nie je v repozitári. Doplň vendor dokumentáciu: host/port, autentifikáciu, " +
        "sensor discovery response, measurement response a stabilné polia SerialNumber/Channel/PeakId/Wavelength/Intensity.");

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }
}
