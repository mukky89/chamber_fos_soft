using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class TemperatureStabilityBandTests
{
    [Theory]
    [InlineData(-40, 0.5, -40.5, -39.5)]
    [InlineData(25, -0.1, 24.9, 25.1)]
    public void AroundCreatesInclusiveLimits(double target, double tolerance, double lower, double upper)
    {
        TemperatureStabilityBand band = TemperatureStabilityBand.Around(target, tolerance);
        Assert.Equal(lower, band.LowerC, 6);
        Assert.Equal(upper, band.UpperC, 6);
    }
}
