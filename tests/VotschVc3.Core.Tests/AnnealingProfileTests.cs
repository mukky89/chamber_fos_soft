using System.Text.Json;
using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

public class AnnealingProfileTests
{
    private static TestProfile Annealing() => new() { IsAnnealing = true, Segments = AnnealingProfile.Build(80, 90, 20, 25, 30) };

    [Fact]
    public void HasExactlyOnePlateauBetweenIndependentRamps()
    {
        var segments = Annealing().Segments;
        Assert.Equal(3, segments.Count);
        Assert.True(segments[0].IsRamp);
        Assert.False(segments[1].IsRamp);
        Assert.True(segments[2].IsRamp);
        Assert.Equal(new double[] { 80, 80, 25 }, segments.Select(s => s.TargetTemperature));
        Assert.Equal(new double[] { 20, 90, 30 }, segments.Select(s => s.Duration.TotalMinutes));
    }

    [Fact]
    public void ChainAdaptsExecutionCopyAndPreservesLibraryProfile()
    {
        var original = Annealing();
        var next = new TestProfile { Segments = [new() { TargetTemperature = -40 }] };
        var connected = AnnealingProfile.ConnectToFollowingProfiles([original, next]);
        Assert.Equal(-40, connected[0].Segments[2].TargetTemperature);
        Assert.Equal(25, original.Segments[2].TargetTemperature);
        Assert.Equal(TimeSpan.FromMinutes(30), connected[0].Segments[2].Duration);
        Assert.Same(next, connected[1]);
        Assert.True(connected[0].IsAnnealing);
    }

    [Fact]
    public void StandalonePreservesConfiguredExit()
    {
        var original = Annealing();
        Assert.Same(original, AnnealingProfile.ConnectToFollowingProfiles([original])[0]);
    }

    [Fact]
    public void OrdinaryProfileDoesNotChangeItsExit()
    {
        var original = Annealing();
        original.IsAnnealing = false;
        Assert.Same(original, AnnealingProfile.ConnectToFollowingProfiles([original, Annealing()])[0]);
    }

    [Fact]
    public void SavedProfileRetainsAnnealingModeAndSegments()
    {
        var restored = JsonSerializer.Deserialize<TestProfile>(JsonSerializer.Serialize(Annealing()))!;
        Assert.True(restored.IsAnnealing);
        Assert.Equal(3, restored.Segments.Count);
        Assert.Equal(90, restored.Segments[1].Duration.TotalMinutes);
    }
}
