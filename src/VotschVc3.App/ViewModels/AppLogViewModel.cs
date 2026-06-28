using System.Collections.ObjectModel;
using System.Windows;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.ViewModels;

/// <summary>Shows the application diagnostic log (starts, errors, calibration events).</summary>
public sealed class AppLogViewModel : ObservableObject
{
    private const int MaxEntries = 2000;

    public AppLogViewModel()
    {
        RefreshCommand = new RelayCommand(Refresh);
        ClearViewCommand = new RelayCommand(() => Entries.Clear());
        AppLog.EntryLogged += OnEntryLogged;
        Refresh();
    }

    public ObservableCollection<AppLogEntry> Entries { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ClearViewCommand { get; }

    private void Refresh()
    {
        Entries.Clear();
        foreach (AppLogEntry entry in AppLog.LoadRecent(MaxEntries))
        {
            Entries.Add(entry);
        }
    }

    private void OnEntryLogged(object? sender, AppLogEntry entry) => RunOnUi(() =>
    {
        Entries.Insert(0, entry);
        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }
    });

    private static void RunOnUi(Action action)
    {
        Application? app = Application.Current;
        if (app?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }
}
