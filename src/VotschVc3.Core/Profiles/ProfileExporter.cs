using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VotschVc3.Core.Profiles;

/// <summary>
/// Serialises a <see cref="TestProfile"/> to disk. The CSV format is the
/// counterpart of <see cref="ProfileImporter"/> (segment table), so a profile
/// can be exported and re-imported without loss.
/// </summary>
public static class ProfileExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Header written by <see cref="ToCsv"/> (recognised by the importer).</summary>
    public const string CsvHeader = "Name;Dauer [min];Temperatur [°C];Feuchte [%];Art";

    /// <summary>Renders the profile as a semicolon separated segment table.</summary>
    public static string ToCsv(TestProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var sb = new StringBuilder();
        sb.AppendLine(CsvHeader);
        foreach (ProfileSegment segment in profile.Segments)
        {
            string humidity = segment.TargetHumidity?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty;
            string art = segment.IsRamp ? "Rampa" : "Halten";
            sb.Append(Escape(segment.Name)).Append(';')
              .Append(segment.Duration.TotalMinutes.ToString("0.###", CultureInfo.InvariantCulture)).Append(';')
              .Append(segment.TargetTemperature.ToString("0.0", CultureInfo.InvariantCulture)).Append(';')
              .Append(humidity).Append(';')
              .Append(art)
              .Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Renders the profile as indented JSON (lossless, app native format).</summary>
    public static string ToJson(TestProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return JsonSerializer.Serialize(profile, JsonOptions);
    }

    /// <summary>Writes the profile to a file, choosing CSV or JSON by extension.</summary>
    public static void ExportFile(TestProfile profile, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string content = Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? ToJson(profile)
            : ToCsv(profile);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Escape(string name) => name.Replace(';', ',');
}
