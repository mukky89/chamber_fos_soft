using System.Text.Json;
using System.Text.Json.Serialization;
using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>
/// The library is a folder with one file per profile, named after the profile – a single
/// profile can be looked at, copied out or handed over without exporting the whole library.
/// </summary>
public class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), $"votsch-profiles-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string LegacyFile => Path.Combine(_dir, "profiles.json");

    private string[] ProfileFiles() => Directory.Exists(_dir)
        ? Directory.GetFiles(_dir, "*.json").Select(Path.GetFileName).OrderBy(n => n).ToArray()!
        : Array.Empty<string>();

    [Fact]
    public void Every_profile_gets_its_own_file_named_after_it()
    {
        var store = new ProfileStore(_dir);

        store.Save(new TestProfile { Name = "Sweep -40…150" });
        store.Save(new TestProfile { Name = "Cyklovanie 25/85" });

        Assert.Equal(new[] { "Cyklovanie 25_85.json", "Sweep -40…150.json" }, ProfileFiles());
        Assert.Equal(2, store.LoadAll().Count);
    }

    [Fact]
    public void Saving_the_same_profile_again_rewrites_its_own_file()
    {
        var store = new ProfileStore(_dir);
        var profile = new TestProfile { Name = "Profil A", Segments = { new ProfileSegment { TargetTemperature = 20 } } };

        store.Save(profile);
        profile.Segments.Add(new ProfileSegment { TargetTemperature = 80 });
        store.Save(profile);

        Assert.Single(ProfileFiles());
        Assert.Equal(2, Assert.Single(store.LoadAll()).Segments.Count);
    }

    /// <summary>A renamed profile must not leave its old file behind – the folder would fill
    /// up with every name a profile ever had.</summary>
    [Fact]
    public void Renaming_a_profile_renames_its_file()
    {
        var store = new ProfileStore(_dir);
        var profile = new TestProfile { Name = "Starý názov" };
        store.Save(profile);

        profile.Name = "Nový názov";
        store.Save(profile);

        Assert.Equal(new[] { "Nový názov.json" }, ProfileFiles());
        Assert.Equal("Nový názov", Assert.Single(store.LoadAll()).Name);
    }

    /// <summary>Two profiles may legitimately share a name; neither may overwrite the other.</summary>
    [Fact]
    public void Two_profiles_with_the_same_name_get_separate_files()
    {
        var store = new ProfileStore(_dir);
        var first = new TestProfile { Name = "Rovnaký názov" };
        var second = new TestProfile { Name = "Rovnaký názov" };

        store.Save(first);
        store.Save(second);

        Assert.Equal(2, ProfileFiles().Length);
        Assert.Equal(2, store.LoadAll().Count);
        Assert.Contains(store.LoadAll(), p => p.Id == first.Id);
        Assert.Contains(store.LoadAll(), p => p.Id == second.Id);
    }

    [Fact]
    public void Characters_a_file_name_cannot_hold_are_replaced()
    {
        string name = ProfileStore.FileNameFor("-40/150 °C : 3 cykly");

        Assert.Equal(-1, name.IndexOfAny(Path.GetInvalidFileNameChars()));
        Assert.DoesNotContain('/', name);
        Assert.StartsWith("-40_150 °C", name);
        Assert.EndsWith("3 cykly", name);

        Assert.Equal("profil", ProfileStore.FileNameFor("   "));
        Assert.Equal("bez bodky", ProfileStore.FileNameFor("bez bodky. "));
    }

    [Fact]
    public void Delete_removes_only_that_profiles_file()
    {
        var store = new ProfileStore(_dir);
        var keep = new TestProfile { Name = "Ostáva" };
        var drop = new TestProfile { Name = "Mizne" };
        store.Save(keep);
        store.Save(drop);

        Assert.True(store.Delete(drop.Id));

        Assert.Equal(new[] { "Ostáva.json" }, ProfileFiles());
        Assert.Equal(keep.Id, Assert.Single(store.LoadAll()).Id);
    }

    [Fact]
    public void Clear_empties_the_folder()
    {
        var store = new ProfileStore(_dir);
        store.Save(new TestProfile { Name = "A" });
        store.Save(new TestProfile { Name = "B" });

        Assert.Equal(2, store.Clear());
        Assert.Empty(ProfileFiles());
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void Save_StampsUpdatedAt_SoTheNewestProfilesCanBeListedFirst()
    {
        var store = new ProfileStore(_dir);
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

        // LoadAll itself hands back the newest change first.
        Assert.Equal(new[] { "Novší", "Starší" }, all.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void LastChangedAt_FallsBackToCreatedAt_ForProfilesStoredBeforeItWasTracked()
    {
        DateTimeOffset created = DateTimeOffset.Now.AddYears(-1);
        var profile = new TestProfile { Name = "Staršia knižnica", CreatedAt = created };

        Assert.Null(profile.UpdatedAt);
        Assert.Equal(created, profile.LastChangedAt);
    }

    // ---- migration off the old single-file library --------------------------------------

    private void WriteLegacyLibrary(params TestProfile[] profiles)
    {
        Directory.CreateDirectory(_dir);
        var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        File.WriteAllText(LegacyFile, JsonSerializer.Serialize(profiles.ToList(), options));
    }

    [Fact]
    public void An_old_single_file_library_is_split_into_one_file_per_profile()
    {
        WriteLegacyLibrary(
            new TestProfile { Name = "Prvý", Segments = { new ProfileSegment { TargetTemperature = 40 } } },
            new TestProfile { Name = "Druhý" });

        var store = new ProfileStore(LegacyFile);
        List<TestProfile> all = store.LoadAll();

        Assert.Equal(2, all.Count);
        Assert.Equal(new[] { "Druhý.json", "Prvý.json" }, ProfileFiles());
        Assert.Equal(40, all.Single(p => p.Name == "Prvý").Segments[0].TargetTemperature);
    }

    /// <summary>The operator's original library is never deleted – it is kept as a backup.</summary>
    [Fact]
    public void The_old_library_file_is_kept_as_a_backup_and_never_read_again()
    {
        WriteLegacyLibrary(new TestProfile { Name = "Prvý" });

        var store = new ProfileStore(LegacyFile);
        store.LoadAll();

        Assert.False(File.Exists(LegacyFile));
        Assert.True(File.Exists(LegacyFile + ProfileStore.MigratedSuffix));

        // A second store over the same folder must not re-import the backup.
        Assert.Single(new ProfileStore(LegacyFile).LoadAll());
    }

    [Fact]
    public void A_file_that_is_not_a_profile_is_ignored_instead_of_showing_up_as_an_empty_one()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "poznamky.json"), "{}");

        var store = new ProfileStore(_dir);
        store.Save(new TestProfile { Name = "Skutočný profil" });

        Assert.Equal("Skutočný profil", Assert.Single(store.LoadAll()).Name);
    }

    [Fact]
    public void A_corrupt_file_does_not_take_the_rest_of_the_library_with_it()
    {
        var store = new ProfileStore(_dir);
        store.Save(new TestProfile { Name = "Dobrý" });
        File.WriteAllText(Path.Combine(_dir, "rozbity.json"), "{ this is not json");

        Assert.Equal("Dobrý", Assert.Single(store.LoadAll()).Name);
    }

    [Fact]
    public void AddMissing_skips_what_is_already_there_and_writes_the_rest()
    {
        var store = new ProfileStore(_dir);
        var existing = new TestProfile { Name = "Už tam je" };
        store.Save(existing);

        int added = store.AddMissing(new[]
        {
            new TestProfile { Id = existing.Id, Name = "Už tam je" },   // same id
            new TestProfile { Name = "už tam je" },                     // same name, other case
            new TestProfile { Name = "Nový" },
        });

        Assert.Equal(1, added);
        Assert.Equal(new[] { "Nový.json", "Už tam je.json" }, ProfileFiles());
    }
}
