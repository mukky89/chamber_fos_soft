using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

public class ProfileStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"votsch-profiles-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Save_StampsUpdatedAt_SoTheNewestProfilesCanBeListedFirst()
    {
        var store = new ProfileStore(_path);
        var older = new TestProfile { Name = "Starší", CreatedAt = DateTimeOffset.Now.AddDays(-10) };
        var newer = new TestProfile { Name = "Novší", CreatedAt = DateTimeOffset.Now.AddDays(-10) };

        store.Save(older);
        store.Save(newer);

        List<TestProfile> all = store.LoadAll();
        TestProfile loadedOlder = all.Single(p => p.Id == older.Id);
        TestProfile loadedNewer = all.Single(p => p.Id == newer.Id);

        Assert.NotNull(loadedOlder.UpdatedAt);
        Assert.NotNull(loadedNewer.UpdatedAt);
        Assert.True(loadedNewer.LastChangedAt >= loadedOlder.LastChangedAt);
        Assert.Equal(
            new[] { "Novší", "Starší" },
            all.OrderByDescending(p => p.LastChangedAt).Select(p => p.Name).ToArray());
    }

    [Fact]
    public void LastChangedAt_FallsBackToCreatedAt_ForProfilesStoredBeforeItWasTracked()
    {
        DateTimeOffset created = DateTimeOffset.Now.AddYears(-1);
        var profile = new TestProfile { Name = "Staršia knižnica", CreatedAt = created };

        Assert.Null(profile.UpdatedAt);
        Assert.Equal(created, profile.LastChangedAt);
    }
}
