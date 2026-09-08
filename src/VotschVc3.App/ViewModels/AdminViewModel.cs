using VotschVc3.App.Mvvm;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// Admin-only settings screen. Groups the e-mail notification configuration and
/// chamber management (add / remove) that used to live on the home page so they
/// are out of the operator's way and only reachable by administrators.
/// </summary>
/// <remarks>
/// The screen owns no state itself: it wraps the <see cref="ShellViewModel"/> and
/// the view binds to the shell's existing e-mail and chamber-management members
/// through <see cref="Shell"/>.
/// </remarks>
public sealed class AdminViewModel : ObservableObject
{
    private readonly CalibrationDefaultsStore _defaultsStore;
    private CalibrationProfileSettings _calibrationDefaults;
    private string _calibrationDefaultsStatus = "Tieto hodnoty sa skopírujú do každého nového zapojenia kalibrácie.";

    public AdminViewModel(ShellViewModel shell)
    {
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _defaultsStore = new CalibrationDefaultsStore(System.IO.Path.Combine(AppPaths.SettingsDir, "fbg-calibration-defaults.json"));
        _calibrationDefaults = _defaultsStore.Load();
        SaveCalibrationDefaultsCommand = new RelayCommand(SaveCalibrationDefaults);
    }

    /// <summary>The root view model that owns the actual settings and commands.</summary>
    public ShellViewModel Shell { get; }

    public CalibrationProfileSettings CalibrationDefaults => _calibrationDefaults;
    public double ChamberStableMinutes { get => _calibrationDefaults.ChamberStableDuration.TotalMinutes; set => _calibrationDefaults.ChamberStableDuration = TimeSpan.FromMinutes(Math.Clamp(value, 0, 1440)); }
    public double ChamberStabilityTimeoutMinutes { get => _calibrationDefaults.ChamberStabilityTimeout.TotalMinutes; set => _calibrationDefaults.ChamberStabilityTimeout = TimeSpan.FromMinutes(Math.Clamp(value, 1, 1440)); }
    public double SensorStabilityTimeoutMinutes { get => _calibrationDefaults.DefaultSensorStabilizationTimeout.TotalMinutes; set => _calibrationDefaults.DefaultSensorStabilizationTimeout = TimeSpan.FromMinutes(Math.Clamp(value, 1, 1440)); }
    public string CalibrationDefaultsStatus { get => _calibrationDefaultsStatus; private set => SetProperty(ref _calibrationDefaultsStatus, value); }
    public RelayCommand SaveCalibrationDefaultsCommand { get; }

    private void SaveCalibrationDefaults()
    {
        _defaultsStore.Save(_calibrationDefaults);
        CalibrationDefaultsStatus = $"Uložené {DateTime.Now:HH:mm}. Interval odberu aj trace logu sa použijú pri každom novom behu; ostatné predvoľby pri novom zapojení.";
    }
}
