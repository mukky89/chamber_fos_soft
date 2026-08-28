using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

public class ProfilePreviewSummaryTests
{
    [Fact]
    public void Analyze_reports_min_max_plateaus_levels_and_total_duration()
    {
        var profile = new TestProfile
        {
            Cycles = 2,
            CycleStartIndex = 1,
            CycleEndIndex = 3,
            Segments =
            {
                new ProfileSegment { Name = "Nábeh", TargetTemperature = -40, Duration = TimeSpan.FromMinutes(30), IsRamp = true },
                new ProfileSegment { Name = "Studené plato", TargetTemperature = -40, Duration = TimeSpan.FromMinutes(60), IsRamp = false },
                new ProfileSegment { Name = "Ohrev", TargetTemperature = 85, Duration = TimeSpan.FromMinutes(45), IsRamp = true },
                new ProfileSegment { Name = "Teplé plato", TargetTemperature = 85, Duration = TimeSpan.FromMinutes(120), IsRamp = false },
                new ProfileSegment { Name = "Dobeh", TargetTemperature = 25, Duration = TimeSpan.FromMinutes(20), IsRamp = true },
            },
        };

        ProfilePreviewSummary summary = ProfilePreviewSummary.Analyze(profile);

        Assert.Equal(-40, summary.MinTemperature);
        Assert.Equal(85, summary.MaxTemperature);
        Assert.Equal(2, summary.Cycles);
        Assert.Equal(4, summary.PlateauCount);
        Assert.Equal(2, summary.TemperatureLevelCount);
        Assert.Equal(TimeSpan.FromMinutes(30 + ((60 + 45 + 120) * 2) + 20), summary.TotalDuration);
        Assert.Collection(summary.Plateaus,
            p =>
            {
                Assert.Equal(-40, p.Temperature);
                Assert.Equal(TimeSpan.FromMinutes(60), p.Duration);
                Assert.Equal(2, p.Repetitions);
            },
            p =>
            {
                Assert.Equal(85, p.Temperature);
                Assert.Equal(TimeSpan.FromMinutes(120), p.Duration);
                Assert.Equal(2, p.Repetitions);
            });
    }

    [Fact]
    public void Analyze_counts_intro_and_outro_plateaus_once()
    {
        var profile = new TestProfile
        {
            Cycles = 3,
            CycleStartIndex = 1,
            CycleEndIndex = 1,
            Segments =
            {
                new ProfileSegment { TargetTemperature = 25, Duration = TimeSpan.FromMinutes(10), IsRamp = false },
                new ProfileSegment { TargetTemperature = 80, Duration = TimeSpan.FromMinutes(20), IsRamp = false },
                new ProfileSegment { TargetTemperature = 25, Duration = TimeSpan.FromMinutes(30), IsRamp = false },
            },
        };

        ProfilePreviewSummary summary = ProfilePreviewSummary.Analyze(profile);

        Assert.Equal(5, summary.PlateauCount); // intro 1 + body 3 + outro 1
        Assert.Equal(2, summary.TemperatureLevelCount);
        Assert.Equal(1, summary.Plateaus[0].Repetitions);
        Assert.Equal(3, summary.Plateaus[1].Repetitions);
        Assert.Equal(1, summary.Plateaus[2].Repetitions);
    }

    [Fact]
    public void Analyze_handles_empty_profile()
    {
        var profile = new TestProfile { Cycles = 4 };

        ProfilePreviewSummary summary = ProfilePreviewSummary.Analyze(profile);

        Assert.Null(summary.MinTemperature);
        Assert.Null(summary.MaxTemperature);
        Assert.Equal(TimeSpan.Zero, summary.TotalDuration);
        Assert.Equal(4, summary.Cycles);
        Assert.Equal(0, summary.PlateauCount);
        Assert.Equal(0, summary.TemperatureLevelCount);
        Assert.Empty(summary.Plateaus);
    }

    /// <summary>A SIKA profile is dwell times only – the approach to each set point has to be
    /// added or the preview promises a run far shorter than it is.</summary>
    [Fact]
    public void A_sika_profile_carries_the_approach_time_next_to_the_dwell_sum()
    {
        var rates = new SettlingRates(10, 5, 2, 4);
        var profile = new TestProfile
        {
            DeviceKind = ProfileDeviceKind.Sika,
            Segments =
            {
                new ProfileSegment { TargetTemperature = 20, Duration = TimeSpan.FromMinutes(30) },
                new ProfileSegment { TargetTemperature = 70, Duration = TimeSpan.FromMinutes(30) },
            },
        };

        ProfilePreviewSummary summary = ProfilePreviewSummary.Analyze(profile, rates);

        Assert.True(summary.HasSettling);
        Assert.Equal(TimeSpan.FromMinutes(60), summary.TotalDuration);
        Assert.Equal(summary.TotalDuration + summary.SettlingDuration, summary.TotalWithSettling);
        Assert.True(summary.SettlingDuration > TimeSpan.Zero);
    }

    /// <summary>A Vötsch profile ramps in its own segments, so nothing may be added on top.</summary>
    [Fact]
    public void A_votsch_profile_gets_no_approach_time_added()
    {
        var profile = new TestProfile
        {
            DeviceKind = ProfileDeviceKind.Votsch,
            Segments =
            {
                new ProfileSegment { TargetTemperature = 70, Duration = TimeSpan.FromMinutes(20), IsRamp = true },
                new ProfileSegment { TargetTemperature = 70, Duration = TimeSpan.FromMinutes(30) },
            },
        };

        ProfilePreviewSummary summary = ProfilePreviewSummary.Analyze(profile, new SettlingRates(10, 5, 2, 4));

        Assert.False(summary.HasSettling);
        Assert.Equal(summary.TotalDuration, summary.TotalWithSettling);
    }
}
