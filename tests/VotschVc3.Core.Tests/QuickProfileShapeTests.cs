using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>
/// Reconstruction of the quick-builder parameters from a saved segment list – the
/// analysis behind "load an existing profile back into the quick builder".
/// </summary>
public class QuickProfileShapeTests
{
    private static ProfileSegment Ramp(double target, double minutes) => new()
    {
        Name = $"Nábeh {target:0.#} °C",
        TargetTemperature = target,
        Duration = TimeSpan.FromMinutes(minutes),
        IsRamp = true,
    };

    private static ProfileSegment Plateau(double target, double minutes) => new()
    {
        Name = $"Plato {target:0.#} °C",
        TargetTemperature = target,
        Duration = TimeSpan.FromMinutes(minutes),
        IsRamp = false,
    };

    /// <summary>Mirrors the parametric builder of the quick profile view model.</summary>
    private static List<ProfileSegment> BuildSweep(
        double low, double high, int intermediateSteps, double plateau, double ramp,
        bool includeDescending, bool doublePeak, double dip,
        double leadInMinutes = 0, double endTemperature = double.NaN, double endHold = 0)
    {
        int n = intermediateSteps + 2;
        double delta = (high - low) / (n - 1);
        var up = new List<double>();
        for (int i = 0; i < n; i++)
        {
            up.Add(low + delta * i);
        }

        up[n - 1] = high;

        var segs = new List<ProfileSegment>();
        if (leadInMinutes > 0)
        {
            segs.Add(Ramp(up[0], leadInMinutes));
        }

        segs.Add(Plateau(up[0], plateau));
        for (int i = 1; i < up.Count; i++)
        {
            segs.Add(Ramp(up[i], ramp));
            segs.Add(Plateau(up[i], plateau));
        }

        if (doublePeak)
        {
            segs.Add(Ramp(high - dip, ramp));
            segs.Add(Plateau(high - dip, plateau));
            segs.Add(Ramp(high, ramp));
            segs.Add(Plateau(high, plateau));
        }

        if (includeDescending)
        {
            for (int i = up.Count - 2; i >= 0; i--)
            {
                segs.Add(Ramp(up[i], ramp));
                segs.Add(Plateau(up[i], plateau));
            }
        }

        if (!double.IsNaN(endTemperature))
        {
            segs.Add(Ramp(endTemperature, ramp));
            segs.Add(Plateau(endTemperature, endHold));
        }

        return segs;
    }

    [Fact]
    public void Analyze_RecognisesPlainAscendingSweep()
    {
        List<ProfileSegment> segments = BuildSweep(
            low: -20, high: 60, intermediateSteps: 3, plateau: 30, ramp: 20,
            includeDescending: false, doublePeak: false, dip: 0);

        QuickProfileShape shape = QuickProfileShape.Analyze(segments);

        Assert.True(shape.IsParametric);
        Assert.Equal(-20, shape.LowTemperature, 3);
        Assert.Equal(60, shape.HighTemperature, 3);
        Assert.Equal(3, shape.IntermediateSteps);
        Assert.Equal(20, shape.TemperatureStep, 3);
        Assert.False(shape.IncludeDescending);
        Assert.False(shape.DoublePeak);
        Assert.Equal(30, shape.PlateauMinutes, 3);
        Assert.Equal(20, shape.RampMinutes, 3);
        Assert.False(shape.HasLeadIn);
        Assert.False(shape.HasEndHold);
    }

    [Fact]
    public void Analyze_RecognisesFullSweepWithLeadInDoublePeakDescendingAndEndHold()
    {
        // The shape the builder produces with every option on – the 72-segment profile
        // an operator gets from "-40 → 120 °C, 17 krokov, plato 60 min".
        List<ProfileSegment> segments = BuildSweep(
            low: -40, high: 120, intermediateSteps: 15, plateau: 60, ramp: 20,
            includeDescending: true, doublePeak: true, dip: 10,
            leadInMinutes: 60, endTemperature: 25, endHold: 60);

        Assert.Equal(72, segments.Count);

        QuickProfileShape shape = QuickProfileShape.Analyze(segments);

        Assert.True(shape.IsParametric);
        Assert.Equal(-40, shape.LowTemperature, 3);
        Assert.Equal(120, shape.HighTemperature, 3);
        Assert.Equal(15, shape.IntermediateSteps);
        Assert.Equal(10, shape.TemperatureStep, 3);
        Assert.True(shape.IncludeDescending);
        Assert.True(shape.DoublePeak);
        Assert.Equal(10, shape.PeakDipCelsius, 3);
        Assert.Equal(60, shape.PlateauMinutes, 3);
        Assert.Equal(20, shape.RampMinutes, 3);
        Assert.True(shape.HasLeadIn);
        Assert.Equal(60, shape.LeadInMinutes, 3);
        Assert.True(shape.HasEndHold);
        Assert.Equal(25, shape.EndTemperature, 3);
        Assert.Equal(60, shape.EndHoldMinutes, 3);
    }

