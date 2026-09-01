using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class SylexFosApiSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sylex-fos-settings-{Guid.NewGuid():N}");

    [Fact]
    public void Saves_and_loads_hostname_and_api_key()
    {
        string path = Path.Combine(_directory, "sylex-fos-api.json");
        var store = new SylexFosApiSettingsStore(path);
        store.Save(new SylexFosApiSettings { BaseUrl = "fos-api-pc:5080/", ApiKey = "secret" });

        SylexFosApiSettings loaded = store.Load();

        Assert.Equal("http://fos-api-pc:5080", loaded.BaseUrl);
        Assert.Equal("secret", loaded.ResolveApiKey());
    }

    [Theory]
    [InlineData("http://localhost:5080", "http://localhost:5080")]
    [InlineData("FOS-SERVER:5080", "http://fos-server:5080")]
    [InlineData("https://fos-api.sylex.local/", "https://fos-api.sylex.local")]
    public void Normalizes_supported_urls(string input, string expected) =>
        Assert.Equal(expected, SylexFosApiSettingsStore.NormalizeBaseUrl(input));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
