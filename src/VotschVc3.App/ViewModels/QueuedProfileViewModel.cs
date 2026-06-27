using VotschVc3.Core.Profiles;

namespace VotschVc3.App.ViewModels;

/// <summary>One profile waiting in a chamber's test queue.</summary>
public sealed class QueuedProfileViewModel
{
    public QueuedProfileViewModel(TestProfile profile) => Profile = profile;

    public TestProfile Profile { get; }

    public string Name => Profile.Name;
    public int Cycles => Profile.Cycles;
    public int SegmentCount => Profile.Segments.Count;

    public string DurationText
    {
        get
        {
            TimeSpan t = Profile.TotalDuration;
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{t.Minutes} min";
        }
    }

    public string Summary => $"{Name} · {Cycles} cyklov · {SegmentCount} segm. · {DurationText}";
}