    [Fact]
    public void Analyze_KeepsPerStepHoldsAsSequenceWhenPlateausDiffer()
    {
        var segments = new List<ProfileSegment>
        {
            Plateau(20, 15),
            Ramp(40, 20), Plateau(40, 90),
            Ramp(0, 20), Plateau(0, 45),
        };

        QuickProfileShape shape = QuickProfileShape.Analyze(segments);

        Assert.False(shape.IsParametric);
        Assert.Equal(3, shape.Points.Count);
        Assert.Equal(new QuickProfilePoint(20, 15), shape.Points[0]);
        Assert.Equal(new QuickProfilePoint(40, 90), shape.Points[1]);
        Assert.Equal(new QuickProfilePoint(0, 45), shape.Points[2]);
        Assert.Equal(20, shape.RampMinutes, 3);
    }

    [Fact]
    public void Analyze_PeelsLeadInRampOffTheSequence()
    {
        var segments = new List<ProfileSegment>
        {
            Ramp(-10, 45),
            Plateau(-10, 30),
            Ramp(50, 20), Plateau(50, 90),
        };

        QuickProfileShape shape = QuickProfileShape.Analyze(segments);

        Assert.True(shape.HasLeadIn);
        Assert.Equal(45, shape.LeadInMinutes, 3);
        Assert.Equal(2, shape.Points.Count);
        Assert.Equal(-10, shape.Points[0].Temperature, 3);
        Assert.Equal(30, shape.Points[0].PlateauMinutes, 3);
    }

    [Fact]
    public void Analyze_LeavesClosingPairInPlaceWhenItsRampIsNotTheSharedOne()
    {
        // A closing ramp of a different length is not the builder's safety cool-down, so
        // it must stay a normal point – otherwise re-saving would change its duration.
        var segments = new List<ProfileSegment>
        {
            Plateau(20, 30),
            Ramp(60, 20), Plateau(60, 30),
            Ramp(25, 90), Plateau(25, 120),
        };

        QuickProfileShape shape = QuickProfileShape.Analyze(segments);

        Assert.False(shape.HasEndHold);
        Assert.Equal(3, shape.Points.Count);
        Assert.Equal(25, shape.Points[2].Temperature, 3);
        Assert.Equal(120, shape.Points[2].PlateauMinutes, 3);
    }

    [Fact]
    public void Analyze_DescendingOnlyProfileIsNotParametric()
    {
        var segments = new List<ProfileSegment>
        {
            Plateau(60, 30),
            Ramp(20, 20), Plateau(20, 30),
            Ramp(-20, 20), Plateau(-20, 30),
        };

        QuickProfileShape shape = QuickProfileShape.Analyze(segments);

        Assert.False(shape.IsParametric);
        Assert.Equal(3, shape.Points.Count);
    }

    [Fact]
    public void Analyze_RecognisesSweepWhoseStepDoesNotDivideTheRangeEvenly()
    {
        // 80 °C over 7 intervals = 11.43 °C per step; rounded setpoints make neighbouring
        // differences alternate between 11.4 and 11.5, which must still read as one sweep.
        List<ProfileSegment> segments = BuildSweep(
            low: -20, high: 60, intermediateSteps: 6, plateau: 30, ramp: 20,
            includeDescending: true, doublePeak: false, dip: 0);

        QuickProfileShape shape = QuickProfileShape.Analyze(segments);

        Assert.True(shape.IsParametric);
        Assert.Equal(6, shape.IntermediateSteps);
        Assert.Equal(-20, shape.LowTemperature, 3);
        Assert.Equal(60, shape.HighTemperature, 3);
        Assert.True(shape.IncludeDescending);
    }

    [Fact]
    public void Analyze_RejectsAnAlmostEvenSequenceSoSetpointsAreNeverMoved()
    {
        // Deliberately uneven setpoints: reading these as a sweep would shift them on the
        // next save, so they have to stay an explicit sequence.
        var segments = new List<ProfileSegment>
        {
            Plateau(0, 30),
            Ramp(12, 20), Plateau(12, 30),
            Ramp(20, 20), Plateau(20, 30),
            Ramp(33, 20), Plateau(33, 30),
        };

        QuickProfileShape shape = QuickProfileShape.Analyze(segments);

        Assert.False(shape.IsParametric);
        Assert.Equal(4, shape.Points.Count);
        Assert.Equal(new[] { 0d, 12d, 20d, 33d }, shape.Points.Select(p => p.Temperature).ToArray());
    }

    [Fact]
    public void Analyze_EmptyProfileYieldsNoPoints()
    {
        QuickProfileShape shape = QuickProfileShape.Analyze(new List<ProfileSegment>());

        Assert.False(shape.IsParametric);
        Assert.Empty(shape.Points);
    }
}
