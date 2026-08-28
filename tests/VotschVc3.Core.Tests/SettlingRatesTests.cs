using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>
/// A SIKA profile has no ramp segments – the bath drives itself to each set point and the
/// dwell only starts once it is there. The planned duration therefore has to carry that
/// approach time or the estimate is far shorter than the real run.
/// </summary>
public class SettlingRatesTests
{
    // Heating 10 °C/min, cooling 5 °C/min, cooling below zero 2 °C/min, 4 min to settle.
    private static readonly SettlingRates Rates = new(10, 5, 2, 4);

    private static ProfileSegment Hold(double t, double minutes = 30) =>
        new() { TargetTemperature = t, Duration = TimeSpan.FromMinutes(minutes), IsRamp = false };

    [Fact]
    public void Heating_is_the_span_over_the_heating_rate_plus_the_settling_allowance()
    {
        Assert.Equal(14, Rates.Estimate(20, 120).TotalMinutes, 3); // 100 °C / 10 + 4
    }

    [Fact]
    public void Cooling_above_zero_uses_the_cooling_rate()
    {
        Assert.Equal(14, Rates.Estimate(70, 20).TotalMinutes, 3); // 50 °C / 5 + 4
    }

    /// <summary>Below zero the bath loses far fewer degrees a minute, so the span is split.</summary>
    [Fact]
    public void Cooling_below_zero_is_charged_at_the_slower_rate()
    {
        // 20 → 0 at 5 °C/min = 4 min, 0 → -40 at 2 °C/min = 20 min, + 4 min settling.
        Assert.Equal(28, Rates.Estimate(20, -40).TotalMinutes, 3);

        // Entirely below zero: only the slow rate applies.
        Assert.Equal(14, Rates.Estimate(-20, -40).TotalMinutes, 3); // 20 / 2 + 4
    }

    [Fact]
    public void A_set_point_the_device_already_sits_on_costs_nothing()
    {
        Assert.Equal(TimeSpan.Zero, Rates.Estimate(25, 25));
        Assert.Equal(TimeSpan.Zero, Rates.Estimate(25, 25.1));
    }

    [Fact]
    public void Rates_that_are_switched_off_add_no_time_at_all()
    {
        var profile = new TestProfile { Segments = { Hold(-20), Hold(100) } };

        Assert.Equal(TimeSpan.Zero, SettlingRates.None.Estimate(20, 150));
        Assert.Equal(TimeSpan.Zero, SettlingRates.None.ForProfile(profile));
    }

    [Fact]
    public void A_whole_profile_counts_every_step_from_where_the_previous_one_ended()
    {
        var profile = new TestProfile { Segments = { Hold(20), Hold(70), Hold(20) } };

        // 23 → 20 cooling: 3/5 + 4 = 4.6 · 20 → 70: 50/10 + 4 = 9 · 70 → 20: 50/5 + 4 = 14
        Assert.Equal(27.6, Rates.ForProfile(profile, startC: 23).TotalMinutes, 3);
    }

    /// <summary>The dwell sum alone is what the runner counts; the approach time comes on top.</summary>
    [Fact]
    public void The_estimate_is_added_to_the_dwell_time_not_hidden_in_it()
    {
        var profile = new TestProfile { Segments = { Hold(20, 30), Hold(70, 30) } };

        TimeSpan dwell = profile.TotalDuration;
        TimeSpan settling = Rates.ForProfile(profile, startC: 20);

        Assert.Equal(60, dwell.TotalMinutes, 3);
        Assert.Equal(9, settling.TotalMinutes, 3); // only the 20 → 70 step costs anything
    }

    /// <summary>Every repetition after the first starts at the end of the previous one, so the
    /// step back down to the start of the body is paid for on each of them.</summary>
    [Fact]
    public void Repeated_cycles_pay_the_step_back_to_the_start_of_the_body()
    {
        var profile = new TestProfile
        {
            Segments = { Hold(20), Hold(70) },
            Cycles = 3,
        };

        // Pass 1 from 20 °C: 20 → 20 free, 20 → 70 = 9 min.
        // Passes 2 and 3 start at 70: 70 → 20 = 14 min, 20 → 70 = 9 min → 23 min each.
        Assert.Equal(9 + (2 * 23), Rates.ForProfile(profile, startC: 20).TotalMinutes, 3);
    }

    /// <summary>Segments outside the cycled region run once, so their approach time is
    /// counted once as well.</summary>
    [Fact]
    public void Only_the_cycled_region_repeats()
    {
        var profile = new TestProfile
        {
            Segments = { Hold(20), Hold(70), Hold(25) },
            Cycles = 2,
            CycleStartIndex = 0,
            CycleEndIndex = 1,
        };

        // Body pass 1 from 20: 0 + 9. Body pass 2 from 70: 14 + 9. Outro 70 → 25: 45/5 + 4 = 13.
        Assert.Equal(9 + 23 + 13, Rates.ForProfile(profile, startC: 20).TotalMinutes, 3);
    }

    [Fact]
    public void The_shipped_sika_defaults_are_a_sane_starting_point()
    {
        SettlingRates sika = SettlingRates.SikaDefault;

        Assert.True(sika.HeatingCPerMin > sika.CoolingCPerMin, "a bath heats faster than it cools");
        Assert.True(sika.CoolingCPerMin > sika.CoolingBelowZeroCPerMin, "cooling gets slower below zero");
        Assert.True(sika.StabilizeMinutes > 0);
        Assert.False(sika.IsEmpty);
    }
}
