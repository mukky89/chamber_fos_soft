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
        if (!File.Exists(Path)) return new CalibrationProfileSettings();
        try
        {
            return JsonSerializer.Deserialize<CalibrationProfileSettings>(File.ReadAllText(Path), JsonOptions)
                ?? new CalibrationProfileSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new CalibrationProfileSettings();
        }
    }

    /// <summary>New runs always use the current admin sampling cadence; recovery keeps its snapshot.</summary>
    public void ApplyAcquisitionInterval(CalibrationProfileSettings settings, bool preserveRunSettings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (preserveRunSettings) return;
        settings.SampleAcquisitionIntervalSeconds = Math.Clamp(Load().SampleAcquisitionIntervalSeconds, 1, 30);
    }

    public void Save(CalibrationProfileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
