using System.Text.Json;
using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationSetupCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "setup-catalog-" + Guid.NewGuid());
    private readonly Guid _profile = Guid.NewGuid(), _chamber = Guid.NewGuid();
    public CalibrationSetupCatalogTests() => Directory.CreateDirectory(_root);
    private CalibrationSetup Setup() => new()
    {
        ProfileId = _profile, ChamberId = _chamber,
        Mappings = [new() { PeakLoggerDeviceSerialNumber = "DEVICE", Channel = "1.3", PeakId = "P1", PeakIndex = 1,
            SerialNumber = "291877/0001", ChannelSerialNumber = "291877/0001", SensorName = "SC-01/T", Selected = true }]
    };
    [Fact] public void LegacyIdsResolveNamesAndBackupsRemainDistinct()
    {
        var store = new CalibrationStore(_root); var setup = Setup();
        store.SaveSetup(setup); store.SaveSetup(setup);
        var entries = CalibrationSetupCatalog.Read(store.SetupsDirectory, new Dictionary<Guid, string> { [_profile] = "Teplotný profil" }, new Dictionary<Guid, string> { [_chamber] = "VC3 – komora 1" });
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => { Assert.Equal("Teplotný profil", e.Profile); Assert.Equal("VC3 – komora 1", e.Chamber); Assert.Equal("SC-01/T", e.Sensors); Assert.Equal("291877/0001", e.SerialNumbers); Assert.True(e.CanLoad); });
        Assert.Contains(entries, x => x.Kind == "Automatická záloha");
        Assert.Contains(entries, x => x.Kind == "Uložené zapojenie");
    }
    [Fact] public void SavedNamesRemainReadableWithoutLibraryAndExportPreservesUnknownFields()
    {
        string path = Path.Combine(_root, "internal.json"); var setup = Setup();
        setup.ProfileName = "Profil / teplota: 25°C"; setup.ChamberName = "Komora 1";
        setup.SavedAt = new DateTimeOffset(2026, 9, 11, 12, 30, 0, TimeSpan.Zero);
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(setup))!.AsObject();
        node["FutureField"] = "preserved"; File.WriteAllText(path, node.ToJsonString());
        var entry = CalibrationSetupCatalog.ReadFile(path, new Dictionary<Guid, string>(), new Dictionary<Guid, string>());
        Assert.Equal(setup.ProfileName, entry.Profile); Assert.Equal(setup.ChamberName, entry.Chamber);
        Assert.Equal(setup.SavedAt, entry.SavedAt);
        Assert.DoesNotContain('/', entry.SuggestedFileName); Assert.DoesNotContain(':', entry.SuggestedFileName);
        string destination = Path.Combine(_root, entry.SuggestedFileName);
        CalibrationSetupCatalog.ExportCopy(entry, destination);
        Assert.Single(CalibrationWiringImporter.Load(destination));
        Assert.Contains("preserved", File.ReadAllText(destination));
        Assert.Throws<IOException>(() => CalibrationSetupCatalog.ExportCopy(entry, destination));
    }
    [Fact] public void CorruptAndEmptyFilesDoNotHideValidEntries()
    {
        File.WriteAllText(Path.Combine(_root, "bad.json"), "broken");
        File.WriteAllText(Path.Combine(_root, "empty.json"), JsonSerializer.Serialize(new CalibrationSetup()));
        File.WriteAllText(Path.Combine(_root, "valid.json"), JsonSerializer.Serialize(Setup()));
        var entries = CalibrationSetupCatalog.Read(_root, new Dictionary<Guid, string>(), new Dictionary<Guid, string>());
        Assert.Equal(3, entries.Count); Assert.Single(entries, x => x.CanLoad);
        Assert.Equal(2, entries.Count(x => x.Error.Length > 0));
    }
    [Fact] public void ImportBackupKeepsPreviousSavedTimeAndNames()
    {
        var store = new CalibrationStore(_root); var setup = Setup(); setup.ProfileName = "Pôvodný profil";
        store.SaveSetup(setup); var savedAt = setup.SavedAt;
        setup.ProfileName = "Aktuálny profil"; store.SaveImportedSetup(setup);
        var backup = Assert.Single(CalibrationSetupCatalog.Read(store.SetupsDirectory, new Dictionary<Guid, string>(), new Dictionary<Guid, string>()), x => x.Kind == "Záloha pred importom");
        Assert.Equal("Pôvodný profil", backup.Profile); Assert.Equal(savedAt, backup.SavedAt);
    }
    public void Dispose() => Directory.Delete(_root, true);
}
