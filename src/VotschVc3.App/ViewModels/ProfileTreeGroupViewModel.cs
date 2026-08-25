using System.Collections.ObjectModel;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.ViewModels;

/// <summary>A top-level node in the profile library tree – a sensor group with its profiles.</summary>
public sealed class ProfileTreeGroupViewModel : ObservableObject
{
    public ProfileTreeGroupViewModel(string header, IEnumerable<TestProfile> profiles, bool isRecent = false)
    {
        Header = header;
        Profiles = new ObservableCollection<TestProfile>(profiles);
        IsRecent = isRecent;
    }

    /// <summary>Group label (sensor name, or "Bez snímača" for ungrouped profiles).</summary>
    public string Header { get; }

    public ObservableCollection<TestProfile> Profiles { get; }

    public int Count => Profiles.Count;

    /// <summary>
    /// <c>true</c> for the synthetic "Najnovšie" shortcut group at the top of the picker,
    /// which repeats the most recently created / edited profiles that are also listed in
    /// their own customer / sensor group further down.
    /// </summary>
    public bool IsRecent { get; }

    /// <summary>Header shown in the tree, e.g. "ADXL sensor (3)".</summary>
    public string DisplayHeader => $"{Header} ({Count})";

    private bool _isExpanded = true;
    /// <summary>Whether the group node is expanded (bound to the TreeViewItem, toggled by expand/collapse-all).</summary>
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
}
