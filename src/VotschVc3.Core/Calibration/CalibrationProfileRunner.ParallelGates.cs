using VotschVc3.Core.Communication;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;

namespace VotschVc3.Core.Calibration;

/// <summary>
/// Temperature-calibration profile runner.
/// For FBG calibration the profile is used only as a source of selected calibration plateau
/// temperatures. Profile ramps, non-calibration segments and all profile durations are ignored.
/// Progression is controlled by the measured WIKA reference stability and then by independent
/// per-FBG stability/measurement completion.
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

    public void RequestTemperatureGateOverride() => _orchestrator.RequestTemperatureGateOverride();

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
            throw new InvalidOperationException("Profil nie je označený ako TemperatureCalibration.");

        HashSet<int> selectedCalibrationSegments = ResolveCalibrationSegmentIndices(profile, setup);
        List<ExecutionStep> calibrationSteps = ExpandExecution(profile)
            .Where(s => !s.Segment.IsRamp && selectedCalibrationSegments.Contains(s.SegmentIndex))
            .ToList();

        if (calibrationSteps.Count == 0)
            throw new InvalidOperationException("Kalibračný profil nemá označené žiadne kalibračné plato.");

        run.State = CalibrationRunState.Preflight;
        Progress?.Invoke(new CalibrationProgressSnapshot(
            CalibrationRunState.Preflight,
            -1,
            calibrationSteps.Count,
            calibrationSteps[0].Segment.TargetTemperature,
            startTemperature,
            null,
            0,
            setup.Mappings.Count(m => m.Selected),
            TimeSpan.Zero,
            Array.Empty<CalibrationTargetProgress>(),
            "Kontrola PeakLoggera a zapojenia. Z profilu sa použijú iba vybrané teploty kalibračných plat; rampy a časy profilu sa ignorujú."));

        await _orchestrator.PreflightAsync(setup, cancellationToken).ConfigureAwait(false);
        run.State = CalibrationRunState.Preparing;

        double? previousHumidity = startHumidity;
        double previousCommandedTemperature = startTemperature;
        CalibrationPlateauResult? validationBaseline = null;
        bool responseValidated = false;

        try
        {
            for (int currentPlateau = 0; currentPlateau < calibrationSteps.Count; currentPlateau++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);

                ExecutionStep step = calibrationSteps[currentPlateau];
                run.State = CalibrationRunState.MovingToPlateau;

                double? targetHumidity = step.Segment.TargetHumidity ?? previousHumidity;
                await MoveSetpointToPlateauAsync(
                    setup.Settings,
                    currentPlateau,
                    calibrationSteps.Count,
                    previousCommandedTemperature,
                    step.Segment.TargetTemperature,
                    targetHumidity,
                    cancellationToken).ConfigureAwait(false);
                previousCommandedTemperature = step.Segment.TargetTemperature;
                previousHumidity = targetHumidity;

                Progress?.Invoke(new CalibrationProgressSnapshot(
                    CalibrationRunState.MovingToPlateau,
                    currentPlateau,
                    calibrationSteps.Count,
                    step.Segment.TargetTemperature,
                    null,
                    null,
                    0,
                    0,
                    TimeSpan.Zero,
                    Array.Empty<CalibrationTargetProgress>(),
                    $"Komora dosiahla koncový setpoint {step.Segment.TargetTemperature:F2} °C" +
                    (setup.Settings.EnableSetpointRamp
                        ? $" plynulým nábehom najviac {NormalizeSetpointRate(setup.Settings.SetpointRampCPerMinute):F2} °C/min. "
                        : ". Plynulý nábeh je vypnutý. ") +
                    "Komora sa reguluje vlastným interným regulátorom. " +
                    "Ďalší krok riadi výhradne stabilita WIKA referencie."));

                Func<double, double?, CancellationToken, Task<string?>>? referenceControl =
                    CreateReferenceControl(setup, targetHumidity);

                CalibrationPlateauResult plateau = await _orchestrator.WaitForPlateauAsync(
                    run,
                    setup,
                    currentPlateau,
                    calibrationSteps.Count,
                    step.Segment.TargetTemperature,
                    TimeSpan.Zero,
                    ReadTemperatureAsync,
                    readReferenceTemperatureAsync,
                    referenceControl,
                    writer,
                    snapshot => Progress?.Invoke(snapshot),
                    cancellationToken).ConfigureAwait(false);

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
                    if (validated) responseValidated = true;
                }

                run.State = CalibrationRunState.PlateauCompleted;
                Progress?.Invoke(new CalibrationProgressSnapshot(
                    run.State,
                    currentPlateau,
                    calibrationSteps.Count,
                    plateau.TargetTemperatureC,
                    plateau.ActualTemperatureC,
                    plateau.ReferenceTemperatureC,
                    plateau.Targets.Count(t => t.Status == CalibrationTargetState.Stable),
                    plateau.Targets.Count,
                    plateau.CompletedAt - plateau.StartedAt,
                    Array.Empty<CalibrationTargetProgress>(),
                    $"Kalibračný bod {currentPlateau + 1} / {calibrationSteps.Count} je dokončený."));
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
            // STOP from the UI cancels the runner token. Do not wait for the outer ViewModel
            // cleanup before stopping the physical chamber: send a best-effort STOP here
            // immediately so the chamber is not left running while reference/FBG cleanup finishes.
            try
            {
                await _chamber.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The outer cleanup will try StopAsync once more. Preserve cancellation as the
                // primary result even if the device is already disconnected or busy.
            }

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

    private Func<double, double?, CancellationToken, Task<string?>>? CreateReferenceControl(
        CalibrationSetup setup,
        double? targetHumidity)
    {
        CalibrationReferenceControlOptions options = CalibrationReferenceControlRegistry.Get(setup.ChamberId).Normalize();
        if (!options.Enabled) return null;

        double biasC = 0;
        DateTimeOffset nextUpdate = DateTimeOffset.MinValue;
        double lastCommandedSetpoint = double.NaN;

        return async (targetTemperatureC, referenceTemperatureC, cancellationToken) =>
        {
            if (referenceTemperatureC is not { } reference)
                return " · WIKA control: čaká na platnú referenciu";

            if (double.IsNaN(lastCommandedSetpoint)) lastCommandedSetpoint = targetTemperatureC;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            double errorC = targetTemperatureC - reference;
            if (now >= nextUpdate && Math.Abs(errorC) > options.DeadbandC)
            {
                double requestedStep = Math.Clamp(errorC * options.Gain, -options.MaxStepC, options.MaxStepC);
                biasC = Math.Clamp(biasC + requestedStep, -options.MaxCorrectionC, options.MaxCorrectionC);
                lastCommandedSetpoint = targetTemperatureC + biasC;
                await WriteSetpointAsync(lastCommandedSetpoint, targetHumidity, cancellationToken).ConfigureAwait(false);
                nextUpdate = now + options.UpdateInterval;
            }

            return $" · WIKA control: setpoint komory {lastCommandedSetpoint:F2} °C (bias {biasC:+0.00;-0.00;0.00} °C)";
        };
    }

    private static HashSet<int> ResolveCalibrationSegmentIndices(TestProfile profile, CalibrationSetup setup)
    {
        if (setup.CalibrationSegmentIndices.Count > 0)
        {
            return setup.CalibrationSegmentIndices
                .Where(index => index >= 0 && index < profile.Segments.Count)
                .ToHashSet();
        }

        return profile.Segments
            .Select((segment, index) => (segment, index))
            .Where(x => !x.segment.IsRamp && x.segment.IsCalibrationPoint)
            .Select(x => x.index)
            .ToHashSet();
    }

    private async Task<double> ReadTemperatureAsync(CancellationToken cancellationToken)
    {
        ChamberReading reading = await _chamber.ReadAsync(cancellationToken).ConfigureAwait(false);
        return reading.Temperature
            ?? throw new InvalidOperationException("Komora neposkytla platnú nameranú teplotu počas kalibrácie.");
    }

    private async Task MoveSetpointToPlateauAsync(
        CalibrationProfileSettings settings,
        int plateauIndex,
        int plateauCount,
        double fromTemperature,
        double targetTemperature,
        double? targetHumidity,
        CancellationToken cancellationToken)
    {
        if (!settings.EnableSetpointRamp || Math.Abs(targetTemperature - fromTemperature) < 0.001)
        {
            await WriteSetpointAsync(targetTemperature, targetHumidity, cancellationToken).ConfigureAwait(false);
            return;
        }

        double rateCPerMinute = NormalizeSetpointRate(settings.SetpointRampCPerMinute);
        double direction = Math.Sign(targetTemperature - fromTemperature);
        double stepC = rateCPerMinute * _updateInterval.TotalMinutes;
        double commanded = fromTemperature;
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;

        while (Math.Abs(targetTemperature - commanded) > 0.001)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);

            commanded += direction * Math.Min(stepC, Math.Abs(targetTemperature - commanded));
            await WriteSetpointAsync(commanded, targetHumidity, cancellationToken).ConfigureAwait(false);

            double? actual = null;
            try { actual = await ReadTemperatureAsync(cancellationToken).ConfigureAwait(false); }
            catch (InvalidOperationException) { }

            Progress?.Invoke(new CalibrationProgressSnapshot(
                CalibrationRunState.MovingToPlateau,
                plateauIndex,
                plateauCount,
                targetTemperature,
                actual,
                null,
                0,
                0,
                DateTimeOffset.UtcNow - startedAt,
                Array.Empty<CalibrationTargetProgress>(),
                $"Plynulý nábeh {rateCPerMinute:F2} °C/min · setpoint {commanded:F2} °C · cieľ {targetTemperature:F2} °C. " +
                "Komora sa reguluje vlastným snímačom; WIKA zatiaľ iba meria."));

            if (Math.Abs(targetTemperature - commanded) > 0.001)
                await Task.Delay(_updateInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static double NormalizeSetpointRate(double value) =>
        double.IsFinite(value) ? Math.Clamp(Math.Abs(value), 0.1, 20.0) : 1.0;

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
