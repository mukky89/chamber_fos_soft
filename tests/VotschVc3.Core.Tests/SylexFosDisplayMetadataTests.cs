using VotschVc3.App.Calibration;
using VotschVc3.Core.Calibration;
using System.Text.Json;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class SylexFosDisplayMetadataTests
{
    [Fact]
    public void SavedTypeAndExplanationRestoreWithoutApiAndNotifyBoundCells()
    {
        var mapping = new CalibrationSensorMapping { SerialNumber = "291877/0001",
            ProductionFbgType = "T", ProductionFbgTypeDetail = "Matched peak P1", SensorName = "SC-01/T" };
        var restored = JsonSerializer.Deserialize<CalibrationSensorMapping>(JsonSerializer.Serialize(mapping));
        var metadata = new SylexFosDisplayMetadata();
        var changes = new List<string?>();
        metadata.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        metadata.Restore(restored);
        Assert.Equal("T", metadata.FbgType);
        Assert.Equal("Matched peak P1", metadata.FbgTypeDetail);
        Assert.Contains("FbgType", changes);
        Assert.Contains("FbgTypeDetail", changes);
    }

    [Fact]
    public void LegacyMappingCannotEraseLiveTypeForSameSerialButNewSerialClearsIt()
    {
        var metadata = new SylexFosDisplayMetadata { SylexSerialNumber = "291877/0001",
            FbgType = "T", FbgTypeDetail = "Old peak", SensorName = "SC-01/T" };
        metadata.Restore(new CalibrationSensorMapping { SerialNumber = "291877/0001" });
        Assert.Equal("T", metadata.FbgType);
        metadata.Restore(new CalibrationSensorMapping { SerialNumber = "291878/0001" });
        Assert.Empty(metadata.FbgType);
        Assert.Empty(metadata.FbgTypeDetail);
        Assert.Empty(metadata.SensorName);
    }

    [Fact]
    public void ApiFieldsNotifyCellsOnlyWhenValuesChange()
    {
        var metadata = new SylexFosDisplayMetadata();
        var changes = new List<string?>();
        metadata.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        metadata.SensorName = "SC-01/T";
        metadata.SylexSerialNumber = "291877/0001";
        metadata.FbgType = "T";
        metadata.SensorName = "SC-01/T";
        metadata.SylexSerialNumber = "291877/0001";
        metadata.FbgType = "T";
        Assert.Equal(new[] { "SensorName", "SylexSerialNumber", "FbgType" }, changes);
    }

    [Fact]
    public void ClearingAssignmentNotifiesAllOldValuesWithoutReplacingBindingSource()
    {
        var metadata = new SylexFosDisplayMetadata
        {
            SensorName = "SC-01/T", SylexSerialNumber = "291877/0001", FbgType = "T"
        };
        var changes = new List<string?>();
        metadata.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        metadata.Clear();
        metadata.Clear();
        Assert.Equal(new[] { "SensorName", "SylexSerialNumber", "FbgType" }, changes);
        Assert.Equal(string.Empty, metadata.SensorName);
        Assert.Equal(string.Empty, metadata.SylexSerialNumber);
        Assert.Equal(string.Empty, metadata.FbgType);
    }
}
