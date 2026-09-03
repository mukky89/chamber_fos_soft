using System.Diagnostics;
using VotschVc3.Core.Communication;
using VotschVc3.Core.Profiles;
using VotschVc3.Core.Protocol;

namespace VotschVc3.Core.Calibration;

/// <summary>
/// Temperature-calibration profile runner. Ordinary ramps/non-calibration holds use their profile
/// duration. Selected calibration points are commanded immediately and progression is controlled by
/// the dedicated calibration minimum-time gate plus measured chamber, reference and FBG stability.
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
            throw new InvalidOperationException("Profil nie je označený ako TemperatureCalibration.");

        List<ExecutionStep> steps = ExpandExecution(profile);
        HashSet<int> selectedCalibrationSegments = ResolveCalibrationSegmentIndices(profile, setup);
        List<ExecutionStep> calibrationSteps = steps
            .Where(s => !s.Segment.IsRamp && selectedCalibrationSegments.Contains(s.SegmentIndex))
            .ToList();
        if (calibrationSteps.Count == 0)
            throw new InvalidOperationException("Kalibračný profil nemá označené žiadne kalibračné plato.");

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

                bool isCalibrationPlateau = !step.Segment.IsRamp && selectedCalibrationSegments.Contains(step.SegmentIndex);
                run.State = CalibrationRunState.MovingToPlateau;

                if (!isCalibrationPlateau)
                {
                    await ExecuteSegmentAsync(step.Segment, previousTemperature, previousHumidity, cancellationToken).ConfigureAwait(false);
                    previousTemperature = step.Segment.TargetTemperature;
                    previousHumidity = step.Segment.TargetHumidity ?? previousHumidity;
                    continue;
                }

                // The ordinary profile hold is not reused as calibration stability time. The
                // dedicated MinimumCalibrationPointDuration setting is explicit and combines with
                // the physical chamber/reference gates.
                double? targetHumidity = step.Segment.TargetHumidity ?? previousHumidity;
                await WriteSetpointAsync(step.Segment.TargetTemperature, targetHumidity, cancellationToken).ConfigureAwait(false);
                previousTemperature = step.Segment.TargetTemperature;
                previousHumidity = targetHumidity;

                int currentPlateau = calibrationPlateauIndex++;
                Func<double, double?, CancellationToken, Task<string?>>? referenceControl =
                    CreateReferenceControl(setup, targetHumidity);

                CalibrationPlateauResult plateau = await _orchestrator.WaitForPlateauAsync(
                    run,
                    setup,
                    currentPlateau,
                    calibrationSteps.Count,
                    step.Segment.TargetTemperature,
                    setup.Settings.MinimumCalibrationPointDuration,
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
            }

            if (!responseValidated && run.Plateaus.Count > 1)
            {
                run.Warnings.Add(new CalibrationWarning
                {
                    Code = "VALIDATION_NOT_COMPLETED",
                    Message = "Profil neposkytol dostatočnú zmenu teploty na automatické overenie odozvy wavelength.",
                });
            }

            run.State = CalibrationRunState.CalculatingResults;
            var calculator = new TemperatureCalibrationCalculator();
            run.CalculationResults = calculator.CalculateRun(run, setup).ToList();
            foreach (TemperatureCalibrationResult failed in run.CalculationResults.Where(result => !result.OverallPassed))
            {
                run.Warnings.Add(new CalibrationWarning
                {
                    Code = "CALIBRATION_RESULT_FAIL",
                    Message = $"FBG SN {failed.SerialNumber}, peak {failed.PeakId}: {failed.StatusMessage}",
                    SerialNumber = failed.SerialNumber,
                    PeakId = failed.PeakId,
                });
            }

            run.CompletedAt = DateTimeOffset.Now;
            run.State = run.Warnings.Count == 0 && run.CalculationResults.All(r => r.OverallPassed)
                ? CalibrationRunState.Completed
                : CalibrationRunState.CompletedWithWarnings;
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
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
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
        CalibrationRecipeKey = m.CalibrationRecipeKey,
        StabilizationTimeoutOverride = m.StabilizationTimeoutOverride,
    };

    private sealed record ExecutionStep(ProfileSegment Segment, int SegmentIndex, int CycleIndex);
}
