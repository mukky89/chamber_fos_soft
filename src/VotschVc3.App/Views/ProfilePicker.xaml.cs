using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.Views;

/// <summary>
/// Dropdown profile picker with a search box and a grouped tree (customer / sensor →
/// profiles). A reliable replacement for the custom-templated ComboBox, which cannot
/// render group headers or a tree. Exposes <see cref="ItemsSource"/> and
/// <see cref="SelectedProfile"/> for MVVM binding.
/// </summary>
public partial class ProfilePicker : UserControl
{
    public ProfilePicker() => InitializeComponent();

    /// <summary>Grouped tree nodes shown in the popup (rebuilt on open / search / source change).</summary>
    public ObservableCollection<ProfileTreeGroupViewModel> Groups { get; } = new();

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable<TestProfile>), typeof(ProfilePicker),
        new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>The profiles to choose from.</summary>
    public IEnumerable<TestProfile>? ItemsSource
    {
        get => (IEnumerable<TestProfile>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SelectedProfileProperty = DependencyProperty.Register(
        nameof(SelectedProfile), typeof(TestProfile), typeof(ProfilePicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedProfileChanged));

    /// <summary>The chosen profile (two-way).</summary>
    public TestProfile? SelectedProfile
    {
        get => (TestProfile?)GetValue(SelectedProfileProperty);
        set => SetValue(SelectedProfileProperty, value);
    }

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(ProfilePicker),
        new PropertyMetadata("Vyber profil"));

    /// <summary>Text shown on the closed button while nothing is selected.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly DependencyProperty SelectedNameProperty = DependencyProperty.Register(
        nameof(SelectedName), typeof(string), typeof(ProfilePicker), new PropertyMetadata(string.Empty));

    /// <summary>Name of the selected profile (for the closed button).</summary>
    public string SelectedName
    {
        get => (string)GetValue(SelectedNameProperty);
        private set => SetValue(SelectedNameProperty, value);
    }

    public static readonly DependencyProperty SelectedCaptionProperty = DependencyProperty.Register(
        nameof(SelectedCaption), typeof(string), typeof(ProfilePicker), new PropertyMetadata(string.Empty));

    /// <summary>Caption (sensors · project · tags) of the selected profile.</summary>
    public string SelectedCaption
    {
        get => (string)GetValue(SelectedCaptionProperty);
        private set => SetValue(SelectedCaptionProperty, value);
    }

    public static readonly DependencyProperty HasSelectionProperty = DependencyProperty.Register(
        nameof(HasSelection), typeof(bool), typeof(ProfilePicker), new PropertyMetadata(false));

    /// <summary><c>true</c> when a profile is selected (drives placeholder vs. content).</summary>
    public bool HasSelection
    {
        get => (bool)GetValue(HasSelectionProperty);
        private set => SetValue(HasSelectionProperty, value);
    }

    public static readonly DependencyProperty ResultSummaryProperty = DependencyProperty.Register(
        nameof(ResultSummary), typeof(string), typeof(ProfilePicker), new PropertyMetadata(string.Empty));

    /// <summary>Footer line under the tree ("12 profilov", "Žiadny výsledok").</summary>
    public string ResultSummary
    {
        get => (string)GetValue(ResultSummaryProperty);
        private set => SetValue(ResultSummaryProperty, value);
    }

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
        picker.SelectedName = profile?.Name ?? string.Empty;
        picker.SelectedCaption = profile?.PickerCaption ?? string.Empty;
        picker.HasSelection = profile is not null;
    }

    private void RebuildTree()
    {
        string filter = SearchBox?.Text?.Trim() ?? string.Empty;
        Groups.Clear();

        List<TestProfile> matched = (ItemsSource ?? Enumerable.Empty<TestProfile>())
            .Where(p => Matches(p, filter))
            .ToList();

        foreach (IGrouping<string, TestProfile> group in matched
                     .GroupBy(p => p.GroupKey)
                     .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            Groups.Add(new ProfileTreeGroupViewModel(
                group.Key,
                group.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                // Collapsed by default (tidy for large libraries); expanded while filtering.
                IsExpanded = filter.Length > 0,
            });
        }

        ResultSummary = matched.Count == 0
            ? (filter.Length > 0 ? "Žiadny výsledok" : "Žiadne profily")
            : $"{matched.Count} {ProfileWord(matched.Count)} · {Groups.Count} {GroupWord(Groups.Count)}";
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

        return In(p.Name) || In(p.OriginalName) || In(p.Customer) || In(p.Project) || In(p.GroupKey)
            || p.Sensors.Any(In) || p.Tags.Any(In);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RebuildTree();

    private void OnToggleChecked(object sender, RoutedEventArgs e)
    {
        // Fresh tree on each open (reflects new profiles; clears stale TreeView selection
        // so re-picking the same profile still fires SelectedItemChanged).
        SearchBox.Clear();
        RebuildTree();
        Dispatcher.BeginInvoke(
            new Action(() => SearchBox.Focus()),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnPopupClosed(object? sender, EventArgs e) => ToggleBtn.IsChecked = false;

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // Only leaves (profiles) are selectable; group headers just expand/collapse.
        if (e.NewValue is TestProfile profile)
        {
            SelectedProfile = profile;
            ToggleBtn.IsChecked = false;
        }
    }
}
