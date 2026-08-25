using VotschVc3.Core.Settings;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class BridgeStatusTests
{
    [Fact]
    public void StatusFileRoundTripsHealthWithoutSecrets()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bridge-status-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "bridge-status.json");
        try
        {
            var expected = new BridgeStatus
            {
                Running = true,
                DashboardReachable = true,
                UpdatedUtc = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc),
                DashboardUrl = "https://fos.example",
                MachineName = "LAB-PC",
                Version = "1.52.1",
            };

            BridgeStatusFile.Write(path, expected);
            BridgeStatus? actual = BridgeStatusFile.Read(path);

            Assert.NotNull(actual);
            Assert.True(actual.Running);
            Assert.True(actual.DashboardReachable);
            Assert.Equal(expected.DashboardUrl, actual.DashboardUrl);
            Assert.DoesNotContain("agentKey", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
