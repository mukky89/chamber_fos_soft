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
    private readonly object _manualRecalibrationSync = new();
    private readonly HashSet<int> _manualRecalibrationRequests = new();
    private int _stopChamberOnCancellation = 1;

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

    /// <summary>Queues an already completed plateau for an operator-requested repeat.</summary>
    public bool RequestPlateauRecalibration(int plateauIndex)
    {
        if (plateauIndex < 0) return false;
        lock (_manualRecalibrationSync)
            return _manualRecalibrationRequests.Add(plateauIndex);
    }

    /// <summary>
    /// Selects whether cancellation should send a physical STOP. The default is the safe legacy
    /// behaviour; the UI may explicitly keep the controller regulating its last setpoint.
    /// </summary>
    public void SetStopChamberOnCancellation(bool stopChamber) =>
        Volatile.Write(ref _stopChamberOnCancellation, stopChamber ? 1 : 0);

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
        lock (_manualRecalibrationSync) _manualRecalibrationRequests.Clear();
        int progressPlateau = workItems.Count > 0 ? workItems[0].PlateauIndex : calibrationSteps.Count - 1;

        run.OperatorSupervisionEnabled = setup.Settings.OperatorSupervisionEnabled;
        run.State = CalibrationRunState.Preflight;
        Progress?.Invoke(new CalibrationProgressSnapshot(
            CalibrationRunState.Preflight,
            -1,
            calibrationSteps.Count,
            calibrationSteps[progressPlateau].Segment.TargetTemperature,
            startTemperature,
            null,
            0,
            setup.ActiveMappings.Count(m => m.Selected),
            TimeSpan.Zero,
            Array.Empty<CalibrationTargetProgress>(),
            "Kontrola PeakLoggera a zapojenia. Z profilu sa použijú iba vybrané teploty kalibračných plat; rampy a časy profilu sa ignorujú."));

        // Persist identity/configuration even if the first plateau never completes.
        writer.SaveSummary();
        SaveCheckpoint(run, setup, progressPlateau, calibrationSteps[progressPlateau].Segment.TargetTemperature,
            workItems.Where(item => item.IsRetry).Select(item => item.PlateauIndex));
        if (resumeFrom is null)
        {
            var discovered = await _orchestrator.PreflightAsync(setup, cancellationToken).ConfigureAwait(false);
            run.PeakIdentityChannels = PeakIdentityGuard.Initialize(discovered, setup.ActiveMappings, DateTimeOffset.UtcNow);
        }
        else
        {
            run.PeakIdentityChannels = resumeFrom.PeakIdentityChannels;
            if (!string.IsNullOrWhiteSpace(resumeFrom.OperatorIdentityConfirmation))
            {
                var discovered = await _orchestrator.PreflightAsync(setup, cancellationToken).ConfigureAwait(false);
                foreach (var channel in run.PeakIdentityChannels)
                    run.PeakIdentityEvents.Add(new PeakIdentityEvent
                    {
                        Timestamp = DateTimeOffset.UtcNow, Device = channel.Device, Channel = channel.Channel,
                        Reason = "Nový operátorom overený úsek merania: " + resumeFrom.OperatorIdentityConfirmation +
                            "; predchádzajúci stav: " + System.Text.Json.JsonSerializer.Serialize(channel),
                    });
                run.PeakIdentityChannels = PeakIdentityGuard.Initialize(discovered, setup.ActiveMappings, DateTimeOffset.UtcNow);
                writer.WriteDiagnostic("WARNING", "IDENTITY_CONFIRMED_BY_OPERATOR", resumeFrom.OperatorIdentityConfirmation);
                resumeFrom.OperatorIdentityConfirmation = null;
                run.FinalVerification = null;
            }
            if (run.PeakIdentityChannels.Count == 0)
                run.PeakIdentityChannels = setup.ActiveMappings.Where(m => m.Selected)
                    .GroupBy(m => (m.SourceDeviceSerialNumber, m.Channel))
                    .Select(g => new PeakIdentityChannel { Device = g.Key.SourceDeviceSerialNumber, Channel = g.Key.Channel }).ToList();
            // Observe performs bounded, unique matching across the offline interval.
            // Existing ambiguity remains latched unless explicitly revalidated by the operator.
            await _orchestrator.ObserveIdentityAsync(run, setup, writer, cancellationToken).ConfigureAwait(false);
        }
        _identityObservation = token => _orchestrator.ObserveIdentityAsync(run, setup, writer, token);
        writer.SaveSummary();
        run.State = CalibrationRunState.Preparing;

        double? previousHumidity = startHumidity;
        // A checkpoint target describes the last completed plateau, not the chamber's
        // current setpoint after a restart. Always shape the first command from the
        // fresh chamber reading supplied by the caller so resume cannot command a
        // needless excursion back to the previous plateau.
        double previousCommandedTemperature = startTemperature;
        CalibrationPlateauResult? validationBaseline = run.Plateaus.FirstOrDefault();
        bool responseValidated = false;
        var recoveryClock = System.Diagnostics.Stopwatch.StartNew();
        int savedPlateau = -1;
        int activeWorkPosition = 0;
        CalibrationRunState? savedState = null;
        void PersistRecovery(CalibrationProgressSnapshot snapshot)
        {
            if (snapshot.State is CalibrationRunState.Completed or CalibrationRunState.CompletedWithWarnings) return;
            if (recoveryClock.Elapsed < TimeSpan.FromSeconds(15) && savedPlateau == snapshot.PlateauIndex && savedState == snapshot.State) return;
            SaveCheckpoint(run, setup, Math.Max(0, snapshot.PlateauIndex), snapshot.TargetTemperatureC,
                workItems.Skip(activeWorkPosition).Where(item => item.IsRetry).Select(item => item.PlateauIndex));
            savedPlateau = snapshot.PlateauIndex;
            savedState = snapshot.State;
            recoveryClock.Restart();
        }
        Progress += PersistRecovery;

        try
        {
            for (int workPosition = 0; workPosition < workItems.Count; workPosition++)
            {
                activeWorkPosition = workPosition;
                PlateauWorkItem workItem = workItems[workPosition];
                int currentPlateau = workItem.PlateauIndex;
                cancellationToken.ThrowIfCancellationRequested();
                await WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);

                ExecutionStep step = calibrationSteps[currentPlateau];
                run.State = CalibrationRunState.MovingToPlateau;

                double? targetHumidity = step.Segment.TargetHumidity ?? previousHumidity;
                double transitionFromTemperature = previousCommandedTemperature;
                var settlingAttempt = SensorSettlingRecorder.CreatePending(run, setup, currentPlateau,
                    step.Segment.TargetTemperature, DateTimeOffset.UtcNow);
                settlingAttempt.FromTemperatureC = transitionFromTemperature;
                SensorSettlingRecorder.TrySave(writer.SaveSettlingProgress);
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
                        writer,
                        snapshot => Progress?.Invoke(snapshot),
                        cancellationToken,
                        deferOnTemperatureTimeout: !workItem.IsRetry && workPosition < workItems.Count - 1,
                        transitionFromTemperatureC: transitionFromTemperature,
                        settlingAttempt: settlingAttempt).ConfigureAwait(false);
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
                        setup.ActiveMappings.Count(mapping => mapping.Selected),
                        TimeSpan.Zero,
                        Array.Empty<CalibrationTargetProgress>(),
                        $"Plato {currentPlateau + 1} / {calibrationSteps.Count} sa odložilo: {deferred.Message} Nasleduje ďalšie dostupné plato."));
                    continue;
                }

                var previousAttempt = run.Plateaus.LastOrDefault(p => p.PlateauIndex == currentPlateau);
                if (previousAttempt is not null)
                {
                    run.SupersededPlateaus.Add(previousAttempt);
                    run.Plateaus.Remove(previousAttempt);
                }
                run.Plateaus.Add(plateau);
                writer.SaveSummary();

                if (validationBaseline is null)
                {
                    if (plateau.Targets.Any(t => t.Status == CalibrationTargetState.Stable))
                        validationBaseline = plateau;
                    run.State = CalibrationRunState.BaselineCollection;
                }
                else if (!responseValidated && plateau.Targets.Any(t => t.Status == CalibrationTargetState.Stable &&
                    validationBaseline.Targets.Any(b => b.Status == CalibrationTargetState.Stable && b.Identity == t.Identity)))
                {
                    bool validated = _orchestrator.ValidateTemperatureResponse(run, validationBaseline, plateau, setup.Settings);
                    if (validated) responseValidated = true;
                    else if (setup.Settings.OperatorSupervisionEnabled)
                    {
                        CalibrationOperatorAnswer answer = await _orchestrator.AskOperatorAsync(run, writer, currentPlateau, CalibrationOperatorIssue.Validation,
                            "Odozva FBG na zmenu teploty nebola potvrdená. Pokračovanie tento bod neoznačí ako úspešný.", cancellationToken).ConfigureAwait(false);
                        if (answer.Decision == CalibrationOperatorDecision.Retry)
                        {
                            run.SupersededPlateaus.Add(plateau);
                            run.Plateaus.Remove(plateau);
                            SaveCheckpoint(run, setup, currentPlateau, step.Segment.TargetTemperature,
                                workItems.Skip(workPosition + 1).Where(item => item.IsRetry).Select(item => item.PlateauIndex));
                            writer.SaveSummary();
                            workPosition--;
                            continue;
                        }
                        foreach (var target in plateau.Targets.Where(t => t.Status == CalibrationTargetState.Stable))
                        { target.Status = CalibrationTargetState.NoTemperatureResponse; target.Problem = "Odozva na teplotu nepotvrdená; pokračovanie povolil operátor."; }
                        writer.SaveSummary();
                    }
                }

                SaveCheckpoint(run, setup, currentPlateau, step.Segment.TargetTemperature,
                    workItems.Skip(workPosition + 1).Where(item => item.IsRetry).Select(item => item.PlateauIndex));
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
                    plateau.Targets.Select(target => new CalibrationTargetProgress(
                        target.SerialNumber, target.Channel, target.PeakId, target.PeakIndex,
                        target.MeanWavelengthNm, target.SampleCount, setup.Settings.RequiredMeasurementSamples,
                        target.StandardDeviationPm, target.DriftPmPerMinute, target.StabilizationTime,
                        TimeSpan.Zero, target.Status, target.Problem,
                        StabilitySamples: target.Status == CalibrationTargetState.Stable ? setup.Settings.RequiredStableSamples : 0,
                        RequiredStabilitySamples: setup.Settings.RequiredStableSamples,
                        MeasurementSamples: target.SampleCount,
                        RequiredMeasurementSamples: setup.Settings.RequiredMeasurementSamples,
                        RangePm: target.RangePm,
                        RangeLimitPm: setup.Settings.MaxWavelengthRangePm,
                        StdDevLimitPm: setup.Settings.MaxWavelengthStdDevPm,
                        DriftLimitPmPerMinute: setup.Settings.MaxWavelengthDriftPmPerMinute,
                        Phase: "Done", BlockingReason: target.Problem ?? string.Empty)).ToArray(),
                    $"Kalibračný bod {currentPlateau + 1} / {calibrationSteps.Count} je dokončený."));

                AppendManualRecalibrationRequests(workItems, workPosition, calibrationSteps.Count, currentPlateau,
                    step.Segment.TargetTemperature, run, setup, writer);
            }

            if (!responseValidated && run.Plateaus.Count > 1)
            {
                run.Warnings.Add(new CalibrationWarning
                {
                    Code = "VALIDATION_NOT_COMPLETED",
                    Message = "Profil neposkytol dostatočnú zmenu teploty na automatické overenie odozvy wavelength.",
                });
            }

            await RunFinalConditioningAsync(
                setup,
                run,
                writer,
                calibrationSteps.Count,
                previousCommandedTemperature,
                previousHumidity,
                readReferenceTemperatureAsync,
                cancellationToken).ConfigureAwait(false);

            run.CompletedAt = DateTimeOffset.Now;
            run.State = run.Warnings.Count == 0 ? CalibrationRunState.Completed : CalibrationRunState.CompletedWithWarnings;
            writer.SaveSummary();
            _store.DeleteCheckpoint(run.ChamberId);
        }
        catch (CalibrationSupervisionStoppedException ex)
        {
            try
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await _chamber.StopAsync(stopTimeout.Token).WaitAsync(stopTimeout.Token).ConfigureAwait(false);
                run.State = CalibrationRunState.Aborted;
                run.CompletedAt = DateTimeOffset.Now;
                writer.WriteDiagnostic("WARNING", "SUPERVISION_STOPPED", ex.Message);
                writer.SaveSummary();
            }
            catch (Exception stopError)
            {
                run.State = CalibrationRunState.Failed;
                run.CompletedAt = DateTimeOffset.Now;
                writer.WriteDiagnostic("ERROR", "SUPERVISION_STOP_FAILED", stopError.ToString());
                writer.SaveSummary();
                throw new InvalidOperationException("Operátorský dohľad: STOP komory sa nepodarilo potvrdiť. " + stopError.Message, stopError);
            }
            throw;
        }
        catch (OperationCanceledException)
        {
            // STOP from the UI cancels the runner token. Do not wait for the outer ViewModel
            // Apply the operator's selected policy immediately; outer cleanup repeats it as a safety net.
            try
            {
                if (Volatile.Read(ref _stopChamberOnCancellation) == 1)
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
        finally
        {
            Progress -= PersistRecovery;
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
        if (checkpoint.Mappings.Count > 0 && setup.ActiveMappings.Count(m => m.Selected) == 0)
            throw new InvalidOperationException("Pred obnovením kalibrácie chýba uložené zapojenie vybraných FBG peakov.");

        run.Plateaus.Clear();
        run.Plateaus.AddRange(checkpoint.CompletedPlateaus);
        run.CompletedAt = null;
        HashSet<int> completed = checkpoint.CompletedPlateaus.Select(plateau => plateau.PlateauIndex).ToHashSet();
        HashSet<int> queuedRetries = checkpoint.DeferredPlateauIndices
            .Where(index => index >= 0 && index < plateauCount)
            .ToHashSet();
        HashSet<int> deferred = queuedRetries.Where(index => !completed.Contains(index)).ToHashSet();
        HashSet<int> manualRetries = queuedRetries.Where(completed.Contains).ToHashSet();
        return Enumerable.Range(0, plateauCount)
            .Where(index => !completed.Contains(index) && !deferred.Contains(index))
            .Select(index => new PlateauWorkItem(index, IsRetry: false))
            .Concat(deferred.OrderBy(index => index).Select(index => new PlateauWorkItem(index, IsRetry: true)))
            .Concat(manualRetries.OrderBy(index => index).Select(index => new PlateauWorkItem(index, IsRetry: true, IsManual: true)))
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
            PeakIdentityChannels = run.PeakIdentityChannels,
            CompletedPlateaus = run.Plateaus.ToList(),
            DeferredPlateauIndices = deferredPlateaus.Distinct().ToList(),
            Mappings = setup.ActiveMappings.Select(CloneMapping).ToList(),
            SettingsSnapshot = CalibrationCheckpointRecovery.CloneSettings(setup.Settings),
            CalibrationSegmentIndices = setup.CalibrationSegmentIndices.ToList(),
        });
    }

    private void AppendManualRecalibrationRequests(
        List<PlateauWorkItem> workItems,
        int workPosition,
        int plateauCount,
        int currentPlateau,
        double currentTargetTemperature,
        CalibrationRunRecord run,
        CalibrationSetup setup,
        CalibrationRunWriter writer)
    {
        int[] requested;
        lock (_manualRecalibrationSync)
        {
            requested = _manualRecalibrationRequests.OrderBy(index => index).ToArray();
            _manualRecalibrationRequests.Clear();
        }

        bool appended = false;
        foreach (int plateauIndex in requested)
        {
            bool completed = run.Plateaus.Any(plateau => plateau.PlateauIndex == plateauIndex);
            bool alreadyPending = workItems.Skip(workPosition + 1).Any(item => item.PlateauIndex == plateauIndex);
            if (!completed || alreadyPending || plateauIndex >= plateauCount)
                continue;

            workItems.Add(new PlateauWorkItem(plateauIndex, IsRetry: true, IsManual: true));
            appended = true;
            writer.WriteDiagnostic("INFO", "PLATEAU_RECALIBRATION_REQUESTED",
                $"Operátor označil plato {plateauIndex + 1} na opakovanú kalibráciu a vyhodnotenie.");
        }

        if (!appended) return;
        SaveCheckpoint(run, setup, currentPlateau, currentTargetTemperature,
            workItems.Skip(workPosition + 1).Where(item => item.IsRetry).Select(item => item.PlateauIndex));
        writer.SaveSummary();
    }

    private sealed record PlateauWorkItem(int PlateauIndex, bool IsRetry, bool IsManual = false);
    private Func<CancellationToken, Task<IReadOnlyList<PeakLoggerMeasurement>>>? _identityObservation;

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
        CancellationToken cancellationToken,
        CalibrationRunState progressState = CalibrationRunState.MovingToPlateau,
        string? progressContext = null)
    {
        if (!settings.EnableSetpointRamp || Math.Abs(targetTemperature - fromTemperature) < 0.001)
        {
            if (_identityObservation is not null) await _identityObservation(cancellationToken).ConfigureAwait(false);
            await WriteSetpointAsync(targetTemperature, targetHumidity, cancellationToken).ConfigureAwait(false);
            return;
        }

        double direction = Math.Sign(targetTemperature - fromTemperature);
        double commanded = fromTemperature;
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;

        while (Math.Abs(targetTemperature - commanded) > 0.001)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);

            if (_identityObservation is not null) await _identityObservation(cancellationToken).ConfigureAwait(false);

            // The operator may tune the ramp speed during an active FBG run. Read the
            // shared settings for every command step so the new limit applies without
            // restarting the run or disturbing any stability windows.
            double rateCPerMinute = NormalizeSetpointRate(settings.SetpointRampCPerMinute);
            double stepC = rateCPerMinute * _updateInterval.TotalMinutes;
            commanded += direction * Math.Min(stepC, Math.Abs(targetTemperature - commanded));
            await WriteSetpointAsync(commanded, targetHumidity, cancellationToken).ConfigureAwait(false);

            double? actual = null;
            try { actual = await ReadTemperatureAsync(cancellationToken).ConfigureAwait(false); }
            catch (InvalidOperationException) { }

            Progress?.Invoke(new CalibrationProgressSnapshot(
                progressState,
                plateauIndex,
                plateauCount,
                targetTemperature,
                actual,
                null,
                0,
                0,
                DateTimeOffset.UtcNow - startedAt,
                Array.Empty<CalibrationTargetProgress>(),
                $"{progressContext ?? "Plynulý nábeh"} · {rateCPerMinute:F2} °C/min · setpoint {commanded:F2} °C · cieľ {targetTemperature:F2} °C. " +
                "Komora sa reguluje vlastným snímačom; WIKA zatiaľ iba meria."));

            if (Math.Abs(targetTemperature - commanded) > 0.001)
                await Task.Delay(_updateInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunFinalConditioningAsync(
        CalibrationSetup setup,
        CalibrationRunRecord run,
        CalibrationRunWriter writer,
        int calibrationPlateauCount,
        double fromTemperature,
        double? humidity,
        Func<CancellationToken, Task<double?>>? readReferenceTemperatureAsync,
        CancellationToken cancellationToken)
    {
        const double target = 25.0;
        TimeSpan required = TimeSpan.Zero; // Legacy conditioning duration must not delay verification.
        run.CalibrationResults = TemperatureCalibrationAnalyzer.Analyze(run);
        run.State = CalibrationRunState.FinalConditioning;
        run.FinalConditioningTemperatureC = target;
        run.FinalConditioningRequiredDuration = required;
        run.FinalConditioningStartedAt = null;
        run.FinalConditioningCompletedAt = null;
        SaveCheckpoint(run, setup, Math.Max(0, calibrationPlateauCount - 1), target, Array.Empty<int>());
        writer.SaveSummary();

        await MoveSetpointToPlateauAsync(
            setup.Settings,
            -1,
            calibrationPlateauCount,
            fromTemperature,
            target,
            humidity,
            cancellationToken,
            CalibrationRunState.FinalConditioning,
            "Záverečný návrat na 25 °C").ConfigureAwait(false);

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        run.FinalConditioningStartedAt = startedAt;
        writer.SaveSummary();

        // The final point uses the production plateau gates and fresh sampling.
        // Keep it separate from run.Plateaus so it cannot influence the fitted coefficients.
        run.FinalVerification = await _orchestrator.WaitForPlateauAsync(
            run, setup, -1, calibrationPlateauCount, target, required,
            ReadTemperatureAsync, readReferenceTemperatureAsync, writer,
            snapshot => Progress?.Invoke(snapshot with
            {
                State = CalibrationRunState.FinalConditioning,
                Message = "ZÁVEREČNÉ OVERENIE 25 °C · " + snapshot.Message +
                    " Kontrolné vzorky sa nepoužijú na výpočet koeficientov."
            }), cancellationToken, deferOnTemperatureTimeout: false).ConfigureAwait(false);
        run.CalibrationResults = TemperatureCalibrationAnalyzer.Analyze(run);
        if (run.CalibrationResults.Any(r => r.FinalCheckStatus != "PASS"))
        {
            var warning = new CalibrationWarning { Code = "FINAL_VERIFICATION_PROBLEM",
                Message = "Kontrola pri 25 °C nevyhovela alebo nie je úplná. Pozrite koeficienty a kontrolné vzorky." };
            run.Warnings.Add(warning);
            writer.WriteDiagnostic("WARNING", warning.Code, warning.Message);
        }
        await _chamber.StopAsync(cancellationToken).ConfigureAwait(false);
        run.FinalConditioningCompletedAt = DateTimeOffset.Now;
        writer.SaveSummary();
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
        PhysicalFbgId = m.PhysicalFbgId,
        Channel = m.Channel,
        Core1 = m.Core1,
        Core2 = m.Core2,
        SerialNumber = m.SerialNumber,
        SensorName = m.SensorName,
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
