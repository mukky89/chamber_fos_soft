using System.Text.Json;
using VotschVc3.Core.Settings;
using Xunit;

namespace VotschVc3.Core.Tests;

public class UiSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "votsch-ui-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_dir, "ui.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void A_fresh_installation_starts_with_the_timeline_hidden()
    {
        UiSettings settings = new UiSettingsStore(SettingsPath).Load();

        Assert.False(settings.ShowTimeline);
    }

    /// <summary>
    /// A settings file written before the default changed still says ShowTimeline = true,
    /// so the store has to reset it once – otherwise the new default never reaches an
    /// existing installation.
    /// </summary>
    [Fact]
    public void An_existing_file_from_before_the_change_is_migrated_once()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { ShowTimeline = true }));
        var store = new UiSettingsStore(SettingsPath);

        Assert.False(store.Load().ShowTimeline);

        // The migration persisted, so turning the timeline back on is not undone.
        UiSettings chosen = store.Load();
        chosen.ShowTimeline = true;
        store.Save(chosen);

        Assert.True(store.Load().ShowTimeline);
    }

    [Fact]
    public void Other_preferences_survive_the_migration()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new
        {
            ShowTimeline = true,
            CompactMode = true,
            SikaSoakToleranceC = 0.7,
            ProfileLogIntervalSeconds = 15,
        }));

        UiSettings settings = new UiSettingsStore(SettingsPath).Load();

        Assert.False(settings.ShowTimeline);
        Assert.True(settings.CompactMode);
        Assert.Equal(0.7, settings.SikaSoakToleranceC);
        Assert.Equal(15, settings.ProfileLogIntervalSeconds);
    }
}
