using System.Diagnostics;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;

namespace VotschVc3.Core.Calibration;

/// <summary>
/// Executes a temperature-calibration profile while delegating every measurement
/// plateau to <see cref="CalibrationOrchestrator"/>. Ordinary profiles continue to use
/// <see cref="ProfileRunner"/>; this dedicated runner keeps the existing production
/// profile path unchanged while adding the stricter calibration gate.
/// </summary>
public sealed class CalibrationProfileRunner
{
    private readonly IChamberDevice _chamber;
    private readonly CalibrationOrchestrator _orchestrator;
    private readonly CalibrationStore _store;
    private readonly TimeSpan _updateInterval;
    private readonly ManualResetEventSlim _resume = new(true);

    public CalibrationProfileRunner(
        IChamberDevice chamber,
        CalibrationOrchestrator orchestrator,
        CalibrationStore store,
        TimeSpan? updateInterval = null)
    {
        _chamber = chamber ?? throw new ArgumentNullException(nameof(chamber));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _updateInterval = updateInterval ?? TimeSpan.FromSeconds(5);
        if (_updateInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(updateInterval));
    }

    public bool IsPaused { get; private set; }
    public event Action<CalibrationProgressSnapshot>? Progress;

    public void Pause()
    {
        IsPaused = true;
        _resume.Reset();
    }

    public void Resume()
    {
        IsPaused = false;
        _resume.Set();
    }

