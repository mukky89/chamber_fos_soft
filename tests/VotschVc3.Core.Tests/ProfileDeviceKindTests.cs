using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>
/// A Vötsch profile ramps between the plateaus, a SIKA profile is a list of setpoints
/// with a dwell time. Running one on the other device does not do what the profile says,
/// so the device card only offers profiles of its own family.
/// </summary>
public class ProfileDeviceKindTests
{
    [Theory]
    [InlineData(ChamberProtocol.VotschAscii2, ProfileDeviceKind.Votsch)]
    [InlineData(ChamberProtocol.PolEkoModbus, ProfileDeviceKind.Votsch)]
    [InlineData(ChamberProtocol.SikaRestApi, ProfileDeviceKind.Sika)]
    public void Protocol_maps_to_its_device_family(ChamberProtocol protocol, ProfileDeviceKind expected) =>
        Assert.Equal(expected, protocol.ToDeviceKind());

    [Fact]
    public void Sika_profiles_are_not_offered_on_a_votsch_chamber()
    {
        Assert.False(ProfileDeviceKind.Sika.CanRunOn(ProfileDeviceKind.Votsch));
        Assert.False(ProfileDeviceKind.Votsch.CanRunOn(ProfileDeviceKind.Sika));
    }

    [Fact]
    public void A_profile_of_the_same_family_is_offered()
    {
        Assert.True(ProfileDeviceKind.Sika.CanRunOn(ProfileDeviceKind.Sika));
        Assert.True(ProfileDeviceKind.Votsch.CanRunOn(ProfileDeviceKind.Votsch));
    }

    /// <summary>Profiles saved before the distinction existed default to "Any" and must
    /// keep showing up everywhere – an upgrade may not empty an existing library.</summary>
    [Fact]
    public void Universal_profiles_stay_visible_on_every_device()
    {
        Assert.Equal(ProfileDeviceKind.Any, new TestProfile().DeviceKind);
        Assert.True(ProfileDeviceKind.Any.CanRunOn(ProfileDeviceKind.Sika));
        Assert.True(ProfileDeviceKind.Any.CanRunOn(ProfileDeviceKind.Votsch));
    }

    [Fact]
    public void Clone_keeps_the_device_family()
    {
        var profile = new TestProfile { DeviceKind = ProfileDeviceKind.Sika };
        Assert.Equal(ProfileDeviceKind.Sika, profile.Clone().DeviceKind);
    }

    [Fact]
    public void Device_family_survives_a_json_round_trip()
    {
        var profile = new TestProfile { Name = "SIKA sweep", DeviceKind = ProfileDeviceKind.Sika };

        List<TestProfile> restored = ProfileFile.Deserialize(ProfileFile.Serialize(new[] { profile }));

        Assert.Equal(ProfileDeviceKind.Sika, Assert.Single(restored).DeviceKind);
    }
}
