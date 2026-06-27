using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

public class ProfileExporterTests
{
    private static TestProfile Sample() => new()
    {
        Name = "Round trip",
        Kind = ChamberKind.TemperatureHumidity,
        Cycles = 2,
        Segments =
        {
            new ProfileSegment { Name = "Ohrev", TargetTemperature = 60, TargetHumidity = 80, Duration = TimeSpan.FromMinutes(30), IsRamp = true },
            new ProfileSegment { Name = "Plato", TargetTemperature = 60, TargetHumidity = 80, Duration = TimeSpan.FromMinutes(60), IsRamp = false },
        },
    };

    [Fact]
    public void Csv_export_reimports_to_equivalent_segments()
    {
        string csv = ProfileExporter.ToCsv(Sample());

        ProfileImportResult result = ProfileImporter.Import(csv, ChamberKind.TemperatureHumidity);

        Assert.Equal(2, result.Profile.Segments.Count);
        ProfileSegment first = result.Profile.Segments[0];
        Assert.Equal(60.0, first.TargetTemperature);
        Assert.Equal(80.0, first.TargetHumidity);
        Assert.Equal(TimeSpan.FromMinutes(30), first.Duration);
        Assert.True(first.IsRamp);
        Assert.False(result.Profile.Segments[1].IsRamp);
    }

    [Fact]
    public void Json_export_reimports_with_cycles_preserved()
    {
        string json = ProfileExporter.ToJson(Sample());

        ProfileImportResult result = ProfileImporter.Import(json, ChamberKind.TemperatureHumidity);

        Assert.Equal(2, result.Profile.Cycles);
        Assert.Equal(2, result.Profile.Segments.Count);
        Assert.Equal("Round trip", result.Profile.Name);
    }
}
