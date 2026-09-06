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
    public void ContinuousGate_RecoversFromOutOfToleranceValueWithFreshWindow()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromMinutes(1),
            toleranceC: 0.5,
            maxDriftCPerMinute: 0.1);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        // Initial value is outside tolerance. The first following valid sample starts a fresh dwell.
        StabilityMetrics metrics = detector.Add(t0, -40.8, target: -40.0);
        for (int second = 1; second <= 5; second++)
        {
            metrics = detector.Add(t0.AddSeconds(second), -40.322, target: -40.0);
        }
        Assert.False(metrics.IsStable);
        Assert.Equal(4, detector.StableScoreSeconds);

        // No old -40.8 sample remains in the continuous all-samples-must-pass window.
        for (int second = 6; second <= 70; second++)
        {
            metrics = detector.Add(t0.AddSeconds(second), -40.322, target: -40.0);
        }

        Assert.True(metrics.IsStable);
        Assert.Equal(60, detector.StableScoreSeconds);
    }

    [Fact]
    public void ContinuousGate_BadSampleResetsTheCompleteDwell()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromMinutes(1),
            toleranceC: 0.5,
            maxDriftCPerMinute: 0.1);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        detector.Add(t0, -40.2, target: -40.0);
        StabilityMetrics metrics = default!;

        // Twenty seconds of valid continuous stability.
        for (int second = 1; second <= 20; second++)
        {
            metrics = detector.Add(t0.AddSeconds(second), -40.2, target: -40.0);
        }
        Assert.Equal(20, detector.StableScoreSeconds);
        Assert.False(metrics.IsStable);

        metrics = detector.Add(t0.AddSeconds(21), -40.7, target: -40.0);

        Assert.False(metrics.IsStable);
        Assert.Equal(0, detector.StableScoreSeconds);
        Assert.Equal(TimeSpan.Zero, metrics.WindowDuration);
    }

    [Fact]
    public void ContinuousGate_RejectsRealTemperatureMovementEvenWhenLatestValueIsInsideTolerance()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromMinutes(1),
            toleranceC: 0.5,
            maxDriftCPerMinute: 0.1);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        StabilityMetrics metrics = detector.Add(t0, -40.30, target: -40.0);
        for (int second = 1; second <= 60; second++)
        {
            // 0.2 °C/min linear change stays inside ±0.5 °C but must keep resetting the dwell.
            double value = -40.30 + (0.2 * second / 60.0);
            metrics = detector.Add(t0.AddSeconds(second), value, target: -40.0);
        }

        Assert.False(metrics.IsStable);
        Assert.Equal(0, detector.StableScoreSeconds);
    }

    [Fact]
    public void Drift_IsRecalculatedFromRealSampleTimesAndTemperatures()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromMinutes(5), toleranceC: 1, maxDriftCPerMinute: 0,
            maxRangeC: 0, maxStdDevC: 0);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        detector.Add(t0, 20.000, target: 20);
        StabilityMetrics metrics = detector.Add(t0.AddSeconds(30), 20.025, target: 20);

        Assert.Equal(0.05, metrics.SlopePerMinute, precision: 10);
        Assert.Equal(0.05, detector.LastNormalizedChangeCPerMinute, precision: 10);
    }

    [Fact]
    public void ShortTermDrift_RejectsCurrentMovementWhenWholeWindowTrendCancelsOut()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromMinutes(20), toleranceC: 1, maxDriftCPerMinute: 0.03,
            maxRangeC: 0, maxStdDevC: 0);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        for (int second = 0; second <= 480; second += 10)
            detector.Add(t0.AddSeconds(second), 20.16 - (0.02 * second / 60d), target: 20);

        StabilityMetrics metrics = default!;
        for (int second = 490; second <= 600; second += 10)
            metrics = detector.Add(t0.AddSeconds(second), 20 + (0.06 * (second - 480) / 60d), target: 20);

        Assert.False(metrics.IsStable);
        Assert.Equal(0, detector.StableScoreSeconds);
        Assert.True(Math.Abs(metrics.SlopePerMinute) > 0.03);
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

    [Fact]
    public void ContinuousGate_RejectsSlowMonotonicRiseThatExceedsWindowRange()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromMinutes(1), toleranceC: 0.5, maxDriftCPerMinute: 0.1,
            maxRangeC: 0.1, maxStdDevC: 0.03);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        StabilityMetrics metrics = detector.Add(t0, 0.05, target: 0);
        for (int second = 1; second <= 120; second++)
            metrics = detector.Add(t0.AddSeconds(second), 0.05 + second * 0.002, target: 0);

        Assert.False(metrics.IsStable);
        Assert.True(detector.StableScoreSeconds < detector.RequiredStableScoreSeconds);
    }

    [Fact]
    public void ContinuousGate_StartsFreshWindowAfterDisturbance()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromSeconds(10), toleranceC: 0.2, maxDriftCPerMinute: 0.02,
            maxRangeC: 0.1, maxStdDevC: 0.03);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        for (int second = 0; second <= 8; second++)
            detector.Add(t0.AddSeconds(second), 0.01, target: 0);
        detector.Add(t0.AddSeconds(9), 0.3, target: 0);

        StabilityMetrics metrics = default!;
        for (int second = 10; second <= 20; second++)
            metrics = detector.Add(t0.AddSeconds(second), 0.01, target: 0);

        Assert.True(metrics.IsStable);
        Assert.Equal(10, detector.StableScoreSeconds);
    }

    [Fact]
    public void DisplayedStableDuration_TracksContinuousWindow()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromMinutes(10), toleranceC: 0.5, maxDriftCPerMinute: 0.1);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        detector.Add(t0, -40.02, target: -40.0);
        StabilityMetrics firstSecond = detector.Add(t0.AddSeconds(1), -40.02, target: -40.0);
        StabilityMetrics secondSecond = detector.Add(t0.AddSeconds(2), -40.02, target: -40.0);

        Assert.Equal(2, detector.StableScoreSeconds);
        Assert.Equal(2, detector.DisplayedStableScoreSeconds);
        Assert.Equal(TimeSpan.FromSeconds(1), firstSecond.WindowDuration);
        Assert.Equal(TimeSpan.FromSeconds(2), secondSecond.WindowDuration);
        Assert.False(secondSecond.IsStable);
    }
}
