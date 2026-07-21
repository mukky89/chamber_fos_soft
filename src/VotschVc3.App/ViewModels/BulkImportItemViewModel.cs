using System.IO;
using VotschVc3.App.Mvvm;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.ViewModels;

/// <summary>One file queued in the <see cref="BulkImportViewModel"/> list.</summary>
public sealed class BulkImportItemViewModel : ObservableObject
{
    public BulkImportItemViewModel(string filePath, TestProfile raw, string formatDescription, IReadOnlyList<string> warnings)
    {
        FilePath = filePath;
        Raw = raw;
        FormatDescription = formatDescription;
        _name = raw.Name;
        Warnings = warnings.Count == 0 ? string.Empty : string.Join(" · ", warnings);
    }

    /// <summary>Full path of the source file.</summary>
    public string FilePath { get; }

    /// <summary>The freshly parsed profile (humidity intact); the standard is applied on a copy at save time.</summary>
    public TestProfile Raw { get; }

    /// <summary>Detected source format (e.g. "JSON profil").</summary>
    public string FormatDescription { get; }

    /// <summary>Import warnings joined into one line (skipped rows etc.), empty if none.</summary>
    public string Warnings { get; }

    public bool HasWarnings => Warnings.Length > 0;

    public string FileName => Path.GetFileName(FilePath);

    private string _name;
    /// <summary>Editable target name (before the optional global prefix).</summary>
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private bool _isIncluded = true;
    /// <summary>Whether this profile is included in the batch import.</summary>
    public bool IsIncluded { get => _isIncluded; set => SetProperty(ref _isIncluded, value); }

    private string _previewText = string.Empty;
    /// <summary>Projected result for the current options (segment count + what the standard adds).</summary>
    public string PreviewText { get => _previewText; private set => SetProperty(ref _previewText, value); }

    private string _statusText = string.Empty;
    /// <summary>Per-row outcome after an import run.</summary>
    public string StatusText
    {
        get => _statusText;
        set { if (SetProperty(ref _statusText, value)) OnPropertyChanged(nameof(HasStatus)); }
    }

    public bool HasStatus => StatusText.Length > 0;

    /// <summary>Recomputes <see cref="PreviewText"/> by running the standard on a copy.</summary>
    public void UpdatePreview(ChamberKind kind, bool applyStandard, ProfileStandardizationOptions options)
    {
        int before = Raw.Segments.Count;
        if (!applyStandard)
        {
            PreviewText = $"{before} segm. (bez štandardu)";
            return;
        }

        TestProfile copy = Raw.Clone();
        copy.Kind = kind;
        IReadOnlyList<string> notes = ProfileStandardizer.Standardize(copy, options);
        int after = copy.Segments.Count;
        int added = after - before;
        string addLabel = added > 0 ? $" (+{added})" : string.Empty;
        PreviewText = $"{before} → {after} segm.{addLabel} · {string.Join(" · ", notes)}";
    }
}
