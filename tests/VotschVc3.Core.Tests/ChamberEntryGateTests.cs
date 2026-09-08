using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;
public sealed class ChamberEntryGateTests
{
    [Fact]
    public void StatusReportsMissingMetricsAndRetainsConfirmedEvidence()
    {
        var settings = new CalibrationProfileSettings { ChamberEntryEnabled = true };
        var gate = new ChamberEntryGate();
        var start = DateTimeOffset.UtcNow;
        gate.Add(start, -42, -40, settings);
        Assert.Equal(2, gate.Status!.DeviationC);
        Assert.Null(gate.Status.RangeC);
        gate.Add(start.AddSeconds(30), -40, -40, settings);
        Assert.Null(gate.Status!.DriftCPerMinute);
        for (int i = 2; i <= 5; i++) gate.Add(start.AddSeconds(30 * i), -40, -40, settings);
        var confirmed = gate.Status;
        Assert.True(confirmed!.IsOpen);
        Assert.Equal(120, confirmed.WindowSeconds);
        Assert.Equal(0, confirmed.RangeC);
        gate.Add(start.AddSeconds(180), -42, -40, settings);
        Assert.Equal(confirmed, gate.Status);
    }
    [Fact]
    public void DisabledGateReportsDisabledWithoutPretendingToPass()
    {
        var gate = new ChamberEntryGate();
        Assert.True(gate.Add(DateTimeOffset.UtcNow, 25, 25, new()));
        Assert.False(gate.Status!.Enabled);
        Assert.False(gate.Status.IsOpen);
    }
    [Fact]
    public void RequiresFullWindowAndRejectsOvershoot()
    {
        var settings = new CalibrationProfileSettings { ChamberEntryEnabled = true };
        var gate = new ChamberEntryGate();
        var start = DateTimeOffset.UtcNow;
        Assert.False(gate.Add(start, -42, -40, settings));
        for (int seconds = 30; seconds < 150; seconds += 30)
            Assert.False(gate.Add(start.AddSeconds(seconds), -40.1, -40, settings));
        Assert.True(gate.Add(start.AddSeconds(150), -40.1, -40, settings));
    }
    [Fact]
    public void PassingThroughToleranceWhileDriftingDoesNotOpen()
    {
        var settings = new CalibrationProfileSettings { ChamberEntryEnabled = true };
        var gate = new ChamberEntryGate();
        var start = DateTimeOffset.UtcNow;
        for (int i = 0; i <= 4; i++)
            Assert.False(gate.Add(start.AddSeconds(30 * i), -40.4 + .2 * i, -40, settings));
    }
    [Fact]
    public void MissingMeasurementsCannotCountAsStableTime()
    {
        var settings = new CalibrationProfileSettings { ChamberEntryEnabled = true };
        var gate = new ChamberEntryGate();
        var start = DateTimeOffset.UtcNow;
        Assert.False(gate.Add(start, 25, 25, settings));
        Assert.False(gate.Add(start.AddMinutes(5), 25, 25, settings));
    }
    [Fact]
    public void DefaultsEnableGateButLegacyCheckpointDoesNotChange()
    {
        var legacy = System.Text.Json.JsonSerializer.Deserialize<CalibrationProfileSettings>("{}")!;
        Assert.False(legacy.ChamberEntryEnabled);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new CalibrationDefaultsStore(path);
            Assert.True(store.Load().ChamberEntryEnabled);
            File.WriteAllText(path, "{}");
            Assert.True(store.Load().ChamberEntryEnabled);
            store.Save(new CalibrationProfileSettings { ChamberEntryEnabled = false });
            Assert.False(store.Load().ChamberEntryEnabled);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
