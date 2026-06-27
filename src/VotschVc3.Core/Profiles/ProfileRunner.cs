using VotschVc3.Core.Communication;
using VotschVc3.Core.Protocol;

namespace VotschVc3.Core.Profiles;

/// <summary>
/// Executes a <see cref="TestProfile"/> against a <see cref="ChamberClient"/>.
/// The runner walks through every segment and cycle, recalculating the
/// interpolated set point on a fixed update interval and writing it to the
/// chamber. Because the timing lives on the PC the same profile works on any
/// chamber regardless of its built-in program features.
/// </summary>
public sealed class ProfileRunner
{
    private readonly ChamberClient _client;
    private readonly TimeSpan _updateInterval;

    /// <param name="client">A connected chamber client.</param>
    /// <param name="updateInterval">How often a fresh set point is written (default 5&#160;s).</param>
    public ProfileRunner(ChamberClient client, TimeSpan? updateInterval = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _updateInterval = updateInterval ?? TimeSpan.FromSeconds(5);
        if (_updateInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(updateInterval), "Update interval must be positive.");
        }
    }

    /// <summary>Raised whenever the runner writes a new set point.</summary>
    public event EventHandler<ProfileProgressEventArgs>? Progress;

    /// <summary>
    /// Runs the profile to completion (or until <paramref name="cancellationToken"/>
    /// is cancelled). The chamber is started by setting the start digital channel
    /// on every write.
    /// </summary>
    /// <param name="profile">The profile to execute.</param>
    /// <param name="startTemperature">
    /// The temperature the first ramp starts from – typically the current measured
    /// value. Read it from the chamber before calling this method.
    /// </param>
    /// <param name="startHumidity">Humidity the first ramp starts from.</param>
    /// <param name="cancellationToken">Cancels the run between / during segments.</param>
    public async Task RunAsync(
        TestProfile profile,
        double startTemperature,
        double? startHumidity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Segments.Count == 0)
        {
            return;
        }

        int cycles = Math.Max(1, profile.Cycles);
        double segStartTemp = startTemperature;
        double? segStartHum = startHumidity;

        for (int cycle = 0; cycle < cycles; cycle++)
        {
            for (int index = 0; index < profile.Segments.Count; index++)
            {
                ProfileSegment segment = profile.Segments[index];
                await RunSegmentAsync(
                    profile, segment, cycle, index, segStartTemp, segStartHum, cancellationToken)
                    .ConfigureAwait(false);

                // The next segment ramps from where this one ended.
                segStartTemp = segment.TargetTemperature;
                segStartHum = segment.TargetHumidity ?? segStartHum;
            }
        }
    }

    private async Task RunSegmentAsync(
        TestProfile profile,
        ProfileSegment segment,
        int cycle,
        int index,
        double startTemp,
        double? startHum,
        CancellationToken cancellationToken)
    {
        TimeSpan duration = segment.Duration > TimeSpan.Zero ? segment.Duration : TimeSpan.FromSeconds(1);

        // Guaranteed soak: hold the target and wait until the measured temperature
        // is within tolerance before starting to count the dwell time.
        if (!segment.IsRamp && segment.GuaranteedSoak)
        {
            await SoakWaitAsync(segment, cycle, index, startHum, cancellationToken).ConfigureAwait(false);
        }

        var segmentClock = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double elapsedSeconds = segmentClock.Elapsed.TotalSeconds;
            double fraction = Math.Clamp(elapsedSeconds / duration.TotalSeconds, 0d, 1d);

            double temperature = segment.TemperatureAt(fraction, startTemp);
            double? humidity = segment.HumidityAt(fraction, startHum);

            await WriteSetpointAsync(temperature, humidity, cancellationToken).ConfigureAwait(false);

            Progress?.Invoke(this, new ProfileProgressEventArgs(
                cycle, index, segment, fraction, temperature, humidity, segmentClock.Elapsed));

            if (fraction >= 1d)
            {
                return;
            }

            TimeSpan remaining = duration - segmentClock.Elapsed;
            TimeSpan delay = remaining < _updateInterval ? remaining : _updateInterval;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task SoakWaitAsync(
        ProfileSegment segment, int cycle, int index, double? startHum, CancellationToken cancellationToken)
    {
        double tolerance = Math.Abs(segment.SoakTolerance);
        double? humidity = segment.TargetHumidity ?? startHum;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Keep driving the chamber to the target while waiting.
            await WriteSetpointAsync(segment.TargetTemperature, humidity, cancellationToken).ConfigureAwait(false);

            double? measured;
            try
            {
                ChamberReading reading = await _client.ReadAsync(cancellationToken).ConfigureAwait(false);
                measured = reading.Temperature;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                measured = null;
            }

            Progress?.Invoke(this, new ProfileProgressEventArgs(
                cycle, index, segment, 0d, segment.TargetTemperature, humidity, TimeSpan.Zero, isSoaking: true));

            if (measured is { } m && Math.Abs(m - segment.TargetTemperature) <= tolerance)
            {
                return;
            }

            await Task.Delay(_updateInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task WriteSetpointAsync(double temperature, double? humidity, CancellationToken cancellationToken)
    {
        var digital = new DigitalChannels
        {
            StartChannelIndex = _client.Settings.StartChannelIndex,
            Start = true,
        };
        var setpoints = new List<double> { temperature, humidity ?? 0d };
        return _client.WriteSetpointsAsync(setpoints, digital, cancellationToken);
    }
}

/// <summary>Progress payload raised by <see cref="ProfileRunner"/> on every set point.</summary>
public sealed class ProfileProgressEventArgs : EventArgs
{
    public ProfileProgressEventArgs(
        int cycle,
        int segmentIndex,
        ProfileSegment segment,
        double fraction,
        double temperatureSetpoint,
        double? humiditySetpoint,
        TimeSpan elapsedInSegment,
        bool isSoaking = false)
    {
        Cycle = cycle;
        SegmentIndex = segmentIndex;
        Segment = segment;
        Fraction = fraction;
        TemperatureSetpoint = temperatureSetpoint;
        HumiditySetpoint = humiditySetpoint;
        ElapsedInSegment = elapsedInSegment;
        IsSoaking = isSoaking;
    }

    /// <summary><c>true</c> while waiting for the guaranteed-soak tolerance.</summary>
    public bool IsSoaking { get; }

    /// <summary>Zero based index of the current cycle.</summary>
    public int Cycle { get; }

    /// <summary>Zero based index of the current segment.</summary>
    public int SegmentIndex { get; }

    /// <summary>The segment currently executing.</summary>
    public ProfileSegment Segment { get; }

    /// <summary>Completion fraction of the current segment (0..1).</summary>
    public double Fraction { get; }

    /// <summary>The temperature set point just written.</summary>
    public double TemperatureSetpoint { get; }

    /// <summary>The humidity set point just written, if any.</summary>
    public double? HumiditySetpoint { get; }

    /// <summary>Time elapsed inside the current segment.</summary>
    public TimeSpan ElapsedInSegment { get; }
}
