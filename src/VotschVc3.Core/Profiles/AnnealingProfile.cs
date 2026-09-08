using System.Text.Json;

namespace VotschVc3.Core.Profiles;

public static class AnnealingProfile
{
    public static List<ProfileSegment> Build(double temperature, double holdMinutes, double rampUpMinutes, double exitTemperature, double rampDownMinutes, double? humidity = null) =>
    [
        new() { Name = "Nábeh na žíhanie", TargetTemperature = temperature, TargetHumidity = humidity, IsRamp = true, Duration = TimeSpan.FromMinutes(Math.Max(0, rampUpMinutes)) },
        new() { Name = "Žíhanie", TargetTemperature = temperature, TargetHumidity = humidity, IsRamp = false, Duration = TimeSpan.FromMinutes(Math.Max(0, holdMinutes)) },
        new() { Name = "Prechod na nasledujúci profil", TargetTemperature = exitTemperature, TargetHumidity = humidity, IsRamp = true, Duration = TimeSpan.FromMinutes(Math.Max(0, rampDownMinutes)) }
    ];

    public static IReadOnlyList<TestProfile> ConnectToFollowingProfiles(IReadOnlyList<TestProfile> profiles)
    {
        var result = profiles.ToArray();
        for (int i = 0; i < result.Length - 1; i++)
        {
            if (!result[i].IsAnnealing || result[i].Segments.Count != 3 || !result[i].Segments[^1].IsRamp || result[i + 1].Segments.Count == 0) continue;
            // Adapt the execution copy only. Never rewrite the saved library profile.
            var copy = JsonSerializer.Deserialize<TestProfile>(JsonSerializer.Serialize(result[i]))!;
            copy.Segments[^1].TargetTemperature = result[i + 1].Segments[0].TargetTemperature;
            result[i] = copy;
        }
        return result;
    }
}
