using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

public class ProfileImporterTests
{
    [Fact]
    public void Imports_segment_table_with_german_header()
    {
        string csv = string.Join('\n',
            "Name;Dauer [min];Temperatur [°C];Feuchte [%];Art",
            "Ohrev;30;60,0;80;Rampe",
            "Plato;60;60,0;80;Halten");

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
    public void Imports_setpoint_timeline_with_hms_times()
    {
        string csv = string.Join('\n',
            "Zeit;Temperatur;Feuchte",
            "00:00:00;25;50",
            "01:00:00;85;50",
            "02:30:00;85;20");

        ProfileImportResult result = ProfileImporter.Import(csv, ChamberKind.TemperatureHumidity);

        Assert.Equal(2, result.Profile.Segments.Count);
        Assert.Equal(TimeSpan.FromMinutes(60), result.Profile.Segments[0].Duration);
        Assert.Equal(85.0, result.Profile.Segments[0].TargetTemperature);
        Assert.Equal(TimeSpan.FromMinutes(90), result.Profile.Segments[1].Duration);
        Assert.Equal(20.0, result.Profile.Segments[1].TargetHumidity);
    }

    [Fact]
    public void Temperature_only_chamber_drops_humidity()
    {
        string csv = string.Join('\n',
            "Dauer;Temperatur;Feuchte",
            "30;60;80");

        ProfileImportResult result = ProfileImporter.Import(csv, ChamberKind.TemperatureOnly);

        Assert.All(result.Profile.Segments, s => Assert.Null(s.TargetHumidity));
        Assert.Contains(result.Warnings, w => w.Contains("vlhkost", StringComparison.OrdinalIgnoreCase)
            || w.Contains("vlhkos", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Imports_headerless_positional_table()
    {
        string csv = string.Join('\n', "30;60;80", "60;55;");

        ProfileImportResult result = ProfileImporter.Import(csv, ChamberKind.TemperatureHumidity);

        Assert.Equal(2, result.Profile.Segments.Count);
        Assert.Equal(60.0, result.Profile.Segments[0].TargetTemperature);
        Assert.Equal(80.0, result.Profile.Segments[0].TargetHumidity);
        Assert.Null(result.Profile.Segments[1].TargetHumidity);
    }

    [Fact]
    public void Imports_own_json_round_trip()
    {
        var original = new TestProfile
        {
            Name = "JSON test",
            Kind = ChamberKind.TemperatureHumidity,
            Cycles = 2,
            Segments = { new ProfileSegment { TargetTemperature = 40, TargetHumidity = 60, Duration = TimeSpan.FromMinutes(15), IsRamp = true } },
        };
        string json = System.Text.Json.JsonSerializer.Serialize(original);

        ProfileImportResult result = ProfileImporter.Import(json, ChamberKind.TemperatureHumidity);

        Assert.Single(result.Profile.Segments);
        Assert.Equal(40.0, result.Profile.Segments[0].TargetTemperature);
        Assert.Equal(2, result.Profile.Cycles);
    }
}
