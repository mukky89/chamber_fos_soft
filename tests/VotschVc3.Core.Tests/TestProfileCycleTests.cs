using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

public class TestProfileCycleTests
{
    private static TestProfile FourSegments(int cycles, int start, int end) => new()
    {
        Cycles = cycles,
        CycleStartIndex = start,
        CycleEndIndex = end,
        Segments =
        {
            new ProfileSegment { Duration = TimeSpan.FromMinutes(10) },
            new ProfileSegment { Duration = TimeSpan.FromMinutes(20) },
            new ProfileSegment { Duration = TimeSpan.FromMinutes(30) },
            new ProfileSegment { Duration = TimeSpan.FromMinutes(40) },
        },
    };

    [Fact]
    public void Total_duration_repeats_only_the_marked_region()
    {
        // Region = segments 1..2 (20 + 30), repeated 3×; segment 0 (intro) and 3 (outro) run once.
        TestProfile profile = FourSegments(cycles: 3, start: 1, end: 2);

        Assert.True(profile.HasCycleRegion);
        Assert.Equal(TimeSpan.FromMinutes(100), profile.SinglePassDuration);
        // 10 + (20+30)*3 + 40 = 200
        Assert.Equal(TimeSpan.FromMinutes(200), profile.TotalDuration);
    }

    [Fact]
    public void No_region_repeats_whole_profile_like_before()
    {
        TestProfile profile = FourSegments(cycles: 2, start: -1, end: -1);

        Assert.False(profile.HasCycleRegion);
        Assert.Equal(0, profile.ResolvedCycleStart);
        Assert.Equal(3, profile.ResolvedCycleEnd);
        // whole profile ×2 = 100 * 2
        Assert.Equal(TimeSpan.FromMinutes(200), profile.TotalDuration);
    }

    [Fact]
    public void Single_cycle_is_never_a_region()
    {
        TestProfile profile = FourSegments(cycles: 1, start: 1, end: 2);

        Assert.False(profile.HasCycleRegion);
        Assert.Equal(TimeSpan.FromMinutes(100), profile.TotalDuration);
    }
}
