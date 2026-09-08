using System.IO;
using System.Windows.Threading;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.ViewModels;

public sealed partial class AdminViewModel
{
    public CalibrationStorageSettings StorageSettings { get; private set; } = new();
    private string _storageSettingsStatus = "Lokálna kópia je povinná. Zmeny ciest platia pre nové behy; existujúce behy a čakajúce kópie si zachovajú svoje cesty.";
    public string StorageSettingsStatus { get => _storageSettingsStatus; private set => SetProperty(ref _storageSettingsStatus, value); }
    public string StorageSyncStatus => CalibrationStorage.Queue.Status();
    public RelayCommand SaveStorageSettingsCommand { get; private set; } = null!;
    public AsyncRelayCommand CheckStoragePathsCommand { get; private set; } = null!;
    public AsyncRelayCommand SyncStorageNowCommand { get; private set; } = null!;
    private DispatcherTimer? _storageTimer;

    private void InitializeStorageSettings()
    {
        StorageSettings = CalibrationStorage.Settings.Load();
        SaveStorageSettingsCommand = new RelayCommand(() =>
        {
            try
            {
                CalibrationStorage.Settings.Save(StorageSettings);
                StorageSettingsStatus = $"Uložené {DateTime.Now:HH:mm}. Cesty platia pre nové behy; rozbehnuté behy a ich zálohy sa nepresúvajú.";
            }
            catch (Exception ex) { StorageSettingsStatus = "Nastavenia neboli uložené: " + ex.Message; }
        });
        CheckStoragePathsCommand = new AsyncRelayCommand(async () =>
        {
            // Use a snapshot so editing fields during a probe cannot change its destination.
            StorageSettings.Validate();
            string[] paths = StorageSettings.NetworkCopyEnabled
                ? [StorageSettings.LocalDirectory, StorageSettings.NetworkDirectory] : [StorageSettings.LocalDirectory];
            StorageSettingsStatus = "Overujem zápis…";
            await Task.Run(() =>
            {
                foreach (string path in paths)
                {
                    Directory.CreateDirectory(path);
                    string probe = Path.Combine(path, ".storage-check-" + Guid.NewGuid().ToString("N") + ".tmp");
                    using var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose);
                    file.WriteByte(1);
                    file.Flush(true);
                }
            });
            StorageSettingsStatus = "Zápis do zvolených úložísk overený. Nastavenia potvrďte tlačidlom Uložiť.";
        }, onError: ex => StorageSettingsStatus = "Kontrola zápisu zlyhala: " + ex.Message);
        SyncStorageNowCommand = new AsyncRelayCommand(async () =>
        {
            CalibrationStorage.Queue.RequestAll();
            await CalibrationStorage.Queue.ProcessPendingAsync();
            OnPropertyChanged(nameof(StorageSyncStatus));
        }, onError: ex => StorageSettingsStatus = "Synchronizácia: " + ex.Message);
        _storageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _storageTimer.Tick += (_, _) => OnPropertyChanged(nameof(StorageSyncStatus));
        _storageTimer.Start();
    }
}
