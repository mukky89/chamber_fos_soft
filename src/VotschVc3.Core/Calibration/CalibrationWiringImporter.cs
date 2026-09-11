using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClosedXML.Excel;

namespace VotschVc3.Core.Calibration;

/// <summary>Reads app-owned wiring files without importing run state or calibration settings.</summary>
public static class CalibrationWiringImporter
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };
    public static IReadOnlyList<CalibrationSensorMapping> Load(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("Súbor zapojenia neexistuje.", path);
        if (file.Length > 20 * 1024 * 1024) throw new InvalidDataException("Súbor zapojenia je príliš veľký (maximum 20 MB).");
        List<CalibrationSensorMapping> mappings;
        if (file.Extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) mappings = ReadExcel(path);
        else
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("Mappings", out var value) || value.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("JSON neobsahuje zapojenie Mappings. Vyber súbor z Calibration/Setups, checkpoint alebo zapojenie.xlsx z priečinka kalibrácie.");
            if (value.GetArrayLength() > 10000) throw new InvalidDataException("Súbor obsahuje príliš veľa peakov.");
            mappings = value.Deserialize<List<CalibrationSensorMapping>>(Options) ?? [];
        }
        Validate(mappings);
        return mappings;
    }

    public static void Validate(IReadOnlyList<CalibrationSensorMapping> mappings)
    {
        if (mappings.Count is 0 or > 10000) throw new InvalidDataException("Zapojenie musí obsahovať 1 až 10000 peakov.");
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mappings)
        {
            if (m is null || string.IsNullOrWhiteSpace(m.PeakLoggerDeviceSerialNumber) ||
                string.IsNullOrWhiteSpace(m.Channel) || string.IsNullOrWhiteSpace(m.PeakId))
                throw new InvalidDataException("Každý peak musí obsahovať SN interrogátora, kanál a Peak ID. Priradenie nemožno odhadnúť podľa wavelength.");
            if (!identities.Add(m.SourceIdentity)) throw new InvalidDataException($"Duplicitný peak v súbore: {m.Channel} / {m.PeakId} ({m.PeakLoggerDeviceSerialNumber}).");
            if (m.PeakIndex < 0 || m.StabilizationTimeoutOverride is { } timeout && timeout <= TimeSpan.Zero)
                throw new InvalidDataException($"Neplatný index alebo timeout peaku {m.Channel} / {m.PeakId}.");
            string effective = string.IsNullOrWhiteSpace(m.ChainSerialNumber) ? CalibrationWiringPersistence.ChannelSerial(m) : m.ChainSerialNumber;
            if (!string.IsNullOrWhiteSpace(m.SerialNumber) && !string.Equals(effective, m.SerialNumber, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SN snímača nezodpovedá SN kanála/CHAIN: {m.Channel} / {m.PeakId}.");
        }
    }

    public static CalibrationSetup PrepareReplacement(CalibrationSetup current, IReadOnlyList<CalibrationSensorMapping> imported,
        IEnumerable<CalibrationSensorMapping> connected)
    {
        Validate(imported);
        var replacement = JsonSerializer.Deserialize<CalibrationSetup>(JsonSerializer.Serialize(current, Options), Options)!;
        replacement.Mappings = JsonSerializer.Deserialize<List<CalibrationSensorMapping>>(JsonSerializer.Serialize(imported, Options), Options)!;
        var identities = replacement.Mappings.Select(m => m.SourceIdentity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in connected.Where(m => !identities.Contains(m.SourceIdentity)))
            replacement.Mappings.Add(new CalibrationSensorMapping
            {
                PeakLoggerDeviceSerialNumber = row.PeakLoggerDeviceSerialNumber, Channel = row.Channel,
                PeakId = row.PeakId, PeakIndex = row.PeakIndex, CurrentWavelengthNm = row.CurrentWavelengthNm
            });
        return replacement;
    }

    private static List<CalibrationSensorMapping> ReadExcel(string path)
    {
        using var book = new XLWorkbook(path);
        if (!book.Worksheets.TryGetWorksheet("Zapojenie", out var sheet))
            throw new InvalidDataException("Excel neobsahuje hárok Zapojenie exportovaný aplikáciou.");
        var columns = sheet.Row(5).CellsUsed().ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
        string[] required = ["Kalibrovať", "Kanál", "Peak ID", "FBG index", "SN snímača", "SN kanála", "SN CHAIN", "SN interrogátora"];
        foreach (string header in required)
            if (!columns.ContainsKey(header)) throw new InvalidDataException($"V exporte chýba stĺpec {header}.");
        int last = sheet.LastRowUsed()?.RowNumber() ?? 5;
        if (last > 10005) throw new InvalidDataException("Súbor obsahuje príliš veľa riadkov.");
        var result = new List<CalibrationSensorMapping>();
        for (int row = 6; row <= last; row++)
        {
            string Text(string header)
            {
                if (!columns.TryGetValue(header, out int col)) return string.Empty;
                var cell = sheet.Cell(row, col);
                if (cell.HasFormula) throw new InvalidDataException($"Riadok {row}: zapojenie musí obsahovať uložené hodnoty, nie vzorce.");
                return cell.GetString().Trim();
            }
            double? Number(string header)
            {
                string raw = Text(header);
                if (raw.Length == 0) return null;
                if (!double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
                    throw new InvalidDataException($"Riadok {row}: neplatná hodnota {header}.");
                return number;
            }
            if (required.All(h => Text(h).Length == 0)) continue;
            string selected = Text("Kalibrovať");
            if (!selected.Equals("Áno", StringComparison.OrdinalIgnoreCase) && !selected.Equals("Nie", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Riadok {row}: Kalibrovať musí byť Áno alebo Nie.");
            if (!int.TryParse(Text("FBG index"), out int index)) throw new InvalidDataException($"Riadok {row}: neplatný FBG index.");
            result.Add(new CalibrationSensorMapping
            {
                Selected = selected.Equals("Áno", StringComparison.OrdinalIgnoreCase), Channel = Text("Kanál"), PeakId = Text("Peak ID"), PeakIndex = index,
                SerialNumber = Text("SN snímača"), ChannelSerialNumber = Text("SN kanála"), ChainSerialNumber = Text("SN CHAIN"),
                PeakLoggerDeviceSerialNumber = Text("SN interrogátora"), CurrentWavelengthNm = Number("λ pri štarte [nm]"), NominalWavelengthNm = Number("Nominálna λ [nm]"),
                Customer = Text("Zákazník"), Order = Text("Zákazka"), ProductDescription = Text("Popis produktu"), Notes = Text("Poznámky"), SensorName = Text("Názov snímača")
            });
        }
        return result;
    }
}
