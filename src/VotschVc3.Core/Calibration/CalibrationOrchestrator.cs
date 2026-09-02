using System.Diagnostics;

namespace VotschVc3.Core.Calibration;

/// <summary>
/// Coordinates PeakLogger acquisition for one calibration run. Every selected peak is
/// evaluated in parallel from the same measurement batches. A plateau is released only
/// when every selected target is stable or has reached a terminal error state allowed by
/// the configured policy.
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
        {
            throw new InvalidOperationException("Nie je vybraná žiadna wavelength na kalibráciu.");
        }

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
            string sourceDeviceSn = mapping.SourceDeviceSerialNumber;
            PeakLoggerSensor? sensor = sensors.FirstOrDefault(s =>
                string.Equals(s.SerialNumber, sourceDeviceSn, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(mapping.Channel) || string.Equals(s.Channel, mapping.Channel, StringComparison.OrdinalIgnoreCase)));
            if (sensor is null)
            {
                throw new InvalidOperationException(
                    $"PeakLogger: zariadenie {sourceDeviceSn} / kanál {mapping.Channel} pre FBG SN {mapping.SerialNumber} nebolo nájdené.");
            }

            PeakLoggerPeak? peak = sensor.Peaks.FirstOrDefault(p => string.Equals(p.PeakId, mapping.PeakId, StringComparison.Ordinal));
            if (peak is null)
            {
                throw new InvalidOperationException(
                    $"PeakLogger: peak {mapping.PeakId} na {sourceDeviceSn} / {mapping.Channel} pre FBG SN {mapping.SerialNumber} nebol nájdený.");
            }

            mapping.PeakLoggerDeviceSerialNumber = sensor.SerialNumber;
            mapping.PeakIndex = peak.PeakIndex;
            mapping.CurrentWavelengthNm = peak.WavelengthNm;
            mapping.NominalWavelengthNm ??= peak.WavelengthNm;
        }

        return sensors;
    }

    public async Task<CalibrationPlateauResult> WaitForPlateauAsync(
        CalibrationRunRecord run,
        CalibrationSetup setup,
        int plateauIndex,
        int plateauCount,
        double targetTemperatureC,
        Func<CancellationToken, Task<double>> readChamberTemperatureAsync,
        Func<CancellationToken, Task<double?>>? readReferenceTemperatureAsync,
        CalibrationRunWriter writer,
        Action<CalibrationProgressSnapshot>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(readChamberTemperatureAsync);
        ArgumentNullException.ThrowIfNull(writer);

        CalibrationProfileSettings settings = setup.Settings;
        List<CalibrationSensorMapping> selected = setup.Mappings.Where(x => x.Selected).ToList();
        if (selected.Count == 0)
        {
            throw new InvalidOperationException("Calibration setup nemá vybrané peaky.");
        }

        DateTimeOffset plateauStarted = DateTimeOffset.Now;
        var plateauClock = Stopwatch.StartNew();
        run.State = CalibrationRunState.WaitingForChamberStability;

        var temperatureDetector = new TemperatureStabilityDetector(
            settings.ChamberStableDuration,
            settings.ChamberToleranceC,
            settings.MaxChamberDriftCPerMinute);
        var chamberWait = Stopwatch.StartNew();
        double actualTemperature = double.NaN;
        double? referenceTemperature = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            actualTemperature = await readChamberTemperatureAsync(cancellationToken).ConfigureAwait(false);
            referenceTemperature = readReferenceTemperatureAsync is null
                ? null
                : await readReferenceTemperatureAsync(cancellationToken).ConfigureAwait(false);

            if (_peakLogger is IPeakLoggerSimulationControl simulation)
            {
                simulation.SimulatedTemperatureC = actualTemperature;
            }

            StabilityMetrics temperatureMetrics = temperatureDetector.Add(DateTimeOffset.UtcNow, actualTemperature, targetTemperatureC);
            progress?.Invoke(new CalibrationProgressSnapshot(
                CalibrationRunState.WaitingForChamberStability,
                plateauIndex,
                plateauCount,
                targetTemperatureC,
                actualTemperature,
                referenceTemperature,
                0,
                selected.Count,
                plateauClock.Elapsed,
                BuildWaitingTargets(selected, settings),
                $"Čaká sa na stabilnú teplotu {targetTemperatureC:F1} °C"));

            if (temperatureMetrics.IsStable)
            {
                break;
            }

            if (settings.ChamberStabilityTimeout > TimeSpan.Zero && chamberWait.Elapsed >= settings.ChamberStabilityTimeout)
            {
                CalibrationWarning warning = RaiseWarning(run, new CalibrationWarning
                {
                    Code = "CHAMBER_STABILITY_TIMEOUT",
                    Message = $"Komora sa neustálila na {targetTemperatureC:F1} °C do {settings.ChamberStabilityTimeout}.",
                    PlateauIndex = plateauIndex,
                });
                throw new CalibrationOperatorActionRequiredException(warning.Message, warning);
            }

            await Task.Delay(PollDelay(setup), cancellationToken).ConfigureAwait(false);
        }

        run.State = CalibrationRunState.StabilizingSensors;
        var trackers = selected.ToDictionary(
            m => m.Identity,
            m => new TargetTracker(m, settings),
            StringComparer.OrdinalIgnoreCase);

        while (trackers.Values.Any(t => !t.IsTerminal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            actualTemperature = await readChamberTemperatureAsync(cancellationToken).ConfigureAwait(false);
            referenceTemperature = readReferenceTemperatureAsync is null
                ? null
                : await readReferenceTemperatureAsync(cancellationToken).ConfigureAwait(false);

            if (_peakLogger is IPeakLoggerSimulationControl simulation)
            {
                simulation.SimulatedTemperatureC = actualTemperature;
            }

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
                    {
                        tracker.Fail(CalibrationTargetState.Disconnected, warning.Message);
                    }
                    break;
                }
                throw;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
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
                    tracker.MarkMissing(now);
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
                        {
                            tracker.Fail(CalibrationTargetState.PeakLost, warning.Message);
                        }
                        else
                        {
                            ApplyFailurePolicy(settings.PeakLostPolicy, warning);
                        }
                    }
                    continue;
                }

                if (now - measurement.Timestamp > TimeSpan.FromSeconds(10))
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
                StabilityMetrics metrics = tracker.Detector.Add(measurement.Timestamp, measurement.WavelengthNm);
                tracker.LastMetrics = metrics;

                if (metrics.IsStable)
                {
                    tracker.CompleteStable(run, plateauIndex, targetTemperatureC, actualTemperature, referenceTemperature);
                    continue;
                }

                if (tracker.Elapsed >= tracker.Timeout)
                {
                    CalibrationWarning warning = RaiseWarning(run, new CalibrationWarning
                    {
                        Code = "SENSOR_STABILITY_TIMEOUT",
                        Message = $"FBG SN {tracker.Mapping.SerialNumber}, peak {tracker.Mapping.PeakId} sa neustálil do {tracker.Timeout}.",
                        PlateauIndex = plateauIndex,
                        SerialNumber = tracker.Mapping.SerialNumber,
                        PeakId = tracker.Mapping.PeakId,
                    });

                    if (settings.SensorTimeoutPolicy == CalibrationFailurePolicy.ContinueAndFlag)
                    {
                        tracker.CompleteTimedOut(run, plateauIndex, targetTemperatureC, actualTemperature, referenceTemperature, warning.Message);
                    }
                    else
                    {
                        ApplyFailurePolicy(settings.SensorTimeoutPolicy, warning);
                    }
                }
            }

            if (rawToWrite.Count > 0)
            {
                await writer.AppendAsync(rawToWrite, cancellationToken).ConfigureAwait(false);
            }

            int stable = trackers.Values.Count(t => t.State is CalibrationTargetState.Stable or CalibrationTargetState.Overridden);
            int resolved = trackers.Values.Count(t => t.IsTerminal);
            progress?.Invoke(new CalibrationProgressSnapshot(
                CalibrationRunState.StabilizingSensors,
                plateauIndex,
                plateauCount,
                targetTemperatureC,
                actualTemperature,
                referenceTemperature,
                stable,
                selected.Count,
                plateauClock.Elapsed,
                trackers.Values.Select(t => t.ToProgress(settings.RequiredStableSamples)).ToArray(),
                resolved == selected.Count
                    ? "Všetky wavelengthy sú vyriešené."
                    : $"Čaká sa na {selected.Count - resolved} wavelength."));

            if (trackers.Values.Any(t => !t.IsTerminal))
            {
                await Task.Delay(PollDelay(setup), cancellationToken).ConfigureAwait(false);
            }
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
        double deltaT = current.ActualTemperatureC - baseline.ActualTemperatureC;
        if (Math.Abs(deltaT) < settings.ValidationMinimumDeltaTemperatureC)
        {
            return false;
        }

        run.State = CalibrationRunState.TemperatureResponseValidation;
        bool allValid = true;
        foreach (CalibrationMeasurementResult currentTarget in current.Targets)
        {
            CalibrationMeasurementResult? baseTarget = baseline.Targets.FirstOrDefault(x =>
                string.Equals(x.Identity, currentTarget.Identity, StringComparison.OrdinalIgnoreCase));
            if (baseTarget is null || currentTarget.Status != CalibrationTargetState.Stable || baseTarget.Status != CalibrationTargetState.Stable)
            {
                continue;
            }

            double deltaPm = (currentTarget.MeanWavelengthNm - baseTarget.MeanWavelengthNm) * 1000d;
            bool magnitudeOk = Math.Abs(deltaPm) >= settings.ValidationMinimumWavelengthResponsePm;
            bool directionOk = settings.ExpectedResponseDirection switch
            {
                ExpectedResponseDirection.Positive => deltaPm / deltaT > 0,
                ExpectedResponseDirection.Negative => deltaPm / deltaT < 0,
                _ => true,
            };

            if (magnitudeOk && directionOk)
            {
                continue;
            }

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

    private static TimeSpan PollDelay(CalibrationSetup setup)
    {
        return TimeSpan.FromSeconds(1);
    }

    private static IReadOnlyList<CalibrationTargetProgress> BuildWaitingTargets(
        IEnumerable<CalibrationSensorMapping> mappings,
        CalibrationProfileSettings settings) =>
        mappings.Select(m => new CalibrationTargetProgress(
            m.SerialNumber,
            m.Channel,
            m.PeakId,
            m.PeakIndex,
            m.CurrentWavelengthNm,
            0,
            settings.RequiredStableSamples,
            null,
            null,
            TimeSpan.Zero,
            m.StabilizationTimeoutOverride ?? settings.DefaultSensorStabilizationTimeout,
            CalibrationTargetState.WaitingForTemperature,
            "Čaká na stabilnú teplotu komory")).ToArray();

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

        public bool IsTerminal => Result is not null ||
            State is CalibrationTargetState.Stable or
                     CalibrationTargetState.TimedOut or
                     CalibrationTargetState.Disconnected or
                     CalibrationTargetState.Overridden or
                     CalibrationTargetState.Failed;

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
            if (!IsTerminal) State = CalibrationTargetState.PeakLost;
        }

        public void Fail(CalibrationTargetState state, string problem)
        {
            State = state;
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
            Result = CreateResult(State, problem, run, plateauIndex, targetTemperature, (actualTemperature, referenceTemperature));
        }

        public CalibrationMeasurementResult CreateFallbackResult() =>
            Result ?? CreateResult(State, "Meranie skončilo bez kompletného výsledku.", null, 0, 0, null);

        public CalibrationTargetProgress ToProgress(int requiredSamples) => new(
            Mapping.SerialNumber,
            Mapping.Channel,
            Mapping.PeakId,
            Mapping.PeakIndex,
            LastMeasurement?.WavelengthNm ?? Mapping.CurrentWavelengthNm,
            Detector.Count,
            requiredSamples,
            LastMetrics?.StandardDeviation,
            LastMetrics?.SlopePerMinute,
            Elapsed,
            Timeout,
            State,
            State == CalibrationTargetState.Stabilizing ? "Vyhodnocuje rolling window" : null);

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
