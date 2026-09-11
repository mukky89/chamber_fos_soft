using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.Mvvm;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.Views;

public partial class SensorPairingPanel : UserControl
{
    private bool _loading;
    private readonly ObservableCollection<PairingPeakPreview> _preview = new();
    private readonly DispatcherTimer _resultTimer = new() { Interval = TimeSpan.FromSeconds(1.2) };
    public SensorPairingPanel()
    {
        InitializeComponent();
        PeakPreview.ItemsSource = _preview;
        _resultTimer.Tick += (_, _) => { _resultTimer.Stop(); if (IsLoaded) { SetStage(1); SerialInput.Focus(); } };
        Unloaded += (_, _) => { _resultTimer.Stop(); LookupProgress.IsIndeterminate = ConnectionProgress.IsIndeterminate = false; };
        CorrectSerial.Click += (_, _) => { ClearIssue(); SerialInput.Focus(); SerialInput.SelectAll(); };
    }
    private Brush Theme(string name) => (Brush)FindResource(name);
    public void SetStage(int stage, string? serial = null)
    {
        Step1.Background = Theme(stage == 1 ? "AccentBrush" : "BackgroundBrush");
        Step2.Background = Theme(stage == 2 ? "AccentBrush" : "BackgroundBrush");
        Step3.Background = Theme(stage == 3 ? "OkBrush" : "BackgroundBrush");
        Step3Icon.Stroke = Theme(stage == 3 ? "BackgroundBrush" : "TextBrush");
        Entry.Visibility = EntryActions.Visibility = stage == 1 ? Visibility.Visible : Visibility.Collapsed;
        PendingHeader.Visibility = Waiting.Visibility = ConfirmPeaks.Visibility = stage == 2 ? Visibility.Visible : Visibility.Collapsed;
        PendingSerial.Text = serial ?? string.Empty;
        if (stage != 2)
        {
            SetLoading(false);
            MetadataCard.Visibility = WavelengthCard.Visibility = Visibility.Collapsed;
            ConnectionProgress.IsIndeterminate = false;
            _preview.Clear();
        }
        ClearIssue();
    }
    public void ClearIssue()
    {
        IssueCard.Visibility = ShowDuplicate.Visibility = Visibility.Collapsed;
        SerialInput.BorderBrush = Theme("BorderBrush");
    }
    public void ShowIssue(string title, string detail, bool duplicate = false)
    {
        var color = Theme(duplicate ? "WarnBrush" : "ErrorBrush");
        IssueCard.BorderBrush = IssueTitle.Foreground = SerialInput.BorderBrush = color;
        IssueCard.Background = Theme("SurfaceBrush");
        IssueTitle.Text = title;
        IssueDetail.Text = detail;
        IssueCard.Visibility = Visibility.Visible;
        ShowDuplicate.Visibility = duplicate ? Visibility.Visible : Visibility.Collapsed;
        CorrectSerial.Content = duplicate ? "Zadať iné SN" : "Opraviť SN";
    }
    public void SetLoading(bool loading)
    {
        _loading = loading;
        LoadingCard.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        LookupProgress.IsIndeterminate = loading;
        if (loading)
        {
            MetadataCard.Visibility = Visibility.Visible;
            CustomerValue.Text = OrderValue.Text = WavelengthValue.Text = "Načítavam…";
            SensorValue.Text = "Čakám na odpoveď API";
        }
    }
    public void ShowMetadata(ProductionMetadata? metadata)
    {
        SetLoading(false);
        MetadataCard.Visibility = Visibility.Visible;
        static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
        CustomerValue.Text = Display(metadata?.CustomerName);
        OrderValue.Text = Display(metadata?.Order);
        WavelengthValue.Text = metadata?.Fbg is { Count: > 0 } fbg
            ? string.Join(" / ", fbg.Select(p => p.ExpectedNm is double nm ? $"{nm:F3} nm" : "—")) : "—";
        SensorValue.Text = metadata is null ? "API neoverené · údaje možno doplniť neskôr"
            : $"{Display(metadata.SensorName)} · API overené\n{metadata.ProductDescription}";
    }
    public void UpdateCandidates(IReadOnlyList<CalibrationPeakRowViewModel> rows, ProductionMetadata? metadata)
    {
        var types = FbgWavelengthComparison.ResolveTypes(rows.Select(p => p.CurrentWavelengthNm).ToArray(), metadata?.Fbg);
        var ordered = rows.OrderBy(p => p.CurrentWavelengthNm).ToArray();
        var nominal = metadata?.Fbg?.OrderBy(p => p.ExpectedNm).ToArray();
        foreach (var stale in _preview.Where(p => !rows.Contains(p.Row)).ToArray()) _preview.Remove(stale);
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var preview = _preview.FirstOrDefault(p => ReferenceEquals(p.Row, row));
            if (preview is null) { preview = new PairingPeakPreview(row); _preview.Add(preview); }
            double? expected = nominal?.Length == rows.Count ? nominal[Array.IndexOf(ordered, row)].ExpectedNm : null;
            preview.Update(expected, types[i]);
        }
        PeakPreview.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ConnectionProgress.Visibility = rows.Count == 0 && !_loading ? Visibility.Visible : Visibility.Collapsed;
        ConnectionProgress.IsIndeterminate = rows.Count == 0 && !_loading;
        ConnectionPrompt.Text = rows.Count == 0 ? "Teraz pripoj snímač" : $"Zistené {rows.Count} peaky · skontroluj priradenie";
        Step2Label.Text = rows.Count == 0 ? "2 · Pripoj snímač" : "2 · Skontroluj snímač";
        ConfirmPeaks.Content = $"Potvrdiť priradenie {rows.Count} peakov";
    }
    public void ApplyPreviewSelection(CalibrationPeakRowViewModel row)
    {
        var preview = _preview.FirstOrDefault(p => ReferenceEquals(p.Row, row));
        if (preview is null) return;
        // Unknown types stay eligible for a later API default unless explicitly edited.
        if (preview.ManuallySelected || preview.Type != "—") row.Selected = preview.Selected;
    }
    public void ShowWavelengthComparison(FbgWavelengthComparison result)
    {
        WavelengthCard.Visibility = Visibility.Visible;
        var color = Theme(result.Passed switch { true => "OkBrush", false => "ErrorBrush", null => "WarnBrush" });
        WavelengthCard.BorderBrush = WavelengthTitle.Foreground = color;
        WavelengthTitle.Text = result.Passed switch { true => "✓ V tolerancii · WL zodpovedajú API", false => "✕ Skontroluj WL / snímač", null => "ⓘ WL · Nevyhodnotené" };
        // Numeric values are already aligned in the table.
        WavelengthDetail.Text = result.Passed == true ? "Všetky peaky zodpovedajú očakávaným vlnovým dĺžkam." : result.Detail;
    }
    public void ShowResult(string serial, string channel, bool apiVerified)
    {
        Result.Text = $"{serial} → Kanál {channel}";
        ResultCard.Visibility = Visibility.Visible;
        ApiStatus.Text = apiVerified ? "API overené" : "API neoverené";
        ApiStatus.Foreground = ApiIcon.Stroke = ApiBadge.BorderBrush = Theme(apiVerified ? "OkBrush" : "WarnBrush");
        ApiIcon.Data = (Geometry)FindResource(apiVerified ? "CheckIcon" : "InfoIcon");
        SetStage(3);
        _resultTimer.Start();
    }
}

