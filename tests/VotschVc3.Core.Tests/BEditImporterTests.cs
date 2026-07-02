using System.Buffers.Binary;
using System.Text;
using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>
/// Tests for the reverse-engineered Weiss/Vötsch BEdit (.b01/.b02) importer,
/// using synthetic binaries that follow the observed byte grammar: doubles on a
/// 4-byte lattice, ramp rows (target + duration at +8), lone hold durations and
/// -x/+x tolerance pairs.
/// </summary>
public class BEditImporterTests
{
    private const int TempLabelOffset = 100;
    private const int HumLabelOffset = 1000;

    private static byte[] NewFile(int size = 2048)
    {
        byte[] data = new byte[size];
        Encoding.ASCII.GetBytes("BEdit").CopyTo(data, 4);
        return data;
    }

    private static void PutLabel(byte[] data, int offset, string label) =>
        Encoding.ASCII.GetBytes(label).CopyTo(data, offset);

    private static void PutDouble(byte[] data, int offset, double value)
    {
        Assert.Equal(3, offset % 4); // keep test data on the real files' lattice
        BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan(offset, 8), value);
    }

    private static byte[] BuildTemperatureOnly()
    {
        byte[] data = NewFile();
        PutLabel(data, TempLabelOffset, "Temperature");

        PutDouble(data, 123, -20);   // ramp row: target …
        PutDouble(data, 131, 600);   // … + duration at +8 (10 min)
        PutDouble(data, 143, -5);    // tolerance pair, must be skipped
        PutDouble(data, 151, 5);
        PutDouble(data, 159, -20);   // hold row: target repeated …
        PutDouble(data, 191, 5400);  // … + duration at +32 → plateau (90 min)
        PutDouble(data, 207, 25);    // ramp row to 25
        PutDouble(data, 215, 300);
        return data;
    }

    [Fact]
    public void LooksLikeBEdit_detects_signature()
    {
        Assert.True(BEditImporter.LooksLikeBEdit(BuildTemperatureOnly()));
        Assert.False(BEditImporter.LooksLikeBEdit(Encoding.UTF8.GetBytes(new string('x', 200))));
    }

    [Fact]
    public void Import_decodes_ramp_hold_and_skips_tolerances()
    {
        ProfileImportResult result = BEditImporter.Import(BuildTemperatureOnly(), ChamberKind.TemperatureOnly);
        List<ProfileSegment> segments = result.Profile.Segments;

        Assert.Equal(3, segments.Count);

        Assert.True(segments[0].IsRamp);
        Assert.Equal(-20, segments[0].TargetTemperature, 3);
        Assert.Equal(600, segments[0].Duration.TotalSeconds, 1);

        Assert.False(segments[1].IsRamp);
        Assert.Equal(-20, segments[1].TargetTemperature, 3);
        Assert.Equal(5400, segments[1].Duration.TotalSeconds, 1);

        Assert.True(segments[2].IsRamp);
        Assert.Equal(25, segments[2].TargetTemperature, 3);
        Assert.Equal(300, segments[2].Duration.TotalSeconds, 1);

        Assert.All(segments, s => Assert.Null(s.TargetHumidity));
    }

    [Fact]
    public void Import_ignores_junk_values_between_rows()
    {
        byte[] data = BuildTemperatureOnly();
        PutDouble(data, 199, 1e-300);  // denormal junk from unaligned reads
        PutDouble(data, 231, -1e300);  // absurd magnitude

        ProfileImportResult result = BEditImporter.Import(data, ChamberKind.TemperatureOnly);
        Assert.Equal(3, result.Profile.Segments.Count);
    }

    [Fact]
    public void Import_merges_temperature_and_humidity_channels()
    {
        byte[] data = NewFile();
        PutLabel(data, TempLabelOffset, "Temperature");
        PutDouble(data, 123, 10);
        PutDouble(data, 131, 3600);   // ramp (flat, start==target)
        PutDouble(data, 159, 10);     // hold row: target repeated + duration at +32
        PutDouble(data, 191, 3600);

        PutLabel(data, HumLabelOffset, "Humidity");
        PutDouble(data, 1023, 50);
        PutDouble(data, 1031, 3600);
        PutDouble(data, 1059, 50);    // hold row
        PutDouble(data, 1091, 3600);

        ProfileImportResult result = BEditImporter.Import(data, ChamberKind.TemperatureHumidity);
        List<ProfileSegment> segments = result.Profile.Segments;

        Assert.NotEmpty(segments);
        Assert.All(segments, s => Assert.NotNull(s.TargetHumidity));
        Assert.Equal(7200, segments.Sum(s => s.Duration.TotalSeconds), 1);
        Assert.Equal(10, segments[^1].TargetTemperature, 3);
        Assert.Equal(50, segments[^1].TargetHumidity!.Value, 3);
    }

    [Fact]
    public void Import_strips_humidity_for_temperature_only_chamber()
    {
        byte[] data = NewFile();
        PutLabel(data, TempLabelOffset, "Temperature");
        PutDouble(data, 123, 30);
        PutDouble(data, 131, 600);

        PutLabel(data, HumLabelOffset, "Humidity");
        PutDouble(data, 1023, 40);
        PutDouble(data, 1031, 600);

        ProfileImportResult result = BEditImporter.Import(data, ChamberKind.TemperatureOnly);

        Assert.All(result.Profile.Segments, s => Assert.Null(s.TargetHumidity));
        Assert.Contains(result.Warnings, w => w.Contains("vlhkostný kanál", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportFile_routes_binary_files_to_the_bedit_importer()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bedit_test_{Guid.NewGuid():N}.b01");
        File.WriteAllBytes(path, BuildTemperatureOnly());
        try
        {
            ProfileImportResult result = ProfileImporter.ImportFile(path, ChamberKind.TemperatureOnly);
            Assert.Equal(3, result.Profile.Segments.Count);
            Assert.StartsWith("bedit_test_", result.Profile.Name);
            Assert.Contains("BEdit", result.FormatDescription);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
