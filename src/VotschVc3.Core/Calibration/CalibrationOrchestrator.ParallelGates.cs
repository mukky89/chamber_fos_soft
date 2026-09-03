using System.Diagnostics;

namespace VotschVc3.Core.Calibration;

/// <summary>
/// Production calibration orchestrator. Minimum plateau time, authoritative reference-temperature
/// stability and every selected FBG rolling stability window are observed from the beginning of
/// the plateau. A calibration point is committed only when all three gates are true at the same
/// time. This avoids throwing away useful warm-up data and prevents an early-stable peak from being
/// accepted if it drifts again before the minimum plateau time expires.
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

    /// <summary>Compatibility overload for tests/callers that do not use a minimum plateau gate.</summary>
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
            referenceControlAsync: null,
            writer,
            progress,
            cancellationToken);

    /// <summary>
    /// Observes all plateau gates in parallel. The reference-control callback is optional and may
    /// slowly trim the chamber setpoint from the WIKA error; its returned text is included in live
    /// diagnostics but does not itself decide stability.
    /// </summary>
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
        var referenceDetector = new TemperatureStabilityDetector(
            settings.ChamberStableDuration,
            settings.ChamberToleranceC,
            settings.MaxChamberDriftCPerMinute);
        var localFallbackDetector = new TemperatureStabilityDetector(
            settings.ChamberStableDuration,
            settings.ChamberToleranceC,
            settings.MaxChamberDriftCPerMinute);
        var trackers = selected.ToDictionary(
            m => m.Identity,
            m => new TargetTracker(m, settings),
            StringComparer.OrdinalIgnoreCase);

        double actualTemperature = double.NaN;
        double? referenceTemperature = null;
        StabilityMetrics? temperatureMetrics = null;
        TimeSpan sensorGateElapsed = TimeSpan.Zero;
        DateTimeOffset previousLoopAt = DateTimeOffset.UtcNow;
        bool previousGateOpen = false;
        TimeSpan effectiveReferenceTimeout = settings.ChamberStabilityTimeout > minimumPlateauDuration
            ? settings.ChamberStabilityTimeout
            : minimumPlateauDuration;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset loopAt = DateTimeOffset.UtcNow;
            TimeSpan loopDelta = loopAt - previousLoopAt;
            previousLoopAt = loopAt;

            actualTemperature = await readChamberTemperatureAsync(cancellationToken).ConfigureAwait(false);
            referenceTemperature = readReferenceTemperatureAsync is null
                ? null
                : await readReferenceTemperatureAsync(cancellationToken).ConfigureAwait(false);

            string controlDetail = referenceControlAsync is null
                ? string.Empty
                : await referenceControlAsync(targetTemperatureC, referenceTemperature, cancellationToken).ConfigureAwait(false) ?? string.Empty;

            if (_peakLogger is IPeakLoggerSimulationControl simulation)
                simulation.SimulatedTemperatureC = referenceTemperature ?? actualTemperature;

            temperatureMetrics = referenceTemperature is { } reference
                ? referenceDetector.Add(loopAt, reference, targetTemperatureC)
                : localFallbackDetector.Add(loopAt, actualTemperature, targetTemperatureC);

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

            var rawToWrite = new List<CalibrationRawSample>();
            foreach (TargetTracker tracker in trackers.Values.Where(t => !t.IsTerminal))
            {
                PeakLoggerMeasurement? measurement = batch
                    .Where(m => string.Equals(m.SerialNumber, tracker.Mapping.SourceDeviceSerialNumber, StringComparison.OrdinalIgnoreCase))
                    .Where(m => string.Equals(m.PeakId, tracker.Mapping.PeakId, StringComparison.Ordinal))
                    .Where(m => string.IsNullOrWhiteSpace(tracker.Mapping.Channel) || string.Equals(m.Channel, tracker.Mapping.Channel, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(m => m.Timestamp)
                    .FirstOrDefault();

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
                rawToWrite.Add(CreateRawSample(
                    run,
                    plateauIndex,
                    targetTemperatureC,
                    actualTemperature,
                    referenceTemperature,
                    tracker.Mapping,
                    measurement));

                tracker.LastMetrics = tracker.Detector.Add(measurement.Timestamp, measurement.WavelengthNm);
                tracker.CurrentStable = tracker.LastMetrics.IsStable;
                tracker.State = tracker.CurrentStable ? CalibrationTargetState.Stable : CalibrationTargetState.Stabilizing;
            }

            if (rawToWrite.Count > 0)
                await writer.AppendAsync(rawToWrite, cancellationToken).ConfigureAwait(false);

            bool minimumElapsed = plateauClock.Elapsed >= minimumPlateauDuration;
            bool referenceStable = temperatureMetrics.IsStable;
            bool sensorGateOpen = minimumElapsed && referenceStable;
            if (sensorGateOpen && previousGateOpen && loopDelta > TimeSpan.Zero)
                sensorGateElapsed += loopDelta;
            previousGateOpen = sensorGateOpen;

            if (sensorGateOpen)
            {
                foreach (TargetTracker tracker in trackers.Values.Where(t => !t.IsTerminal && !t.CurrentStable))
                {
                    if (sensorGateElapsed < tracker.Timeout) continue;
                    CalibrationWarning warning = RaiseWarning(run, new CalibrationWarning
                    {
                        Code = "SENSOR_STABILITY_TIMEOUT",
                        Message = $"FBG SN {tracker.Mapping.SerialNumber}, peak {tracker.Mapping.PeakId} sa neustálil do {tracker.Timeout} po otvorení teplotnej/minimálnej brány.",
                        PlateauIndex = plateauIndex,
                        SerialNumber = tracker.Mapping.SerialNumber,
                        PeakId = tracker.Mapping.PeakId,
                    });
                    if (settings.SensorTimeoutPolicy == CalibrationFailurePolicy.ContinueAndFlag)
                        tracker.CompleteTimedOut(run, plateauIndex, targetTemperatureC, actualTemperature, referenceTemperature, warning.Message);
                    else
                        ApplyFailurePolicy(settings.SensorTimeoutPolicy, warning);
                }
            }

            bool allFbgStableNow = trackers.Values
                .Where(t => !t.IsTerminal)
                .All(t => t.CurrentStable);
            bool allGatesSatisfied = minimumElapsed && referenceStable && allFbgStableNow;

            int stableNow = trackers.Values.Count(t => t.CurrentStable || t.Result?.Status is CalibrationTargetState.Stable or CalibrationTargetState.Overridden);
            CalibrationRunState progressState = !minimumElapsed || !referenceStable
                ? CalibrationRunState.WaitingForChamberStability
                : CalibrationRunState.StabilizingSensors;
            run.State = progressState;

            string referenceDetail = BuildReferenceDetail(
                referenceTemperature,
                actualTemperature,
                targetTemperatureC,
                temperatureMetrics,
                settings,
                readReferenceTemperatureAsync is not null);
            string gateMessage = BuildGateMessage(
                plateauClock.Elapsed,
                minimumPlateauDuration,
                referenceStable,
                stableNow,
                selected.Count,
                referenceDetail,
                controlDetail);

            progress?.Invoke(new CalibrationProgressSnapshot(
                progressState,
                plateauIndex,
                plateauCount,
                targetTemperatureC,
                actualTemperature,
                referenceTemperature,
                stableNow,
                selected.Count,
                plateauClock.Elapsed,
                trackers.Values.Select(t => t.ToProgress(
                    settings,
                    plateauClock.Elapsed,
                    minimumPlateauDuration,
                    referenceStable,
                    sensorGateElapsed)).ToArray(),
                gateMessage));

            if (allGatesSatisfied)
            {
                foreach (TargetTracker tracker in trackers.Values.Where(t => !t.IsTerminal))
                    tracker.CompleteStable(run, plateauIndex, targetTemperatureC, actualTemperature, referenceTemperature);
                break;
            }

            if (!referenceStable && effectiveReferenceTimeout > TimeSpan.Zero && plateauClock.Elapsed >= effectiveReferenceTimeout)
            {
                string measured = referenceTemperature is { } wika
                    ? $"WIKA {wika:F3} °C"
                    : $"lokálna sonda {actualTemperature:F3} °C";
                CalibrationWarning warning = RaiseWarning(run, new CalibrationWarning
                {
                    Code = "REFERENCE_STABILITY_TIMEOUT",
                    Message = $"Referenčná teplota sa neustálila na {targetTemperatureC:F1} °C do {effectiveReferenceTimeout}. Posledná hodnota: {measured}.",
                    PlateauIndex = plateauIndex,
                });
                throw new CalibrationOperatorActionRequiredException(warning.Message, warning);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
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
                Message = $"FBG SN {currentTarget.SerialNumber}, peak {currentTarget.PeakId}: Δλ={deltaPm:F2} pm pri ΔT(WIKA)={deltaT:F2} °C – vybraná wavelength nereaguje podľa nastavených limitov.",
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

    private static string BuildReferenceDetail(
        double? referenceTemperature,
        double chamberTemperature,
        double targetTemperature,
        StabilityMetrics metrics,
        CalibrationProfileSettings settings,
        bool hasExternalReference)
    {
        double measured = referenceTemperature ?? chamberTemperature;
        double error = measured - targetTemperature;
        bool toleranceOk = Math.Abs(error) <= settings.ChamberToleranceC;
        bool durationOk = metrics.WindowDuration >= settings.ChamberStableDuration;
        bool driftOk = settings.MaxChamberDriftCPerMinute <= 0 || Math.Abs(metrics.SlopePerMinute) <= settings.MaxChamberDriftCPerMinute;
        string source = hasExternalReference ? "WIKA" : "lokálna sonda (fallback)";
        return $"{source} {measured:F3} °C · Δ {error:+0.000;-0.000;0.000} / ±{settings.ChamberToleranceC:F3} {(toleranceOk ? "✓" : "×")} · " +
               $"stable čas {FormatTime(metrics.WindowDuration)}/{FormatTime(settings.ChamberStableDuration)} {(durationOk ? "✓" : "…")} · " +
               $"drift {Math.Abs(metrics.SlopePerMinute):F3}/{settings.MaxChamberDriftCPerMinute:F3} °C/min {(driftOk ? "✓" : "×")}";
    }

    private static string BuildGateMessage(
        TimeSpan elapsed,
        TimeSpan minimum,
        bool referenceStable,
        int stableFbg,
        int totalFbg,
        string referenceDetail,
        string controlDetail)
    {
        bool minimumOk = elapsed >= minimum;
        string minimumText = minimum <= TimeSpan.Zero
            ? "MINIMUM ✓ (bez minima)"
            : $"MINIMUM {(minimumOk ? "✓" : "…")} {FormatTime(elapsed < minimum ? elapsed : minimum)}/{FormatTime(minimum)}";
        string fbgText = $"FBG {(stableFbg >= totalFbg ? "✓" : "…")} {stableFbg}/{totalFbg}";
        string blockers = string.Join(" + ", new[]
        {
            minimumOk ? null : "minimálny čas plata",
            referenceStable ? null : "stabilná referencia",
            stableFbg >= totalFbg ? null : $"{totalFbg - stableFbg} FBG peak(ov)",
        }.Where(x => x is not null));
        if (string.IsNullOrWhiteSpace(blockers)) blockers = "všetky brány splnené – ukladám kalibračný bod";
        return $"{minimumText} · WIKA {(referenceStable ? "✓" : "…")} · {fbgText} · ČAKÁM NA: {blockers}\n{referenceDetail}{controlDetail}";
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

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");

    private sealed class TargetTracker
    {
        private DateTimeOffset? _missingSince;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly int _averagingSamples;
        private readonly Queue<PeakLoggerMeasurement> _averagingWindow = new();

        public TargetTracker(CalibrationSensorMapping mapping, CalibrationProfileSettings settings)
        {
            Mapping = mapping;
            _averagingSamples = settings.EnableWavelengthAveraging
                ? Math.Clamp(settings.WavelengthAveragingSamples, 1, 1000)
                : 1;
            Detector = new RollingStabilityDetector(
                settings.RequiredStableSamples,
                settings.MaxWavelengthRangePm,
                settings.MaxWavelengthStdDevPm,
                settings.MaxWavelengthDriftPmPerMinute);
            Timeout = mapping.StabilizationTimeoutOverride ?? settings.DefaultSensorStabilizationTimeout;
            State = CalibrationTargetState.Stabilizing;
        }

        public CalibrationSensorMapping Mapping { get; }
        public RollingStabilityDetector Detector { get; }
        public TimeSpan Timeout { get; }
        public TimeSpan Elapsed => _clock.Elapsed;
        public CalibrationTargetState State { get; set; }
        public StabilityMetrics? LastMetrics { get; set; }
        public PeakLoggerMeasurement? LastMeasurement { get; private set; }
        public CalibrationMeasurementResult? Result { get; private set; }
        public TimeSpan MissingFor => _missingSince is { } since ? DateTimeOffset.UtcNow - since : TimeSpan.Zero;
        public bool CurrentStable { get; set; }
        public bool IsTerminal => Result is not null;

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
            if (!IsTerminal) State = CalibrationTargetState.Stabilizing;
        }

        public void MarkMissing(DateTimeOffset now)
        {
            _missingSince ??= now;
            CurrentStable = false;
            if (!IsTerminal) State = CalibrationTargetState.PeakLost;
        }

        public void Fail(CalibrationTargetState state, string problem)
        {
            State = state;
            CurrentStable = false;
            Result = CreateResult(state, problem, null, 0, 0, null);
        }

        public void CompleteStable(
            CalibrationRunRecord run,
            int plateauIndex,
            double targetTemperature,
            double actualTemperature,
            double? referenceTemperature)
        {
            State = CalibrationTargetState.Stable;
            CurrentStable = true;
            Result = CreateResult(State, null, run, plateauIndex, targetTemperature, (actualTemperature, referenceTemperature));
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
            CurrentStable = false;
            Result = CreateResult(State, problem, run, plateauIndex, targetTemperature, (actualTemperature, referenceTemperature));
        }

        public CalibrationMeasurementResult CreateFallbackResult() =>
            Result ?? CreateResult(State, "Meranie skončilo bez kompletného výsledku.", null, 0, 0, null);

        public CalibrationTargetProgress ToProgress(
            CalibrationProfileSettings settings,
            TimeSpan plateauElapsed,
            TimeSpan minimumPlateauDuration,
            bool referenceStable,
            TimeSpan sensorGateElapsed)
        {
            StabilityMetrics metrics = LastMetrics ?? Detector.Evaluate();
            bool enough = metrics.Count >= settings.RequiredStableSamples;
            bool rangeOk = settings.MaxWavelengthRangePm <= 0 || metrics.Range <= settings.MaxWavelengthRangePm;
            bool stdOk = settings.MaxWavelengthStdDevPm <= 0 || metrics.StandardDeviation <= settings.MaxWavelengthStdDevPm;
            bool driftOk = settings.MaxWavelengthDriftPmPerMinute <= 0 || Math.Abs(metrics.SlopePerMinute) <= settings.MaxWavelengthDriftPmPerMinute;
            bool minimumOk = plateauElapsed >= minimumPlateauDuration;
            string detail =
                $"samples {metrics.Count}/{settings.RequiredStableSamples} {(enough ? "✓" : "…")} · " +
                $"range {metrics.Range:F3}/{settings.MaxWavelengthRangePm:F3} pm {(rangeOk ? "✓" : "×")} · " +
                $"std {metrics.StandardDeviation:F3}/{settings.MaxWavelengthStdDevPm:F3} pm {(stdOk ? "✓" : "×")} · " +
                $"drift {Math.Abs(metrics.SlopePerMinute):F3}/{settings.MaxWavelengthDriftPmPerMinute:F3} pm/min {(driftOk ? "✓" : "×")} · " +
                $"minimum {(minimumOk ? "✓" : "…")} · WIKA {(referenceStable ? "✓" : "…")} · timeout po gate {FormatTime(sensorGateElapsed)}/{FormatTime(Timeout)}";
            if (State == CalibrationTargetState.PeakLost) detail = "Peak chýba · " + detail;
            if (Result?.Problem is { Length: > 0 } problem) detail = problem + " · " + detail;

            return new CalibrationTargetProgress(
                Mapping.SerialNumber,
                Mapping.Channel,
                Mapping.PeakId,
                Mapping.PeakIndex,
                LastMeasurement?.WavelengthNm ?? Mapping.CurrentWavelengthNm,
                metrics.Count,
                settings.RequiredStableSamples,
                metrics.Count > 0 ? metrics.StandardDeviation : null,
                metrics.Count > 0 ? metrics.SlopePerMinute : null,
                plateauElapsed,
                Timeout,
                State,
                detail);
        }

        private CalibrationMeasurementResult CreateResult(
            CalibrationTargetState state,
            string? problem,
            CalibrationRunRecord? run,
            int plateauIndex,
            double targetTemperature,
            (double Actual, double? Reference)? temperatures)
        {
            StabilityMetrics metrics = Detector.Evaluate();
            var result = new CalibrationMeasurementResult
            {
                SerialNumber = Mapping.SerialNumber,
                PeakLoggerDeviceSerialNumber = Mapping.SourceDeviceSerialNumber,
                Channel = Mapping.Channel,
                PeakId = Mapping.PeakId,
                PeakIndex = Mapping.PeakIndex,
                Status = state,
                SampleCount = metrics.Count,
                MeanWavelengthNm = metrics.Mean,
                MedianWavelengthNm = metrics.Median,
                MinWavelengthNm = metrics.Minimum,
                MaxWavelengthNm = metrics.Maximum,
                RangePm = metrics.Range,
                StandardDeviationPm = metrics.StandardDeviation,
                DriftPmPerMinute = metrics.SlopePerMinute,
                StabilizationTime = Elapsed,
                Problem = problem,
            };

            if (run is not null && temperatures is { } temps)
            {
                foreach ((DateTimeOffset timestamp, double value) in Detector.Samples)
                {
                    result.StableSamples.Add(new CalibrationRawSample
                    {
                        RunId = run.RunId,
                        ProfileId = run.ProfileId,
                        PlateauIndex = plateauIndex,
                        TargetTemperatureC = targetTemperature,
                        ActualTemperatureC = temps.Actual,
                        ReferenceTemperatureC = temps.Reference,
                        Timestamp = timestamp,
                        SerialNumber = Mapping.SerialNumber,
                        PeakLoggerDeviceSerialNumber = Mapping.SourceDeviceSerialNumber,
                        Channel = Mapping.Channel,
                        PeakId = Mapping.PeakId,
                        PeakIndex = Mapping.PeakIndex,
                        WavelengthNm = value,
                        Intensity = LastMeasurement?.Intensity,
                    });
                }
            }
            return result;
        }
    }
}
