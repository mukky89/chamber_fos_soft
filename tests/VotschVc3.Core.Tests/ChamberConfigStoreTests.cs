using VotschVc3.Core.Profiles;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class ChamberConfigStoreTests
{
    [Fact]
    public void SaveAndLoad_PreservesManualTimerSettings()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"votsch-config-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "chambers.json");

        try
        {
            var store = new ChamberConfigStore(path);
            store.SaveAll([
                new ChamberConfig
                {
                    Name = "Komora 2",
                    ManualTimerEnabled = true,
                    ManualDurationMinutes = 30,
                },
            ]);

            ChamberConfig restored = Assert.Single(store.LoadAll());
            Assert.True(restored.ManualTimerEnabled);
            Assert.Equal(30, restored.ManualDurationMinutes);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
