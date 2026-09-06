using System.IO;
using System.Text.Json;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.Calibration;

/// <summary>Desktop persistence for FBG-calibration options that belong to a physical chamber.</summary>
public sealed class CalibrationDeviceOptionsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path = Path.Combine(AppPaths.SettingsDir, "fbg-calibration-device-options.json");

    public CalibrationDeviceOptions Load(Guid chamberId)
    {
        lock (_gate)
        {
            Dictionary<Guid, CalibrationDeviceOptions> all = LoadAllUnsafe();
            return all.TryGetValue(chamberId, out CalibrationDeviceOptions? options)
                ? options.Normalize()
                : new CalibrationDeviceOptions();
        }
    }

    public void Save(Guid chamberId, CalibrationDeviceOptions options)
    {
        if (chamberId == Guid.Empty) return;
        lock (_gate)
        {
            Dictionary<Guid, CalibrationDeviceOptions> all = LoadAllUnsafe();
            all[chamberId] = options.Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(all, JsonOptions));
        }
    }

    private Dictionary<Guid, CalibrationDeviceOptions> LoadAllUnsafe()
    {
        try
        {
            if (!File.Exists(_path)) return new Dictionary<Guid, CalibrationDeviceOptions>();
            return JsonSerializer.Deserialize<Dictionary<Guid, CalibrationDeviceOptions>>(File.ReadAllText(_path), JsonOptions)
                ?? new Dictionary<Guid, CalibrationDeviceOptions>();
        }
        catch
        {
            return new Dictionary<Guid, CalibrationDeviceOptions>();
        }
    }
}

public sealed class CalibrationDeviceOptions
{
    public bool ControlTemperatureByReference { get; set; } = true;
    public int ReferenceControlConfigurationVersion { get; set; } = 3;
    public double ReferenceControlGain { get; set; } = 0.35;
    public double ReferenceControlDeadbandC { get; set; } = 0.05;
    public double ReferenceControlMaxCorrectionC { get; set; } = 3.0;
    public double ReferenceControlMaxStepC { get; set; } = 0.30;
    public double ReferenceControlResponseDelaySeconds { get; set; } = 120;
    public double ReferenceControlObservationSeconds { get; set; } = 30;

    public CalibrationDeviceOptions Normalize()
    {
        // Version 2 enables the bounded WIKA trim by default. Migrate the previous
        // version once so existing chamber settings receive the new application default;
        // after v2 is stored, an operator can still turn the option off explicitly.
        if (ReferenceControlConfigurationVersion < 2)
        {
            ControlTemperatureByReference = true;
            ReferenceControlConfigurationVersion = 2;
        }

        ReferenceControlGain = Math.Clamp(double.IsFinite(ReferenceControlGain) ? ReferenceControlGain : 0.35, 0.01, 2.0);
        ReferenceControlDeadbandC = Math.Clamp(double.IsFinite(ReferenceControlDeadbandC) ? Math.Abs(ReferenceControlDeadbandC) : 0.05, 0.01, 1.0);
        ReferenceControlMaxCorrectionC = Math.Clamp(double.IsFinite(ReferenceControlMaxCorrectionC) ? Math.Abs(ReferenceControlMaxCorrectionC) : 3.0, 0.1, 10.0);
        ReferenceControlMaxStepC = Math.Clamp(double.IsFinite(ReferenceControlMaxStepC) ? Math.Abs(ReferenceControlMaxStepC) : 0.30, 0.02, 2.0);
        if (ReferenceControlConfigurationVersion < 3)
        {
            ReferenceControlResponseDelaySeconds = 120;
            ReferenceControlObservationSeconds = 30;
            ReferenceControlConfigurationVersion = 3;
        }
        ReferenceControlResponseDelaySeconds = Math.Clamp(double.IsFinite(ReferenceControlResponseDelaySeconds) ? ReferenceControlResponseDelaySeconds : 120, 30, 600);
        ReferenceControlObservationSeconds = Math.Clamp(double.IsFinite(ReferenceControlObservationSeconds) ? ReferenceControlObservationSeconds : 30, 10, 120);
        return this;
    }

    public CalibrationReferenceControlOptions ToCoreOptions() => new(
        ControlTemperatureByReference,
        ReferenceControlGain,
        ReferenceControlDeadbandC,
        ReferenceControlMaxCorrectionC,
        ReferenceControlMaxStepC,
        TimeSpan.FromSeconds(ReferenceControlResponseDelaySeconds),
        TimeSpan.FromSeconds(ReferenceControlObservationSeconds));
}
