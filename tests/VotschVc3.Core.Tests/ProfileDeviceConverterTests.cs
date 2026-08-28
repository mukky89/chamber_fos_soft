using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>
/// Converting a Vötsch profile to the SIKA format keeps the setpoints and how long the
/// specimen sits at each of them, and drops the ramps – the bath drives to a set point on
/// its own.
/// </summary>
public class ProfileDeviceConverterTests
{
    private static ProfileSegment Ramp(double t, double minutes) =>
        new() { Name = "Nábeh", TargetTemperature = t, Duration = TimeSpan.FromMinutes(minutes), IsRamp = true };

    private static ProfileSegment Hold(double t, double minutes) =>
        new() { Name = "Plato", TargetTemperature = t, Duration = TimeSpan.FromMinutes(minutes), IsRamp = false };

    private static TestProfile VotschSweep() => new()
    {
        Name = "Sweep -20…60",
        DeviceKind = ProfileDeviceKind.Votsch,
        Kind = ChamberKind.TemperatureHumidity,
        Segments =
        {
            Ramp(-20, 20), Hold(-20, 30),
            Ramp(20, 20), Hold(20, 30),
            Ramp(60, 20), Hold(60, 30),
        },
    };

    [Fact]
    public void Ramps_are_dropped_and_the_holds_survive_unchanged()
    {
        TestProfile sika = ProfileDeviceConverter.ToSika(VotschSweep());

        Assert.Equal(ProfileDeviceKind.Sika, sika.DeviceKind);
        Assert.All(sika.Segments, s => Assert.False(s.IsRamp));
        Assert.Equal(new[] { -20d, 20d, 60d }, sika.Segments.Select(s => s.TargetTemperature));
        Assert.All(sika.Segments, s => Assert.Equal(TimeSpan.FromMinutes(30), s.Duration));
    }

    [Fact]
    public void The_run_gets_shorter_by_exactly_the_ramp_time()
    {
        TestProfile source = VotschSweep();

        TestProfile sika = ProfileDeviceConverter.ToSika(source);

        Assert.Equal(TimeSpan.FromMinutes(150), source.SinglePassDuration);
        Assert.Equal(TimeSpan.FromMinutes(90), sika.SinglePassDuration);
    }

    [Fact]
    public void The_original_profile_is_left_alone()
    {
        TestProfile source = VotschSweep();

        TestProfile sika = ProfileDeviceConverter.ToSika(source);

        Assert.Equal(6, source.Segments.Count);
        Assert.Equal(ProfileDeviceKind.Votsch, source.DeviceKind);
        Assert.NotEqual(source.Id, sika.Id);
        Assert.Equal("Sweep -20…60 · SIKA", sika.Name);
    }

    /// <summary>Without the ramp between them two holds at the same temperature are one
    /// longer hold – re-settling on a value the bath already sits at is pointless.</summary>
    [Fact]
    public void Adjacent_holds_at_the_same_temperature_are_merged()
    {
        var profile = new TestProfile
        {
            DeviceKind = ProfileDeviceKind.Votsch,
            Segments = { Hold(25, 30), Ramp(25, 10), Hold(25, 45), Ramp(60, 20), Hold(60, 15) },
        };

        TestProfile sika = ProfileDeviceConverter.ToSika(profile);

        Assert.Equal(2, sika.Segments.Count);
        Assert.Equal(TimeSpan.FromMinutes(75), sika.Segments[0].Duration);
        Assert.Equal(60, sika.Segments[1].TargetTemperature);
    }

    /// <summary>A ramp-only profile must not convert to nothing.</summary>
    [Fact]
    public void A_profile_without_any_hold_keeps_every_setpoint_as_a_dwell()
    {
        var profile = new TestProfile
        {
            DeviceKind = ProfileDeviceKind.Votsch,
            Segments = { Ramp(0, 15), Ramp(40, 15) },
        };

        TestProfile sika = ProfileDeviceConverter.ToSika(profile);

        Assert.Equal(2, sika.Segments.Count);
        Assert.All(sika.Segments, s => Assert.False(s.IsRamp));
        Assert.Equal(TimeSpan.FromMinutes(30), sika.SinglePassDuration);
    }

    /// <summary>The cycled region is stored as segment indices, so it has to be remapped
    /// onto the shorter list or the wrong part of the profile would repeat.</summary>
    [Fact]
    public void The_cycled_region_is_remapped_onto_the_converted_segments()
    {
        TestProfile source = VotschSweep();
        source.Cycles = 3;
        source.CycleStartIndex = 2; // Ramp(20) … the body without the first ramp+hold
        source.CycleEndIndex = 5;   // … up to Hold(60)

        TestProfile sika = ProfileDeviceConverter.ToSika(source);

        // Ramp(20)/Hold(20) collapsed to index 1, Hold(60) to index 2.
        Assert.Equal(1, sika.CycleStartIndex);
        Assert.Equal(2, sika.CycleEndIndex);
        Assert.Equal(3, sika.Cycles);
    }

    [Fact]
    public void Humidity_is_dropped_because_a_bath_has_no_humidity_channel()
    {
        var profile = new TestProfile
        {
            Kind = ChamberKind.TemperatureHumidity,
            Segments = { new ProfileSegment { TargetTemperature = 40, TargetHumidity = 60, IsRamp = false } },
        };

        TestProfile sika = ProfileDeviceConverter.ToSika(profile);

        Assert.Equal(ChamberKind.TemperatureOnly, sika.Kind);
        Assert.Null(Assert.Single(sika.Segments).TargetHumidity);
    }

    [Fact]
    public void Converting_twice_does_not_stack_the_name_marker()
    {
        TestProfile once = ProfileDeviceConverter.ToSika(VotschSweep());

        TestProfile twice = ProfileDeviceConverter.ToSika(once);

        Assert.Equal("Sweep -20…60 · SIKA", twice.Name);
    }
}
