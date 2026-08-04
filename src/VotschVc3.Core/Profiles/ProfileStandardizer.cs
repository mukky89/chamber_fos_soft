namespace VotschVc3.Core.Profiles;

/// <summary>Tunable parameters for <see cref="ProfileStandardizer"/>.</summary>
public sealed class ProfileStandardizationOptions
{
    /// <summary>Room / "safe" temperature (°C) the profile is ramped back to at the end.</summary>
    public double RoomTemperature { get; set; } = 25;

    /// <summary>Length (min) of the prepended initial ramp to the first temperature.</summary>
    public double InitialRampMinutes { get; set; } = 60;

    /// <summary>Length (min) of the appended final ramp to <see cref="RoomTemperature"/>.</summary>
    public double FinalRampMinutes { get; set; } = 60;

    /// <summary>Length (min) of the closing hold at <see cref="RoomTemperature"/>. Zero = no hold.</summary>
    public double FinalHoldMinutes { get; set; } = 60;

    /// <summary>Tolerance (°C) for deciding a segment is "already at" a temperature.</summary>
    public double TemperatureTolerance { get; set; } = 0.5;
}

/// <summary>
/// Applies the lab's profile standard to a <see cref="TestProfile"/>:
/// every profile must <b>begin with a ramp to its first temperature</b> and
/// <b>end with a ramp to room temperature</b> (optionally held for a while), so a
/// run always eases onto the first setpoint and parks the chamber near room
/// temperature before the power is cut. The change is made in place; the returned
/// list describes what was added (for a human-readable summary).
/// </summary>
public static class ProfileStandardizer
{
    public static IReadOnlyList<string> Standardize(TestProfile profile, ProfileStandardizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        options ??= new ProfileStandardizationOptions();
        var notes = new List<string>();

        if (profile.Segments.Count == 0)
        {
            notes.Add("Profil nemá segmenty – štandard sa nedá aplikovať.");
            return notes;
        }

        double tolerance = Math.Abs(options.TemperatureTolerance);

        // 1) Initial ramp to the first temperature. If the profile already starts by
        //    ramping, that ramp *is* the lead-in; otherwise prepend one.
        ProfileSegment first = profile.Segments[0];
        if (!first.IsRamp)
        {
            profile.Segments.Insert(0, new ProfileSegment
            {
                Name = $"Nábeh na {first.TargetTemperature:0.#} °C",
                TargetTemperature = first.TargetTemperature,
                TargetHumidity = first.TargetHumidity,
                Duration = TimeSpan.FromMinutes(Math.Max(0, options.InitialRampMinutes)),
                IsRamp = true,
            });
            notes.Add($"Pridaný začiatočný nábeh na {first.TargetTemperature:0.#} °C.");
        }

        // 2) Final ramp to room temperature (+ optional hold). Skip when the profile
        //    already ends at room temperature.
        ProfileSegment last = profile.Segments[^1];
        if (Math.Abs(last.TargetTemperature - options.RoomTemperature) > tolerance)
        {
            double? humidity = last.TargetHumidity;
            profile.Segments.Add(new ProfileSegment
            {
                Name = $"Nábeh na izbovú {options.RoomTemperature:0.#} °C",
                TargetTemperature = options.RoomTemperature,
                TargetHumidity = humidity,
                Duration = TimeSpan.FromMinutes(Math.Max(0, options.FinalRampMinutes)),
                IsRamp = true,
            });

            if (options.FinalHoldMinutes > 0)
            {
                profile.Segments.Add(new ProfileSegment
                {
                    Name = $"Plato izbová {options.RoomTemperature:0.#} °C",
                    TargetTemperature = options.RoomTemperature,
                    TargetHumidity = humidity,
                    Duration = TimeSpan.FromMinutes(options.FinalHoldMinutes),
                    IsRamp = false,
                });
            }

            notes.Add(options.FinalHoldMinutes > 0
                ? $"Pridaný koncový nábeh na izbovú {options.RoomTemperature:0.#} °C + plato {options.FinalHoldMinutes:0} min."
                : $"Pridaný koncový nábeh na izbovú {options.RoomTemperature:0.#} °C.");
        }
        else
        {
            notes.Add($"Profil už končí na izbovej teplote ({options.RoomTemperature:0.#} °C).");
        }

        return notes;
    }
}
