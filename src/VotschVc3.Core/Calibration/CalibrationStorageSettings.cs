using System.Text.Json;

namespace VotschVc3.Core.Calibration;

public sealed class CalibrationStorageSettings
{
    public string LocalDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Lab Control", "Calibration", "Runs");
    public string NetworkDirectory { get; set; } = @"G:\Projekty\FOS\1_Production\C_Calibration_data\10_Calibration_automatic\01_Temperature";
    public bool NetworkCopyEnabled { get; set; } = true;
    public int SynchronizationIntervalSeconds { get; set; } = 10;

    public void Validate()
    {
        if (!Path.IsPathFullyQualified(LocalDirectory) || !Path.IsPathFullyQualified(NetworkDirectory))
            throw new ArgumentException("Obe úložiská musia mať úplnú absolútnu cestu.");
        string local = Path.GetFullPath(LocalDirectory);
        string network = Path.GetFullPath(NetworkDirectory);
        if (IsWithin(local, network) || IsWithin(network, local))
            throw new ArgumentException("Lokálna a sieťová cesta musia byť oddelené; nesmú byť totožné ani vnorené.");
        if (local.StartsWith(@"\\", StringComparison.Ordinal) ||
            new DriveInfo(Path.GetPathRoot(local)!).DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Ram))
            throw new ArgumentException("Lokálna záloha musí byť na disku PC, nie na sieťovom disku.");
        if (SynchronizationIntervalSeconds is < 1 or > 3600)
            throw new ArgumentException("Interval synchronizácie musí byť 1 až 3600 sekúnd.");
    }

    internal static bool IsWithin(string path, string root) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase) ||
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

public sealed class CalibrationStorageSettingsStore
{
    private readonly string _path;
    public CalibrationStorageSettingsStore(string path) => _path = path;
    public CalibrationStorageSettings Load() => File.Exists(_path)
        ? JsonSerializer.Deserialize<CalibrationStorageSettings>(File.ReadAllText(_path)) ?? new()
        : new();
    public void Save(CalibrationStorageSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _path, overwrite: true);
    }
}
