using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

public class ProfileStandardizerTests
{
    private static ProfileStandardizationOptions Options() => new()
    {
        RoomTemperature = 25,
        InitialRampMinutes = 60,
        FinalRampMinutes = 60,
        FinalHoldMinutes = 60,
    };

    [Fact]
    public void Prepends_initial_ramp_when_profile_starts_with_a_hold()
    {
        var profile = new TestProfile
        {
            Segments =
            {
                new ProfileSegment { TargetTemperature = 60, IsRamp = false, Duration = TimeSpan.FromMinutes(30) },
            },
        };

        ProfileStandardizer.Standardize(profile, Options());

        ProfileSegment first = profile.Segments[0];
        Assert.True(first.IsRamp);
        Assert.Equal(60, first.TargetTemperature);
        Assert.Equal(TimeSpan.FromMinutes(60), first.Duration);
    }

    [Fact]
    public void Does_not_prepend_when_profile_already_starts_with_a_ramp()
    {
        var profile = new TestProfile
        {
            Segments =
            {
                new ProfileSegment { TargetTemperature = 60, IsRamp = true, Duration = TimeSpan.FromMinutes(20) },
                new ProfileSegment { TargetTemperature = 60, IsRamp = false, Duration = TimeSpan.FromMinutes(30) },
            },
        };

        ProfileStandardizer.Standardize(profile, Options());

        // No extra lead-in ramp: still the same first segment (plus the closing ramp/hold at the end).
        Assert.True(profile.Segments[0].IsRamp);
        Assert.Equal(60, profile.Segments[0].TargetTemperature);
        Assert.Equal(TimeSpan.FromMinutes(20), profile.Segments[0].Duration);
    }

    [Fact]
    public void Appends_final_ramp_and_hold_to_room_temperature()
    {
        var profile = new TestProfile
        {
            Segments =
            {
                new ProfileSegment { TargetTemperature = -40, IsRamp = true, Duration = TimeSpan.FromMinutes(30) },
                new ProfileSegment { TargetTemperature = -40, IsRamp = false, Duration = TimeSpan.FromMinutes(60) },
            },
        };

        ProfileStandardizer.Standardize(profile, Options());

        ProfileSegment ramp = profile.Segments[^2];
        ProfileSegment hold = profile.Segments[^1];
        Assert.True(ramp.IsRamp);
        Assert.Equal(25, ramp.TargetTemperature);
        Assert.False(hold.IsRamp);
        Assert.Equal(25, hold.TargetTemperature);
        Assert.Equal(TimeSpan.FromMinutes(60), hold.Duration);
    }

    [Fact]
    public void Skips_final_ramp_when_already_at_room_temperature()
    {
        var profile = new TestProfile
        {
            Segments =
            {
                new ProfileSegment { TargetTemperature = 25, IsRamp = true, Duration = TimeSpan.FromMinutes(20) },
            },
        };

        ProfileStandardizer.Standardize(profile, Options());

        // Only the one ramp remains: it already both starts with a ramp and ends at room temp.
        Assert.Single(profile.Segments);
        Assert.Equal(25, profile.Segments[0].TargetTemperature);
    }

    [Fact]
    public void No_final_hold_when_hold_minutes_is_zero()
    {
        var options = Options();
        options.FinalHoldMinutes = 0;

        var profile = new TestProfile
        {
            Segments = { new ProfileSegment { TargetTemperature = 80, IsRamp = true, Duration = TimeSpan.FromMinutes(30) } },
        };

        ProfileStandardizer.Standardize(profile, options);

        ProfileSegment last = profile.Segments[^1];
        Assert.True(last.IsRamp);
        Assert.Equal(25, last.TargetTemperature);
    }
}
