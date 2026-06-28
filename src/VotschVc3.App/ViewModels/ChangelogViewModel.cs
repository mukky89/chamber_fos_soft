using System.IO;
using System.Reflection;
using VotschVc3.App.Mvvm;

namespace VotschVc3.App.ViewModels;

/// <summary>Shows the embedded CHANGELOG.md inside the app.</summary>
public sealed class ChangelogViewModel : ObservableObject
{
    public ChangelogViewModel()
    {
        Text = LoadChangelog();
    }

    public string Text { get; }

    private static string LoadChangelog()
    {
        try
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using Stream? stream = assembly.GetManifestResourceStream("CHANGELOG.md");
            if (stream is null)
            {
                return "Changelog nie je k dispozícii.";
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            return $"Changelog sa nepodarilo načítať: {ex.Message}";
        }
    }
}
