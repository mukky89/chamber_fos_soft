using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using VotschVc3.App.Changelog;
using VotschVc3.App.Mvvm;

namespace VotschVc3.App.ViewModels;

/// <summary>Shows the embedded CHANGELOG.md inside the app as styled version cards,
/// with a one-click export/preview as a self-contained HTML page.</summary>
public sealed class ChangelogViewModel : ObservableObject
{
    private readonly string _markdown;

    public ChangelogViewModel()
    {
        _markdown = LoadChangelog();
        Releases = ChangelogParser.Parse(_markdown);

        OpenAsHtmlCommand = new RelayCommand(OpenAsHtml);
        SaveHtmlCommand = new RelayCommand(SaveHtml);
    }

    /// <summary>Parsed releases (newest first) rendered as cards in the view.</summary>
    public IReadOnlyList<ChangelogRelease> Releases { get; }

    /// <summary>Raw markdown, kept as a fallback / for the HTML title.</summary>
    public string Text => _markdown;

    public bool HasReleases => Releases.Count > 0;

    private string _status = string.Empty;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool HasStatus => !string.IsNullOrEmpty(Status);

    /// <summary>Renders the changelog to a temp HTML file and opens it in the default browser.</summary>
    public RelayCommand OpenAsHtmlCommand { get; }

    /// <summary>Saves the changelog as a self-contained HTML file (Save dialog).</summary>
    public RelayCommand SaveHtmlCommand { get; }

    private void OpenAsHtml()
    {
        try
        {
            string html = ChangelogHtmlWriter.BuildHtml(Releases);
            string path = Path.Combine(Path.GetTempPath(), "VotschVc3-changelog.html");
            File.WriteAllText(path, html);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            SetStatus("Changelog otvorený v prehliadači (HTML).");
        }
        catch (System.Exception ex)
        {
            SetStatus($"Otvorenie HTML zlyhalo: {ex.Message}");
        }
    }

    private void SaveHtml()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Uložiť changelog ako HTML",
            Filter = "HTML (*.html)|*.html",
            DefaultExt = ".html",
            FileName = "changelog.html",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, ChangelogHtmlWriter.BuildHtml(Releases));
            SetStatus($"Changelog uložený: {dialog.FileName}");
        }
        catch (System.Exception ex)
        {
            SetStatus($"Uloženie HTML zlyhalo: {ex.Message}");
        }
    }

    private void SetStatus(string message)
    {
        Status = message;
        OnPropertyChanged(nameof(HasStatus));
    }

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
        catch (System.Exception ex)
        {
            return $"Changelog sa nepodarilo načítať: {ex.Message}";
        }
    }
}
