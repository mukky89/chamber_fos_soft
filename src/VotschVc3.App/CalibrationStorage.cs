using System.IO;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App;

public static class CalibrationStorage
{
    public static CalibrationStorageSettingsStore Settings { get; } = new(Path.Combine(AppPaths.SettingsDir, "calibration-storage.json"));
    public static CalibrationReplicationQueue Queue { get; } = new(Path.Combine(AppPaths.CalibrationDir, "SyncQueue"));

    public static void Start() => Queue.Start(() => Settings.Load().SynchronizationIntervalSeconds);

    public static CalibrationStore CreateStore()
    {
        CalibrationStorageSettings settings = Settings.Load();
        settings.Validate();
        return new CalibrationStore(AppPaths.CalibrationDir, settings.LocalDirectory,
            settings.NetworkCopyEnabled ? settings.NetworkDirectory : null, Queue, new[] { settings.NetworkDirectory });
    }
}
