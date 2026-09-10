namespace VotschVc3.Core.Calibration;

public sealed partial class CalibrationOrchestrator
{
    /// <summary>Independent final point with the same temperature and wavelength gates as production plateaus.</summary>
    public Task<CalibrationPlateauResult> CollectFinalVerificationAsync(
        CalibrationSetup setup, CalibrationRunRecord run, CalibrationRunWriter writer,
        Func<CancellationToken, Task<double>> readChamber,
        Func<CancellationToken, Task<double?>>? readReference, CancellationToken token,
        Action<int, int, double?, double, TimeSpan>? progress = null) =>
        WaitForPlateauAsync(run, setup, -1, run.Plateaus.Count, 25,
            TimeSpan.Zero, readChamber, readReference, writer,
            snapshot => progress?.Invoke(snapshot.Targets.Select(t => t.MeasurementSamples).DefaultIfEmpty(0).Min(), setup.Settings.RequiredMeasurementSamples,
                snapshot.ReferenceTemperatureC, snapshot.ActualTemperatureC ?? double.NaN, snapshot.PlateauElapsed),
            token, deferOnTemperatureTimeout: false);
}
