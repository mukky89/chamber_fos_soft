using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationIgnoredChannelsTests
{
    [Fact]
    public void SettingsPersistPerChamberAndProfileWithoutLosingIgnoredSerials()
    {
        string path = Path.Combine(Path.GetTempPath(), "ignored-channels-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CalibrationStore(path);
            var setup = new CalibrationSetup
            {
                ProfileId = Guid.NewGuid(), ChamberId = Guid.NewGuid(), IgnoredPeakLoggerChannels = ["2.2"],
                Mappings = [new() { Channel = "2.2", SerialNumber = "291877/0001", Selected = true }, new() { Channel = "1.3", Selected = true }],
            };
            store.SaveSetup(setup);
            var loaded = store.LoadSetup(setup.ProfileId, setup.ChamberId)!;
            Assert.Equal("1.3", Assert.Single(loaded.ActiveMappings).Channel);
            Assert.Equal("291877/0001", loaded.Mappings[0].SerialNumber);
            Assert.Null(store.LoadSetup(setup.ProfileId, Guid.NewGuid()));
            Assert.Null(store.LoadSetup(Guid.NewGuid(), setup.ChamberId));
            loaded.IgnoredPeakLoggerChannels.Clear();
            Assert.Equal(2, loaded.ActiveMappings.Count);
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public async Task IgnoredSelectedChannelCannotBlockPreflightWithMissingSensorOrSerial()
    {
        await using var client = new FakePeakLoggerClient();
        await client.ConnectAsync(new PeakLoggerSettings());
        var sensor = (await client.DiscoverSensorsAsync())[0];
        var peak = sensor.Peaks[0];
        var setup = new CalibrationSetup
        {
            IgnoredPeakLoggerChannels = ["external-channel"],
            Mappings = [
                new() { Channel = sensor.Channel, PeakLoggerDeviceSerialNumber = sensor.SerialNumber, PeakId = peak.PeakId, PeakIndex = peak.PeakIndex, SerialNumber = "291877/0001", Selected = true },
                new() { Channel = "external-channel", Selected = true },
            ],
        };
        var orchestrator = new CalibrationOrchestrator(client);
        await orchestrator.PreflightAsync(setup);
        setup.IgnoredPeakLoggerChannels.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.PreflightAsync(setup));
    }

    [Fact]
    public void CheckpointRecoveryDoesNotReenableIgnoredTargets()
    {
        var setup = new CalibrationSetup { IgnoredPeakLoggerChannels = ["2.2"], Mappings = [new() { Channel = "2.2", SerialNumber = "291877/0001", Selected = true }] };
        var checkpoint = new CalibrationCheckpoint
        {
            Mappings = [new() { Channel = "2.2", SerialNumber = "291877/0001", Selected = true }, new() { Channel = "1.3", SerialNumber = "291878/0001", Selected = true }],
        };
        Assert.True(CalibrationCheckpointRecovery.RestoreMappingsIfMissing(setup, checkpoint));
        Assert.Equal("1.3", Assert.Single(setup.ActiveMappings).Channel);
        Assert.Equal(2, setup.Mappings.Count);
    }
}
