using System.Collections.ObjectModel;
using System.Text;
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
        CopyCommand = new RelayCommand(CopyAll);
        AppLog.EntryLogged += OnEntryLogged;
        Refresh();
    }

    public ObservableCollection<AppLogEntry> Entries { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ClearViewCommand { get; }

    /// <summary>Copies the whole visible log to the clipboard (tab separated).</summary>
    public RelayCommand CopyCommand { get; }

    private void CopyAll()
    {
        if (Entries.Count == 0)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.Append("Čas\tÚroveň\tZdroj\tSpráva").Append('\n');
        foreach (AppLogEntry e in Entries)
        {
            sb.Append(e.TimestampText).Append('\t')
              .Append(e.LevelText).Append('\t')
              .Append(e.Source).Append('\t')
              .Append(e.Message).Append('\n');
        }

        try
        {
            Clipboard.SetText(sb.ToString());
        }
        catch
        {
            // The clipboard can be momentarily locked by another process; ignore.
        }
    }

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
