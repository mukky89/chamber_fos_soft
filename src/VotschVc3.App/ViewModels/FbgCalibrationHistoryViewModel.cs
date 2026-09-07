using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.ViewModels;

public sealed class FbgCalibrationHistoryViewModel : ObservableObject
{
    private readonly CalibrationStore _store = new(AppPaths.CalibrationDir);
    private FbgCalibrationHistoryItem? _selectedCalibration;
    private string _statusMessage = string.Empty;

    public FbgCalibrationHistoryViewModel()
    {
        RefreshCommand = new RelayCommand(Refresh);
        OpenRootFolderCommand = new RelayCommand(() => OpenFolder(_store.RunsDirectory));
        OpenSelectedFolderCommand = new RelayCommand(
            () => OpenFolder(SelectedCalibration!.FolderPath),
            () => SelectedCalibration is not null);
        Refresh();
    }

    public ObservableCollection<FbgCalibrationHistoryItem> Calibrations { get; } = [];

    public FbgCalibrationHistoryItem? SelectedCalibration
    {
        get => _selectedCalibration;
        set
        {
            if (SetProperty(ref _selectedCalibration, value))
            {
                OpenSelectedFolderCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => SelectedCalibration is not null;
    public string RunsFolder => _store.RunsDirectory;
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenRootFolderCommand { get; }
    public RelayCommand OpenSelectedFolderCommand { get; }

    public void Refresh()
    {
        Guid? selectedId = SelectedCalibration?.Run.RunId;
        try
        {
            IReadOnlyList<FbgCalibrationHistoryItem> items = _store.LoadHistory()
                .Select(run => new FbgCalibrationHistoryItem(run, Path.Combine(_store.RunsDirectory, run.RunId.ToString("N"))))
                .ToArray();

            Calibrations.Clear();
            foreach (FbgCalibrationHistoryItem item in items)
            {
                Calibrations.Add(item);
            }

            SelectedCalibration = Calibrations.FirstOrDefault(item => item.Run.RunId == selectedId)
                ?? Calibrations.FirstOrDefault();
            StatusMessage = Calibrations.Count == 0
                ? "Zatiaľ nie je uložená žiadna FBG kalibrácia."
                : $"Načítaných kalibrácií: {Calibrations.Count}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Históriu kalibrácií sa nepodarilo načítať: {ex.Message}";
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Priečinok sa nepodarilo otvoriť: {ex.Message}";
        }
    }
}

public sealed class FbgCalibrationHistoryItem
{
    public FbgCalibrationHistoryItem(CalibrationRunRecord run, string folderPath)
    {
        Run = run;
        FolderPath = folderPath;
    }

    public CalibrationRunRecord Run { get; }
    public string FolderPath { get; }
    public int SensorCount => Run.CalibrationResults.Select(result => result.SerialNumber).Distinct().Count();
    public int ResultCount => Run.CalibrationResults.Count;
    public int PassCount => Run.CalibrationResults.Count(result => result.Result == "PASS");
    public int FailCount => Run.CalibrationResults.Count(result => result.Result == "FAIL");
    public string ProfileDisplay => string.IsNullOrWhiteSpace(Run.ProfileCode)
        ? Run.ProfileName
        : $"{Run.ProfileCode} · {Run.ProfileName}";
    public string ResultSummary => $"Snímače: {SensorCount} · výsledky: {ResultCount} · PASS: {PassCount} · FAIL: {FailCount}";
}
