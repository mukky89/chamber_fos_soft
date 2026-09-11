using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationWiringPersistenceTests
{
    private static CalibrationSensorMapping Mapping(string channel = "1.3", string sn = "291877/0001") => new()
    {
        PeakLoggerDeviceSerialNumber = "LOGGER", Channel = channel, PeakId = "P1", SerialNumber = sn,
        ChannelSerialNumber = sn, SensorName = "SC-01/T", Selected = true, Notes = "Keep", Order = "Order",
        ProductionFbgType = "T", ProductionFbgTypeDetail = "API peak P1",
        StabilizationTimeoutOverride = TimeSpan.FromMinutes(42)
    };
    [Fact]
    public void AutosaveBeforeDiscoveryAndWithPartialTopologyRetainsWiring()
    {
        var original = new[] { Mapping(), Mapping("2.1", "291878/0001") };
        var empty = CalibrationWiringPersistence.MergeVisibleMappings(original, []);
        Assert.Equal(2, empty.Count);
        var visible = Mapping(); visible.Notes = "Edited";
        var partial = CalibrationWiringPersistence.MergeVisibleMappings(empty, [visible]);
        Assert.Equal(2, partial.Count);
        Assert.Equal("Edited", partial[0].Notes);
        Assert.Equal("291878/0001", partial[1].SerialNumber);
        Assert.Equal("SC-01/T", partial[1].SensorName);
        Assert.True(partial[1].Selected);
        Assert.Equal(TimeSpan.FromMinutes(42), partial[1].StabilizationTimeoutOverride);
    }
    [Fact]
    public void ExplicitVisibleSerialClearAndExplicitResetRemainPossible()
    {
        var cleared = Mapping(sn: "");
        var result = Assert.Single(CalibrationWiringPersistence.MergeVisibleMappings([Mapping()], [cleared]));
        Assert.Empty(result.SerialNumber);
        Assert.Empty(CalibrationWiringPersistence.MergeVisibleMappings([], []));
    }
    [Fact]
    public void IdentityNeverMatchesByWavelengthOrAcrossInterrogators()
    {
        var original = Mapping(); original.CurrentWavelengthNm = 1530;
        var other = Mapping(sn: "OTHER"); other.PeakLoggerDeviceSerialNumber = "OTHER_LOGGER";
        other.CurrentWavelengthNm = 1530;
        Assert.Equal(2, CalibrationWiringPersistence.MergeVisibleMappings([original], [other]).Count);
        other.PeakLoggerDeviceSerialNumber = "logger"; other.CurrentWavelengthNm = 1540;
        Assert.Single(CalibrationWiringPersistence.MergeVisibleMappings([original], [other]));
    }
    [Fact]
    public void LegacySerialRestoresWithoutCopyingChainOverrideToChannel()
    {
        var old = new CalibrationSensorMapping { SerialNumber = "291877/0001" };
        Assert.Equal(old.SerialNumber, CalibrationWiringPersistence.ChannelSerial(old));
        old.ChainSerialNumber = old.SerialNumber;
        Assert.Empty(CalibrationWiringPersistence.ChannelSerial(old));
        old.ChannelSerialNumber = "291878/0001";
        Assert.Equal("291878/0001", CalibrationWiringPersistence.ChannelSerial(old));
    }
    [Fact]
    public void SavedSetupUsesBackupAfterInterruptedWriteAndKeepsChamberScope()
    {
        string root = Path.Combine(Path.GetTempPath(), "wiring-recovery-" + Guid.NewGuid());
        try
        {
            var store = new CalibrationStore(root);
            var setup = new CalibrationSetup { ProfileId = Guid.NewGuid(), ChamberId = Guid.NewGuid(), Mappings = [Mapping()] };
            store.SaveSetup(setup);
            store.SaveSetup(setup);
            string path = Assert.Single(Directory.GetFiles(store.SetupsDirectory, "*.json"));
            Assert.True(File.Exists(path + ".bak"));
            File.WriteAllText(path, "{broken");
            var restored = new CalibrationStore(root).LoadSetup(setup.ProfileId, setup.ChamberId)!;
            Assert.Equal("291877/0001", Assert.Single(restored.Mappings).SerialNumber);
            Assert.Equal("T", restored.Mappings[0].ProductionFbgType);
            Assert.Equal("API peak P1", restored.Mappings[0].ProductionFbgTypeDetail);
            Assert.Null(store.LoadSetup(setup.ProfileId, Guid.NewGuid()));
            restored.Mappings = CalibrationWiringPersistence.MergeVisibleMappings(restored.Mappings, []);
            store.SaveSetup(restored);
            Assert.Single(store.LoadSetup(setup.ProfileId, setup.ChamberId)!.Mappings);
        }
        finally { Directory.Delete(root, true); }
    }
}
