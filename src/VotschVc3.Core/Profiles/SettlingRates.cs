namespace VotschVc3.Core.Profiles;

/// <summary>
/// How long a device that drives itself to a set point (a SIKA TP bath / dry block) needs
/// before a dwell can start.
/// </summary>
/// <remarks>
/// <para>
/// A SIKA profile is a list of temperatures with a dwell time and no ramp segments – the
/// bath is told the new set point and reaches it on its own. The runner waits for the
/// measured temperature to come within the soak tolerance and only then starts counting the
/// dwell (see <c>ProfileRunner</c>, <c>soakAllSegments</c>), so the real run is always longer
/// than the sum of the dwell times by exactly this approach time. Planning a run off the
/// dwell sum alone therefore under-estimates it badly: eight steps of 30 min across
/// -40…+150 °C are 4 h of dwell but the best part of a working day in reality.
/// </para>
/// <para>
/// The model is deliberately simple: a constant rate while heating, a slower one while
/// cooling, an even slower one below 0 °C (the compressor has to work against a rising
/// temperature difference to ambient), plus a fixed allowance for the last fraction of a
/// degree, which no proportional rate describes well. The numbers are per-installation –
/// load in the block, the insert, ambient temperature and the age of the bath all change
/// them – so they are settings, not constants, and the app measures the real ones during
/// every run (each soak's duration is logged) so they can be corrected against the device.
/// </para>
/// </remarks>
/// <param name="HeatingCPerMin">Average heating rate (°C/min) while going up.</param>
/// <param name="CoolingCPerMin">Average cooling rate (°C/min) while going down, above 0 °C.</param>
/// <param name="CoolingBelowZeroCPerMin">Average cooling rate (°C/min) below 0 °C.</param>
/// <param name="StabilizeMinutes">Fixed allowance for settling inside the tolerance band.</param>
public sealed record SettlingRates(
    double HeatingCPerMin,
    double CoolingCPerMin,
    double CoolingBelowZeroCPerMin,
    double StabilizeMinutes)
{
    /// <summary>Temperature a run is assumed to start from when nothing is measured yet.</summary>
    public const double RoomTemperatureC = 23;

    /// <summary>Set points closer together than this need no approach time at all.</summary>
    private const double NegligibleStepC = 0.2;

    /// <summary>
    /// Default estimate for the lab's SIKA TP Premium baths. Starting values only – they are
    /// editable in Administrácia and should be corrected against the device's own behaviour
    /// (the measured settling time of every step is written to the application log).
    /// </summary>
    public static SettlingRates SikaDefault { get; } = new(
        HeatingCPerMin: 8,
        CoolingCPerMin: 5,
        CoolingBelowZeroCPerMin: 2.5,
        StabilizeMinutes: 5);

    /// <summary>No approach time at all – for devices whose profiles carry explicit ramps.</summary>
    public static SettlingRates None { get; } = new(0, 0, 0, 0);

    /// <summary><c>true</c> when this model adds nothing (every rate and the allowance are off).</summary>
    public bool IsEmpty => StabilizeMinutes <= 0 && HeatingCPerMin <= 0 && CoolingCPerMin <= 0;

    /// <summary>
    /// Time the device needs to get from <paramref name="fromC"/> to <paramref name="toC"/>
    /// and settle there. A step below <see cref="NegligibleStepC"/> costs nothing – the bath
    /// is already on temperature.
    /// </summary>
    public TimeSpan Estimate(double fromC, double toC)
    {
        double delta = toC - fromC;
        if (Math.Abs(delta) < NegligibleStepC)
        {
            return TimeSpan.Zero;
        }

        double minutes = delta > 0
            ? Divide(delta, HeatingCPerMin)
            : CoolingMinutes(fromC, toC);

        return TimeSpan.FromMinutes(minutes + Math.Max(0, StabilizeMinutes));
    }

    /// <summary>
    /// Total approach time of a whole run: the segments before the cycled region run once,
    /// the region repeats, and the segments after it run once. Every repetition after the
    /// first starts where the previous one ended, so its approach times differ from the
    /// first pass – both are counted separately.
    /// </summary>
    /// <param name="profile">The profile as it will run.</param>
    /// <param name="startC">Temperature the device sits at when the run starts.</param>
    public TimeSpan ForProfile(TestProfile? profile, double startC = RoomTemperatureC)
    {
        if (profile is null || profile.Segments.Count == 0 || IsEmpty)
        {
            return TimeSpan.Zero;
        }

        int cycles = Math.Max(1, profile.Cycles);
        int bodyStart = profile.ResolvedCycleStart;
        int bodyEnd = profile.ResolvedCycleEnd;

        double current = startC;
        TimeSpan total = Walk(profile, 0, bodyStart - 1, ref current);         // intro, once
        total += Walk(profile, bodyStart, bodyEnd, ref current);               // first pass of the body

        if (cycles > 1)
        {
            // Every later pass starts where the body ended, so they are all identical to
            // the second one – no need to walk them one by one.
            TimeSpan onePass = Walk(profile, bodyStart, bodyEnd, ref current);
            total += onePass * (cycles - 1);
        }

        total += Walk(profile, bodyEnd + 1, profile.Segments.Count - 1, ref current); // outro, once
        return total;
    }

    /// <summary>Approach time of segments <paramref name="first"/>…<paramref name="last"/>,
    /// advancing <paramref name="current"/> to the last target reached.</summary>
    private TimeSpan Walk(TestProfile profile, int first, int last, ref double current)
    {
        TimeSpan total = TimeSpan.Zero;
        for (int i = Math.Max(0, first); i <= last && i < profile.Segments.Count; i++)
        {
            double target = profile.Segments[i].TargetTemperature;
            total += Estimate(current, target);
            current = target;
        }

        return total;
    }

    /// <summary>Cooling is split at 0 °C – below it the bath loses far fewer degrees a minute.</summary>
    private double CoolingMinutes(double fromC, double toC)
    {
        double aboveZero = Math.Max(0, fromC - Math.Max(toC, 0));
        double belowZero = Math.Max(0, Math.Min(fromC, 0) - toC);
        return Divide(aboveZero, CoolingCPerMin) + Divide(belowZero, CoolingBelowZeroCPerMin);
    }

    /// <summary>Degrees ÷ rate, with a rate of zero meaning "no time charged for this part".</summary>
    private static double Divide(double degrees, double ratePerMinute) =>
        ratePerMinute > 0 && degrees > 0 ? degrees / ratePerMinute : 0;
}
