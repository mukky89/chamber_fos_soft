using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class TemperatureStabilityDetectorTests
{
    [Fact]
    public void Minus40Point322_IsAcceptedByPaliStyleGateInsideHalfDegreeTolerance()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromMinutes(1),
            toleranceC: 0.5,
            maxDriftCPerMinute: 0.1);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        StabilityMetrics metrics = detector.Add(t0, -40.322, target: -40.0);
        for (int second = 1; second <= 65; second++)
        {
            metrics = detector.Add(t0.AddSeconds(second), -40.322, target: -40.0);
        }

        Assert.True(metrics.IsStable);
        Assert.Equal(60, detector.StableScoreSeconds);
        Assert.Equal(60, detector.RequiredStableScoreSeconds);
        Assert.InRange(Math.Abs(metrics.Mean - (-40.0)), 0, 0.5);
        Assert.InRange(detector.LastAverageDeltaC, 0, 0.000001);
        Assert.InRange(detector.LastNormalizedChangeCPerMinute, 0, 0.000001);
    }

    [Fact]
    public void PaliStyleGate_RecoversFromOldOutOfToleranceValueWithoutKeepingRollingHistory()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromMinutes(1),
            toleranceC: 0.5,
            maxDriftCPerMinute: 0.1);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        // Initial baseline is outside tolerance. The first five samples at the new value fail because
        // they are still compared with that old baseline; Pali then moves prev_temp to the new value.
        StabilityMetrics metrics = detector.Add(t0, -40.8, target: -40.0);
        for (int second = 1; second <= 5; second++)
        {
            metrics = detector.Add(t0.AddSeconds(second), -40.322, target: -40.0);
        }
        Assert.False(metrics.IsStable);
        Assert.Equal(0, detector.StableScoreSeconds);

        // From the new baseline the same stable WIKA value is accepted. No old -40.8 sample remains
        // in a rolling all-samples-must-pass window.
        for (int second = 6; second <= 70; second++)
        {
            metrics = detector.Add(t0.AddSeconds(second), -40.322, target: -40.0);
        }

        Assert.True(metrics.IsStable);
        Assert.Equal(60, detector.StableScoreSeconds);
    }

    [Fact]
    public void PaliStyleGate_BadFiveSampleBlockPenalizesByTenInsteadOfResettingEverything()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromMinutes(1),
            toleranceC: 0.5,
            maxDriftCPerMinute: 0.1);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        detector.Add(t0, -40.2, target: -40.0);
        StabilityMetrics metrics = default!;

        // Four good blocks = 20 stability points.
        for (int second = 1; second <= 20; second++)
        {
            metrics = detector.Add(t0.AddSeconds(second), -40.2, target: -40.0);
        }
        Assert.Equal(20, detector.StableScoreSeconds);
        Assert.False(metrics.IsStable);

        // One bad five-sample block subtracts 10 points, exactly like change -= i*2 in Pali.
        for (int second = 21; second <= 25; second++)
        {
            metrics = detector.Add(t0.AddSeconds(second), -40.7, target: -40.0);
        }

        Assert.False(metrics.IsStable);
        Assert.Equal(10, detector.StableScoreSeconds);
        Assert.Equal(TimeSpan.FromSeconds(10), metrics.WindowDuration);
    }

    [Fact]
    public void PaliStyleGate_RejectsRealTemperatureMovementEvenWhenLatestValueIsInsideTolerance()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromMinutes(1),
            toleranceC: 0.5,
            maxDriftCPerMinute: 0.1);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        StabilityMetrics metrics = detector.Add(t0, -40.30, target: -40.0);
        for (int second = 1; second <= 60; second++)
        {
            // 0.2 °C/min linear change. Values stay inside ±0.5 °C, but Pali's five-sample
            // baseline-change test must keep rejecting the block.
            double value = -40.30 + (0.2 * second / 60.0);
            metrics = detector.Add(t0.AddSeconds(second), value, target: -40.0);
        }

        Assert.False(metrics.IsStable);
        Assert.Equal(0, detector.StableScoreSeconds);
        Assert.True(detector.LastNormalizedChangeCPerMinute > 0.1);
        Assert.True(Math.Abs(metrics.SlopePerMinute) > 0.1);
    }

    [Fact]
    public void StableDuration_UsesRealElapsedTimeWhenWikaSamplesAreSlowerThanOneHertz()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromMinutes(1), toleranceC: 0.5, maxDriftCPerMinute: 0.1);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        StabilityMetrics metrics = detector.Add(t0, -40.02, target: -40.0);
        for (int sample = 1; sample <= 20; sample++)
            metrics = detector.Add(t0.AddSeconds(sample * 3), -40.02, target: -40.0);

        Assert.True(metrics.IsStable);
        Assert.Equal(60, detector.StableScoreSeconds);
        Assert.Equal(TimeSpan.FromSeconds(60), metrics.WindowDuration);
    }
}
