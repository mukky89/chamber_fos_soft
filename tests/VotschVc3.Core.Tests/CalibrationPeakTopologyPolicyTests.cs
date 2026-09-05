using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationPeakTopologyPolicyTests
{
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
