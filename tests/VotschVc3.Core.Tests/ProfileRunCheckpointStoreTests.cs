using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>Round-trip and overwrite behaviour of the profile-run checkpoint store used for
/// recovering a run interrupted by a power outage or an app crash.</summary>
public class ProfileRunCheckpointStoreTests
{
    private static ProfileRunCheckpointStore CreateStore(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "ProfileRunCheckpointStoreTests_" + Guid.NewGuid().ToString("N"));
        return new ProfileRunCheckpointStore(dir);
    }

    private static ProfileRunCheckpoint MakeCheckpoint(Guid chamberId, int segmentIndex = 2) => new()
    {
        ChamberId = chamberId,
        ChamberName = "Komora 1",
        Profile = new TestProfile
        {
            Name = "IEC 60068-2-14",
            Segments =
            {
                new ProfileSegment { TargetTemperature = -20, Duration = TimeSpan.FromMinutes(30) },
                new ProfileSegment { TargetTemperature = 60, Duration = TimeSpan.FromMinutes(30) },
            },
        },
        RemainingQueue = new List<TestProfile> { new() { Name = "Ďalší v poradí" } },
        SegmentIndex = segmentIndex,
        CycleIndex = 1,
        Phase = ProfileRunPhase.Cycle,
        ElapsedInSegmentSeconds = 123.5,
        SegmentStartTemperature = -40,
        SegmentStartHumidity = 35,
        IsSoaking = false,
        StartedUtc = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
        LastWriteUtc = new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void TryLoad_returns_null_when_nothing_was_ever_saved()
    {
        var store = CreateStore(out _);
        Assert.Null(store.TryLoad(Guid.NewGuid()));
    }

    [Fact]
    public void Save_then_TryLoad_round_trips_every_field()
    {
        var store = CreateStore(out _);
        Guid chamberId = Guid.NewGuid();
        ProfileRunCheckpoint saved = MakeCheckpoint(chamberId);

        store.Save(saved);
        ProfileRunCheckpoint? loaded = store.TryLoad(chamberId);

        Assert.NotNull(loaded);
        Assert.Equal(saved.ChamberId, loaded!.ChamberId);
        Assert.Equal(saved.ChamberName, loaded.ChamberName);
        Assert.Equal(saved.Profile.Name, loaded.Profile.Name);
        Assert.Equal(saved.Profile.Segments.Count, loaded.Profile.Segments.Count);
        Assert.Equal(saved.RemainingQueue.Count, loaded.RemainingQueue.Count);
        Assert.Equal(saved.SegmentIndex, loaded.SegmentIndex);
        Assert.Equal(saved.CycleIndex, loaded.CycleIndex);
        Assert.Equal(saved.Phase, loaded.Phase);
        Assert.Equal(saved.ElapsedInSegmentSeconds, loaded.ElapsedInSegmentSeconds);
        Assert.Equal(saved.SegmentStartTemperature, loaded.SegmentStartTemperature);
        Assert.Equal(saved.SegmentStartHumidity, loaded.SegmentStartHumidity);
        Assert.Equal(saved.IsSoaking, loaded.IsSoaking);
        Assert.Equal(saved.StartedUtc, loaded.StartedUtc);
        Assert.Equal(saved.LastWriteUtc, loaded.LastWriteUtc);
    }

    [Fact]
    public void Save_overwrites_the_previous_checkpoint_for_the_same_chamber()
    {
        var store = CreateStore(out _);
        Guid chamberId = Guid.NewGuid();

        store.Save(MakeCheckpoint(chamberId, segmentIndex: 0));
        store.Save(MakeCheckpoint(chamberId, segmentIndex: 5));

        Assert.Equal(5, store.TryLoad(chamberId)!.SegmentIndex);
    }

    [Fact]
    public void Checkpoints_for_different_chambers_do_not_collide()
    {
        var store = CreateStore(out _);
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();

        store.Save(MakeCheckpoint(a, segmentIndex: 1));
        store.Save(MakeCheckpoint(b, segmentIndex: 9));

        Assert.Equal(1, store.TryLoad(a)!.SegmentIndex);
        Assert.Equal(9, store.TryLoad(b)!.SegmentIndex);
    }

    [Fact]
    public void Delete_removes_the_checkpoint_and_is_safe_when_none_exists()
    {
        var store = CreateStore(out _);
        Guid chamberId = Guid.NewGuid();
        store.Save(MakeCheckpoint(chamberId));

        store.Delete(chamberId);
        Assert.Null(store.TryLoad(chamberId));

        // Deleting again (nothing left to delete) must not throw.
        store.Delete(chamberId);
    }

    [Fact]
    public void TryLoad_ignores_a_corrupt_checkpoint_file_instead_of_throwing()
    {
        Guid chamberId = Guid.NewGuid();
        var store = CreateStore(out string dir);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{chamberId:N}.json"), "{ not valid json");

        Assert.Null(store.TryLoad(chamberId));
    }
}
