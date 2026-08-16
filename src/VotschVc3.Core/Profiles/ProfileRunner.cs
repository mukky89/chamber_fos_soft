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
    private readonly IChamberDevice _client;
    private readonly TimeSpan _updateInterval;
    private readonly bool _soakAllHolds;
    private readonly bool _soakAllSegments;
    private readonly double _defaultSoakTolerance;

    // Pause gate: set = running, reset = paused. The segment clock is stopped
    // while paused so no test time elapses; the chamber keeps its last set point.
    private readonly ManualResetEventSlim _resume = new(true);

    /// <param name="client">A connected chamber client.</param>
    /// <param name="updateInterval">How often a fresh set point is written (default 5&#160;s).</param>
    /// <param name="soakAllHolds">
    /// When <c>true</c>, every hold (non-ramp) segment waits until the measured
    /// temperature reaches its target within tolerance before the dwell time starts –
    /// even if the segment itself has <see cref="ProfileSegment.GuaranteedSoak"/> off.
    /// </param>
    /// <param name="defaultSoakTolerance">
    /// Tolerance band (°C) used when a segment soaks only because of
    /// <paramref name="soakAllHolds"/> or <paramref name="soakAllSegments"/>; a segment
    /// with its own <see cref="ProfileSegment.GuaranteedSoak"/> keeps its own tolerance.
    /// </param>
    /// <param name="soakAllSegments">
    /// When <c>true</c>, every segment – ramps included – is treated as a
    /// guaranteed-soak hold: the target is written immediately (no PC-side time
    /// interpolation of the set point), the runner waits until the measured
    /// temperature is within tolerance, and only then does the segment's own
    /// <see cref="ProfileSegment.Duration"/> start counting down before moving to
    /// the next step. Used for SIKA thermal baths, which settle on a new set point
    /// on their own and gain nothing from a PC-driven ramp – the operator wants
    /// "reach target, then hold for the requested time, then move on".
    /// </param>
    public ProfileRunner(
        IChamberDevice client,
        TimeSpan? updateInterval = null,
        bool soakAllHolds = false,
        double defaultSoakTolerance = 1.0,
        bool soakAllSegments = false)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _updateInterval = updateInterval ?? TimeSpan.FromSeconds(5);
        if (_updateInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(updateInterval), "Update interval must be positive.");
        }

        _soakAllHolds = soakAllHolds;
        _soakAllSegments = soakAllSegments;
        _defaultSoakTolerance = Math.Abs(defaultSoakTolerance);
    }

    /// <summary>Raised whenever the runner writes a new set point.</summary>
    public event EventHandler<ProfileProgressEventArgs>? Progress;

    /// <summary><c>true</c> while the run is paused (test time is frozen).</summary>
    public bool IsPaused { get; private set; }

    /// <summary>Pauses the run: the test clock stops and no further set points advance.</summary>
    public void Pause()
    {
        IsPaused = true;
        _resume.Reset();
    }

    /// <summary>Resumes a paused run from exactly where it stopped.</summary>
    public void Resume()
    {
        IsPaused = false;
        _resume.Set();
    }

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
        int start = profile.ResolvedCycleStart;
        int end = profile.ResolvedCycleEnd;
        double totalSeconds = Math.Max(1, profile.TotalDuration.TotalSeconds);

        double completed = 0;
        double segStartTemp = startTemperature;
        double? segStartHum = startHumidity;

        // Runs one segment, then advances the ramp origin and the completed-time counter.
        // Segments before the region are the intro (run once), the region repeats
        // `cycles` times, and segments after it are the outro (run once).
        async Task RunOneAsync(int index, ProfileRunPhase phase, int cycleIndex)
        {
            ProfileSegment segment = profile.Segments[index];
            await RunSegmentAsync(
                profile, segment, cycleIndex, index, segStartTemp, segStartHum,
                phase, cycles, completed, totalSeconds, cancellationToken).ConfigureAwait(false);

            completed += segment.Duration.TotalSeconds;
            segStartTemp = segment.TargetTemperature;
            segStartHum = segment.TargetHumidity ?? segStartHum;
        }

        for (int i = 0; i < start; i++)
        {
            await RunOneAsync(i, ProfileRunPhase.Intro, 0).ConfigureAwait(false);
        }

        for (int cycle = 0; cycle < cycles; cycle++)
        {
            for (int i = start; i <= end; i++)
            {
                await RunOneAsync(i, ProfileRunPhase.Cycle, cycle).ConfigureAwait(false);
            }
        }

        for (int i = end + 1; i < profile.Segments.Count; i++)
        {
            await RunOneAsync(i, ProfileRunPhase.Outro, cycles - 1).ConfigureAwait(false);
        }
    }

    private async Task RunSegmentAsync(
        TestProfile profile,
        ProfileSegment segment,
        int cycle,
        int index,
        double startTemp,
        double? startHum,
        ProfileRunPhase phase,
        int totalCycles,
        double completedSeconds,
        double totalSeconds,
        CancellationToken cancellationToken)
    {
        TimeSpan duration = segment.Duration > TimeSpan.Zero ? segment.Duration : TimeSpan.FromSeconds(1);

        // With soakAllSegments, every step (ramp or hold) is driven as an immediate
        // jump to the target rather than a timed interpolation – the device is
        // expected to settle on its own (SIKA baths).
        bool holdBehavior = !segment.IsRamp || _soakAllSegments;

        // Guaranteed soak: hold the target and wait until the measured temperature
        // is within tolerance before starting to count the dwell time. A step soaks
        // when it is explicitly marked, when the device soaks every hold
        // (soakAllHolds), or when the device soaks every segment including ramps
        // (soakAllSegments – SIKA baths).
        if (holdBehavior && (segment.GuaranteedSoak || _soakAllHolds || _soakAllSegments))
        {
            await SoakWaitAsync(segment, cycle, index, startHum, phase, totalCycles,
                completedSeconds, totalSeconds, cancellationToken).ConfigureAwait(false);
        }

        var segmentClock = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(segmentClock, cancellationToken).ConfigureAwait(false);

            double elapsedSeconds = segmentClock.Elapsed.TotalSeconds;
            double fraction = Math.Clamp(elapsedSeconds / duration.TotalSeconds, 0d, 1d);

            double temperature = holdBehavior ? segment.TargetTemperature : segment.TemperatureAt(fraction, startTemp);
            double? humidity = holdBehavior ? (segment.TargetHumidity ?? startHum) : segment.HumidityAt(fraction, startHum);

            await WriteSetpointAsync(temperature, humidity, cancellationToken).ConfigureAwait(false);

            double overall = Math.Clamp((completedSeconds + Math.Min(elapsedSeconds, duration.TotalSeconds)) / totalSeconds, 0d, 1d);
            Progress?.Invoke(this, new ProfileProgressEventArgs(
                cycle, index, segment, fraction, temperature, humidity, segmentClock.Elapsed,
                phase, totalCycles, overall));

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
        ProfileSegment segment, int cycle, int index, double? startHum,
        ProfileRunPhase phase, int totalCycles, double completedSeconds, double totalSeconds,
        CancellationToken cancellationToken)
    {
        // A segment marked GuaranteedSoak keeps its own tolerance; a hold that soaks
        // only because the device soaks every hold uses the runner-wide default.
        double tolerance = segment.GuaranteedSoak ? Math.Abs(segment.SoakTolerance) : _defaultSoakTolerance;
        double? humidity = segment.TargetHumidity ?? startHum;
        double overall = Math.Clamp(completedSeconds / totalSeconds, 0d, 1d);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(null, cancellationToken).ConfigureAwait(false);

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
                cycle, index, segment, 0d, segment.TargetTemperature, humidity, TimeSpan.Zero,
                phase, totalCycles, overall, isSoaking: true));

            if (measured is { } m && Math.Abs(m - segment.TargetTemperature) <= tolerance)
            {
                return;
            }

            await Task.Delay(_updateInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Blocks while the run is paused, stopping <paramref name="clock"/> so no test
    /// time elapses and restarting it on resume. Returns immediately when running.
    /// </summary>
    private async Task WaitWhilePausedAsync(System.Diagnostics.Stopwatch? clock, CancellationToken cancellationToken)
    {
        if (_resume.IsSet)
        {
            return;
        }

        clock?.Stop();
        try
        {
            while (!_resume.IsSet)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            clock?.Start();
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

/// <summary>Which part of the run a segment belongs to.</summary>
public enum ProfileRunPhase
{
    /// <summary>Segments before the cycled region (run once) – e.g. the initial ramp.</summary>
    Intro,

    /// <summary>Segments inside the repeated region.</summary>
    Cycle,

    /// <summary>Segments after the cycled region (run once) – e.g. the final ramp to room temperature.</summary>
    Outro,
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
        ProfileRunPhase phase = ProfileRunPhase.Cycle,
        int totalCycles = 1,
        double overallFraction = 0,
        bool isSoaking = false)
    {
        Cycle = cycle;
        SegmentIndex = segmentIndex;
        Segment = segment;
        Fraction = fraction;
        TemperatureSetpoint = temperatureSetpoint;
        HumiditySetpoint = humiditySetpoint;
        ElapsedInSegment = elapsedInSegment;
        Phase = phase;
        TotalCycles = totalCycles;
        OverallFraction = overallFraction;
        IsSoaking = isSoaking;
    }

    /// <summary>Which part of the run (intro / cycled region / outro) this segment is in.</summary>
    public ProfileRunPhase Phase { get; }

    /// <summary>Number of repeats of the cycled region.</summary>
    public int TotalCycles { get; }

    /// <summary>Completion fraction of the whole run (0..1), across intro, all cycles and outro.</summary>
    public double OverallFraction { get; }

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
