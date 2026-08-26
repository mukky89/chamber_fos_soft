using System.Globalization;
using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

public class QuickProfileNamingTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static QuickProfileParameters Sequence(params double[] temps) => new()
    {
        IsSequence = true,
        Temperatures = temps,
        PlateauMinutes = temps.Select(_ => 90d).ToList(),
        RampMinutes = 30,
        Cycles = 1,
        TotalMinutes = 24 * 60,
    };

    [Fact]
    public void SequenceName_IsTheCoveredRangeAndPointCount_NotTheListOfSetpoints()
    {
        string name = QuickProfileNaming.Name(Sequence(-20, -10, 0, 20), culture: Inv);

        Assert.StartsWith("-20…20 °C · 4 teploty", name);
        Assert.DoesNotContain("→", name);
    }

    [Fact]
    public void SequenceName_StaysShortForALongSequence()
    {
        double[] temps = { -20, -10, 0, 20, 40, 60, 50, 60, 40, 20, 0, -10, -20 };

        string name = QuickProfileNaming.Name(Sequence(temps), culture: Inv);

        Assert.StartsWith("-20…60 °C · 13 teplôt", name);
        Assert.True(name.Length < 80, $"name too long: {name}");
    }

    [Fact]
    public void SequenceDescription_ListsTheSetpoints_JoinedSoNegativeValuesStayReadable()
    {
        string text = QuickProfileNaming.Description(Sequence(-20, -10, 0, 20), Inv);

        Assert.Contains("-20 → -10 → 0 → 20 °C", text);
        // The old "-" join produced "-20--10-0-20", which cannot be read back.
        Assert.DoesNotContain("--", text);
    }

    [Fact]
    public void SequenceName_DescribesPlateauRampAndTotal()
    {
        string name = QuickProfileNaming.Name(Sequence(-20, 0, 20), culture: Inv);

        Assert.Contains("plato 1.5 h", name);
        Assert.Contains("rampa 30 min", name);
        Assert.Contains("Σ 1 d 0 h", name);
    }

    [Fact]
    public void SequenceName_ReportsPlateauRangeWhenPointsHoldForDifferentTimes()
    {
        QuickProfileParameters p = Sequence(-20, 0, 20) with { PlateauMinutes = new[] { 30d, 60d, 45d } };

        Assert.Contains("plato 30 min–1 h", QuickProfileNaming.Name(p, culture: Inv));
    }

    [Fact]
    public void Name_TakesTheOptionalPrefix()
    {
        string name = QuickProfileNaming.Name(Sequence(0, 20), prefix: " SN-42 ", culture: Inv);

        Assert.StartsWith("SN-42 0…20 °C", name);
    }

    [Fact]
    public void Name_MentionsEveryEnabledStage()
    {
        QuickProfileParameters p = Sequence(-20, 0, 20) with
        {
            HasLeadIn = true,
            LeadInFrom = 25,
            LeadInMinutes = 60,
            HasEndHold = true,
            EndTemperature = 25,
            EndHoldMinutes = 60,
            Cycles = 3,
        };

        string name = QuickProfileNaming.Name(p, culture: Inv);

        Assert.Contains("nábeh 1 h", name);
        Assert.Contains("koniec 25 °C", name);
        Assert.Contains("×3", name);
    }

    [Fact]
    public void ParametricName_UsesTheSweepParameters()
    {
        var p = new QuickProfileParameters
        {
            IsSequence = false,
            Temperatures = new[] { -20d, -10, 0, 10, 20, 30, 40, 50, 60 },
            PlateauMinutes = new[] { 30d },
            RampMinutes = 20,
            LowTemperature = -20,
            HighTemperature = 60,
            TemperatureStep = 10,
            IncludeDescending = true,
            DoublePeak = true,
            PeakDipCelsius = 10,
            Cycles = 1,
            TotalMinutes = 26 * 60,
        };

        string name = QuickProfileNaming.Name(p, culture: Inv);

        Assert.Contains("-20…60 °C", name);
        Assert.Contains("9 krokov", name);
        Assert.Contains("krok 10 °C", name);
        Assert.Contains("↕", name);
        Assert.Contains("2 vrcholy", name);
        Assert.Contains("plato 30 min", name);
        Assert.Contains("Σ 1 d 2 h", name);
    }

    [Fact]
    public void Description_SpellsOutEveryStageOfASequence()
    {
        QuickProfileParameters p = Sequence(-20, 0, 20) with
        {
            HasLeadIn = true,
            LeadInFrom = 25,
            LeadInMinutes = 60,
            HasEndHold = true,
            EndTemperature = 25,
            EndHoldMinutes = 90,
            Cycles = 2,
            CycleBodyOnly = true,
        };

        string text = QuickProfileNaming.Description(p, Inv);

        Assert.Contains("Postupnosť 3 teploty: -20 → 0 → 20 °C", text);
        Assert.Contains("plato 1.5 h", text);
        Assert.Contains("rampa 30 min", text);
        Assert.Contains("nábeh z 25 °C (1 h)", text);
        Assert.Contains("koniec na 25 °C (1.5 h)", text);
        Assert.Contains("cyklus ×2 (len telo)", text);
    }

    [Fact]
    public void Description_OfAnEmptySequenceAsksForPoints()
    {
        QuickProfileParameters p = Sequence() with { PlateauMinutes = Array.Empty<double>() };

        Assert.Equal("Pridaj aspoň dve teploty (body postupnosti).", QuickProfileNaming.Description(p, Inv));
    }

    [Theory]
    [InlineData(0.4, "< 1 min")]
    [InlineData(45, "45 min")]
    [InlineData(510, "8 h 30 min")]
    [InlineData(1635, "1 d 3 h 15 min")]
    [InlineData(1440, "1 d 0 h")]
    public void Duration_IsFormattedForOperators(double minutes, string expected) =>
        Assert.Equal(expected, QuickProfileNaming.Duration(minutes, Inv));
}
