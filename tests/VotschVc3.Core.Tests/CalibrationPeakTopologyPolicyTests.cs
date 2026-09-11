using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationPeakTopologyPolicyTests
{
    [Theory]
    [InlineData(true, false, "", false, true, false)]
    [InlineData(true, false, "291877/0001", false, false, true)]
    [InlineData(true, true, "", false, false, true)]
    [InlineData(false, false, "", false, false, false)]
    [InlineData(true, false, "", true, false, false)]
    public void MissingNoiseCanBeDiscardedButAssignedSelectedAndRunningPeaksAreProtected(
        bool disconnected, bool selected, string serial, bool running, bool removable, bool reconnect)
    {
        Assert.Equal(removable, CalibrationPeakTopologyPolicy.CanDiscardMissingPeak(disconnected, selected, serial, running));
        Assert.Equal(reconnect, CalibrationPeakTopologyPolicy.RequiresReconnect(disconnected, selected, serial));
    }

    [Fact]
    public void EquivalentTopology_IgnoresMissingInterrogatorSerialAfterResume()
    {
        string[] live = ["|1.1|P1", "|1.1|P2", "|2.4|P1"];
        string[] displayed = ["289594|1.1|P1", "289594|1.1|P2", "289594|2.4|P1"];

        Assert.True(PeakTopologyComparer.AreEquivalent(live, displayed));
    }

    [Fact]
    public void EquivalentTopology_StillDetectsRealPeakChange()
    {
        string[] live = ["|1.1|P1", "|1.1|P3"];
        string[] displayed = ["289594|1.1|P1", "289594|1.1|P2"];

        Assert.False(PeakTopologyComparer.AreEquivalent(live, displayed));
    }

    [Fact]
    public void EquivalentTopology_IgnoresRefreshedInterrogatorIdentifier()
    {
        string[] beforeRefresh = ["logger-old|1.3|P1", "logger-old|2.3|P1"];
        string[] afterRefresh = ["logger-new|1.3|P1", "logger-new|2.3|P1"];

        Assert.True(PeakTopologyComparer.AreEquivalent(afterRefresh, beforeRefresh));
    }

    [Fact]
    public void ActiveRun_DoesNotMaterializeUnknownLiveSources()
    {
        IReadOnlyList<string> result = CalibrationPeakTopologyPolicy.SelectNewSources(
            new[] { "logger|1.1|P1" },
            new[] { "logger|1.1|P1", "logger|1.2|P1" },
            calibrationIsRunning: true);

        Assert.Empty(result);
    }

    [Fact]
    public void IdleWorkspace_AddsEachUnknownSourceOnceCaseInsensitively()
    {
        IReadOnlyList<string> result = CalibrationPeakTopologyPolicy.SelectNewSources(
            new[] { "logger|1.1|P1" },
            new[] { "LOGGER|1.1|p1", "logger|1.2|P1", "LOGGER|1.2|p1" },
            calibrationIsRunning: false);

        Assert.Equal(new[] { "logger|1.2|P1" }, result);
    }
}
