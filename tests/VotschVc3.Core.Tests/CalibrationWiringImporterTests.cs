using System.Text.Json;
using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationWiringImporterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "wiring-import-" + Guid.NewGuid());
    public CalibrationWiringImporterTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);
    private static CalibrationSensorMapping Mapping() => new()
    {
        PeakLoggerDeviceSerialNumber = "LOGGER", Channel = "1.3", PeakId = "P2", PeakIndex = 2,
        SerialNumber = "CHAIN1/0001", ChannelSerialNumber = "291877/0001", ChainSerialNumber = "CHAIN1/0001",
        Selected = true, Notes = "Poznámka; nový riadok\nďalší", SensorName = "SC-01/T", ProductDescription = "Produkt",
        Customer = "Zákazník", Order = "104011", CurrentWavelengthNm = 1531.661, NominalWavelengthNm = 1531.67,
        StabilizationTimeoutOverride = TimeSpan.FromMinutes(42)
    };
    [Fact]
    public void ReadsActualSetupFileAndBackupIncludingMetadataAndChain()
    {
        var store = new CalibrationStore(_directory);
        var setup = new CalibrationSetup { ProfileId = Guid.NewGuid(), ChamberId = Guid.NewGuid(), Mappings = [Mapping()] };
        store.SaveSetup(setup); store.SaveSetup(setup);
        foreach (string path in Directory.GetFiles(store.SetupsDirectory))
        {
            var row = Assert.Single(CalibrationWiringImporter.Load(path));
            Assert.Equal("CHAIN1/0001", row.SerialNumber);
            Assert.Equal("291877/0001", row.ChannelSerialNumber);
            Assert.Equal("SC-01/T", row.SensorName);
            Assert.Equal(TimeSpan.FromMinutes(42), row.StabilizationTimeoutOverride);
        }
    }
    [Fact]
    public void ReadsWorkbookProducedByApplicationExporter()
    {
        var setup = new CalibrationSetup { Mappings = [Mapping()] };
        CalibrationWiringExporter.Export(_directory, new CalibrationRunRecord(), setup);
        var actual = Assert.Single(CalibrationWiringImporter.Load(Path.Combine(_directory, CalibrationWiringExporter.FileName)));
        Assert.Equal(setup.Mappings[0].SourceIdentity, actual.SourceIdentity);
        Assert.Equal(setup.Mappings[0].SerialNumber, actual.SerialNumber);
        Assert.Equal(setup.Mappings[0].Notes, actual.Notes);
        Assert.Equal(setup.Mappings[0].CurrentWavelengthNm, actual.CurrentWavelengthNm);
        Assert.Equal(setup.Mappings[0].NominalWavelengthNm, actual.NominalWavelengthNm);
        Assert.Equal(setup.Mappings[0].Order, actual.Order);
        Assert.True(actual.Selected);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Mappings\":[]}")]
    [InlineData("{\"Mappings\":[null]}")]
    [InlineData("{\"Mappings\":[{\"Channel\":\"1.3\",\"PeakId\":\"P1\"}]}")]
    public void RejectsMissingOrUnidentifiableWiring(string json)
    {
        string path = Path.Combine(_directory, "invalid.json"); File.WriteAllText(path, json);
        Assert.Throws<InvalidDataException>(() => CalibrationWiringImporter.Load(path));
    }
    [Fact]
    public void RejectsDuplicatesAndContradictorySerials()
    {
        Assert.Throws<InvalidDataException>(() => CalibrationWiringImporter.Validate([Mapping(), Mapping()]));
        var invalid = Mapping(); invalid.SerialNumber = "OTHER1/0001";
        Assert.Throws<InvalidDataException>(() => CalibrationWiringImporter.Validate([invalid]));
    }
    [Fact]
    public void ReadsCheckpointMappingsWithoutImportingRunState()
    {
        string path = Path.Combine(_directory, "checkpoint.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new CalibrationCheckpoint { RunId = Guid.NewGuid(), Mappings = [Mapping()] }));
        Assert.Equal("CHAIN1/0001", Assert.Single(CalibrationWiringImporter.Load(path)).SerialNumber);
    }
    [Fact]
    public void ImportBackupSurvivesLaterAutomaticSaves()
    {
        var store = new CalibrationStore(_directory);
        var setup = new CalibrationSetup { ProfileId = Guid.NewGuid(), ChamberId = Guid.NewGuid(), Mappings = [Mapping()] };
        store.SaveSetup(setup);
        setup.Mappings[0].Notes = "Imported";
        string backup = store.SaveImportedSetup(setup)!;
        store.SaveSetup(setup); store.SaveSetup(setup);
        Assert.Equal(Mapping().Notes, Assert.Single(CalibrationWiringImporter.Load(backup)).Notes);
        Assert.Equal("Imported", Assert.Single(store.LoadSetup(setup.ProfileId, setup.ChamberId)!.Mappings).Notes);
    }
    [Fact]
    public void ReplacementPreservesCurrentSettingsAndClearsUnlistedLiveAssignmentsWithoutMutatingInputs()
    {
        var original = Mapping(); original.PeakId = "P1";
        var imported = Mapping();
        var current = new CalibrationSetup
        {
            ProfileId = Guid.NewGuid(), ChamberId = Guid.NewGuid(), Mappings = [original],
            IgnoredPeakLoggerChannels = ["2.1"], CalibrationSegmentIndices = [2, 4],
            Settings = new() { RequiredStableSamples = 73, ChamberToleranceC = 0.25 }
        };
        var next = CalibrationWiringImporter.PrepareReplacement(current, [imported], [original]);
        Assert.Equal(current.ProfileId, next.ProfileId); Assert.Equal(current.ChamberId, next.ChamberId);
        Assert.Equal(73, next.Settings.RequiredStableSamples); Assert.Equal(0.25, next.Settings.ChamberToleranceC);
        Assert.Equal(current.CalibrationSegmentIndices, next.CalibrationSegmentIndices);
        Assert.Equal(current.IgnoredPeakLoggerChannels, next.IgnoredPeakLoggerChannels);
        var unlisted = Assert.Single(next.Mappings, m => m.PeakId == "P1");
        Assert.False(unlisted.Selected); Assert.Empty(unlisted.SerialNumber); Assert.Empty(unlisted.ChainSerialNumber);
        Assert.True(original.Selected); Assert.Equal("CHAIN1/0001", original.SerialNumber);
        next.Mappings[0].Notes = "Edited after import";
        Assert.NotEqual(next.Mappings[0].Notes, imported.Notes);
    }
}
