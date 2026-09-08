using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Calibration;

/// <summary>Persists the template copied into every newly created calibration setup.</summary>
public sealed class CalibrationDefaultsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public CalibrationDefaultsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }

    public CalibrationProfileSettings Load()
    {
        if (!File.Exists(Path)) return new CalibrationProfileSettings { ChamberEntryEnabled = true };
        try
        {
            string json = File.ReadAllText(Path);
            var settings = JsonSerializer.Deserialize<CalibrationProfileSettings>(json, JsonOptions)
                ?? new CalibrationProfileSettings { ChamberEntryEnabled = true };
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty(nameof(settings.ChamberEntryEnabled), out _))
                settings.ChamberEntryEnabled = true;
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new CalibrationProfileSettings { ChamberEntryEnabled = true };
        }
    }

    /// <summary>New runs always use the current admin sampling cadence; recovery keeps its snapshot.</summary>
    public void ApplyAcquisitionInterval(CalibrationProfileSettings settings, bool preserveRunSettings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (preserveRunSettings) return;
        CalibrationProfileSettings defaults = Load();
        settings.SampleAcquisitionIntervalSeconds = Math.Clamp(defaults.SampleAcquisitionIntervalSeconds, 1, 30);
        settings.WavelengthTraceIntervalSeconds = Math.Clamp(defaults.WavelengthTraceIntervalSeconds, 1, 86400);
    }

    public void Save(CalibrationProfileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
