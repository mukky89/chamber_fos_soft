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

    public void RequestTemperatureStabilityExtension(TimeSpan extension) =>
        _orchestrator.RequestTemperatureStabilityExtension(extension);

    public async Task RunAsync(
        TestProfile profile,
        CalibrationSetup setup,
        CalibrationRunRecord run,
        CalibrationRunWriter writer,
        double startTemperature,
        double? startHumidity,
        Func<CancellationToken, Task<double?>>? readReferenceTemperatureAsync = null,
        CancellationToken cancellationToken = default,
        CalibrationCheckpoint? resumeFrom = null)
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

        List<PlateauWorkItem> workItems = PrepareResume(run, profile, setup, calibrationSteps.Count, resumeFrom);
        int progressPlateau = workItems.Count > 0 ? workItems[0].PlateauIndex : calibrationSteps.Count - 1;

        run.State = CalibrationRunState.Preflight;
        Progress?.Invoke(new CalibrationProgressSnapshot(
            CalibrationRunState.Preflight,
            -1,
            calibrationSteps.Count,
            calibrationSteps[progressPlateau].Segment.TargetTemperature,
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
        // A checkpoint target describes the last completed plateau, not the chamber's
        // current setpoint after a restart. Always shape the first command from the
        // fresh chamber reading supplied by the caller so resume cannot command a
        // needless excursion back to the previous plateau.
        double previousCommandedTemperature = startTemperature;
        CalibrationPlateauResult? validationBaseline = run.Plateaus.FirstOrDefault();
        bool responseValidated = false;

        try
        {
            for (int workPosition = 0; workPosition < workItems.Count; workPosition++)
            {
                PlateauWorkItem workItem = workItems[workPosition];
                int currentPlateau = workItem.PlateauIndex;
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

                CalibrationPlateauResult plateau;
                try
                {
                    plateau = await _orchestrator.WaitForPlateauAsync(
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
                        cancellationToken,
                        deferOnTemperatureTimeout: !workItem.IsRetry && workPosition < workItems.Count - 1).ConfigureAwait(false);
                }
                catch (CalibrationPlateauDeferredException deferred)
                {
                    workItems.Add(new PlateauWorkItem(currentPlateau, IsRetry: true));
                    run.State = CalibrationRunState.MovingToNextPlateau;
                    SaveCheckpoint(run, setup, currentPlateau, step.Segment.TargetTemperature,
                        workItems.Skip(workPosition + 1).Where(item => item.IsRetry).Select(item => item.PlateauIndex));
                    writer.SaveSummary();
                    Progress?.Invoke(new CalibrationProgressSnapshot(
                        run.State,
                        currentPlateau,
                        calibrationSteps.Count,
                        step.Segment.TargetTemperature,
                        null,
                        null,
                        0,
                        setup.Mappings.Count(mapping => mapping.Selected),
                        TimeSpan.Zero,
                        Array.Empty<CalibrationTargetProgress>(),
                        $"Plato {currentPlateau + 1} / {calibrationSteps.Count} sa odložilo: {deferred.Message} Nasleduje ďalšie dostupné plato."));
                    continue;
                }

                run.Plateaus.Add(plateau);
                writer.SaveSummary();

                SaveCheckpoint(run, setup, currentPlateau, step.Segment.TargetTemperature,
                    workItems.Skip(workPosition + 1).Where(item => item.IsRetry).Select(item => item.PlateauIndex));

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

    private List<PlateauWorkItem> PrepareResume(
        CalibrationRunRecord run,
        TestProfile profile,
        CalibrationSetup setup,
        int plateauCount,
        CalibrationCheckpoint? checkpoint)
    {
        if (checkpoint is null)
            return Enumerable.Range(0, plateauCount).Select(index => new PlateauWorkItem(index, IsRetry: false)).ToList();
        if (checkpoint.RunId != run.RunId || checkpoint.ProfileId != profile.Id || checkpoint.ChamberId != run.ChamberId)
            throw new InvalidOperationException("Checkpoint nepatrí k vybranému profilu, komore alebo kalibračnému behu.");
        if (checkpoint.CompletedPlateaus.Count > plateauCount)
            throw new InvalidOperationException("Checkpoint obsahuje viac dokončených plat, než má aktuálny kalibračný plán.");
        if (checkpoint.Mappings.Count > 0 && setup.Mappings.Count(m => m.Selected) == 0)
            throw new InvalidOperationException("Pred obnovením kalibrácie chýba uložené zapojenie vybraných FBG peakov.");

        run.Plateaus.Clear();
        run.Plateaus.AddRange(checkpoint.CompletedPlateaus);
        run.CompletedAt = null;
        HashSet<int> completed = checkpoint.CompletedPlateaus.Select(plateau => plateau.PlateauIndex).ToHashSet();
        HashSet<int> deferred = checkpoint.DeferredPlateauIndices
            .Where(index => index >= 0 && index < plateauCount && !completed.Contains(index))
            .ToHashSet();
        return Enumerable.Range(0, plateauCount)
            .Where(index => !completed.Contains(index) && !deferred.Contains(index))
            .Select(index => new PlateauWorkItem(index, IsRetry: false))
            .Concat(deferred.OrderBy(index => index).Select(index => new PlateauWorkItem(index, IsRetry: true)))
            .ToList();
    }

    private void SaveCheckpoint(
        CalibrationRunRecord run,
        CalibrationSetup setup,
        int currentPlateau,
        double targetTemperature,
        IEnumerable<int> deferredPlateaus)
    {
        _store.SaveCheckpoint(new CalibrationCheckpoint
        {
            RunId = run.RunId,
            ProfileId = run.ProfileId,
            ChamberId = run.ChamberId,
            CurrentPlateauIndex = currentPlateau,
            CurrentTargetTemperatureC = targetTemperature,
            State = run.State,
            CompletedPlateaus = run.Plateaus.ToList(),
            DeferredPlateauIndices = deferredPlateaus.Distinct().ToList(),
            Mappings = setup.Mappings.Select(CloneMapping).ToList(),
            SettingsSnapshot = CalibrationCheckpointRecovery.CloneSettings(setup.Settings),
            CalibrationSegmentIndices = setup.CalibrationSegmentIndices.ToList(),
        });
    }

    private sealed record PlateauWorkItem(int PlateauIndex, bool IsRetry);

    private Func<double, double?, CancellationToken, Task<string?>>? CreateReferenceControl(
        CalibrationSetup setup,
        double? targetHumidity)
    {
        CalibrationReferenceControlOptions options = CalibrationReferenceControlRegistry.Get(setup.ChamberId).Normalize();
        if (!options.Enabled) return null;

        double biasC = 0;
        double correctionThresholdC = Math.Max(options.DeadbandC, Math.Abs(setup.Settings.ChamberToleranceC));
        DateTimeOffset nextUpdate = DateTimeOffset.MinValue;
        double lastCommandedSetpoint = double.NaN;

        return async (targetTemperatureC, referenceTemperatureC, cancellationToken) =>
        {
            if (referenceTemperatureC is not { } reference)
                return " · WIKA control: čaká na platnú referenciu";

            if (double.IsNaN(lastCommandedSetpoint)) lastCommandedSetpoint = targetTemperatureC;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            double errorC = targetTemperatureC - reference;
            if (now >= nextUpdate && Math.Abs(errorC) > correctionThresholdC)
            {
                double requestedStep = Math.Clamp(errorC * options.Gain, -options.MaxStepC, options.MaxStepC);
                biasC = Math.Clamp(biasC + requestedStep, -options.MaxCorrectionC, options.MaxCorrectionC);
                lastCommandedSetpoint = targetTemperatureC + biasC;
                await WriteSetpointAsync(lastCommandedSetpoint, targetHumidity, cancellationToken).ConfigureAwait(false);
                nextUpdate = now + options.UpdateInterval;
            }

            string state = Math.Abs(errorC) <= correctionThresholdC
                ? "WIKA je v tolerancii, bez ďalšej korekcie"
                : "dorovnáva WIKA do tolerancie";
            return $" · WIKA control: {state} · setpoint komory {lastCommandedSetpoint:F2} °C (bias {biasC:+0.00;-0.00;0.00} °C)";
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