    public async Task RunAsync(
        TestProfile profile,
        CalibrationSetup setup,
        CalibrationRunRecord run,
        CalibrationRunWriter writer,
        double startTemperature,
        double? startHumidity,
        Func<CancellationToken, Task<double?>>? readReferenceTemperatureAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(writer);

        if (profile.ExecutionMode != ProfileExecutionMode.TemperatureCalibration)
        {
            throw new InvalidOperationException("Profil nie je označený ako TemperatureCalibration.");
        }

        List<ExecutionStep> steps = ExpandExecution(profile);
        HashSet<int> selectedCalibrationSegments = ResolveCalibrationSegmentIndices(profile, setup);
        List<ExecutionStep> calibrationSteps = steps
            .Where(s => !s.Segment.IsRamp && selectedCalibrationSegments.Contains(s.SegmentIndex))
            .ToList();
        if (calibrationSteps.Count == 0)
        {
            throw new InvalidOperationException("Kalibračný profil nemá označené žiadne kalibračné plato.");
        }

        run.State = CalibrationRunState.Preflight;
        await _orchestrator.PreflightAsync(setup, cancellationToken).ConfigureAwait(false);
        run.State = CalibrationRunState.Preparing;

        double previousTemperature = startTemperature;
        double? previousHumidity = startHumidity;
        int calibrationPlateauIndex = 0;
        CalibrationPlateauResult? validationBaseline = null;
        bool responseValidated = false;

        try
        {
            foreach (ExecutionStep step in steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);

                run.State = CalibrationRunState.MovingToPlateau;
                await ExecuteSegmentAsync(
                    step.Segment,
                    previousTemperature,
                    previousHumidity,
                    cancellationToken).ConfigureAwait(false);

                previousTemperature = step.Segment.TargetTemperature;
                previousHumidity = step.Segment.TargetHumidity ?? previousHumidity;

                if (step.Segment.IsRamp || !selectedCalibrationSegments.Contains(step.SegmentIndex))
                {
                    continue;
                }

                int currentPlateau = calibrationPlateauIndex++;

                // When a physical WIKA reference is configured, peak stability must not start
                // merely because the chamber controller reports a stable temperature. Require
                // the chamber and the reference to be inside the same target tolerance, with
                // acceptable drift, for the configured stability duration first.
                if (readReferenceTemperatureAsync is not null)
                {
                    await WaitForCombinedTemperatureStabilityAsync(
                        run,
                        setup,
                        currentPlateau,
                        calibrationSteps.Count,
                        step.Segment.TargetTemperature,
                        readReferenceTemperatureAsync,
                        cancellationToken).ConfigureAwait(false);
                }

                // The combined gate above has already satisfied the configured chamber stable
                // duration. Keep the orchestrator's existing safety check, but make it a fresh
                // instantaneous in-tolerance verification so we do not wait the same minute twice.
                TimeSpan originalChamberStableDuration = setup.Settings.ChamberStableDuration;
                if (readReferenceTemperatureAsync is not null)
                {
                    setup.Settings.ChamberStableDuration = TimeSpan.Zero;
                }

                CalibrationPlateauResult plateau;
                try
                {
                    plateau = await _orchestrator.WaitForPlateauAsync(
                        run,
                        setup,
                        currentPlateau,
                        calibrationSteps.Count,
                        step.Segment.TargetTemperature,
                        ReadTemperatureAsync,
                        readReferenceTemperatureAsync,
                        writer,
                        snapshot => Progress?.Invoke(snapshot),
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    setup.Settings.ChamberStableDuration = originalChamberStableDuration;
                }

                run.Plateaus.Add(plateau);
                writer.SaveSummary();

                _store.SaveCheckpoint(new CalibrationCheckpoint
                {
                    RunId = run.RunId,
                    ProfileId = run.ProfileId,
                    ChamberId = run.ChamberId,
                    CurrentPlateauIndex = currentPlateau,
                    CurrentTargetTemperatureC = step.Segment.TargetTemperature,
                    State = run.State,
                    CompletedPlateaus = run.Plateaus.ToList(),
                    Mappings = setup.Mappings.Select(CloneMapping).ToList(),
                });

                if (validationBaseline is null)
                {
                    validationBaseline = plateau;
                    run.State = CalibrationRunState.BaselineCollection;
                }
                else if (!responseValidated)
                {
                    bool validated = _orchestrator.ValidateTemperatureResponse(run, validationBaseline, plateau, setup.Settings);
                    if (validated)
                    {
                        responseValidated = true;
                    }
                }

                run.State = CalibrationRunState.PlateauCompleted;
            }

            if (!responseValidated && run.Plateaus.Count > 1)
            {
                run.Warnings.Add(new CalibrationWarning
                {
                    Code = "VALIDATION_NOT_COMPLETED",
                    Message = "Profil neposkytol dostatočnú zmenu teploty na automatické overenie odozvy wavelength.",
                });
            }

            run.CompletedAt = DateTimeOffset.Now;
            run.State = run.Warnings.Count == 0 ? CalibrationRunState.Completed : CalibrationRunState.CompletedWithWarnings;
            writer.SaveSummary();
            _store.DeleteCheckpoint(run.ChamberId);
        }
        catch (OperationCanceledException)
        {
            run.CompletedAt = DateTimeOffset.Now;
            run.State = CalibrationRunState.Aborted;
            writer.SaveSummary();
            throw;
        }
        catch (CalibrationOperatorActionRequiredException)
        {
            run.State = CalibrationRunState.AwaitingOperator;
            writer.SaveSummary();
            throw;
        }
        catch
        {
            run.CompletedAt = DateTimeOffset.Now;
            run.State = CalibrationRunState.Failed;
            writer.SaveSummary();
            throw;
        }
    }

    private async Task WaitForCombinedTemperatureStabilityAsync(
        CalibrationRunRecord run,
        CalibrationSetup setup,
        int plateauIndex,
        int plateauCount,
        double targetTemperatureC,
        Func<CancellationToken, Task<double?>> readReferenceTemperatureAsync,
        CancellationToken cancellationToken)
    {
        CalibrationProfileSettings settings = setup.Settings;
        var chamberDetector = new TemperatureStabilityDetector(
            settings.ChamberStableDuration,
            settings.ChamberToleranceC,
            settings.MaxChamberDriftCPerMinute);
        var referenceDetector = new TemperatureStabilityDetector(
            settings.ChamberStableDuration,
            settings.ChamberToleranceC,
            settings.MaxChamberDriftCPerMinute);
        Stopwatch wait = Stopwatch.StartNew();
        Stopwatch plateauClock = Stopwatch.StartNew();
        double? lastReference = null;

        run.State = CalibrationRunState.WaitingForChamberStability;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);

