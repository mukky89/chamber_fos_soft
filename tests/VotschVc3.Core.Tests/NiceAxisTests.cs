using VotschVc3.Core.Charting;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>Rounded bounds of the chart value axis – round labels, no wobble while panning.</summary>
public class NiceAxisTests
{
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
}
