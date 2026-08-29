using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using VotschVc3.App.Charting;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.Views;

/// <summary>
/// Dropdown profile picker with search, grouped tree and a non-destructive live preview.
/// Hovering a profile (or moving to it with the keyboard) updates the graph and summary;
/// only a click on a leaf or Enter confirms <see cref="SelectedProfile"/>.
/// </summary>
public partial class ProfilePicker : UserControl
{
    public ProfilePicker() => InitializeComponent();

    /// <summary>Grouped tree nodes shown in the popup (rebuilt on open / search / source change).</summary>
    public ObservableCollection<ProfileTreeGroupViewModel> Groups { get; } = new();

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable<TestProfile>), typeof(ProfilePicker),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public IEnumerable<TestProfile>? ItemsSource
    {
        get => (IEnumerable<TestProfile>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SelectedProfileProperty = DependencyProperty.Register(
        nameof(SelectedProfile), typeof(TestProfile), typeof(ProfilePicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedProfileChanged));

    public TestProfile? SelectedProfile
    {
        get => (TestProfile?)GetValue(SelectedProfileProperty);
        set => SetValue(SelectedProfileProperty, value);
    }

    /// <summary>
    /// Optional live chamber temperature. It is used as the start of the first ramp;
    /// without it the preview starts at the first profile target because a saved profile
    /// intentionally does not persist an absolute starting temperature.
    /// </summary>
    public static readonly DependencyProperty InitialTemperatureProperty = DependencyProperty.Register(
        nameof(InitialTemperature), typeof(double?), typeof(ProfilePicker),
        new PropertyMetadata(null, OnInitialTemperatureChanged));

    public double? InitialTemperature
    {
        get => (double?)GetValue(InitialTemperatureProperty);
        set => SetValue(InitialTemperatureProperty, value);
    }

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(ProfilePicker),
        new PropertyMetadata("Vyber profil"));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly DependencyProperty SelectedNameProperty = DependencyProperty.Register(
        nameof(SelectedName), typeof(string), typeof(ProfilePicker), new PropertyMetadata(string.Empty));

    public string SelectedName
    {
        get => (string)GetValue(SelectedNameProperty);
        private set => SetValue(SelectedNameProperty, value);
    }

    public static readonly DependencyProperty SelectedCaptionProperty = DependencyProperty.Register(
        nameof(SelectedCaption), typeof(string), typeof(ProfilePicker), new PropertyMetadata(string.Empty));

    public string SelectedCaption
    {
        get => (string)GetValue(SelectedCaptionProperty);
        private set => SetValue(SelectedCaptionProperty, value);
    }

    public static readonly DependencyProperty HasSelectionProperty = DependencyProperty.Register(
        nameof(HasSelection), typeof(bool), typeof(ProfilePicker), new PropertyMetadata(false));

    public bool HasSelection
    {
        get => (bool)GetValue(HasSelectionProperty);
        private set => SetValue(HasSelectionProperty, value);
    }

    public static readonly DependencyProperty ResultSummaryProperty = DependencyProperty.Register(
        nameof(ResultSummary), typeof(string), typeof(ProfilePicker), new PropertyMetadata(string.Empty));

    public string ResultSummary
    {
        get => (string)GetValue(ResultSummaryProperty);
        private set => SetValue(ResultSummaryProperty, value);
    }

    private TestProfile? _previewProfile;

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ProfilePicker)d;
        if (e.OldValue is INotifyCollectionChanged oldNc)
        {
            oldNc.CollectionChanged -= picker.OnSourceCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newNc)
        {
            newNc.CollectionChanged += picker.OnSourceCollectionChanged;
        }

        picker.RebuildTree();
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildTree();

    private static void OnSelectedProfileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ProfilePicker)d;
        var profile = e.NewValue as TestProfile;
        picker.SelectedName = profile?.CodeAndName ?? string.Empty;
        picker.SelectedCaption = profile?.PickerCaption ?? string.Empty;
        picker.HasSelection = profile is not null;

        if (picker.Popup?.IsOpen == true && profile is not null)
        {
            picker.UpdatePreview(profile);
        }
    }

    private static void OnInitialTemperatureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ProfilePicker)d;
        if (picker._previewProfile is not null)
        {
            picker.UpdatePreview(picker._previewProfile);
        }
    }

    private const int RecentCount = 8;

    private void RebuildTree()
    {
        string filter = SearchBox?.Text?.Trim() ?? string.Empty;
        Groups.Clear();

        List<TestProfile> matched = (ItemsSource ?? Enumerable.Empty<TestProfile>())
            .Where(p => Matches(p, filter))
            .ToList();

        List<TestProfile> recent = matched
            .OrderByDescending(p => p.LastChangedAt)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(RecentCount)
            .ToList();
        if (recent.Count > 0)
        {
            Groups.Add(new ProfileTreeGroupViewModel("🕘 Najnovšie", recent, isRecent: true) { IsExpanded = true });
        }

        int groupCount = 0;
        foreach (IGrouping<string, TestProfile> group in matched
                     .GroupBy(p => p.GroupKey)
                     .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            Groups.Add(new ProfileTreeGroupViewModel(
                group.Key,
                group.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                IsExpanded = filter.Length > 0,
            });
            groupCount++;
        }

        ResultSummary = matched.Count == 0
            ? (filter.Length > 0 ? "Žiadny výsledok" : "Žiadne profily")
            : $"{matched.Count} {ProfileWord(matched.Count)} · {groupCount} {GroupWord(groupCount)}";

        if (Popup?.IsOpen == true)
        {
            TestProfile? next = SelectedProfile is not null && matched.Any(p => p.Id == SelectedProfile.Id)
                ? matched.First(p => p.Id == SelectedProfile.Id)
                : matched.FirstOrDefault();
            UpdatePreview(next);
        }
    }

    private static string ProfileWord(int n) => n == 1 ? "profil" : (n is >= 2 and <= 4 ? "profily" : "profilov");

    private static string GroupWord(int n) => n == 1 ? "skupina" : (n is >= 2 and <= 4 ? "skupiny" : "skupín");

    private static bool Matches(TestProfile p, string filter)
    {
        if (filter.Length == 0)
        {
            return true;
        }

        bool In(string? s) => !string.IsNullOrEmpty(s) &&
            s.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

        return In(p.Code) || In(p.Name) || In(p.OriginalName) || In(p.Customer) || In(p.Project)
            || In(p.Notes) || In(p.Warning) || In(p.GroupKey)
            || p.Sensors.Any(In) || p.Tags.Any(In);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RebuildTree();

    private void OnToggleChecked(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        RebuildTree();
        UpdatePreview(SelectedProfile ?? ItemsSource?.OrderByDescending(p => p.LastChangedAt).FirstOrDefault());
        Dispatcher.BeginInvoke(
            new Action(() => SearchBox.Focus()),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnPopupClosed(object? sender, EventArgs e) => ToggleBtn.IsChecked = false;

    /// <summary>Keyboard arrows may select a leaf, but selection alone is only a preview.</summary>
    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TestProfile profile)
        {
            UpdatePreview(profile);
        }
    }

    /// <summary>Mouse hover previews without mutating the actual selected profile.</summary>
    private void OnProfileMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TestProfile profile })
        {
            UpdatePreview(profile);
        }
    }

    /// <summary>Single click on a leaf confirms it; group clicks still only expand/collapse.</summary>
    private void OnTreeMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Tree.SelectedItem is TestProfile profile)
        {
            CommitProfile(profile);
            e.Handled = true;
        }
    }

    /// <summary>Enter confirms the currently highlighted profile; Escape closes without changing it.</summary>
    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Tree.SelectedItem is TestProfile profile)
        {
            CommitProfile(profile);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ToggleBtn.IsChecked = false;
            e.Handled = true;
        }
    }

    private void CommitProfile(TestProfile profile)
    {
        SelectedProfile = profile;
        ToggleBtn.IsChecked = false;
    }

    /// <summary>
    /// "✎ Upraviť v rýchlom profile": closes the popup and hands the previewed profile to
    /// the quick builder, so it can be edited without hunting for it in the library again.
    /// </summary>
    private void OnEditProfileClick(object sender, RoutedEventArgs e)
    {
        if (_previewProfile is not { } profile)
        {
            return;
        }

        ToggleBtn.IsChecked = false;
        if (Window.GetWindow(this)?.DataContext is ShellViewModel shell)
        {
            shell.OpenQuickProfileFor(profile);
        }
    }

    private void UpdatePreview(TestProfile? profile)
    {
        _previewProfile = profile;
        if (PreviewContent is null || PreviewEmpty is null)
        {
            return;
        }

        bool hasProfile = profile is not null;
        PreviewContent.Visibility = hasProfile ? Visibility.Visible : Visibility.Collapsed;
        PreviewEmpty.Visibility = hasProfile ? Visibility.Collapsed : Visibility.Visible;
        if (profile is null)
        {
            PreviewChart.Series = Array.Empty<ChartSeries>();
            PlateauList.ItemsSource = null;
            return;
        }

        ProfilePreviewSummary summary = ProfilePreviewSummary.Analyze(profile, ViewModels.ChamberViewModel.SikaSettling);
        PreviewName.Text = profile.CodeAndName;
        PreviewCaption.Text = profile.PickerCaption;
        PreviewNotes.Text = profile.Notes?.Trim() ?? string.Empty;
        PreviewNotesBorder.Visibility = string.IsNullOrWhiteSpace(PreviewNotes.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
        PreviewWarning.Text = profile.Warning?.Trim() ?? string.Empty;
        PreviewWarningBorder.Visibility = profile.HasWarning ? Visibility.Visible : Visibility.Collapsed;
        Brush statusBrush = TryFindResource("ProfileStatusToBrush") is IValueConverter converter
            ? (Brush)converter.Convert(profile.ValidationStatus, typeof(Brush), null, System.Globalization.CultureInfo.CurrentCulture)
            : Brushes.SteelBlue;
        PreviewStatusText.Text = profile.ValidationStatus.ToString();
        PreviewStatusText.Foreground = statusBrush;
        PreviewStatusBadge.BorderBrush = statusBrush;
        PreviewStatusBadge.ToolTip = profile.ValidationStatusDescription;
        PreviewKindText.Text = profile.DeviceKindLabel;
        PreviewKindText.Foreground = (profile.DeviceKind switch
        {
            ProfileDeviceKind.Sika => TryFindResource("OkBrush"),
            ProfileDeviceKind.Votsch => TryFindResource("AccentBrush"),
            _ => TryFindResource("MutedBrush"),
        }) as Brush ?? Brushes.Gray;
        PreviewKindBadge.ToolTip = profile.DeviceKind switch
        {
            ProfileDeviceKind.Sika => "SIKA TP: profil je zoznam teplôt s dobou výdrže, bez rámp.",
            ProfileDeviceKind.Votsch => "Vötsch / Weiss: profil má nábehy (rampy) medzi platami.",
            _ => "Univerzálny profil – ponúka sa na každom zariadení.",
        };
        MinTemperatureText.Text = summary.MinTemperature is { } min ? $"{min:0.#} °C" : "—";
        MaxTemperatureText.Text = summary.MaxTemperature is { } max ? $"{max:0.#} °C" : "—";
        // On a SIKA profile the dwell sum is not the run time – the bath drives itself to each
        // set point first and the dwell only starts once it is there. The tile stays one short
        // number (it sits in a row of them); the split is in the tooltip.
        TotalDurationText.Text = FormatDuration(summary.TotalWithSettling);
        TotalDurationText.ToolTip = summary.HasSettling
            ? $"Výdrž {FormatDuration(summary.TotalDuration)} + odhad ustálenia {FormatDuration(summary.SettlingDuration)}. "
              + "SIKA kúpeľ si na každý setpoint nabehne sám a výdrž sa začne počítať až potom. "
              + "Rýchlosti sa nastavujú v Administrácia → SIKA – odhad času ustálenia."
            : "Súčet trvania všetkých segmentov vrátane cyklov.";
        CyclesText.Text = summary.Cycles.ToString();
        PlateauCountText.Text = summary.PlateauCount.ToString();
        TemperatureLevelsText.Text = summary.TemperatureLevelCount.ToString();

        List<PlateauDisplayRow> rows = summary.Plateaus
            .Select(p => new PlateauDisplayRow(
                $"{p.Temperature:0.#} °C",
                FormatDuration(p.Duration),
                p.Repetitions > 1 ? $"× {p.Repetitions}" : string.Empty))
            .ToList();
        PlateauList.ItemsSource = rows;
        NoPlateausText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        Brush stroke = TryFindResource("AccentBrush") as Brush ?? Brushes.SteelBlue;
        PreviewChart.Series = new[]
        {
            new ChartSeries("Teplota profilu", stroke, BuildTemperaturePoints(profile)),
        };

        (double cycleStart, double cycleEnd) = ResolveCycleBand(profile);
        PreviewChart.CycleStartX = cycleStart;
        PreviewChart.CycleEndX = cycleEnd;
        PreviewChart.CycleCount = Math.Max(1, profile.Cycles);
        PreviewChart.ChartTitle = profile.Name;
    }

    /// <summary>
    /// Builds the actual execution path in minutes: intro once, cycle region N times,
    /// outro once. The first ramp starts at the chamber's live temperature when known.
    /// </summary>
    private IReadOnlyList<Point> BuildTemperaturePoints(TestProfile profile)
    {
        if (profile.Segments.Count == 0)
        {
            return Array.Empty<Point>();
        }

        List<ProfileSegment> execution = ExpandExecution(profile);
        if (execution.Count == 0)
        {
            return Array.Empty<Point>();
        }

        double previous = InitialTemperature ?? execution[0].TargetTemperature;
        double elapsed = 0;
        var points = new List<Point> { new(0, previous) };

        foreach (ProfileSegment segment in execution)
        {
            double end = elapsed + Math.Max(0, segment.Duration.TotalMinutes);
            if (!segment.IsRamp)
            {
                // A hold starts at its target immediately. Add a vertical corner only
                // when the preceding step did not already finish there.
                if (Math.Abs(previous - segment.TargetTemperature) > 0.0001)
                {
                    points.Add(new Point(elapsed, segment.TargetTemperature));
                }
            }

            points.Add(new Point(end, segment.TargetTemperature));
            previous = segment.TargetTemperature;
            elapsed = end;
        }

        return points;
    }

    private static List<ProfileSegment> ExpandExecution(TestProfile profile)
    {
        var result = new List<ProfileSegment>();
        if (profile.Segments.Count == 0)
        {
            return result;
        }

        int start = profile.ResolvedCycleStart;
        int end = profile.ResolvedCycleEnd;
        for (int i = 0; i < start; i++) result.Add(profile.Segments[i]);
        for (int cycle = 0; cycle < Math.Max(1, profile.Cycles); cycle++)
        {
            for (int i = start; i <= end; i++) result.Add(profile.Segments[i]);
        }
        for (int i = end + 1; i < profile.Segments.Count; i++) result.Add(profile.Segments[i]);
        return result;
    }

    /// <summary>Returns the full repeated-region span in minutes for ChartView's cycle shading.</summary>
    private static (double Start, double End) ResolveCycleBand(TestProfile profile)
    {
        if (profile.Segments.Count == 0 || Math.Max(1, profile.Cycles) <= 1)
        {
            return (double.NaN, double.NaN);
        }

        int start = profile.ResolvedCycleStart;
        int end = profile.ResolvedCycleEnd;
        double intro = profile.Segments.Take(start).Sum(s => Math.Max(0, s.Duration.TotalMinutes));
        double body = profile.Segments.Skip(start).Take(end - start + 1)
            .Sum(s => Math.Max(0, s.Duration.TotalMinutes));
        return (intro, intro + (body * Math.Max(1, profile.Cycles)));
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1)
        {
            return $"{Math.Max(0, duration.TotalSeconds):0} s";
        }

        int days = (int)duration.TotalDays;
        int hours = duration.Hours;
        int minutes = duration.Minutes;
        if (days > 0) return $"{days} d {hours} h {minutes} min";
        if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours} h {minutes} min";
        return $"{(int)duration.TotalMinutes} min";
    }

    private sealed record PlateauDisplayRow(string TemperatureText, string DurationText, string RepeatText);
}
