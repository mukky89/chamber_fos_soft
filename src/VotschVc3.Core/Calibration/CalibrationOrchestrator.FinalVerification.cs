using System.Diagnostics;

namespace VotschVc3.Core.Calibration;

public sealed partial class CalibrationOrchestrator
{
    public async Task<CalibrationPlateauResult> CollectFinalVerificationAsync(
        CalibrationSetup setup, CalibrationRunRecord run, CalibrationRunWriter writer,
        Func<CancellationToken, Task<double>> readChamber,
        Func<CancellationToken, Task<double?>>? readReference, CancellationToken token,
        Action<int, int, double?, double, TimeSpan>? progress = null)
    {
        var result = new CalibrationPlateauResult { PlateauIndex = -1, TargetTemperatureC = 25, StartedAt = DateTimeOffset.Now };
        var selected = setup.Mappings.Where(m => m.Selected).ToArray();
        var samples = selected.ToDictionary(m => m, _ => new List<CalibrationRawSample>());
        int count = Math.Max(2, setup.Settings.RequiredMeasurementSamples);
        int interval = Math.Clamp(setup.Settings.SampleAcquisitionIntervalSeconds, 1, 30);
        var watch = Stopwatch.StartNew();
        TimeSpan budget = TimeSpan.FromSeconds(Math.Min(5400, count * interval * 1.2 + 20));
        string? error = null;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(budget);
        try
        {
            while (samples.Values.Any(values => values.Count < count))
            {
                bounded.Token.ThrowIfCancellationRequested();
                double chamber = await readChamber(bounded.Token).ConfigureAwait(false);
                double? reference = readReference is null ? chamber : await readReference(bounded.Token).ConfigureAwait(false);
                if (_peakLogger is IPeakLoggerSimulationControl simulation) simulation.SimulatedTemperatureC = reference ?? chamber;
                var batch = await _peakLogger.ReadMeasurementsAsync(bounded.Token).ConfigureAwait(false);
                var raw = new List<CalibrationRawSample>();
                foreach (var mapping in selected)
                {
                    var values = samples[mapping];
                    if (values.Count >= count) continue;
                    var measurement = FindMeasurement(batch, mapping);
                    if (measurement is null || !double.IsFinite(measurement.WavelengthNm) || measurement.WavelengthNm <= 0 ||
                        DateTimeOffset.UtcNow - measurement.Timestamp > TimeSpan.FromSeconds(10) ||
                        values.Any(v => v.Timestamp == measurement.Timestamp) ||
                        reference is not { } t || !double.IsFinite(t)) continue;
                    var sample = CreateRawSample(run, -1, 25, chamber, reference, mapping, measurement);
                    values.Add(sample);
                    raw.Add(sample);
                }
                if (raw.Count > 0) await writer.AppendAsync(raw, bounded.Token).ConfigureAwait(false);
                progress?.Invoke(samples.Values.Min(v => v.Count), count, reference, chamber, watch.Elapsed);
                writer.WriteDiagnostic("INFO", "FINAL_VERIFICATION_SAMPLING",
                    $"Kontrola 25 °C · odber {samples.Values.Min(v => v.Count)}/{count} · WIKA {reference:F3} °C.");
                if (samples.Values.All(v => v.Count >= count)) break;
                await Task.Delay(TimeSpan.FromSeconds(interval), bounded.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { error = "Vypršal limit kontrolného odberu."; }
        catch (Exception ex) when (ex is IOException or TimeoutException or System.Net.Http.HttpRequestException) { error = "Kontrolné meranie: " + ex.Message; }
        token.ThrowIfCancellationRequested();
        foreach (var mapping in selected)
        {
            var values = samples[mapping];
            var detector = new RollingStabilityDetector(count, setup.Settings.MaxWavelengthRangePm,
                setup.Settings.MaxWavelengthStdDevPm, setup.Settings.MaxWavelengthDriftPmPerMinute);
            foreach (var value in values) detector.Add(value.Timestamp, value.WavelengthNm);
            var metrics = detector.Evaluate();
            bool temperatureOk = values.Count > 0 && values.All(v => Math.Abs(v.ReferenceTemperatureC!.Value - 25) <= setup.Settings.FinalConditioningToleranceC);
            var temperatureDetector = new TemperatureStabilityDetector(setup.Settings.ChamberStableDuration,
                setup.Settings.ChamberToleranceC, setup.Settings.MaxChamberDriftCPerMinute,
                setup.Settings.MaxChamberRangeC, setup.Settings.MaxChamberStdDevC);
            StabilityMetrics? temperatureMetrics = null;
            foreach (var value in values) temperatureMetrics = temperatureDetector.Add(value.Timestamp, value.ReferenceTemperatureC!.Value, 25);
            string? problem = error;
            if (values.Count < count) problem += $" Neúplný odber {values.Count}/{count}.";
            if (temperatureMetrics?.IsStable != true) problem += " Stabilita referencie nebola potvrdená.";
            if (!temperatureOk) problem += " WIKA mimo tolerancie 25 °C alebo chýba.";
            if (!metrics.IsStable) problem += " Nestabilná kontrolná wavelength.";
            // Reference drift/range are reported independently of coefficient residuals.
            if (values.Count > 1 && values.Max(v => v.ReferenceTemperatureC!.Value) - values.Min(v => v.ReferenceTemperatureC!.Value) >
                Math.Max(0.000001, setup.Settings.MaxChamberRangeC) && setup.Settings.MaxChamberRangeC > 0)
                problem += " Kolísanie referencie pri kontrolnom odbere.";
            result.Targets.Add(new CalibrationMeasurementResult
            {
                SerialNumber = mapping.SerialNumber, PeakLoggerDeviceSerialNumber = mapping.SourceDeviceSerialNumber,
                Channel = mapping.Channel, PeakId = mapping.PeakId, PeakIndex = mapping.PeakIndex,
                SampleCount = values.Count, MeanWavelengthNm = metrics.Mean, MedianWavelengthNm = metrics.Median,
                MinWavelengthNm = metrics.Minimum, MaxWavelengthNm = metrics.Maximum,
                RangePm = metrics.Range, StandardDeviationPm = metrics.StandardDeviation, DriftPmPerMinute = metrics.SlopePerMinute,
                StableSamples = values, Status = string.IsNullOrWhiteSpace(problem) ? CalibrationTargetState.Stable :
                    CalibrationTargetState.CompletedWithStabilityWarning, Problem = problem, StabilizationTime = watch.Elapsed,
            });
        }
        result.ReferenceTemperatureC = samples.Values.SelectMany(v => v).Select(v => v.ReferenceTemperatureC).DefaultIfEmpty(null).Average();
        var allSamples = samples.Values.SelectMany(v => v).ToArray();
        if (allSamples.Length > 0) result.ActualTemperatureC = allSamples.Average(v => v.ActualTemperatureC);
        result.CompletedAt = DateTimeOffset.Now;
        return result;
    }
}