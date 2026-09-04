using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class TemperatureStabilityDetectorTests
{
    [Fact]
    public void Minus40Point322_IsStableInsideHalfDegreeToleranceAfterOneMinute()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromMinutes(1),
            toleranceC: 0.5,
            maxDriftCPerMinute: 0.1);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        StabilityMetrics metrics = default!;
        for (int second = 0; second <= 60; second++)
        {
            metrics = detector.Add(t0.AddSeconds(second), -40.322, target: -40.0);
        }

        Assert.True(metrics.IsStable);
        Assert.True(metrics.WindowDuration >= TimeSpan.FromMinutes(1));
        Assert.InRange(Math.Abs(metrics.Mean - (-40.0)), 0, 0.5);
        Assert.InRange(Math.Abs(metrics.SlopePerMinute), 0, 0.1);
    }

    [Fact]
    public void OutOfToleranceHistory_DoesNotBlockRecoveredWikaForExtraMinute()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromMinutes(1),
            toleranceC: 0.5,
            maxDriftCPerMinute: 0.1);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        Assert.False(detector.Add(t0, -40.8, target: -40.0).IsStable);

        StabilityMetrics metrics = default!;
        for (int second = 1; second <= 61; second++)
        {
            metrics = detector.Add(t0.AddSeconds(second), -40.322, target: -40.0);
        }

        Assert.True(metrics.IsStable);
        Assert.True(metrics.Minimum >= -40.5);
        Assert.True(metrics.Maximum <= -39.5);
        Assert.InRange(metrics.WindowDuration.TotalSeconds, 60, 61.1);
    }

    [Fact]
    public void LeavingTolerance_ResetsContinuousStabilityWindow()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromMinutes(1),
            toleranceC: 0.5,
            maxDriftCPerMinute: 0.1);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        StabilityMetrics metrics = default!;
        for (int second = 0; second <= 60; second++)
        {
            metrics = detector.Add(t0.AddSeconds(second), -40.2, target: -40.0);
        }
        Assert.True(metrics.IsStable);

        metrics = detector.Add(t0.AddSeconds(61), -40.7, target: -40.0);
        Assert.False(metrics.IsStable);

        metrics = detector.Add(t0.AddSeconds(62), -40.2, target: -40.0);
        Assert.False(metrics.IsStable);
        Assert.True(metrics.WindowDuration < TimeSpan.FromSeconds(2));

        for (int second = 63; second <= 122; second++)
        {
            metrics = detector.Add(t0.AddSeconds(second), -40.2, target: -40.0);
        }

        Assert.True(metrics.IsStable);
    }

    [Fact]
    public void ExcessiveTemperatureDrift_IsStillRejectedInsideTolerance()
    {
        var detector = new TemperatureStabilityDetector(
            TimeSpan.FromMinutes(1),
            toleranceC: 0.5,
            maxDriftCPerMinute: 0.1);
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        StabilityMetrics metrics = default!;
        for (int second = 0; second <= 60; second++)
        {
            // 0.2 °C/min drift, entirely within the ±0.5 °C target band.
            double value = -40.3 + (0.2 * second / 60.0);
            metrics = detector.Add(t0.AddSeconds(second), value, target: -40.0);
        }

        Assert.False(metrics.IsStable);
        Assert.True(Math.Abs(metrics.SlopePerMinute) > 0.1);
    }
}
