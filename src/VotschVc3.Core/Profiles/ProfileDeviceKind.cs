namespace VotschVc3.Core.Profiles;

/// <summary>
/// Which family of device a profile was authored for.
/// <para>
/// A Vötsch / Weiss chamber is driven with explicit ramps between the plateaus, so its
/// profiles alternate "nábeh" and "plato". A SIKA TP Premium bath settles on a new set
/// point on its own, so its profiles are a plain list of setpoints with a dwell time –
/// there is no ramp up / ramp down to configure. Keeping the two apart lets the device
/// card offer only the profiles that actually make sense for the connected device.
/// </para>
/// </summary>
public enum ProfileDeviceKind
{
    /// <summary>Not tied to a device family – offered on every device. Default for profiles
    /// created before the distinction existed, so nothing disappears from an old library.</summary>
    Any,

    /// <summary>Vötsch / Weiss (and POL-EKO) chamber: ramps + plateaus.</summary>
    Votsch,

    /// <summary>SIKA TP Premium bath / dry block: setpoints with a dwell time, no ramps.</summary>
    Sika,
}

/// <summary>Helpers mapping a chamber protocol onto a <see cref="ProfileDeviceKind"/>.</summary>
public static class ProfileDeviceKindExtensions
{
    /// <summary>The device family a wire protocol belongs to.</summary>
    public static ProfileDeviceKind ToDeviceKind(this ChamberProtocol protocol) =>
        protocol == ChamberProtocol.SikaRestApi ? ProfileDeviceKind.Sika : ProfileDeviceKind.Votsch;

    /// <summary>
    /// <c>true</c> when a profile authored for <paramref name="profileKind"/> may be offered
    /// on a device of <paramref name="deviceKind"/>. <see cref="ProfileDeviceKind.Any"/>
    /// matches every device (and every device accepts an "Any" profile).
    /// </summary>
    public static bool CanRunOn(this ProfileDeviceKind profileKind, ProfileDeviceKind deviceKind) =>
        profileKind == ProfileDeviceKind.Any ||
        deviceKind == ProfileDeviceKind.Any ||
        profileKind == deviceKind;

    /// <summary>Short Slovak label for the UI.</summary>
    public static string Label(this ProfileDeviceKind kind) => kind switch
    {
        ProfileDeviceKind.Votsch => "Vötsch",
        ProfileDeviceKind.Sika => "SIKA",
        _ => "Univerzálny",
    };
}