public sealed class PairingPeakPreview : ObservableObject
{
    public CalibrationPeakRowViewModel Row { get; }
    public string Label => $"{Row.Channel} / {Row.PeakId}";
    public double Measured => Row.CurrentWavelengthNm;
    private string _expected = "—", _delta = "—", _type = "—";
    private bool _selected;
    public string Expected { get => _expected; private set => SetProperty(ref _expected, value); }
    public string Delta { get => _delta; private set => SetProperty(ref _delta, value); }
    public string Type { get => _type; private set => SetProperty(ref _type, value); }
    public bool ManuallySelected { get; private set; }
    public bool Selected { get => _selected; set { ManuallySelected = true; SetProperty(ref _selected, value); } }
    public PairingPeakPreview(CalibrationPeakRowViewModel row) { Row = row; }
    public void Update(double? expected, string? type)
    {
        OnPropertyChanged(nameof(Measured));
        Expected = expected?.ToString("F3") ?? "—";
        Delta = expected is double nm ? (Measured - nm).ToString("+0.000;-0.000;0.000") : "—";
        Type = string.IsNullOrWhiteSpace(type) ? "—" : type;
        if (!ManuallySelected) SetProperty(ref _selected, string.Equals(type, "T", StringComparison.OrdinalIgnoreCase), nameof(Selected));
    }
}
