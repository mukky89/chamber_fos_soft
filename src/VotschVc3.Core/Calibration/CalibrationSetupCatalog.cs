using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Calibration;

public sealed record CalibrationSetupCatalogEntry(string Path, string Chamber, string Profile,
    DateTimeOffset SavedAt, string Kind, int PeakCount, int SelectedCount, string Sensors,
    string SerialNumbers, string Error)
{
    public bool CanLoad => Error.Length == 0 && PeakCount > 0;
    public string SavedText => SavedAt == DateTimeOffset.MinValue ? "Dátum neznámy" : SavedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    public string CountText => $"{PeakCount} peakov / {SelectedCount} vybraných";
    public string Description => $"Komora: {Chamber}\nProfil: {Profile}\n{Kind} · {SavedText}\n{CountText}\nSnímače: {Sensors}\nSN: {SerialNumbers}";
    public string SearchText => $"{Chamber} {Profile} {Sensors} {SerialNumbers} {Kind} {SavedText}";
    public string SuggestedFileName => $"Zapojenie - {Safe(Chamber)} - {Safe(Profile)} - {SavedAt.ToLocalTime():yyyy-MM-dd_HH-mm-ss}.json";
    private static string Safe(string value)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
        string safe = new(value.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
        return safe.Trim().TrimEnd('.').Substring(0, Math.Min(safe.Trim().TrimEnd('.').Length, 55));
    }
}

public static class CalibrationSetupCatalog
{
    public static void ExportCopy(CalibrationSetupCatalogEntry entry, string destination)
    {
        if (!entry.CanLoad) throw new InvalidDataException("Vybrané zapojenie nie je platné.");
        // Preserve unknown JSON members while adding names so the copy remains understandable on another PC.
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(entry.Path))!.AsObject();
        document["ProfileName"] = entry.Profile; document["ChamberName"] = entry.Chamber;
        document["SavedAt"] = entry.SavedAt;
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.Write(document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };
    public static IReadOnlyList<CalibrationSetupCatalogEntry> Read(string directory,
        IReadOnlyDictionary<Guid, string> profiles, IReadOnlyDictionary<Guid, string> chambers)
    {
        if (!Directory.Exists(directory)) return [];
        var paths = Directory.EnumerateFiles(directory).Where(IsSetupFile).ToList();
        string backups = Path.Combine(directory, "ImportBackups");
        if (Directory.Exists(backups)) paths.AddRange(Directory.EnumerateFiles(backups).Where(IsSetupFile));
        return paths.Select(path => ReadFile(path, profiles, chambers))
            .OrderByDescending(x => x.SavedAt).ThenBy(x => x.Chamber).ToArray();
    }
    private static bool IsSetupFile(string path) => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
    public static CalibrationSetupCatalogEntry ReadFile(string path, IReadOnlyDictionary<Guid, string> profiles,
        IReadOnlyDictionary<Guid, string> chambers)
    {
        string kind = path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ? "Automatická záloha" :
            Path.GetFileName(Path.GetDirectoryName(path)) == "ImportBackups" ? "Záloha pred importom" : "Uložené zapojenie";
        DateTimeOffset modified = DateTimeOffset.MinValue;
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException("Súbor už nie je dostupný.", path);
            modified = file.LastWriteTimeUtc;
            if (file.Length > 20 * 1024 * 1024) throw new InvalidDataException("Súbor prekračuje 20 MB.");
            var setup = JsonSerializer.Deserialize<CalibrationSetup>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Prázdny JSON.");
            string profile = !string.IsNullOrWhiteSpace(setup.ProfileName) ? setup.ProfileName : profiles.GetValueOrDefault(setup.ProfileId) ?? "Profil už nie je v knižnici";
            string chamber = !string.IsNullOrWhiteSpace(setup.ChamberName) ? setup.ChamberName : chambers.GetValueOrDefault(setup.ChamberId) ??
                (setup.ChamberId == Guid.Empty ? "Staršie zapojenie bez priradenej komory" : "Komora už nie je v zozname");
            string error = "";
            try { CalibrationWiringImporter.Validate(setup.Mappings); }
            catch (Exception ex) when (ex is InvalidDataException or NullReferenceException) { error = ex.Message; }
            var mappings = setup.Mappings?.Where(m => m is not null).ToArray() ?? [];
            string Join(IEnumerable<string?> values) => string.Join(", ", values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
            string sensors = Join(mappings.Select(m => m.SensorName));
            string serials = Join(mappings.Select(m => !string.IsNullOrWhiteSpace(m.SerialNumber) ? m.SerialNumber :
                !string.IsNullOrWhiteSpace(m.ChainSerialNumber) ? m.ChainSerialNumber : CalibrationWiringPersistence.ChannelSerial(m)));
            return new(Path.GetFullPath(path), chamber, profile, setup.SavedAt ?? modified, kind, mappings.Length,
                mappings.Count(m => m.Selected), sensors.Length == 0 ? "Názvy nie sú uložené" : sensors,
                serials.Length == 0 ? "SN nie sú vyplnené" : serials, error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new(Path.GetFullPath(path), "Nedá sa načítať", Path.GetFileName(path), modified, kind, 0, 0, "—", "—", ex.Message);
        }
    }
}
