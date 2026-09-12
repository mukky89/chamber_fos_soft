namespace VotschVc3.Core.Calibration;

/// <summary>A continuous, measured cooling hold; never a calibration measurement.</summary>
public sealed class InterPlateauCooling
{
    public const double DropC = 10;
    public static readonly TimeSpan RequiredHold = TimeSpan.FromMinutes(30);
    private TimeSpan? _enteredAt;
    public TimeSpan Held { get; private set; }
    public static bool RequiresCooling(double? previous, double next) =>
        previous is { } value && double.IsFinite(value) && double.IsFinite(next) && Math.Abs(value - next) < 0.001;
    public bool Observe(TimeSpan elapsed, double measuredC, double targetC)
    {
        if (!double.IsFinite(measuredC) || measuredC > targetC || measuredC < targetC - 1)
        {
            Reset();
            return false;
        }
        _enteredAt ??= elapsed;
        Held = elapsed - _enteredAt.Value;
        return Held >= RequiredHold;
    }
    public void Reset() { _enteredAt = null; Held = TimeSpan.Zero; }
}

public sealed partial class CalibrationProfileRunner
{
    private async Task CoolBetweenPlateausAsync(CalibrationSetup setup, CalibrationRunRecord run,
        CalibrationRunWriter writer, int nextIndex, int count, double fromC, double nextC, double? humidity,
        Func<CancellationToken, Task<double?>>? readReference, CancellationToken token)
    {
        double target = nextC - InterPlateauCooling.DropC;
        run.State = CalibrationRunState.InterPlateauCooling;
        const string context = "Medzikrok – ochladenie bez kalibrácie";
        writer.WriteDiagnostic("INFO", "INTER_PLATEAU_COOLING_START",
            $"Pred platom {nextIndex + 1}: {nextC:F1} → {target:F1} °C; súvislá výdrž 30 min po dosiahnutí teploty. Po obnovení sa výdrž meria nanovo.");
        Progress?.Invoke(new CalibrationProgressSnapshot(run.State, nextIndex, count, target, null, null,
            0, 0, TimeSpan.Zero, Array.Empty<CalibrationTargetProgress>(), context));
        await MoveSetpointToPlateauAsync(setup.Settings, nextIndex, count, fromC, target, humidity, token,
            CalibrationRunState.InterPlateauCooling, context).ConfigureAwait(false);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var hold = new InterPlateauCooling();
        // Bounded settling budget plus the mandatory hold; timeouts never advance to calibration.
        TimeSpan limit = setup.Settings.ChamberStabilityTimeout + setup.Settings.MaxAutomaticChamberStabilityExtension + InterPlateauCooling.RequiredHold;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (IsPaused) { hold.Reset(); clock.Stop(); }
            await WaitWhilePausedAsync(token).ConfigureAwait(false);
            clock.Start();
            double chamber = await ReadTemperatureAsync(token).ConfigureAwait(false);
            double? reference = readReference is null ? null : await readReference(token).ConfigureAwait(false);
            if (_identityObservation is not null) await _identityObservation(token).ConfigureAwait(false);
            bool done = hold.Observe(clock.Elapsed, readReference is null ? chamber : reference ?? double.NaN, target);
            Progress?.Invoke(new CalibrationProgressSnapshot(run.State, nextIndex, count, target, chamber, reference,
                0, 0, clock.Elapsed, Array.Empty<CalibrationTargetProgress>(),
                $"{context} · cieľ {target:F1} °C · výdrž {hold.Held.TotalMinutes:F1}/30 min. " +
                "Výdrž sa počíta pri nameranej teplote v pásme cieľ − 1 °C až cieľ; opustenie pásma ju vynuluje. Potom návrat a nové meranie plata."));
            if (done) break;
            if (clock.Elapsed >= limit)
            {
                var warning = new CalibrationWarning { Code = "INTER_PLATEAU_COOLING_TIMEOUT", PlateauIndex = nextIndex,
                    Message = "Medzikrok ochladenia nedosiahol súvislú 30-minútovú výdrž. Skontrolujte teplotu a obnovte beh; ďalšie plato sa nezmeralo." };
                run.Warnings.Add(warning);
                writer.WriteDiagnostic("ERROR", warning.Code, warning.Message);
                throw new CalibrationOperatorActionRequiredException(warning.Message, warning);
            }
            await Task.Delay(_updateInterval, token).ConfigureAwait(false);
        }
        writer.WriteDiagnostic("INFO", "INTER_PLATEAU_COOLING_COMPLETE", $"Pred platom {nextIndex + 1}: dokončená 30-minútová výdrž pri {target:F1} °C.");
    }
}
