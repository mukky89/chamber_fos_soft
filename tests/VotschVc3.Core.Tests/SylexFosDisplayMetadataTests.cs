using VotschVc3.App.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class SylexFosDisplayMetadataTests
{
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
