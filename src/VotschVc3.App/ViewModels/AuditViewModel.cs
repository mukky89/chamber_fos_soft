using System.Collections.ObjectModel;
using System.Windows;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Security;

namespace VotschVc3.App.ViewModels;

/// <summary>Shows the audit trail (operator action history).</summary>
public sealed class AuditViewModel : ObservableObject
{
    private const int MaxEntries = 1000;
    private readonly AuditLog _log;

    public AuditViewModel(AuditLog log)
    {
        _log = log;
        RefreshCommand = new RelayCommand(Refresh);
        _log.EntryAdded += OnEntryAdded;
        Refresh();
    }

    public ObservableCollection<AuditEntry> Entries { get; } = new();

    public RelayCommand RefreshCommand { get; }

    private void Refresh()
    {
        Entries.Clear();
        foreach (AuditEntry entry in _log.LoadRecent(MaxEntries))
        {
            Entries.Add(entry);
        }
    }

    private void OnEntryAdded(object? sender, AuditEntry entry) => RunOnUi(() =>
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
