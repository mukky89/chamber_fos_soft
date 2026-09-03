using System.Diagnostics;

namespace VotschVc3.Core.Calibration;

/// <summary>
/// Production temperature-calibration orchestrator.
///
/// The gate order mirrors the proven Auto_calibrator_Pali process while keeping the modern
/// parallel FBG implementation:
/// 1) chamber must reach and hold its own stable window;
/// 2) when an external reference is configured, WIKA/CTH7000 must then be stable too;
/// 3) every selected FBG qualifies independently in a rolling stability window;
/// 4) a qualified FBG starts a fresh final measurement window;
/// 5) the plateau completes only when every selected FBG has a terminal result.
///
/// If either temperature gate is lost, unfinished FBG stability/final-sample windows are reset.
/// Stabilization samples are never reused as final calibration samples.
/// </summary>
public sealed class CalibrationOrchestrator
{
    private readonly IPeakLoggerClient _peakLogger;

    public CalibrationOrchestrator(IPeakLoggerClient peakLogger)
    {
        _peakLogger = peakLogger ?? throw new ArgumentNullException(nameof(peakLogger));
    }

    public event Action<CalibrationWarning>? WarningRaised;

    public async Task<IReadOnlyList<PeakLoggerSensor>> PreflightAsync(
        CalibrationSetup setup,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setup);
        List<CalibrationSensorMapping> selected = setup.Mappings.Where(x => x.Selected).ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException("Nie je vybraná žiadna wavelength na kalibráciu.");

        foreach (CalibrationSensorMapping mapping in selected)
        {
            if (string.IsNullOrWhiteSpace(mapping.SerialNumber))
            {
                throw new InvalidOperationException(
                    $"Pre PeakLogger {mapping.SourceDeviceSerialNumber} / kanál {mapping.Channel} / peak {mapping.PeakId} chýba sériové číslo FBG senzora.");
            }
        }

        IReadOnlyList<PeakLoggerSensor> sensors = await _peakLogger.DiscoverSensorsAsync(cancellationToken).ConfigureAwait(false);
        foreach (CalibrationSensorMapping mapping in selected)
        {
            PeakLoggerSensor? sensor = sensors.FirstOrDefault(s =>
                string.Equals(s.SerialNumber, mapping.SourceDeviceSerialNumber, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(mapping.Channel) || string.Equals(s.Channel, mapping.Channel, StringComparison.OrdinalIgnoreCase)));
            if (sensor is null)
            {
                throw new InvalidOperationException(
                    $"PeakLogger: zariadenie {mapping.SourceDeviceSerialNumber} / kanál {mapping.Channel} pre FBG SN {mapping.SerialNumber} nebolo nájdené.");
            }

            PeakLoggerPeak? peak = sensor.Peaks.FirstOrDefault(p => string.Equals(p.PeakId, mapping.PeakId, StringComparison.Ordinal));
            if (peak is null)
            {
                throw new InvalidOperationException(
                    $"PeakLogger: peak {mapping.PeakId} na {mapping.SourceDeviceSerialNumber} / {mapping.Channel} pre FBG SN {mapping.SerialNumber} nebol nájdený.");
            }

            mapping.PeakLoggerDeviceSerialNumber = sensor.SerialNumber;
            mapping.PeakIndex = peak.PeakIndex;
            mapping.CurrentWavelengthNm = peak.WavelengthNm;
            mapping.NominalWavelengthNm ??= peak.WavelengthNm;
        }