            double chamberTemperature = await ReadTemperatureAsync(cancellationToken).ConfigureAwait(false);
            double? referenceTemperature = await readReferenceTemperatureAsync(cancellationToken).ConfigureAwait(false);
            lastReference = referenceTemperature ?? lastReference;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            StabilityMetrics chamberMetrics = chamberDetector.Add(now, chamberTemperature, targetTemperatureC);
            StabilityMetrics? referenceMetrics = referenceTemperature is { } reference
                ? referenceDetector.Add(now, reference, targetTemperatureC)
                : null;

            bool chamberStable = chamberMetrics.IsStable;
            bool referenceStable = referenceMetrics?.IsStable == true;
            string detail = referenceTemperature is null
                ? "WIKA CTH7000 nevrátil platnú referenčnú teplotu."
                : $"Komora {(chamberStable ? "stable" : "čaká")} · WIKA {(referenceStable ? "stable" : "čaká")}.";

            Progress?.Invoke(new CalibrationProgressSnapshot(
                CalibrationRunState.WaitingForChamberStability,
                plateauIndex,
                plateauCount,
                targetTemperatureC,
                chamberTemperature,
                referenceTemperature,
                0,
                setup.Mappings.Count(m => m.Selected),
                plateauClock.Elapsed,
                BuildTemperatureWaitingTargets(setup, detail),
                $"Čaká sa na stabilnú teplotu komory aj WIKA CTH7000 · {detail}"));

            if (chamberStable && referenceStable)
            {
                return;
            }

