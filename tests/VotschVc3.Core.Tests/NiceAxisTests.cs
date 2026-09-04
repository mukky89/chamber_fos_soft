using VotschVc3.Core.Charting;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>Rounded bounds of the chart value axis – round labels, no wobble while panning.</summary>
public class NiceAxisTests
{
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0.5, 0, 1)]
    [InlineData(0.25, 0, 2)]
    [InlineData(1, 2, 2)]
    public void RequiredDecimalPlacesPreservesStepPrecision(double step, int minimum, int expected) =>
        Assert.Equal(expected, NiceAxis.RequiredDecimalPlaces(step, minimum));

    [Fact]
    public void Exact_axis_keeps_profile_minimum_and_maximum_without_padding()
    {
        ValueAxis axis = NiceAxis.Exact(-20, 60);

        Assert.Equal(-20, axis.Min);
        Assert.Equal(60, axis.Max);
        Assert.Equal(20, axis.Step);
        Assert.Equal(4, axis.Intervals);
    }

    [Fact]
    public void Exact_axis_gives_a_flat_profile_visible_height()
    {
        ValueAxis axis = NiceAxis.Exact(25, 25);

        Assert.Equal(24, axis.Min);
        Assert.Equal(26, axis.Max);
    }

    [Theory]
    [InlineData(0.8, 1d)]
    [InlineData(1d, 1d)]
    [InlineData(1.4, 1.5)]
    [InlineData(1.7, 2d)]
    [InlineData(2.3, 2.5)]
    [InlineData(2.9, 3d)]
    [InlineData(4d, 5d)]
    [InlineData(6d, 10d)]
    [InlineData(23d, 25d)]
    [InlineData(0.028, 0.03)]
    public void NiceStepSnapsToTheOneTwoFiveSeries(double raw, double expected)
    {
        Assert.Equal(expected, NiceAxis.NiceStep(raw), 9);
    }

    [Fact]
    public void NiceStepSurvivesNonsense()
    {
        Assert.Equal(1d, NiceAxis.NiceStep(0));
        Assert.Equal(1d, NiceAxis.NiceStep(-5));
        Assert.Equal(1d, NiceAxis.NiceStep(double.NaN));
    }

    [Fact]
    public void NextNiceStepIsStrictlyCoarser()
    {
        double step = 1;
        for (int i = 0; i < 12; i++)
        {
            double next = NiceAxis.NextNiceStep(step);
            Assert.True(next > step, $"{next} musí byť viac ako {step}");
            step = next;
        }
    }

    [Fact]
    public void BoundsCoverTheDataAndDivideIntoEqualSteps()
    {
        (double min, double max) = NiceAxis.Round(68, 97.3);

        Assert.True(min <= 68);
        Assert.True(max >= 97.3);

        double step = (max - min) / 4;
        for (int i = 0; i <= 4; i++)
        {
            double label = min + (i * step);
            Assert.Equal(Math.Round(label, 6), Math.Round(label, 1), 6);
        }
    }

    [Fact]
    public void SmallWindowKeepsResolution()
    {
        // A plateau that only ripples by a few tenths must not be rounded to a
        // 10 °C axis – the ripple is exactly what the operator zoomed in for.
        (double min, double max) = NiceAxis.Round(84.9, 85.4);

        Assert.True(min <= 84.9);
        Assert.True(max >= 85.4);
        Assert.True(max - min <= 2, $"rozsah {max - min} je zbytočne široký");
    }

    [Fact]
    public void FlatLineStillGetsAnAxis()
    {
        (double min, double max) = NiceAxis.Round(25, 25);

        Assert.True(max > min);
        Assert.True(min <= 25 && max >= 25);
    }

    [Fact]
    public void NegativeRangeIsHandled()
    {
        (double min, double max) = NiceAxis.Round(-52.8, -12.1);

        Assert.True(min <= -52.8);
        Assert.True(max >= -12.1);
    }

    [Fact]
    public void SwappedInputIsHandled()
    {
        (double min, double max) = NiceAxis.Round(100, 20);

        Assert.True(min <= 20);
        Assert.True(max >= 100);
    }

    [Theory]
    [InlineData(60, 10)]        // hodinový profil → po desiatich minútach
    [InlineData(180, 30)]       // tri hodiny → polhodiny
    [InlineData(480, 120)]      // osem hodín → po dvoch hodinách
    [InlineData(1635, 360)]     // 1 d 3 h → po šiestich hodinách
    [InlineData(3270, 720)]     // 2 d 6 h → po dvanástich hodinách
    [InlineData(20160, 4320)]   // dva týždne → po troch dňoch
    public void TimeStepLandsOnAReadableUnit(double spanMinutes, double expected) =>
        Assert.Equal(expected, NiceAxis.NiceTimeStep(spanMinutes));

    [Fact]
    public void TimeStepOfAVeryLongSpanStaysOnWholeDays()
    {
        double step = NiceAxis.NiceTimeStep(365 * 24 * 60);

        Assert.True(step >= 1440);
        Assert.Equal(0d, step % 1440);
    }

    [Fact]
    public void TimeStepOfAnEmptySpanIsSafe() => Assert.Equal(1d, NiceAxis.NiceTimeStep(0));

    [Theory]
    // Rozsah profilu -40…120 °C sa predtým zaokrúhlil na -100…300 °C a krivka
    // sa tlačila v spodnej tretine grafu.
    [InlineData(-40, 120)]
    [InlineData(-30, 60)]
    [InlineData(0, 85)]
    [InlineData(-70, 180)]
    [InlineData(15, 22)]
    public void BoundsDoNotWasteMoreThanHalfTheAxis(double min, double max)
    {
        (double lo, double hi) = NiceAxis.Round(min, max);

        Assert.True(lo <= min, $"{lo} musí byť pod {min}");
        Assert.True(hi >= max, $"{hi} musí byť nad {max}");
        Assert.True(hi - lo <= (max - min) * 2,
            $"os {lo}…{hi} je viac než dvojnásobok dát {min}…{max}");
    }

    [Fact]
    public void ProfileRangeGetsATightAxis()
    {
        (double lo, double hi) = NiceAxis.Round(-40, 120);

        Assert.Equal(-50, lo);
        Assert.Equal(150, hi);
    }

    [Theory]
    [InlineData(-40, 120)]
    [InlineData(-30, 60)]
    [InlineData(0, 85)]
    [InlineData(-70, 180)]
    [InlineData(15, 22)]
    [InlineData(-52.8, -12.1)]
    [InlineData(20, 100)]
    public void ScaleCropsCloseToTheDataAndKeepsRoundLabels(double min, double max)
    {
        ValueAxis axis = NiceAxis.Scale(min, max);

        Assert.True(axis.Min <= min, $"{axis.Min} musí byť pod {min}");
        Assert.True(axis.Max >= max, $"{axis.Max} musí byť nad {max}");
        Assert.True(axis.Span <= (max - min) * 1.5,
            $"os {axis.Min}…{axis.Max} necháva priveľa prázdna pre dáta {min}…{max}");

        Assert.Equal(axis.Max, axis.LabelAt(axis.Intervals), 9);
        for (int i = 0; i <= axis.Intervals; i++)
        {
            double label = axis.LabelAt(i);
            Assert.Equal(Math.Round(label / axis.Step), label / axis.Step, 6);
        }
    }

    [Fact]
    public void ScaleOfAProfileRangeIsTightEnoughToRead()
    {
        // -40…120 °C sa predtým zaokrúhlilo na -100…300 °C a krivka sa tlačila dole.
        ValueAxis axis = NiceAxis.Scale(-40, 120);

        Assert.True(axis.Min >= -60 && axis.Min <= -40);
        Assert.True(axis.Max >= 120 && axis.Max <= 150);
    }

    [Fact]
    public void ScaleOfAFlatLineStillGivesAnAxis()
    {
        ValueAxis axis = NiceAxis.Scale(25, 25);

        Assert.True(axis.Max > axis.Min);
        Assert.True(axis.Min <= 25 && axis.Max >= 25);
        Assert.True(axis.Intervals >= 1);
    }

    [Fact]
    public void ScaleHandlesSwappedInput()
    {
        ValueAxis axis = NiceAxis.Scale(100, 20);

        Assert.True(axis.Min <= 20);
        Assert.True(axis.Max >= 100);
    }
}
