using VotschVc3.Core.Calibration;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class CalibrationAcquisitionDefaultsTests
{
    [Fact]
    public void ChamberDefaultsMigrateLegacySetupAndKeepLocalEditsUntilAdminChanges()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CalibrationDefaultsStore(Path.Combine(root, "defaults.json"));
            var defaults = new CalibrationProfileSettings { ChamberEntryEnabled = true,
                ChamberEntryToleranceC = 0.7, ChamberEntryStableSeconds = 180,
                ChamberEntryRangeC = 0.4, ChamberEntryDriftCPerMinute = 0.08 };
            store.Save(defaults);
            var setup = new CalibrationProfileSettings { ChamberEntryEnabled = false, RequiredStableSamples = 77 };
            store.ApplyAcquisitionInterval(setup, false);
            Assert.True(setup.ChamberEntryEnabled);
            Assert.Equal(0.7, setup.ChamberEntryToleranceC);
            Assert.Equal(180, setup.ChamberEntryStableSeconds);
            Assert.Equal(0.4, setup.ChamberEntryRangeC);
            Assert.Equal(0.08, setup.ChamberEntryDriftCPerMinute);
            Assert.Equal(77, setup.RequiredStableSamples);
            setup.ChamberEntryStableSeconds = 240;
            var reopened = CalibrationCheckpointRecovery.CloneSettings(setup);
            store.ApplyAcquisitionInterval(reopened, false);
            Assert.Equal(240, reopened.ChamberEntryStableSeconds);
            defaults.ChamberEntryStableSeconds = 300;
            defaults.ChamberEntryEnabled = false;
            store.Save(defaults);
            store.ApplyAcquisitionInterval(reopened, false);
            Assert.Equal(300, reopened.ChamberEntryStableSeconds);
            Assert.False(reopened.ChamberEntryEnabled);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ChamberDefaultsNeverModifyActiveOrResumedSnapshot()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CalibrationDefaultsStore(Path.Combine(root, "defaults.json"));
            store.Save(new CalibrationProfileSettings { ChamberEntryEnabled = true, ChamberEntryStableSeconds = 300 });
            var snapshot = new CalibrationProfileSettings { ChamberEntryEnabled = false, ChamberEntryStableSeconds = 90 };
            store.ApplyAcquisitionInterval(snapshot, true);
            Assert.False(snapshot.ChamberEntryEnabled);
            Assert.Equal(90, snapshot.ChamberEntryStableSeconds);
            Assert.Null(snapshot.AppliedChamberEntryDefaults);
        }
        finally { Directory.Delete(root, true); }
    }

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
