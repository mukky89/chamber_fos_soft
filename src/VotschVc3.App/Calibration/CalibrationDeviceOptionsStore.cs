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
                : new CalibrationDeviceOptions { ReferenceControlConfigurationVersion = 1 };
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
    public bool ControlTemperatureByReference { get; set; }
    public int ReferenceControlConfigurationVersion { get; set; }
    public double ReferenceControlGain { get; set; } = 0.35;
    public double ReferenceControlDeadbandC { get; set; } = 0.05;
    public double ReferenceControlMaxCorrectionC { get; set; } = 3.0;
    public double ReferenceControlMaxStepC { get; set; } = 0.30;
    public double ReferenceControlIntervalSeconds { get; set; } = 10;

    public CalibrationDeviceOptions Normalize()
    {
        // Version 1 makes chamber-internal control the safe default. Existing files
        // created before this migration may contain an automatically enabled WIKA
        // outer loop, so require the operator to opt in again explicitly.
        if (ReferenceControlConfigurationVersion < 1)
        {
            ControlTemperatureByReference = false;
            ReferenceControlConfigurationVersion = 1;
        }

        ReferenceControlGain = Math.Clamp(double.IsFinite(ReferenceControlGain) ? ReferenceControlGain : 0.35, 0.01, 2.0);
        ReferenceControlDeadbandC = Math.Clamp(double.IsFinite(ReferenceControlDeadbandC) ? Math.Abs(ReferenceControlDeadbandC) : 0.05, 0.01, 1.0);
        ReferenceControlMaxCorrectionC = Math.Clamp(double.IsFinite(ReferenceControlMaxCorrectionC) ? Math.Abs(ReferenceControlMaxCorrectionC) : 3.0, 0.1, 10.0);
        ReferenceControlMaxStepC = Math.Clamp(double.IsFinite(ReferenceControlMaxStepC) ? Math.Abs(ReferenceControlMaxStepC) : 0.30, 0.02, 2.0);
        ReferenceControlIntervalSeconds = Math.Clamp(double.IsFinite(ReferenceControlIntervalSeconds) ? ReferenceControlIntervalSeconds : 10, 2, 120);
        return this;
    }

    public CalibrationReferenceControlOptions ToCoreOptions() => new(
        ControlTemperatureByReference,
        ReferenceControlGain,
        ReferenceControlDeadbandC,
        ReferenceControlMaxCorrectionC,
        ReferenceControlMaxStepC,
        TimeSpan.FromSeconds(ReferenceControlIntervalSeconds));
}
