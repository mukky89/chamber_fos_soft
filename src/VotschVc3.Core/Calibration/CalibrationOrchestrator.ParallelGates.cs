using System.Diagnostics;

namespace VotschVc3.Core.Calibration;

/// <summary>
/// Production FBG calibration orchestrator.
///
/// The plateau is deliberately phased:
/// 1) wait until the authoritative temperature source (WIKA when configured, otherwise the chamber
///    probe) is stable and the profile's minimum hold time has elapsed;
/// 2) only then start wavelength-stability evaluation for every selected FBG in parallel;
/// 3) as soon as one FBG becomes stable, that FBG alone enters a fresh measurement-sampling window;
/// 4) other FBGs keep stabilizing independently;
/// 5) the plateau completes when every selected FBG has finished its measurement samples (or an
///    explicit failure policy has produced a terminal result).
///
/// Stabilization samples are never reused as final calibration samples. If temperature stability is
/// lost, unfinished FBGs are reset. If an FBG loses wavelength stability while its measurement window
/// is running, that FBG's measurement samples are discarded and it returns to stabilization.
/// </summary>
public sealed class CalibrationOrchestrator
{
    private readonly IPeakLoggerClient _peakLogger;
    private readonly TimeProvider _timeProvider;
    private int _temperatureGateOverrideRequested;

