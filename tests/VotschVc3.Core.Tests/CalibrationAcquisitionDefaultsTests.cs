using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationAcquisitionDefaultsTests
{
    [Fact]
    public void NewRunUsesLatestAdminIntervalWithoutReplacingOtherSetupValues()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CalibrationDefaultsStore(Path.Combine(root, "defaults.json"));
            store.Save(new CalibrationProfileSettings { SampleAcquisitionIntervalSeconds = 10 });
            var savedSetup = new CalibrationProfileSettings { SampleAcquisitionIntervalSeconds = 1, RequiredStableSamples = 77 };
            store.ApplyAcquisitionInterval(savedSetup, preserveRunSettings: false);
            Assert.Equal(10, savedSetup.SampleAcquisitionIntervalSeconds);
            Assert.Equal(77, savedSetup.RequiredStableSamples);

            // Admin may change the interval after the calibration window has opened.
            store.Save(new CalibrationProfileSettings { SampleAcquisitionIntervalSeconds = 20 });
            store.ApplyAcquisitionInterval(savedSetup, preserveRunSettings: false);
            Assert.Equal(20, savedSetup.SampleAcquisitionIntervalSeconds);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ActiveOrResumedRunKeepsItsOriginalInterval()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CalibrationDefaultsStore(Path.Combine(root, "defaults.json"));
            store.Save(new CalibrationProfileSettings { SampleAcquisitionIntervalSeconds = 10 });
            var snapshot = new CalibrationProfileSettings { SampleAcquisitionIntervalSeconds = 1 };
            store.ApplyAcquisitionInterval(snapshot, preserveRunSettings: true);
            Assert.Equal(1, snapshot.SampleAcquisitionIntervalSeconds);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