        return sensors;
    }

    public Task<CalibrationPlateauResult> WaitForPlateauAsync(
        CalibrationRunRecord run,
        CalibrationSetup setup,
        int plateauIndex,
        int plateauCount,
        double targetTemperatureC,
        Func<CancellationToken, Task<double>> readChamberTemperatureAsync,
        Func<CancellationToken, Task<double?>>? readReferenceTemperatureAsync,
        CalibrationRunWriter writer,
        Action<CalibrationProgressSnapshot>? progress = null,
        CancellationToken cancellationToken = default) =>
        WaitForPlateauAsync(
            run,
            setup,
            plateauIndex,
            plateauCount,
            targetTemperatureC,
            setup.Settings.MinimumCalibrationPointDuration,
            readChamberTemperatureAsync,
            readReferenceTemperatureAsync,
            referenceControlAsync: null,
            writer,
            progress,
            cancellationToken);

    public async Task<CalibrationPlateauResult> WaitForPlateauAsync(
        CalibrationRunRecord run,
        CalibrationSetup setup,
        int plateauIndex,
        int plateauCount,
        double targetTemperatureC,
        TimeSpan minimumPlateauDuration,
        Func<CancellationToken, Task<double>> readChamberTemperatureAsync,
        Func<CancellationToken, Task<double?>>? readReferenceTemperatureAsync,
        Func<double, double?, CancellationToken, Task<string?>>? referenceControlAsync,
        CalibrationRunWriter writer,
        Action<CalibrationProgressSnapshot>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(readChamberTemperatureAsync);
        ArgumentNullException.ThrowIfNull(writer);

        CalibrationProfileSettings settings = setup.Settings;
        minimumPlateauDuration = minimumPlateauDuration < TimeSpan.Zero ? TimeSpan.Zero : minimumPlateauDuration;
        List<CalibrationSensorMapping> selected = setup.Mappings.Where(x => x.Selected).ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException("Calibration setup nemá vybrané peaky.");

        DateTimeOffset plateauStarted = DateTimeOffset.Now;
        Stopwatch plateauClock = Stopwatch.StartNew();
        bool hasExternalReference = readReferenceTemperatureAsync is not null;
        var chamberDetector = new TemperatureStabilityDetector(
            settings.ChamberStableDuration,
            settings.ChamberToleranceC,
            settings.MaxChamberDriftCPerMinute);
        var referenceDetector = new TemperatureStabilityDetector(
            settings.ReferenceStableDuration,
            settings.ReferenceToleranceC,
            settings.MaxReferenceDriftCPerMinute);
        var trackers = selected.ToDictionary(
            m => m.Identity,
            m => new TargetTracker(m, settings),
            StringComparer.OrdinalIgnoreCase);

        double actualTemperature = double.NaN;
        double? referenceTemperature = null;
        StabilityMetrics? chamberMetrics = null;
        StabilityMetrics? referenceMetrics = null;
        bool temperatureGateOpen = false;
        DateTimeOffset? chamberWaitStarted = null;
        DateTimeOffset? referenceWaitStarted = null;
        DateTimeOffset? referenceMissingSince = null;
        DateTimeOffset? peakLoggerRecoveryStarted = null;
        DateTimeOffset previousLoopAt = DateTimeOffset.UtcNow;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset loopAt = DateTimeOffset.UtcNow;
            TimeSpan loopDelta = loopAt - previousLoopAt;
            previousLoopAt = loopAt;

            actualTemperature = await readChamberTemperatureAsync(cancellationToken).ConfigureAwait(false);
            referenceTemperature = hasExternalReference
                ? await readReferenceTemperatureAsync!(cancellationToken).ConfigureAwait(false)
                : null;

            string controlDetail = referenceControlAsync is null
                ? string.Empty
                : await referenceControlAsync(targetTemperatureC, referenceTemperature, cancellationToken).ConfigureAwait(false) ?? string.Empty;

            if (_peakLogger is IPeakLoggerSimulationControl simulation)
                simulation.SimulatedTemperatureC = referenceTemperature ?? actualTemperature;

            chamberMetrics = chamberDetector.Add(loopAt, actualTemperature, targetTemperatureC);
            bool chamberStable = chamberMetrics.IsStable;
            bool minimumElapsed = plateauClock.Elapsed >= minimumPlateauDuration;

            if (chamberWaitStarted is null && minimumElapsed) chamberWaitStarted = loopAt;
            if (!chamberStable || !minimumElapsed)
            {
                run.State = CalibrationRunState.WaitingForChamberStability;
                referenceMetrics = null;
                referenceDetector = new TemperatureStabilityDetector(
                    settings.ReferenceStableDuration,
                    settings.ReferenceToleranceC,
                    settings.MaxReferenceDriftCPerMinute);
                referenceWaitStarted = null;
                referenceMissingSince = null;
                ResetTemperatureGate(trackers, ref temperatureGateOpen);

                string minimumDetail = MinimumDetail(plateauClock.Elapsed, minimumPlateauDuration);
                string chamberDetail = BuildTemperatureDetail(
                    "KOMORA",
                    actualTemperature,
                    targetTemperatureC,
                    chamberMetrics,
                    settings.ChamberToleranceC,
                    settings.ChamberStableDuration,
                    settings.MaxChamberDriftCPerMinute);
                progress?.Invoke(BuildSnapshot(
                    CalibrationRunState.WaitingForChamberStability,
                    plateauIndex,
                    plateauCount,
                    targetTemperatureC,
                    actualTemperature,
                    referenceTemperature,
                    plateauClock.Elapsed,
                    trackers,
                    settings,
                    $"KROK 2/6 · Stabilizácia komory · {minimumDetail} · {chamberDetail}{controlDetail}"));

                if (minimumElapsed && settings.ChamberStabilityTimeout > TimeSpan.Zero &&
                    chamberWaitStarted is { } chamberStart && loopAt - chamberStart >= settings.ChamberStabilityTimeout)
                {
                    throw BuildTemperatureTimeout(
                        run,
                        "CHAMBER_STABILITY_TIMEOUT",
                        "Komora",
                        plateauIndex,
                        targetTemperatureC,
                        actualTemperature,
                        settings.ChamberStabilityTimeout);
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (hasExternalReference)
            {
                run.State = CalibrationRunState.WaitingForReferenceStability;
                if (referenceTemperature is null)
                {
                    referenceMissingSince ??= loopAt;
                    ResetTemperatureGate(trackers, ref temperatureGateOpen);
                    progress?.Invoke(BuildSnapshot(
                        CalibrationRunState.RecoveringDevice,
                        plateauIndex,
                        plateauCount,
                        targetTemperatureC,
                        actualTemperature,
                        null,
                        plateauClock.Elapsed,
                        trackers,
                        settings,
                        "KROK 3/6 · WIKA/CTH7000 nevrátil platnú teplotu · čakám na obnovenie zariadenia."));

                    if (settings.DeviceRecoveryTimeout > TimeSpan.Zero &&
                        loopAt - referenceMissingSince.Value >= settings.DeviceRecoveryTimeout)
                    {
                        throw BuildTemperatureTimeout(
                            run,
                            "REFERENCE_DEVICE_RECOVERY_TIMEOUT",
                            "WIKA CTH7000",
                            plateauIndex,
                            targetTemperatureC,
                            double.NaN,
                            settings.DeviceRecoveryTimeout);
                    }

                    await Task.Delay(RecoveryDelay(settings), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                referenceMissingSince = null;
                referenceMetrics = referenceDetector.Add(loopAt, referenceTemperature.Value, targetTemperatureC);
                bool referenceStable = referenceMetrics.IsStable;
                referenceWaitStarted ??= loopAt;
                if (!referenceStable)
                {
                    ResetTemperatureGate(trackers, ref temperatureGateOpen);
                    string referenceDetail = BuildTemperatureDetail(
                        "WIKA",
                        referenceTemperature.Value,
                        targetTemperatureC,
                        referenceMetrics,
                        settings.ReferenceToleranceC,
                        settings.ReferenceStableDuration,
                        settings.MaxReferenceDriftCPerMinute);
                    progress?.Invoke(BuildSnapshot(
                        CalibrationRunState.WaitingForReferenceStability,
                        plateauIndex,
                        plateauCount,
                        targetTemperatureC,
                        actualTemperature,
                        referenceTemperature,
                        plateauClock.Elapsed,
                        trackers,
                        settings,
                        $"KROK 3/6 · Komora stabilná ✓ · stabilizácia referencie · {referenceDetail}{controlDetail}"));

                    if (settings.ReferenceStabilityTimeout > TimeSpan.Zero &&
                        loopAt - referenceWaitStarted.Value >= settings.ReferenceStabilityTimeout)
                    {
                        throw BuildTemperatureTimeout(
                            run,
                            "REFERENCE_STABILITY_TIMEOUT",
                            "WIKA CTH7000",
                            plateauIndex,
                            targetTemperatureC,
                            referenceTemperature.Value,
                            settings.ReferenceStabilityTimeout);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            if (!temperatureGateOpen)
            {
                temperatureGateOpen = true;
                foreach (TargetTracker tracker in trackers.Values.Where(t => !t.IsTerminal)) tracker.BeginSensorPhase();
            }

            run.State = CalibrationRunState.StabilizingSensors;
            IReadOnlyList<PeakLoggerMeasurement> batch;
            try
            {
                batch = await _peakLogger.ReadMeasurementsAsync(cancellationToken).ConfigureAwait(false);
                peakLoggerRecoveryStarted = null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                CalibrationWarning warning = RaiseWarning(run, new CalibrationWarning
                {
                    Code = "PEAKLOGGER_DISCONNECTED",
                    Message = $"PeakLogger prestal poskytovať dáta: {ex.Message}",
                    PlateauIndex = plateauIndex,
                });

                if (settings.PeakLoggerDisconnectPolicy == CalibrationFailurePolicy.WaitAndRecover)
                {
                    peakLoggerRecoveryStarted ??= loopAt;
                    ResetTemperatureGate(trackers, ref temperatureGateOpen);
                    progress?.Invoke(BuildSnapshot(
                        CalibrationRunState.RecoveringDevice,
                        plateauIndex,
                        plateauCount,
                        targetTemperatureC,
                        actualTemperature,
                        referenceTemperature,
                        plateauClock.Elapsed,
                        trackers,
                        settings,
                        $"PeakLogger výpadok · čakám na automatické obnovenie · {ex.Message}"));
                    if (settings.DeviceRecoveryTimeout > TimeSpan.Zero &&
                        loopAt - peakLoggerRecoveryStarted.Value >= settings.DeviceRecoveryTimeout)
                    {
                        throw new CalibrationOperatorActionRequiredException(
                            $"PeakLogger sa neobnovil do {settings.DeviceRecoveryTimeout}.", warning);
                    }
                    await Task.Delay(RecoveryDelay(settings), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (settings.PeakLoggerDisconnectPolicy == CalibrationFailurePolicy.ContinueAndFlag)
                {
                    foreach (TargetTracker tracker in trackers.Values.Where(t => !t.IsTerminal))
                        tracker.Fail(CalibrationTargetState.Disconnected, warning.Message);
                    break;
                }

                ApplyFailurePolicy(settings.PeakLoggerDisconnectPolicy, warning);
                throw;
            }

            var rawToWrite = new List<CalibrationRawSample>();
            foreach (TargetTracker tracker in trackers.Values.Where(t => !t.IsTerminal))
            {
                tracker.AddActiveElapsed(loopDelta);
                PeakLoggerMeasurement? measurement = FindMeasurement(batch, tracker.Mapping);
                if (measurement is null)
                {
                    tracker.MarkMissing(loopAt);
                    if (tracker.MissingFor >= settings.PeakLostGracePeriod)
                    {
                        if (settings.PeakLostPolicy == CalibrationFailurePolicy.WaitAndRecover &&
                            tracker.MissingFor < settings.DeviceRecoveryTimeout)
                            continue;

                        CalibrationWarning warning = RaiseWarning(run, new CalibrationWarning
                        {
                            Code = "PEAK_LOST",
                            Message = $"FBG SN {tracker.Mapping.SerialNumber}, peak {tracker.Mapping.PeakId} sa počas kalibrácie stratil.",
                            PlateauIndex = plateauIndex,
                            SerialNumber = tracker.Mapping.SerialNumber,
                            PeakId = tracker.Mapping.PeakId,
                        });
                        if (settings.PeakLostPolicy == CalibrationFailurePolicy.ContinueAndFlag)
                            tracker.Fail(CalibrationTargetState.PeakLost, warning.Message);
                        else
                            ApplyFailurePolicy(settings.PeakLostPolicy, warning);
                    }
                    continue;
                }

                if (loopAt - measurement.Timestamp > TimeSpan.FromSeconds(10))
                {
                    tracker.MarkMissing(loopAt);
                    continue;
                }

                measurement = tracker.ApplyAveraging(measurement);
                tracker.MarkMeasurement(measurement);
                CalibrationRawSample raw = CreateRawSample(
                    run,
                    plateauIndex,
                    targetTemperatureC,
                    actualTemperature,
                    referenceTemperature,
                    tracker.Mapping,
                    measurement);
                rawToWrite.Add(raw);
                tracker.ProcessStableTemperatureSample(raw);

                if (!tracker.IsTerminal && tracker.ActiveElapsed >= tracker.Timeout)
                {
                    CalibrationWarning warning = RaiseWarning(run, new CalibrationWarning
                    {
                        Code = "SENSOR_STABILITY_TIMEOUT",
                        Message = $"FBG SN {tracker.Mapping.SerialNumber}, peak {tracker.Mapping.PeakId} nedokončil stabilizáciu/meranie do {tracker.Timeout} počas stabilnej teploty.",
                        PlateauIndex = plateauIndex,
                        SerialNumber = tracker.Mapping.SerialNumber,
                        PeakId = tracker.Mapping.PeakId,
                    });
                    if (settings.SensorTimeoutPolicy == CalibrationFailurePolicy.ContinueAndFlag)
                        tracker.CompleteTimedOut(warning.Message);
                    else
                        ApplyFailurePolicy(settings.SensorTimeoutPolicy, warning);
                }
            }

            if (rawToWrite.Count > 0)
                await writer.AppendAsync(rawToWrite, cancellationToken).ConfigureAwait(false);

            int completed = trackers.Values.Count(t => t.IsCompletedStable);
            int measuring = trackers.Values.Count(t => t.IsMeasuring && !t.IsTerminal);
            int stabilizing = trackers.Values.Count(t => !t.IsMeasuring && !t.IsTerminal);
            bool allTerminal = trackers.Values.All(t => t.IsTerminal);
            string phaseMessage = allTerminal
                ? "KROK 6/6 · Všetky FBG sú dokončené · ukladám kalibračný bod."
                : $"KROK 4–5/6 · FBG paralelne: stabilizuje sa {stabilizing}, meria sa {measuring}, hotovo {completed}/{selected.Count}.";
            progress?.Invoke(BuildSnapshot(
                CalibrationRunState.StabilizingSensors,
                plateauIndex,
                plateauCount,
                targetTemperatureC,
                actualTemperature,
                referenceTemperature,
                plateauClock.Elapsed,
                trackers,
                settings,
                $"{phaseMessage} Finálne meranie používa {Math.Max(2, settings.RequiredMeasurementSamples)} nových samples na peak."));

            if (allTerminal) break;
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        List<CalibrationMeasurementResult> targetResults = trackers.Values
            .Select(t => t.Result ?? t.CreateFallbackResult())
            .ToList();
        double? meanReference = targetResults
            .Where(t => t.MeanReferenceTemperatureC.HasValue)
            .Select(t => t.MeanReferenceTemperatureC!.Value)
            .DefaultIfEmpty()
            .AverageOrNull();
        double? meanChamber = targetResults
            .Where(t => t.MeanChamberTemperatureC.HasValue)
            .Select(t => t.MeanChamberTemperatureC!.Value)
            .DefaultIfEmpty()
            .AverageOrNull();

        var result = new CalibrationPlateauResult
        {
            PlateauIndex = plateauIndex,
            TargetTemperatureC = targetTemperatureC,
            ActualTemperatureC = meanChamber ?? actualTemperature,
            ReferenceTemperatureC = meanReference ?? referenceTemperature,
            StartedAt = plateauStarted,
            CompletedAt = DateTimeOffset.Now,
            Targets = targetResults,
        };
        run.State = CalibrationRunState.PlateauCompleted;
        return result;
    }

    public bool ValidateTemperatureResponse(
        CalibrationRunRecord run,
        CalibrationPlateauResult baseline,
        CalibrationPlateauResult current,
        CalibrationProfileSettings settings)
    {
        double baselineTemperature = baseline.ReferenceTemperatureC ?? baseline.ActualTemperatureC;
        double currentTemperature = current.ReferenceTemperatureC ?? current.ActualTemperatureC;
        double deltaT = currentTemperature - baselineTemperature;
        if (Math.Abs(deltaT) < settings.ValidationMinimumDeltaTemperatureC) return false;

        run.State = CalibrationRunState.TemperatureResponseValidation;
        bool allValid = true;
        foreach (CalibrationMeasurementResult currentTarget in current.Targets)
        {
            CalibrationMeasurementResult? baseTarget = baseline.Targets.FirstOrDefault(x =>
                string.Equals(x.Identity, currentTarget.Identity, StringComparison.OrdinalIgnoreCase));
            if (baseTarget is null ||
                currentTarget.Status is not (CalibrationTargetState.Stable or CalibrationTargetState.Overridden) ||
                baseTarget.Status is not (CalibrationTargetState.Stable or CalibrationTargetState.Overridden))
                continue;

            double targetDeltaT =
                (currentTarget.MeanReferenceTemperatureC ?? current.ReferenceTemperatureC ?? current.ActualTemperatureC) -
                (baseTarget.MeanReferenceTemperatureC ?? baseline.ReferenceTemperatureC ?? baseline.ActualTemperatureC);
            if (Math.Abs(targetDeltaT) < settings.ValidationMinimumDeltaTemperatureC) continue;

            double deltaPm = (currentTarget.MeanWavelengthNm - baseTarget.MeanWavelengthNm) * 1000d;
            bool magnitudeOk = Math.Abs(deltaPm) >= settings.ValidationMinimumWavelengthResponsePm;
            bool directionOk = settings.ExpectedResponseDirection switch
            {
                ExpectedResponseDirection.Positive => deltaPm / targetDeltaT > 0,
                ExpectedResponseDirection.Negative => deltaPm / targetDeltaT < 0,
                _ => true,
            };
            if (magnitudeOk && directionOk) continue;

            allValid = false;
            CalibrationWarning warning = RaiseWarning(run, new CalibrationWarning
            {
                Code = "NO_TEMPERATURE_RESPONSE",
                Message = $"FBG SN {currentTarget.SerialNumber}, peak {currentTarget.PeakId}: Δλ={deltaPm:F2} pm pri ΔT={targetDeltaT:F2} °C – vybraná wavelength nereaguje podľa nastavených limitov.",
                PlateauIndex = current.PlateauIndex,
                SerialNumber = currentTarget.SerialNumber,
                PeakId = currentTarget.PeakId,
                Overridden = settings.AllowValidationOverride,
                OverrideReason = settings.AllowValidationOverride ? settings.ValidationOverrideReason : null,
            });

            if (settings.AllowValidationOverride)
            {
                currentTarget.Status = CalibrationTargetState.Overridden;
                currentTarget.Problem = warning.Message;
                continue;
            }
            ApplyFailurePolicy(settings.ValidationFailurePolicy, warning);
        }

        return allValid || settings.AllowValidationOverride;
    }

    private static void ResetTemperatureGate(Dictionary<string, TargetTracker> trackers, ref bool gateOpen)
    {
        if (gateOpen)
        {
            foreach (TargetTracker tracker in trackers.Values.Where(t => !t.IsTerminal)) tracker.ResetForTemperatureLoss();
        }
        gateOpen = false;
    }

    private CalibrationProgressSnapshot BuildSnapshot(
        CalibrationRunState state,
        int plateauIndex,
        int plateauCount,
        double target,
        double chamber,
        double? reference,
        TimeSpan elapsed,
        Dictionary<string, TargetTracker> trackers,
        CalibrationProfileSettings settings,
        string message)
    {
        int completed = trackers.Values.Count(t => t.IsCompletedStable);
        return new CalibrationProgressSnapshot(
            state,
            plateauIndex,
            plateauCount,
            target,
            chamber,
            reference,
            completed,
            trackers.Count,
            elapsed,
            trackers.Values.Select(t => t.ToProgress(settings, state)).ToArray(),
            message);
    }

    private CalibrationOperatorActionRequiredException BuildTemperatureTimeout(
        CalibrationRunRecord run,
        string code,
        string source,
        int plateauIndex,
        double targetTemperatureC,
        double measuredTemperature,
        TimeSpan timeout)
    {
        string measured = double.IsFinite(measuredTemperature) ? $"{measuredTemperature:F3} °C" : "bez platnej hodnoty";
        CalibrationWarning warning = RaiseWarning(run, new CalibrationWarning
        {
            Code = code,
            Message = $"{source} sa neustálila/nezotavila na {targetTemperatureC:F1} °C do {timeout}. Posledná hodnota: {measured}.",
            PlateauIndex = plateauIndex,
        });
        return new CalibrationOperatorActionRequiredException(warning.Message, warning);
    }

    private static PeakLoggerMeasurement? FindMeasurement(
        IReadOnlyList<PeakLoggerMeasurement> batch,
        CalibrationSensorMapping mapping) => batch
        .Where(m => string.Equals(m.SerialNumber, mapping.SourceDeviceSerialNumber, StringComparison.OrdinalIgnoreCase))
        .Where(m => string.Equals(m.PeakId, mapping.PeakId, StringComparison.Ordinal))
        .Where(m => string.IsNullOrWhiteSpace(mapping.Channel) || string.Equals(m.Channel, mapping.Channel, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(m => m.Timestamp)
        .FirstOrDefault();

    private static string MinimumDetail(TimeSpan elapsed, TimeSpan minimum)
    {
        if (minimum <= TimeSpan.Zero) return "minimum calibration hold: bez minima";
        TimeSpan shown = elapsed < minimum ? elapsed : minimum;
        return $"minimum calibration hold {FormatTime(shown)}/{FormatTime(minimum)} {(elapsed >= minimum ? "✓" : "…")}";
    }

    private static string BuildTemperatureDetail(
        string source,
        double measured,
        double target,
        StabilityMetrics metrics,
        double tolerance,
        TimeSpan stableDuration,
        double maxDrift)
    {
        double error = measured - target;
        bool toleranceOk = Math.Abs(error) <= tolerance;
        bool durationOk = metrics.WindowDuration >= stableDuration;
        bool driftOk = maxDrift <= 0 || Math.Abs(metrics.SlopePerMinute) <= maxDrift;
        return $"{source} {measured:F3} °C · Δ {error:+0.000;-0.000;0.000} / ±{tolerance:F3} {(toleranceOk ? "✓" : "×")} · " +
               $"stable čas {FormatTime(metrics.WindowDuration)}/{FormatTime(stableDuration)} {(durationOk ? "✓" : "…")} · " +
               $"drift {Math.Abs(metrics.SlopePerMinute):F3}/{maxDrift:F3} °C/min {(driftOk ? "✓" : "×")}";
    }

    private CalibrationWarning RaiseWarning(CalibrationRunRecord run, CalibrationWarning warning)
    {
        run.Warnings.Add(warning);
        WarningRaised?.Invoke(warning);
        return warning;
    }

    private static void ApplyFailurePolicy(CalibrationFailurePolicy policy, CalibrationWarning warning)
    {
        switch (policy)
        {
            case CalibrationFailurePolicy.ContinueAndFlag:
            case CalibrationFailurePolicy.WaitAndRecover:
                return;
            case CalibrationFailurePolicy.PauseForOperator:
                throw new CalibrationOperatorActionRequiredException(warning.Message, warning);
            case CalibrationFailurePolicy.AbortCalibration:
                throw new InvalidOperationException(warning.Message);
        }
    }

    private static CalibrationRawSample CreateRawSample(
        CalibrationRunRecord run,
        int plateauIndex,
        double targetTemperatureC,
        double actualTemperatureC,
        double? referenceTemperatureC,
        CalibrationSensorMapping mapping,
        PeakLoggerMeasurement measurement) => new()
    {
        RunId = run.RunId,
        ProfileId = run.ProfileId,
        PlateauIndex = plateauIndex,
        TargetTemperatureC = targetTemperatureC,
        ActualTemperatureC = actualTemperatureC,
        ReferenceTemperatureC = referenceTemperatureC,
        Timestamp = measurement.Timestamp,
        SerialNumber = mapping.SerialNumber,
        PeakLoggerDeviceSerialNumber = measurement.SerialNumber,
        Channel = measurement.Channel,
        PeakId = measurement.PeakId,
        PeakIndex = measurement.PeakIndex,
        WavelengthNm = measurement.WavelengthNm,
        Intensity = measurement.Intensity,
    };

    private static TimeSpan RecoveryDelay(CalibrationProfileSettings settings) =>
        settings.DeviceRecoveryPollInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : settings.DeviceRecoveryPollInterval;

    private static string FormatTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");
    }

    private sealed class TargetTracker
    {
        private readonly CalibrationProfileSettings _settings;
        private readonly int _averagingSamples;
        private readonly Queue<PeakLoggerMeasurement> _averagingWindow = new();
        private readonly List<CalibrationRawSample> _measurementSamples = new();
        private DateTimeOffset? _missingSince;
        private RollingStabilityDetector _stabilityDetector;
        private bool _sensorPhaseStarted;

        public TargetTracker(CalibrationSensorMapping mapping, CalibrationProfileSettings settings)
        {
            Mapping = mapping;
            _settings = settings;
            _averagingSamples = settings.EnableWavelengthAveraging
                ? Math.Clamp(settings.WavelengthAveragingSamples, 1, 1000)
                : 1;
            _stabilityDetector = NewStabilityDetector();
            Timeout = mapping.StabilizationTimeoutOverride ?? settings.DefaultSensorStabilizationTimeout;
            State = CalibrationTargetState.WaitingForTemperature;
        }

        public CalibrationSensorMapping Mapping { get; }
        public TimeSpan Timeout { get; }
        public TimeSpan ActiveElapsed { get; private set; }
        public CalibrationTargetState State { get; private set; }
        public StabilityMetrics? LastMetrics { get; private set; }
        public PeakLoggerMeasurement? LastMeasurement { get; private set; }
        public CalibrationMeasurementResult? Result { get; private set; }
        public TimeSpan MissingFor => _missingSince is { } since ? DateTimeOffset.UtcNow - since : TimeSpan.Zero;
        public bool IsMeasuring { get; private set; }
        public bool IsTerminal => Result is not null;
        public bool IsCompletedStable => Result?.Status is CalibrationTargetState.Stable or CalibrationTargetState.Overridden;

        public void BeginSensorPhase()
        {
            if (IsTerminal) return;
            _sensorPhaseStarted = true;
            if (State == CalibrationTargetState.WaitingForTemperature) State = CalibrationTargetState.Stabilizing;
        }

        public void AddActiveElapsed(TimeSpan delta)
        {
            if (_sensorPhaseStarted && !IsTerminal && delta > TimeSpan.Zero) ActiveElapsed += delta;
        }

        public PeakLoggerMeasurement ApplyAveraging(PeakLoggerMeasurement measurement)
        {
            if (_averagingSamples <= 1) return measurement;
            _averagingWindow.Enqueue(measurement);
            while (_averagingWindow.Count > _averagingSamples) _averagingWindow.Dequeue();
            double? intensity = _averagingWindow.Any(sample => sample.Intensity.HasValue)
                ? _averagingWindow.Where(sample => sample.Intensity.HasValue).Average(sample => sample.Intensity!.Value)
                : null;
            return measurement with
            {
                WavelengthNm = _averagingWindow.Average(sample => sample.WavelengthNm),
                Intensity = intensity,
            };
        }

        public void MarkMeasurement(PeakLoggerMeasurement measurement)
        {
            LastMeasurement = measurement;
            Mapping.CurrentWavelengthNm = measurement.WavelengthNm;
            _missingSince = null;
        }

        public void MarkMissing(DateTimeOffset now)
        {
            _missingSince ??= now;
            if (!IsTerminal) State = CalibrationTargetState.PeakLost;
        }

        public void ProcessStableTemperatureSample(CalibrationRawSample raw)
        {
            if (IsTerminal) return;
            LastMetrics = _stabilityDetector.Add(raw.Timestamp, raw.WavelengthNm);

            if (!IsMeasuring)
            {
                State = CalibrationTargetState.Stabilizing;
                if (LastMetrics.IsStable)
                {
                    IsMeasuring = true;
                    State = CalibrationTargetState.Live;
                    _measurementSamples.Clear();
                }
                return;
            }

            if (!LastMetrics.IsStable)
            {
                ResetToStabilizing();
                return;
            }

            State = CalibrationTargetState.Live;
            _measurementSamples.Add(raw);
            int requiredMeasurementSamples = Math.Max(2, _settings.RequiredMeasurementSamples);
            if (_measurementSamples.Count >= requiredMeasurementSamples) CompleteStableFromMeasurementWindow();
        }

        public void ResetForTemperatureLoss()
        {
            if (IsTerminal) return;
            _sensorPhaseStarted = false;
            State = CalibrationTargetState.WaitingForTemperature;
            IsMeasuring = false;
            _measurementSamples.Clear();
            _averagingWindow.Clear();
            _stabilityDetector = NewStabilityDetector();
            LastMetrics = null;
            _missingSince = null;
        }

        public void Fail(CalibrationTargetState state, string problem)
        {
            State = state;
            IsMeasuring = false;
            Result = CreateResultFromCurrentWindow(state, problem);
        }

        public void CompleteTimedOut(string problem)
        {
            State = CalibrationTargetState.TimedOut;
            IsMeasuring = false;
            Result = CreateResultFromCurrentWindow(State, problem);
        }

        public CalibrationMeasurementResult CreateFallbackResult() =>
            Result ?? CreateResultFromCurrentWindow(State, "Meranie skončilo bez kompletného výsledku.");

        public CalibrationTargetProgress ToProgress(CalibrationProfileSettings settings, CalibrationRunState runState)
        {
            StabilityMetrics metrics = LastMetrics ?? _stabilityDetector.Evaluate();
            int requiredStability = Math.Max(2, settings.RequiredStableSamples);
            int requiredMeasurement = Math.Max(2, settings.RequiredMeasurementSamples);
            bool enough = metrics.Count >= requiredStability;
            bool rangeOk = settings.MaxWavelengthRangePm <= 0 || metrics.Range <= settings.MaxWavelengthRangePm;
            bool stdOk = settings.MaxWavelengthStdDevPm <= 0 || metrics.StandardDeviation <= settings.MaxWavelengthStdDevPm;
            bool driftOk = settings.MaxWavelengthDriftPmPerMinute <= 0 || Math.Abs(metrics.SlopePerMinute) <= settings.MaxWavelengthDriftPmPerMinute;

            string phase;
            CalibrationTargetState displayState;
            if (IsTerminal)
            {
                phase = Result?.Status == CalibrationTargetState.Stable
                    ? $"HOTOVO · finálne samples {_measurementSamples.Count}/{requiredMeasurement} ✓"
                    : $"KONIEC · {Result?.Status}: {Result?.Problem}";
                displayState = Result?.Status ?? State;
            }
            else if (runState is CalibrationRunState.WaitingForChamberStability or CalibrationRunState.WaitingForReferenceStability or CalibrationRunState.RecoveringDevice)
            {
                phase = runState == CalibrationRunState.RecoveringDevice ? "ČAKÁ NA OBNOVENIE ZARIADENIA" : "ČAKÁ NA STABILNÚ TEPLOTU";
                displayState = CalibrationTargetState.WaitingForTemperature;
            }
            else if (IsMeasuring)
            {
                phase = $"MERANIE · {_measurementSamples.Count}/{requiredMeasurement} · stabilita range {metrics.Range:F3}/{settings.MaxWavelengthRangePm:F3} pm {(rangeOk ? "✓" : "×")} · std {metrics.StandardDeviation:F3}/{settings.MaxWavelengthStdDevPm:F3} {(stdOk ? "✓" : "×")} · drift {Math.Abs(metrics.SlopePerMinute):F3}/{settings.MaxWavelengthDriftPmPerMinute:F3} {(driftOk ? "✓" : "×")}";
                displayState = CalibrationTargetState.Live;
            }
            else
            {
                phase = $"STABILIZÁCIA · {metrics.Count}/{requiredStability} {(enough ? "✓" : "…")} · range {metrics.Range:F3}/{settings.MaxWavelengthRangePm:F3} {(rangeOk ? "✓" : "×")} · std {metrics.StandardDeviation:F3}/{settings.MaxWavelengthStdDevPm:F3} {(stdOk ? "✓" : "×")} · drift {Math.Abs(metrics.SlopePerMinute):F3}/{settings.MaxWavelengthDriftPmPerMinute:F3} {(driftOk ? "✓" : "×")}";
                displayState = CalibrationTargetState.Stabilizing;
            }

            int displaySamples = IsMeasuring || IsTerminal ? _measurementSamples.Count : metrics.Count;
            int displayRequired = IsMeasuring || IsTerminal ? requiredMeasurement : requiredStability;
            return new CalibrationTargetProgress(
                Mapping.SerialNumber,
                Mapping.Channel,
                Mapping.PeakId,
                Mapping.PeakIndex,
                LastMeasurement?.WavelengthNm ?? Mapping.CurrentWavelengthNm,
                displaySamples,
                displayRequired,
                metrics.Count > 0 ? metrics.StandardDeviation : null,
                metrics.Count > 0 ? metrics.SlopePerMinute : null,
                ActiveElapsed,
                Timeout,
                displayState,
                phase,
                StabilitySamples: metrics.Count,
                RequiredStabilitySamples: requiredStability,
                MeasurementSamples: _measurementSamples.Count,
                RequiredMeasurementSamples: requiredMeasurement,
                RangePm: metrics.Count > 0 ? metrics.Range : null,
                RangeLimitPm: settings.MaxWavelengthRangePm,
                StdDevLimitPm: settings.MaxWavelengthStdDevPm,
                DriftLimitPmPerMinute: settings.MaxWavelengthDriftPmPerMinute,
                Phase: phase,
                BlockingReason: phase);
        }

        private void ResetToStabilizing()
        {
            IsMeasuring = false;
            State = CalibrationTargetState.Stabilizing;
            _measurementSamples.Clear();
            _stabilityDetector = NewStabilityDetector();
            LastMetrics = null;
        }

        private RollingStabilityDetector NewStabilityDetector() => new(
            Math.Max(2, _settings.RequiredStableSamples),
            _settings.MaxWavelengthRangePm,
            _settings.MaxWavelengthStdDevPm,
            _settings.MaxWavelengthDriftPmPerMinute);

        private void CompleteStableFromMeasurementWindow()
        {
            State = CalibrationTargetState.Stable;
            IsMeasuring = false;
            Result = CreateResultFromMeasurementSamples(CalibrationTargetState.Stable, null);
        }

        private CalibrationMeasurementResult CreateResultFromMeasurementSamples(
            CalibrationTargetState state,
            string? problem)
        {
            CalibrationRawSample[] samples = _measurementSamples.ToArray();
            StabilityMetrics metrics = CalculateMetrics(samples);
            return new CalibrationMeasurementResult
            {
                SerialNumber = Mapping.SerialNumber,
                PeakLoggerDeviceSerialNumber = Mapping.SourceDeviceSerialNumber,
                Channel = Mapping.Channel,
                PeakId = Mapping.PeakId,
                PeakIndex = Mapping.PeakIndex,
                Status = state,
                SampleCount = samples.Length,
                MeanWavelengthNm = metrics.Mean,
                MedianWavelengthNm = metrics.Median,
                MinWavelengthNm = metrics.Minimum,
                MaxWavelengthNm = metrics.Maximum,
                RangePm = metrics.Range,
                StandardDeviationPm = metrics.StandardDeviation,
                DriftPmPerMinute = metrics.SlopePerMinute,
                MeanReferenceTemperatureC = AverageNullable(samples.Select(s => s.ReferenceTemperatureC)),
                MeanChamberTemperatureC = samples.Length == 0 ? null : samples.Average(s => s.ActualTemperatureC),
                StabilizationTime = ActiveElapsed,
                Problem = problem,
                StableSamples = samples.ToList(),
            };
        }

        private CalibrationMeasurementResult CreateResultFromCurrentWindow(
            CalibrationTargetState state,
            string? problem)
        {
            if (_measurementSamples.Count > 0) return CreateResultFromMeasurementSamples(state, problem);
            StabilityMetrics metrics = LastMetrics ?? _stabilityDetector.Evaluate();
            return new CalibrationMeasurementResult
            {
                SerialNumber = Mapping.SerialNumber,
                PeakLoggerDeviceSerialNumber = Mapping.SourceDeviceSerialNumber,
                Channel = Mapping.Channel,
                PeakId = Mapping.PeakId,
                PeakIndex = Mapping.PeakIndex,
                Status = state,
                SampleCount = 0,
                MeanWavelengthNm = metrics.Mean,
                MedianWavelengthNm = metrics.Median,
                MinWavelengthNm = metrics.Minimum,
                MaxWavelengthNm = metrics.Maximum,
                RangePm = metrics.Range,
                StandardDeviationPm = metrics.StandardDeviation,
                DriftPmPerMinute = metrics.SlopePerMinute,
                StabilizationTime = ActiveElapsed,
                Problem = problem,
            };
        }

        private static double? AverageNullable(IEnumerable<double?> values)
        {
            double[] valid = values.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            return valid.Length == 0 ? null : valid.Average();
        }

        private static StabilityMetrics CalculateMetrics(IReadOnlyList<CalibrationRawSample> samples)
        {
            if (samples.Count == 0)
                return new StabilityMetrics(0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, false);
            var detector = new RollingStabilityDetector(Math.Max(2, samples.Count), 0, 0, 0);
            StabilityMetrics metrics = new(0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, false);
            foreach (CalibrationRawSample sample in samples) metrics = detector.Add(sample.Timestamp, sample.WavelengthNm);
            return metrics;
        }
    }
}

internal static class CalibrationEnumerableExtensions
{
    public static double? AverageOrNull(this IEnumerable<double> values)
    {
        double[] data = values.ToArray();
        return data.Length == 0 ? null : data.Average();
    }
}