            if (settings.ChamberStabilityTimeout > TimeSpan.Zero && wait.Elapsed >= settings.ChamberStabilityTimeout)
            {
                string referenceText = lastReference is { } value ? $"{value:F3} °C" : "bez platnej hodnoty";
                var warning = new CalibrationWarning
                {
                    Code = "REFERENCE_STABILITY_TIMEOUT",
                    Message = $"Komora a referencia WIKA CTH7000 sa spolu neustálili na {targetTemperatureC:F1} °C do {settings.ChamberStabilityTimeout}. Posledná WIKA: {referenceText}.",
                    PlateauIndex = plateauIndex,
                };
                run.Warnings.Add(warning);
                throw new CalibrationOperatorActionRequiredException(warning.Message, warning);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<CalibrationTargetProgress> BuildTemperatureWaitingTargets(
        CalibrationSetup setup,
        string detail) => setup.Mappings
        .Where(m => m.Selected)
        .Select(m => new CalibrationTargetProgress(
            m.SerialNumber,
            m.Channel,
            m.PeakId,
            m.PeakIndex,
            m.CurrentWavelengthNm,
            0,
            setup.Settings.RequiredStableSamples,
            null,
            null,
            TimeSpan.Zero,
            m.StabilizationTimeoutOverride ?? setup.Settings.DefaultSensorStabilizationTimeout,
            CalibrationTargetState.WaitingForTemperature,
            detail))
        .ToArray();

    private static HashSet<int> ResolveCalibrationSegmentIndices(TestProfile profile, CalibrationSetup setup)
    {
        if (setup.CalibrationSegmentIndices.Count > 0)
        {
            return setup.CalibrationSegmentIndices
                .Where(index => index >= 0 && index < profile.Segments.Count)
                .ToHashSet();
        }

        // Backward compatibility for profiles/setups created before explicit UI plateau
        // selection was persisted. New runs use CalibrationSegmentIndices as the source of truth.
        return profile.Segments
            .Select((segment, index) => (segment, index))
            .Where(x => !x.segment.IsRamp && x.segment.IsCalibrationPoint)
            .Select(x => x.index)
            .ToHashSet();
    }

    private async Task ExecuteSegmentAsync(
        ProfileSegment segment,
        double startTemperature,
        double? startHumidity,
        CancellationToken cancellationToken)
    {
        TimeSpan duration = segment.Duration > TimeSpan.Zero ? segment.Duration : TimeSpan.FromSeconds(1);
        Stopwatch clock = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_resume.IsSet)
            {
                clock.Stop();
                await WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
                clock.Start();
            }

            double fraction = Math.Clamp(clock.Elapsed.TotalSeconds / duration.TotalSeconds, 0d, 1d);
            double temperature = segment.IsRamp
                ? segment.TemperatureAt(fraction, startTemperature)
                : segment.TargetTemperature;
            double? humidity = segment.IsRamp
                ? segment.HumidityAt(fraction, startHumidity)
                : (segment.TargetHumidity ?? startHumidity);

            await WriteSetpointAsync(temperature, humidity, cancellationToken).ConfigureAwait(false);
            if (fraction >= 1d) return;

            TimeSpan remaining = duration - clock.Elapsed;
            TimeSpan delay = remaining < _updateInterval ? remaining : _updateInterval;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<double> ReadTemperatureAsync(CancellationToken cancellationToken)
    {
        ChamberReading reading = await _chamber.ReadAsync(cancellationToken).ConfigureAwait(false);
        return reading.Temperature
            ?? throw new InvalidOperationException("Komora neposkytla platnú nameranú teplotu počas kalibrácie.");
    }

    private Task WriteSetpointAsync(double temperature, double? humidity, CancellationToken cancellationToken)
    {
        var digital = new DigitalChannels
        {
            StartChannelIndex = _chamber.Settings.StartChannelIndex,
            Start = true,
        };
        var setpoints = new List<double> { temperature, humidity ?? 0d };
        return _chamber.WriteSetpointsAsync(setpoints, digital, cancellationToken);
    }

    private async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        while (!_resume.IsSet)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }
    }

    private static List<ExecutionStep> ExpandExecution(TestProfile profile)
    {
        var steps = new List<ExecutionStep>();
        int cycles = Math.Max(1, profile.Cycles);
        int start = profile.ResolvedCycleStart;
        int end = profile.ResolvedCycleEnd;

        for (int i = 0; i < start; i++) steps.Add(new ExecutionStep(profile.Segments[i], i, 0));
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            for (int i = start; i <= end; i++) steps.Add(new ExecutionStep(profile.Segments[i], i, cycle));
        }
        for (int i = end + 1; i < profile.Segments.Count; i++) steps.Add(new ExecutionStep(profile.Segments[i], i, cycles - 1));
        return steps;
    }

    private static CalibrationSensorMapping CloneMapping(CalibrationSensorMapping m) => new()
    {
        Channel = m.Channel,
        Core1 = m.Core1,
        Core2 = m.Core2,
        SerialNumber = m.SerialNumber,
        ChannelSerialNumber = m.ChannelSerialNumber,
        ChainSerialNumber = m.ChainSerialNumber,
        PeakLoggerDeviceSerialNumber = m.PeakLoggerDeviceSerialNumber,
        PeakId = m.PeakId,
        PeakIndex = m.PeakIndex,
        NominalWavelengthNm = m.NominalWavelengthNm,
        CurrentWavelengthNm = m.CurrentWavelengthNm,
        Selected = m.Selected,
        Notes = m.Notes,
        ProductDescription = m.ProductDescription,
        Customer = m.Customer,
        Order = m.Order,
        StabilizationTimeoutOverride = m.StabilizationTimeoutOverride,
    };

    private sealed record ExecutionStep(ProfileSegment Segment, int SegmentIndex, int CycleIndex);
}
