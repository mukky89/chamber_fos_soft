using System.Text.Json;
using VotschVc3.Core.Recording;

namespace VotschVc3.Core.Thermometers;

public sealed class ExternalHumiditySettings
{
    public bool Enabled { get; set; }
    public string Port { get; set; } = "";
    public double IntervalSeconds { get; set; } = 2;
    public double MaxAgeSeconds { get; set; } = 5;
    public bool UsePeaks { get; set; }
    public string ApiHost { get; set; } = "localhost";
    public int ApiPort { get; set; }
    public List<ExternalPeakKey> Peaks { get; set; } = [];
    public string OutputPath { get; set; } = "";

    public void Validate()
    {
        if (!double.IsFinite(IntervalSeconds) || IntervalSeconds < 1 || IntervalSeconds > 3600)
            throw new ArgumentException("Interval merania musí byť 1 až 3600 s.");
        if (!double.IsFinite(MaxAgeSeconds) || MaxAgeSeconds < 1 || MaxAgeSeconds > 3600)
            throw new ArgumentException("Prípustný vek/odstup musí byť 1 až 3600 s.");
        if (UsePeaks && (string.IsNullOrWhiteSpace(ApiHost) || ApiPort < 1 || ApiPort > 65535))
            throw new ArgumentException("Vyberte konkrétny PeakLogger host a port (1–65535).");
    }
}

/// <summary>Per-chamber file under AppPaths.SettingsDir; corrupt sources are never silently replaced.</summary>
public sealed class ExternalHumiditySettingsStore(string directory)
{
    private string PathFor(Guid chamber) => Path.Combine(directory, $"testo645-{chamber:N}.json");
    public ExternalHumiditySettings Load(Guid chamber)
    {
        string path = PathFor(chamber);
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<ExternalHumiditySettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Neplatné nastavenia Testo 645: " + path);
    }
    public void Save(Guid chamber, ExternalHumiditySettings settings)
    {
        settings.Validate();
        string path = PathFor(chamber);
        if (File.Exists(path)) _ = Load(chamber); // Do not overwrite an unreadable source.
        Directory.CreateDirectory(directory);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