    public CalibrationOrchestrator(IPeakLoggerClient peakLogger, TimeProvider? timeProvider = null)
    {
        _peakLogger = peakLogger ?? throw new ArgumentNullException(nameof(peakLogger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event Action<CalibrationWarning>? WarningRaised;

    /// <summary>Requests an audited one-time bypass of the stability gate for the current plateau.</summary>
    public void RequestTemperatureGateOverride() =>
        Interlocked.Exchange(ref _temperatureGateOverrideRequested, 1);

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

    /// <summary>Compatibility overload for callers without a profile minimum hold.</summary>
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
            TimeSpan.Zero,
            readChamberTemperatureAsync,
            readReferenceTemperatureAsync,
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
        CalibrationRunWriter writer,
        Action<CalibrationProgressSnapshot>? progress = null,
        CancellationToken cancellationToken = default,
        bool deferOnTemperatureTimeout = false)
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
        var referenceDetector = new TemperatureStabilityDetector(
            settings.ChamberStableDuration,
            settings.ChamberToleranceC,
            settings.MaxChamberDriftCPerMinute,
            settings.MaxChamberRangeC,
            settings.MaxChamberStdDevC);
        var chamberDetector = new TemperatureStabilityDetector(
            settings.ChamberStableDuration,
            settings.ChamberToleranceC,
            settings.MaxChamberDriftCPerMinute,
            settings.MaxChamberRangeC,
            settings.MaxChamberStdDevC);
        var trackers = selected.ToDictionary(
            m => m.Identity,
            m => new TargetTracker(m, settings, _timeProvider),
            StringComparer.OrdinalIgnoreCase);
        StabilityConfiguration activeStabilityConfiguration = StabilityConfiguration.From(settings);

        double actualTemperature = double.NaN;
        double? referenceTemperature = null;
        StabilityMetrics? temperatureMetrics = null;
        bool temperatureGateOpen = false;
        bool temperatureGateForced = false;
        bool temperatureGateEverOpened = false;
        DateTimeOffset? temperatureRecoveryStartedAt = null;
        TimeSpan automaticTemperatureExtensionUsed = TimeSpan.Zero;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset loopAt = DateTimeOffset.UtcNow;

            StabilityConfiguration requestedStabilityConfiguration = StabilityConfiguration.From(settings);
            if (requestedStabilityConfiguration != activeStabilityConfiguration)
            {
                string change = activeStabilityConfiguration.DescribeChanges(requestedStabilityConfiguration);
                activeStabilityConfiguration = requestedStabilityConfiguration;
                referenceDetector = NewTemperatureDetector(settings);
                chamberDetector = NewTemperatureDetector(settings);
                foreach (TargetTracker tracker in trackers.Values.Where(t => !t.IsTerminal))
                    tracker.ResetForRuntimeSettingsChange();
                temperatureGateOpen = false;
                temperatureGateForced = false;
                temperatureRecoveryStartedAt = loopAt;

                CalibrationWarning warning = RaiseWarning(run, new CalibrationWarning
                {
                    Code = "STABILITY_SETTINGS_CHANGED",
                    PlateauIndex = plateauIndex,
                    Message = $"Operátor počas kalibrácie zmenil nastavenia stability: {change}. " +
                              "Rozpracované stabilizačné a finálne meracie okná boli vynulované; nové limity platia okamžite.",
                });
                writer.WriteDiagnostic("WARNING", warning.Code, warning.Message);
            }

            // Wall time continues through reference loss, missing peaks and window resets.
            foreach (TargetTracker tracker in trackers.Values.Where(t => !t.IsTerminal))
            {
                tracker.UpdateDeadline();
                if (!tracker.HasStarted || tracker.ActiveElapsed < tracker.EffectiveTimeout) continue;
                if (tracker.TryExtendDeadline(out string reason))
                {
                    CalibrationWarning extension = RaiseWarning(run, new CalibrationWarning
                    {
                        Code = "SENSOR_STABILITY_TIMEOUT_EXTENDED",
                        PlateauIndex = plateauIndex,
                        SerialNumber = tracker.Mapping.SerialNumber,
                        PeakId = tracker.Mapping.PeakId,
                        Message = $"FBG SN {tracker.Mapping.SerialNumber}, peak {tracker.Mapping.PeakId}: {reason} " +
                                  $"Limit sa predĺžil o najviac 10 min na {FormatTime(tracker.EffectiveTimeout)}; pevný strop 90 min od začiatku FBG fázy.",
                    });
                    writer.WriteDiagnostic("WARNING", extension.Code, extension.Message);
                    continue;
                }
                CalibrationWarning warning = RaiseWarning(run, new CalibrationWarning
                {
                    Code = "SENSOR_STABILITY_TIMEOUT",
                    PlateauIndex = plateauIndex,
                    SerialNumber = tracker.Mapping.SerialNumber,
                    PeakId = tracker.Mapping.PeakId,
                    Message = $"FBG SN {tracker.Mapping.SerialNumber}, peak {tracker.Mapping.PeakId}: stabilita nepotvrdená / meranie nedokončené. " +
                              $"Uplynulo {FormatTime(tracker.ActiveElapsed)}, limit {FormatTime(tracker.EffectiveTimeout)}, pevný strop 90 min. " +
                              $"{tracker.DeadlineProgress}. " +
                              (tracker.ActiveElapsed >= SensorTimeoutBudget.HardLimit ? "Dosiahnutý pevný strop. " : "Bez dostatočného aktuálneho pokroku na predĺženie. ") +
                              (settings.SensorTimeoutPolicy == CalibrationFailurePolicy.ContinueAndFlag
                                  ? "Senzor je označený ako neúspešný; pokračujeme bez náhradného merania."
                                  : "Automatický postup sa zastaví podľa nastavenej politiky; skontrolujte dáta a nastavenia."),
                });
                writer.WriteDiagnostic("WARNING", warning.Code, warning.Message);
                tracker.Fail(CalibrationTargetState.TimedOut, warning.Message);
                ApplyFailurePolicy(settings.SensorTimeoutPolicy, warning);
            }
            if (trackers.Values.All(t => t.IsTerminal))
            {
                progress?.Invoke(new CalibrationProgressSnapshot(CalibrationRunState.StabilizingSensors,
                    plateauIndex, plateauCount, targetTemperatureC, actualTemperature, referenceTemperature,
                    trackers.Values.Count(t => t.IsCompletedStable), selected.Count, plateauClock.Elapsed,
                    trackers.Values.Select(t => t.ToProgress(settings)).ToArray(),
                    "FBG fáza ukončená · neúspešné peaky sú označené · ukladám plato."));
                break;
            }
            actualTemperature = await readChamberTemperatureAsync(cancellationToken).ConfigureAwait(false);
            referenceTemperature = hasExternalReference
                ? await readReferenceTemperatureAsync!(cancellationToken).ConfigureAwait(false)
                : null;

            if (_peakLogger is IPeakLoggerSimulationControl simulation)
                simulation.SimulatedTemperatureC = referenceTemperature ?? actualTemperature;

            // If WIKA is configured, a missing WIKA reading is NOT silently replaced by the chamber
            // probe. The chamber probe is used only when no external reference is configured.
            temperatureMetrics = hasExternalReference
                ? (referenceTemperature is { } reference
                    ? referenceDetector.Add(loopAt, reference, targetTemperatureC)
                    : null)
                : chamberDetector.Add(loopAt, actualTemperature, targetTemperatureC);

            bool temperatureGateOverrideRequested = Interlocked.Exchange(ref _temperatureGateOverrideRequested, 0) == 1;
            if (!temperatureGateForced && temperatureGateOverrideRequested)
            {
                bool hasAuthoritativeTemperature = hasExternalReference
                    ? referenceTemperature is not null
                    : double.IsFinite(actualTemperature);
                if (hasAuthoritativeTemperature)
                {
                    temperatureGateForced = true;
                    RaiseWarning(run, new CalibrationWarning
                    {
                        Code = "TEMPERATURE_STABILITY_FORCED",
                        PlateauIndex = plateauIndex,
                        Message = $"Operátor vynútil pokračovanie z teplotnej stability na plate {plateauIndex + 1} pri cieli {targetTemperatureC:F2} °C " +
                                  $"(WIKA {(referenceTemperature is { } forcedReference ? $"{forcedReference:F3} °C" : "bez referencie")}, komora {actualTemperature:F2} °C).",
                    });
                }
            }

            bool minimumElapsed = plateauClock.Elapsed >= minimumPlateauDuration;
            bool temperatureStable = temperatureMetrics?.IsStable == true || temperatureGateForced;
            bool shouldOpenTemperatureGate = minimumElapsed && temperatureStable;

            if (!shouldOpenTemperatureGate)
            {
                run.State = CalibrationRunState.WaitingForChamberStability;
                if (temperatureGateOpen)
                {
                    // Temperature was stable but left the accepted window. Do not keep unfinished
                    // sensor qualification/measurement data across an unstable temperature period.
                    foreach (TargetTracker tracker in trackers.Values.Where(t => !t.IsTerminal))
                        tracker.ResetForTemperatureLoss();
                    temperatureRecoveryStartedAt = loopAt;
                }
                temperatureGateOpen = false;

                string temperatureDetail = BuildTemperatureDetail(
                    referenceTemperature,
                    actualTemperature,
                    targetTemperatureC,
                    temperatureMetrics,
                    settings,
                    hasExternalReference);
                string minimumDetail = minimumPlateauDuration <= TimeSpan.Zero
                    ? "minimum hold: bez minima"
                    : $"minimum hold {FormatTime(plateauClock.Elapsed < minimumPlateauDuration ? plateauClock.Elapsed : minimumPlateauDuration)}/{FormatTime(minimumPlateauDuration)} {(minimumElapsed ? "✓" : "…")}";
                string extensionDetail = automaticTemperatureExtensionUsed > TimeSpan.Zero
                    ? $" · automatické predĺženie {FormatTime(automaticTemperatureExtensionUsed)}/{FormatTime(settings.MaxAutomaticChamberStabilityExtension)}"
                    : string.Empty;
                TimeSpan temperatureSettlingElapsed = !temperatureGateEverOpened
                    ? plateauClock.Elapsed - minimumPlateauDuration
                    : temperatureRecoveryStartedAt is { } recoveryStartedAt
                        ? loopAt - recoveryStartedAt
                        : TimeSpan.Zero;
                if (temperatureSettlingElapsed < TimeSpan.Zero)
                    temperatureSettlingElapsed = TimeSpan.Zero;

                progress?.Invoke(new CalibrationProgressSnapshot(
                    CalibrationRunState.WaitingForChamberStability,
                    plateauIndex,
                    plateauCount,
                    targetTemperatureC,
                    actualTemperature,
                    referenceTemperature,
                    trackers.Values.Count(t => t.IsCompletedStable),
                    selected.Count,
                    plateauClock.Elapsed,
                    trackers.Values.Select(t => t.ToWaitingForTemperatureProgress(settings, temperatureDetail, minimumDetail)).ToArray(),
                    $"KROK 2/5 · Stabilizácia teploty · {minimumDetail} · {temperatureDetail}{extensionDetail}\nĎALŠÍ KROK: po stabilnej teplote začne paralelná stabilizácia všetkých FBG peakov.",
                    (hasExternalReference ? referenceDetector : chamberDetector).DisplayedStableScoreSeconds,
                    (hasExternalReference ? referenceDetector : chamberDetector).RequiredStableScoreSeconds,
                    false,
                    temperatureMetrics?.SlopePerMinute,
                    temperatureSettlingElapsed,
                    settings.ChamberStabilityTimeout,
                    automaticTemperatureExtensionUsed,
                    settings.MaxAutomaticChamberStabilityExtension));

                if (!temperatureGateEverOpened)
                {
                    if (settings.ChamberStabilityTimeout > TimeSpan.Zero &&
                        plateauClock.Elapsed >= minimumPlateauDuration + settings.ChamberStabilityTimeout + automaticTemperatureExtensionUsed)
                    {
                        if (TryExtendTemperatureTimeout(run, plateauIndex, targetTemperatureC, referenceTemperature, actualTemperature,
                            settings, hasExternalReference, ref automaticTemperatureExtensionUsed))
                            continue;
                        throw BuildTemperatureTimeout(run, plateauIndex, targetTemperatureC, referenceTemperature, actualTemperature,
                            settings.ChamberStabilityTimeout + automaticTemperatureExtensionUsed, hasExternalReference,
                            (hasExternalReference ? referenceDetector : chamberDetector).DisplayedStableScoreSeconds,
                            (hasExternalReference ? referenceDetector : chamberDetector).RequiredStableScoreSeconds,
                            deferOnTemperatureTimeout);
                    }
                }
                else if (temperatureRecoveryStartedAt is { } recoveryStart &&
                         settings.ChamberStabilityTimeout > TimeSpan.Zero &&
                         loopAt - recoveryStart >= settings.ChamberStabilityTimeout + automaticTemperatureExtensionUsed)
                {
                    if (TryExtendTemperatureTimeout(run, plateauIndex, targetTemperatureC, referenceTemperature, actualTemperature,
                        settings, hasExternalReference, ref automaticTemperatureExtensionUsed))
                        continue;
                    throw BuildTemperatureTimeout(run, plateauIndex, targetTemperatureC, referenceTemperature, actualTemperature,
                        settings.ChamberStabilityTimeout + automaticTemperatureExtensionUsed, hasExternalReference,
                        (hasExternalReference ? referenceDetector : chamberDetector).DisplayedStableScoreSeconds,
                        (hasExternalReference ? referenceDetector : chamberDetector).RequiredStableScoreSeconds,
                        deferOnTemperatureTimeout);
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!temperatureGateOpen)
            {
                temperatureGateOpen = true;
                temperatureGateEverOpened = true;
                temperatureRecoveryStartedAt = null;
                foreach (TargetTracker tracker in trackers.Values.Where(t => !t.IsTerminal))
                    tracker.BeginSensorPhase();
            }

            run.State = CalibrationRunState.StabilizingSensors;
            IReadOnlyList<PeakLoggerMeasurement> batch;
            try
            {
                batch = await _peakLogger.ReadMeasurementsAsync(cancellationToken).ConfigureAwait(false);
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
                ApplyFailurePolicy(settings.PeakLoggerDisconnectPolicy, warning);
                if (settings.PeakLoggerDisconnectPolicy == CalibrationFailurePolicy.ContinueAndFlag)
                {
                    foreach (TargetTracker tracker in trackers.Values.Where(t => !t.IsTerminal))
                        tracker.Fail(CalibrationTargetState.Disconnected, warning.Message);
                    break;
                }
                throw;
            }

            if (trackers.Values.Any(t => !t.IsTerminal && t.HasStarted && t.ActiveElapsed >= t.EffectiveTimeout)) continue;

            var rawToWrite = new List<CalibrationRawSample>();
            foreach (TargetTracker tracker in trackers.Values.Where(t => !t.IsTerminal))
            {
                PeakLoggerMeasurement? measurement = FindMeasurement(batch, tracker.Mapping);
                if (measurement is null)
                {
                    tracker.MarkMissing(loopAt);
                    if (tracker.MissingFor >= settings.PeakLostGracePeriod)
                    {
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
                    tracker.Fail(CalibrationTargetState.Disconnected, "PeakLogger poskytol zastarané dáta pre vybraný peak.");
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

                string? resetMessage = tracker.ProcessStableTemperatureSample(raw, settings);
                if (resetMessage is not null)
                    writer.WriteDiagnostic("WARNING", "FBG_MEASUREMENT_RESET", resetMessage);
            }

            if (rawToWrite.Count > 0)
                await writer.AppendAsync(rawToWrite, cancellationToken).ConfigureAwait(false);

            int completed = trackers.Values.Count(t => t.IsCompletedStable);
            int measuring = trackers.Values.Count(t => t.IsMeasuring && !t.IsTerminal);
            int stabilizing = trackers.Values.Count(t => !t.IsMeasuring && !t.IsTerminal);
            bool allTerminal = trackers.Values.All(t => t.IsTerminal);

            string temperatureDetailNow = BuildTemperatureDetail(
                referenceTemperature,
                actualTemperature,
                targetTemperatureC,
                temperatureMetrics,
                settings,
                hasExternalReference);
            string phaseMessage = allTerminal
                ? "KROK 5/5 · Všetky FBG sú dokončené · ukladám plato."
                : $"KROK 3–4/5 · FBG paralelne: stabilizuje sa {stabilizing}, meria sa {measuring}, hotovo {completed}/{selected.Count}.";

            progress?.Invoke(new CalibrationProgressSnapshot(
                CalibrationRunState.StabilizingSensors,
                plateauIndex,
                plateauCount,
                targetTemperatureC,
                actualTemperature,
                referenceTemperature,
                completed,
                selected.Count,
                plateauClock.Elapsed,
                trackers.Values.Select(t => t.ToProgress(settings)).ToArray(),
                $"{phaseMessage}\nTEPLOTA: stabilná ✓ · {temperatureDetailNow}\nĎALŠÍ KROK: každý stabilný peak samostatne zbiera {Math.Max(2, settings.RequiredMeasurementSamples)} meracích samples; plato skončí až keď sú hotové všetky vybrané peaky.",
                (hasExternalReference ? referenceDetector : chamberDetector).DisplayedStableScoreSeconds,
                (hasExternalReference ? referenceDetector : chamberDetector).RequiredStableScoreSeconds,
                true,
                temperatureMetrics?.SlopePerMinute));

            if (allTerminal) break;
            await Task.Delay(
                TimeSpan.FromSeconds(Math.Clamp(settings.SampleAcquisitionIntervalSeconds, 1, 30)),
                cancellationToken).ConfigureAwait(false);
        }

        var result = new CalibrationPlateauResult
        {
            PlateauIndex = plateauIndex,
            TargetTemperatureC = targetTemperatureC,
            ActualTemperatureC = actualTemperature,
            ReferenceTemperatureC = referenceTemperature,
            StartedAt = plateauStarted,
            CompletedAt = DateTimeOffset.Now,
            Targets = trackers.Values.Select(t => t.Result ?? t.CreateFallbackResult()).ToList(),
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
        if (Math.Abs(deltaT) < settings.ValidationMinimumDeltaTemperatureC)
            return false;

        run.State = CalibrationRunState.TemperatureResponseValidation;
        bool allValid = true;
        foreach (CalibrationMeasurementResult currentTarget in current.Targets)
        {
            CalibrationMeasurementResult? baseTarget = baseline.Targets.FirstOrDefault(x =>
                string.Equals(x.Identity, currentTarget.Identity, StringComparison.OrdinalIgnoreCase));
            if (baseTarget is null || currentTarget.Status != CalibrationTargetState.Stable || baseTarget.Status != CalibrationTargetState.Stable)
                continue;

            double deltaPm = (currentTarget.MeanWavelengthNm - baseTarget.MeanWavelengthNm) * 1000d;
            bool magnitudeOk = Math.Abs(deltaPm) >= settings.ValidationMinimumWavelengthResponsePm;
            bool directionOk = settings.ExpectedResponseDirection switch
            {
                ExpectedResponseDirection.Positive => deltaPm / deltaT > 0,
                ExpectedResponseDirection.Negative => deltaPm / deltaT < 0,
                _ => true,
            };
            if (magnitudeOk && directionOk) continue;

            allValid = false;
            CalibrationWarning warning = RaiseWarning(run, new CalibrationWarning
            {
                Code = "NO_TEMPERATURE_RESPONSE",
                Message = $"FBG SN {currentTarget.SerialNumber}, peak {currentTarget.PeakId}: Δλ={deltaPm:F2} pm pri ΔT={deltaT:F2} °C – vybraná wavelength nereaguje podľa nastavených limitov.",
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

    private Exception BuildTemperatureTimeout(
        CalibrationRunRecord run,
        int plateauIndex,
        double targetTemperatureC,
        double? referenceTemperature,
        double chamberTemperature,
        TimeSpan timeout,
        bool hasExternalReference,
        int stableScoreSeconds,
        int requiredStableScoreSeconds,
        bool deferPlateau)
    {
        string source = hasExternalReference ? "WIKA CTH7000" : "interná sonda komory";
        string measured = hasExternalReference
            ? (referenceTemperature is { } wika ? $"{wika:F3} °C" : "bez platnej hodnoty")
            : $"{chamberTemperature:F3} °C";
        CalibrationWarning warning = RaiseWarning(run, new CalibrationWarning
        {
            Code = deferPlateau ? "REFERENCE_STABILITY_DEFERRED" : "REFERENCE_STABILITY_TIMEOUT",
            Message = $"{source} sa neustálila na {targetTemperatureC:F1} °C ani po maximálnom čase {FormatTime(timeout)}. " +
                      $"Posledná hodnota: {measured}; stabilné skóre {stableScoreSeconds}/{requiredStableScoreSeconds} s. " +
                      (deferPlateau
                          ? "Kalibračný bod sa zatiaľ neprijal; odloží sa, beh pokračuje ďalším platom a po prejdení ostatných bodov sa k nemu automaticky raz vráti."
                          : "Automatický postup bol bezpečne zastavený a kalibračný bod nebol prijatý. Skontrolujte pripojenie a polohu WIKA sondy, rozloženie alebo tepelnú kapacitu náplne komory a nastavené limity. Potom obnovte kontrolu; zdôvodnené vynútenie ďalšieho kroku použite iba po odbornom posúdení."),
            PlateauIndex = plateauIndex,
        });
        if (deferPlateau)
            return new CalibrationPlateauDeferredException(warning.Message, warning);
        return new CalibrationOperatorActionRequiredException(warning.Message, warning);
    }

    private bool TryExtendTemperatureTimeout(
        CalibrationRunRecord run,
        int plateauIndex,
        double targetTemperatureC,
        double? referenceTemperature,
        double chamberTemperature,
        CalibrationProfileSettings settings,
        bool hasExternalReference,
        ref TimeSpan extensionUsed)
    {
        TimeSpan step = settings.ChamberStabilityExtensionStep < TimeSpan.Zero
            ? TimeSpan.Zero
            : settings.ChamberStabilityExtensionStep;
        TimeSpan maximum = settings.MaxAutomaticChamberStabilityExtension < TimeSpan.Zero
            ? TimeSpan.Zero
            : settings.MaxAutomaticChamberStabilityExtension;
        bool hasValidTemperature = hasExternalReference
            ? referenceTemperature is { } value && double.IsFinite(value)
            : double.IsFinite(chamberTemperature);
        TimeSpan remaining = maximum - extensionUsed;
        if (!hasValidTemperature || step <= TimeSpan.Zero || remaining <= TimeSpan.Zero) return false;

        TimeSpan granted = step <= remaining ? step : remaining;
        extensionUsed += granted;
        string source = hasExternalReference ? "WIKA CTH7000" : "interná sonda komory";
        string measured = hasExternalReference
            ? $"{referenceTemperature!.Value:F3} °C"
            : $"{chamberTemperature:F3} °C";
        RaiseWarning(run, new CalibrationWarning
        {
            Code = "REFERENCE_STABILITY_TIMEOUT_EXTENDED",
            PlateauIndex = plateauIndex,
            Message = $"{source} pri cieli {targetTemperatureC:F1} °C ešte nie je stabilná (aktuálne {measured}). " +
                      $"Čakanie sa automaticky predĺžilo o {FormatTime(granted)}; spolu je využitých {FormatTime(extensionUsed)} " +
                      $"z maximálneho automatického predĺženia {FormatTime(maximum)}. Kalibrácia ďalej bezpečne čaká a zbiera stabilné skóre.",
        });
        return true;
    }

    private static PeakLoggerMeasurement? FindMeasurement(
        IReadOnlyList<PeakLoggerMeasurement> batch,
        CalibrationSensorMapping mapping) => batch
        .Where(m => string.Equals(m.SerialNumber, mapping.SourceDeviceSerialNumber, StringComparison.OrdinalIgnoreCase))
        .Where(m => string.Equals(m.PeakId, mapping.PeakId, StringComparison.Ordinal))
        .Where(m => string.IsNullOrWhiteSpace(mapping.Channel) || string.Equals(m.Channel, mapping.Channel, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(m => m.Timestamp)
        .FirstOrDefault();

    private static string BuildTemperatureDetail(
        double? referenceTemperature,
        double chamberTemperature,
        double targetTemperature,
        StabilityMetrics? metrics,
        CalibrationProfileSettings settings,
        bool hasExternalReference)
    {
        string source = hasExternalReference ? "WIKA" : "interná sonda";
        if (hasExternalReference && referenceTemperature is null)
            return "WIKA: bez platnej vzorky ×";
        if (metrics is null)
            return $"{source}: čakám na prvé stability dáta…";

        double measured = referenceTemperature ?? chamberTemperature;
        double error = measured - targetTemperature;
        bool toleranceOk = Math.Abs(error) <= settings.ChamberToleranceC;
        bool durationOk = metrics.WindowDuration >= settings.ChamberStableDuration;
        bool driftOk = settings.MaxChamberDriftCPerMinute <= 0 || Math.Abs(metrics.SlopePerMinute) <= settings.MaxChamberDriftCPerMinute;
        bool rangeOk = settings.MaxChamberRangeC <= 0 || metrics.Range <= settings.MaxChamberRangeC;
        bool stdDevOk = settings.MaxChamberStdDevC <= 0 || metrics.StandardDeviation <= settings.MaxChamberStdDevC;
        return $"{source} {measured:F3} °C · Δ {error:+0.000;-0.000;0.000} / ±{settings.ChamberToleranceC:F3} {(toleranceOk ? "✓" : "×")} · " +
               $"stable čas {FormatTime(metrics.WindowDuration)}/{FormatTime(settings.ChamberStableDuration)} {(durationOk ? "✓" : "…")} · " +
               $"rozsah {metrics.Range:F3}/{settings.MaxChamberRangeC:F3} °C {(rangeOk ? "✓" : "×")} · " +
               $"σ {metrics.StandardDeviation:F3}/{settings.MaxChamberStdDevC:F3} °C {(stdDevOk ? "✓" : "×")} · " +
               $"drift {Math.Abs(metrics.SlopePerMinute):F3}/{settings.MaxChamberDriftCPerMinute:F3} °C/min {(driftOk ? "✓" : "×")}";
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

    private static string FormatTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");
    }

    private static TemperatureStabilityDetector NewTemperatureDetector(CalibrationProfileSettings settings) => new(
        settings.ChamberStableDuration,
        settings.ChamberToleranceC,
        settings.MaxChamberDriftCPerMinute,
        settings.MaxChamberRangeC,
        settings.MaxChamberStdDevC);

    private readonly record struct StabilityConfiguration(
        double ChamberToleranceC,
        TimeSpan ChamberStableDuration,
        double MaxChamberDriftCPerMinute,
        double MaxChamberRangeC,
        double MaxChamberStdDevC,
        int RequiredStableSamples,
        double MaxWavelengthRangePm,
        double MaxWavelengthStdDevPm,
        double MaxWavelengthDriftPmPerMinute)
    {
        public static StabilityConfiguration From(CalibrationProfileSettings settings) => new(
            settings.ChamberToleranceC,
            settings.ChamberStableDuration,
            settings.MaxChamberDriftCPerMinute,
            settings.MaxChamberRangeC,
            settings.MaxChamberStdDevC,
            settings.RequiredStableSamples,
            settings.MaxWavelengthRangePm,
            settings.MaxWavelengthStdDevPm,
            settings.MaxWavelengthDriftPmPerMinute);

        public string DescribeChanges(StabilityConfiguration next)
        {
            var changes = new List<string>();
            Add(changes, "teplotná tolerancia", ChamberToleranceC, next.ChamberToleranceC, "°C");
            if (ChamberStableDuration != next.ChamberStableDuration)
                changes.Add($"stabilný čas {ChamberStableDuration.TotalMinutes:0.###} → {next.ChamberStableDuration.TotalMinutes:0.###} min");
            Add(changes, "drift WIKA", MaxChamberDriftCPerMinute, next.MaxChamberDriftCPerMinute, "°C/min");
            Add(changes, "rozsah WIKA", MaxChamberRangeC, next.MaxChamberRangeC, "°C");
            Add(changes, "StdDev WIKA", MaxChamberStdDevC, next.MaxChamberStdDevC, "°C");
            if (RequiredStableSamples != next.RequiredStableSamples)
                changes.Add($"FBG vzorky stability {RequiredStableSamples} → {next.RequiredStableSamples}");
            Add(changes, "FBG range", MaxWavelengthRangePm, next.MaxWavelengthRangePm, "pm");
            Add(changes, "FBG StdDev", MaxWavelengthStdDevPm, next.MaxWavelengthStdDevPm, "pm");
            Add(changes, "FBG drift", MaxWavelengthDriftPmPerMinute, next.MaxWavelengthDriftPmPerMinute, "pm/min");
            return string.Join(", ", changes);
        }

        private static void Add(List<string> changes, string name, double before, double after, string unit)
        {
            if (before != after) changes.Add($"{name} {before:0.###} → {after:0.###} {unit}");
        }
    }

    private sealed class TargetTracker
    {
        private readonly CalibrationProfileSettings _settings;
        private readonly int _averagingSamples;
        private readonly Queue<PeakLoggerMeasurement> _averagingWindow = new();
        private readonly List<CalibrationRawSample> _measurementSamples = new();
        private readonly Queue<double> _observedCadenceSeconds = new();
        private DateTimeOffset? _missingSince;
        private DateTimeOffset? _previousMeasurementAt;
        private RollingStabilityDetector _stabilityDetector;
        private bool _sensorPhaseStarted;
        private readonly TimeProvider _clock;
        private long? _startedAt;
        private TimeSpan _deadline;
        private TimeSpan? _finishedElapsed;
        private TimeSpan _lastProgressAt;
        private double? _previousWindowScore;
        private bool _improving;
        private string? _lastResetMessage;

        public TargetTracker(CalibrationSensorMapping mapping, CalibrationProfileSettings settings, TimeProvider clock)
        {
            _clock = clock;
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
        public TimeSpan EffectiveTimeout => _deadline;
        public TimeSpan ActiveElapsed => _finishedElapsed ?? (_startedAt is { } start ? _clock.GetElapsedTime(start) : TimeSpan.Zero);
        public bool HasStarted => _startedAt.HasValue;
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
            _startedAt ??= _clock.GetTimestamp();
            UpdateDeadline();
            if (State == CalibrationTargetState.WaitingForTemperature)
                State = CalibrationTargetState.Stabilizing;
        }

        public void UpdateDeadline()
        {
            // Re-evaluate cadence while inside the base budget, never move the hard deadline.
            if (_deadline > TimeSpan.Zero && ActiveElapsed >= _deadline) return;
            TimeSpan basis = SensorTimeoutBudget.BaseLimit(_settings.RequiredStableSamples,
                _settings.RequiredMeasurementSamples, ObservedCadence(), Timeout);
            if (basis > _deadline) _deadline = basis;
        }

        public string DeadlineProgress =>
            $"Stabilizácia {LastMetrics?.Count ?? 0}/{Math.Max(2, _settings.RequiredStableSamples)}, meranie {_measurementSamples.Count}/{Math.Max(2, _settings.RequiredMeasurementSamples)}";

        public bool TryExtendDeadline(out string reason)
        {
            bool fresh = _sensorPhaseStarted && LastMeasurement is not null &&
                ActiveElapsed - _lastProgressAt <= SensorTimeoutBudget.ExtensionStep;
            bool progressing = fresh && ((IsMeasuring && _measurementSamples.Count > 0) || _improving);
            TimeSpan next = SensorTimeoutBudget.Extend(_deadline, ActiveElapsed, progressing);
            reason = IsMeasuring ? "Pribúdajú platné finálne vzorky." : "Odchýlky stability sa zlepšili aspoň o 10 % medzi úplnými oknami.";
            _improving = false;
            if (next <= _deadline || next <= ActiveElapsed) return false;
            _deadline = next;
            return true;
        }

        private void ObserveFullWindow(StabilityMetrics metrics, CalibrationProfileSettings settings)
        {
            static double Ratio(double value, double limit) => limit > 0 ? Math.Abs(value) / limit : 0;
            double score = Math.Max(Ratio(metrics.Range, settings.MaxWavelengthRangePm),
                Math.Max(Ratio(metrics.StandardDeviation, settings.MaxWavelengthStdDevPm),
                    Ratio(metrics.SlopePerMinute, settings.MaxWavelengthDriftPmPerMinute)));
            _improving = _previousWindowScore is { } previous && score <= previous * 0.9;
            if (_improving) _lastProgressAt = ActiveElapsed;
            _previousWindowScore = score;
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
            if (_previousMeasurementAt is { } previous)
            {
                double seconds = (measurement.Timestamp - previous).TotalSeconds;
                if (seconds is >= 0.1 and <= 300.0)
                {
                    _observedCadenceSeconds.Enqueue(seconds);
                    while (_observedCadenceSeconds.Count > 30)
                        _observedCadenceSeconds.Dequeue();
                }
            }
            _previousMeasurementAt = measurement.Timestamp;
            LastMeasurement = measurement;
            Mapping.CurrentWavelengthNm = measurement.WavelengthNm;
            _missingSince = null;
        }

        public void MarkMissing(DateTimeOffset now)
        {
            _missingSince ??= now;
            if (!IsTerminal) State = CalibrationTargetState.PeakLost;
        }

        public string? ProcessStableTemperatureSample(CalibrationRawSample raw, CalibrationProfileSettings settings)
        {
            if (IsTerminal) return null;
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
                else if (LastMetrics.Count >= Math.Max(2, settings.RequiredStableSamples))
                {
                    ObserveFullWindow(LastMetrics, settings);
                    string message = $"Stabilizácia FBG SN {Mapping.SerialNumber}, peak {Mapping.PeakId} bola resetovaná pri " +
                                     $"{LastMetrics.Count}/{Math.Max(2, settings.RequiredStableSamples)} vzorkách: " +
                                     $"{FailedCriteria(LastMetrics, settings)}. Začína sa nový čistý stabilizačný pokus od 0.";
                    _lastResetMessage = message;
                    _stabilityDetector.Reset();
                    LastMetrics = null;
                    return message;
                }
                return null;
            }

            // Continue checking the rolling stability window during final sampling. If it leaves the
            // limits, samples collected since the last qualification are invalid and are discarded.
            if (!LastMetrics.IsStable)
            {
                int discarded = _measurementSamples.Count;
                string message = $"Finálne meranie FBG SN {Mapping.SerialNumber}, peak {Mapping.PeakId} bolo zrušené pri " +
                                 $"{discarded}/{Math.Max(2, settings.RequiredMeasurementSamples)} vzorkách: {FailedCriteria(LastMetrics, settings)}.";
                ResetToStabilizing(message);
                return _lastResetMessage;
            }

            State = CalibrationTargetState.Live;
            _measurementSamples.Add(raw);
            _lastProgressAt = ActiveElapsed;
            int requiredMeasurementSamples = Math.Max(2, settings.RequiredMeasurementSamples);
            if (_measurementSamples.Count >= requiredMeasurementSamples)
                CompleteStableFromMeasurementWindow();
            return null;
        }

        public void ResetForTemperatureLoss()
        {
            if (IsTerminal) return;
            _sensorPhaseStarted = false;
            _improving = false;
            _previousWindowScore = null;
            State = CalibrationTargetState.WaitingForTemperature;
            IsMeasuring = false;
            _measurementSamples.Clear();
            _averagingWindow.Clear();
            _stabilityDetector = NewStabilityDetector();
            LastMetrics = null;
            _lastResetMessage = null;
        }

        public void ResetForRuntimeSettingsChange()
        {
            if (IsTerminal) return;
            _sensorPhaseStarted = false;
            _improving = false;
            _previousWindowScore = null;
            State = CalibrationTargetState.WaitingForTemperature;
            IsMeasuring = false;
            _measurementSamples.Clear();
            _averagingWindow.Clear();
            _stabilityDetector = NewStabilityDetector();
            LastMetrics = null;
            _lastResetMessage = "Nastavenia stability boli zmenené operátorom; začína sa nové čisté okno.";
        }

        public void Fail(CalibrationTargetState state, string problem)
        {
            _finishedElapsed ??= ActiveElapsed;
            State = state;
            IsMeasuring = false;
            Result = CreateResultFromCurrentWindow(state, problem);
        }

        public void CompleteTimedOut(
            CalibrationRunRecord run,
            int plateauIndex,
            double targetTemperature,
            double actualTemperature,
            double? referenceTemperature,
            string problem)
        {
            State = CalibrationTargetState.TimedOut;
            IsMeasuring = false;
            Result = CreateResultFromCurrentWindow(State, problem);
        }

        public CalibrationMeasurementResult CreateFallbackResult() =>
            Result ?? CreateResultFromCurrentWindow(State, "Meranie skončilo bez kompletného výsledku.");

        public CalibrationTargetProgress ToWaitingForTemperatureProgress(
            CalibrationProfileSettings settings,
            string temperatureDetail,
            string minimumDetail)
        {
            string phase = IsTerminal
                ? "HOTOVO"
                : $"ČAKÁ NA TEPLOTU · {minimumDetail} · {temperatureDetail}";
            return BuildProgress(
                IsTerminal ? Result!.Status : CalibrationTargetState.WaitingForTemperature,
                IsTerminal ? Math.Max(2, settings.RequiredMeasurementSamples) : 0,
                Math.Max(2, settings.RequiredMeasurementSamples),
                phase);
        }

        public CalibrationTargetProgress ToProgress(CalibrationProfileSettings settings)
        {
            if (IsTerminal)
            {
                string terminal = Result?.Status == CalibrationTargetState.Stable
                    ? $"HOTOVO · meranie {_measurementSamples.Count}/{Math.Max(2, settings.RequiredMeasurementSamples)} samples ✓"
                    : Result?.Status == CalibrationTargetState.CompletedWithStabilityWarning
                        ? $"HOTOVO S UPOZORNENÍM · finálne meranie {_measurementSamples.Count}/{Math.Max(2, settings.RequiredMeasurementSamples)} samples · problém so stabilizáciou"
                    : $"KONIEC · {Result?.Status}: {Result?.Problem}";
                return BuildProgress(Result?.Status ?? State, _measurementSamples.Count, Math.Max(2, settings.RequiredMeasurementSamples), terminal);
            }

            StabilityMetrics metrics = LastMetrics ?? _stabilityDetector.Evaluate();
            bool enough = metrics.Count >= settings.RequiredStableSamples;
            bool rangeOk = settings.MaxWavelengthRangePm <= 0 || metrics.Range <= settings.MaxWavelengthRangePm;
            bool stdOk = settings.MaxWavelengthStdDevPm <= 0 || metrics.StandardDeviation <= settings.MaxWavelengthStdDevPm;
            bool driftOk = settings.MaxWavelengthDriftPmPerMinute <= 0 || Math.Abs(metrics.SlopePerMinute) <= settings.MaxWavelengthDriftPmPerMinute;

            if (IsMeasuring)
            {
                string measuring =
                    $"MERANIE · {_measurementSamples.Count}/{Math.Max(2, settings.RequiredMeasurementSamples)} samples · " +
                    $"stabilita stále OK: range {metrics.Range:F3}/{settings.MaxWavelengthRangePm:F3} pm {(rangeOk ? "✓" : "×")} · " +
                    $"std {metrics.StandardDeviation:F3}/{settings.MaxWavelengthStdDevPm:F3} pm {(stdOk ? "✓" : "×")} · " +
                    $"drift {Math.Abs(metrics.SlopePerMinute):F3}/{settings.MaxWavelengthDriftPmPerMinute:F3} pm/min {(driftOk ? "✓" : "×")}";
                return BuildProgress(CalibrationTargetState.Live, _measurementSamples.Count, Math.Max(2, settings.RequiredMeasurementSamples), measuring);
            }

            string stabilizing =
                $"STABILIZÁCIA · samples {metrics.Count}/{settings.RequiredStableSamples} {(enough ? "✓" : "…")} · " +
                $"range {metrics.Range:F3}/{settings.MaxWavelengthRangePm:F3} pm {(rangeOk ? "✓" : "×")} · " +
                $"std {metrics.StandardDeviation:F3}/{settings.MaxWavelengthStdDevPm:F3} pm {(stdOk ? "✓" : "×")} · " +
                $"drift {Math.Abs(metrics.SlopePerMinute):F3}/{settings.MaxWavelengthDriftPmPerMinute:F3} pm/min {(driftOk ? "✓" : "×")}";
            if (_lastResetMessage is not null)
                stabilizing += $" · {_lastResetMessage}";
            return BuildProgress(CalibrationTargetState.Stabilizing, metrics.Count, settings.RequiredStableSamples, stabilizing);
        }

        private CalibrationTargetProgress BuildProgress(
            CalibrationTargetState state,
            int samples,
            int required,
            string detail)
        {
            StabilityMetrics metrics = LastMetrics ?? _stabilityDetector.Evaluate();
            return new CalibrationTargetProgress(
                Mapping.SerialNumber,
                Mapping.Channel,
                Mapping.PeakId,
                Mapping.PeakIndex,
                LastMeasurement?.WavelengthNm ?? Mapping.CurrentWavelengthNm,
                samples,
                required,
                metrics.Count > 0 ? metrics.StandardDeviation : null,
                metrics.Count > 0 ? metrics.SlopePerMinute : null,
                ActiveElapsed,
                EffectiveTimeout,
                state,
                detail + (HasStarted && !IsTerminal ? $" · zostáva {FormatTime(EffectiveTimeout > ActiveElapsed ? EffectiveTimeout - ActiveElapsed : TimeSpan.Zero)} · do pevného stropu {FormatTime(SensorTimeoutBudget.HardLimit > ActiveElapsed ? SensorTimeoutBudget.HardLimit - ActiveElapsed : TimeSpan.Zero)}" : string.Empty),
                StabilitySamples: metrics.Count,
                RequiredStabilitySamples: Math.Max(2, _settings.RequiredStableSamples),
                MeasurementSamples: _measurementSamples.Count,
                RequiredMeasurementSamples: Math.Max(2, _settings.RequiredMeasurementSamples),
                RangePm: metrics.Range,
                RangeLimitPm: _settings.MaxWavelengthRangePm,
                StdDevLimitPm: _settings.MaxWavelengthStdDevPm,
                DriftLimitPmPerMinute: _settings.MaxWavelengthDriftPmPerMinute,
                Phase: IsTerminal ? "Done" : state == CalibrationTargetState.WaitingForTemperature ? "Temperature" : IsMeasuring ? "Measuring" : "Stabilizing",
                BlockingReason: Result?.Problem ?? (state == CalibrationTargetState.WaitingForTemperature ? detail : string.Empty));
        }

        private void ResetToStabilizing(string message)
        {
            message += " Časový limit ani pevný strop sa resetom neposúva.";
            _improving = false;
            _lastResetMessage = message;
            IsMeasuring = false;
            State = CalibrationTargetState.Stabilizing;
            _measurementSamples.Clear();
            _stabilityDetector = NewStabilityDetector();
            LastMetrics = null;
        }

        private TimeSpan ObservedCadence()
        {
            if (_observedCadenceSeconds.Count == 0)
                return TimeSpan.FromSeconds(Math.Clamp(_settings.SampleAcquisitionIntervalSeconds, 1, 30));
            double[] ordered = _observedCadenceSeconds.OrderBy(value => value).ToArray();
            double median = ordered.Length % 2 == 1
                ? ordered[ordered.Length / 2]
                : (ordered[(ordered.Length / 2) - 1] + ordered[ordered.Length / 2]) / 2.0;
            return TimeSpan.FromSeconds(median);
        }

        private static string FailedCriteria(StabilityMetrics metrics, CalibrationProfileSettings settings)
        {
            var failures = new List<string>();
            if (settings.MaxWavelengthRangePm > 0 && metrics.Range > settings.MaxWavelengthRangePm)
                failures.Add($"range {metrics.Range:F3} pm prekročil {settings.MaxWavelengthRangePm:F3} pm");
            if (settings.MaxWavelengthStdDevPm > 0 && metrics.StandardDeviation > settings.MaxWavelengthStdDevPm)
                failures.Add($"StdDev {metrics.StandardDeviation:F3} pm prekročil {settings.MaxWavelengthStdDevPm:F3} pm");
            if (settings.MaxWavelengthDriftPmPerMinute > 0 && Math.Abs(metrics.SlopePerMinute) > settings.MaxWavelengthDriftPmPerMinute)
                failures.Add($"drift {Math.Abs(metrics.SlopePerMinute):F3} pm/min prekročil {settings.MaxWavelengthDriftPmPerMinute:F3} pm/min");
            return failures.Count == 0 ? "rolling okno už nespĺňa stabilitu" : string.Join(", ", failures);
        }

        private RollingStabilityDetector NewStabilityDetector() => new(
            Math.Max(2, _settings.RequiredStableSamples),
            _settings.MaxWavelengthRangePm,
            _settings.MaxWavelengthStdDevPm,
            _settings.MaxWavelengthDriftPmPerMinute);

        private void CompleteStableFromMeasurementWindow()
        {
            _finishedElapsed ??= ActiveElapsed;
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
                StabilizationTime = ActiveElapsed,
                Problem = problem,
                StableSamples = samples.ToList(),
            };
        }

        private CalibrationMeasurementResult CreateResultFromCurrentWindow(
            CalibrationTargetState state,
            string? problem)
        {
            if (_measurementSamples.Count > 0)
                return CreateResultFromMeasurementSamples(state, problem);

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

        private static StabilityMetrics CalculateMetrics(IReadOnlyList<CalibrationRawSample> samples)
        {
            if (samples.Count == 0)
                return new StabilityMetrics(0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, false);
            var detector = new RollingStabilityDetector(Math.Max(2, samples.Count), 0, 0, 0);
            StabilityMetrics metrics = new(0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, false);
            foreach (CalibrationRawSample sample in samples)
                metrics = detector.Add(sample.Timestamp, sample.WavelengthNm);
            return metrics;
        }

    }
}
