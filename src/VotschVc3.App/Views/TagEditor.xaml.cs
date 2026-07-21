using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VotschVc3.App.Views;

/// <summary>
/// Reusable multi-value editor: shows the current values as removable chips and lets
/// the user either pick an existing value from a suggestions drop-down or type a new
/// one (Enter / ＋). Bound collection is mutated in place, so the owning view model
/// sees every add/remove.
/// </summary>
public partial class TagEditor : UserControl
{
    public TagEditor() => InitializeComponent();

    // One-way by default: the bound collection is mutated in place (add/remove),
    // never reassigned, so the control never needs to write the property back.
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(ObservableCollection<string>), typeof(TagEditor),
        new PropertyMetadata(null, OnItemsChanged));

    /// <summary>The selected values (chips). Mutated in place on add/remove.</summary>
    public ObservableCollection<string> Items
    {
        get => (ObservableCollection<string>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly DependencyProperty SuggestionsProperty = DependencyProperty.Register(
        nameof(Suggestions), typeof(IEnumerable), typeof(TagEditor), new PropertyMetadata(null));

    /// <summary>Known values offered in the drop-down.</summary>
    public IEnumerable? Suggestions
    {
        get => (IEnumerable?)GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    public static readonly DependencyProperty HasItemsProperty = DependencyProperty.Register(
        nameof(HasItems), typeof(bool), typeof(TagEditor), new PropertyMetadata(false));

    /// <summary>True when at least one chip is present (drives the chip row visibility).</summary>
    public bool HasItems
    {
        get => (bool)GetValue(HasItemsProperty);
        private set => SetValue(HasItemsProperty, value);
    }

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TagEditor editor)
        {
            return;
        }

        if (e.OldValue is ObservableCollection<string> oldCol)
        {
            oldCol.CollectionChanged -= editor.OnItemsCollectionChanged;
        }

        if (e.NewValue is ObservableCollection<string> newCol)
        {
            newCol.CollectionChanged += editor.OnItemsCollectionChanged;
        }

        editor.UpdateHasItems();
    }

    private void OnItemsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => UpdateHasItems();

    private void UpdateHasItems() => HasItems = Items is { Count: > 0 };

    private void AddValue(string? value)
    {
        string v = value?.Trim() ?? string.Empty;
        if (v.Length == 0 || Items is null)
        {
            return;
        }

        if (!Items.Any(x => string.Equals(x, v, System.StringComparison.OrdinalIgnoreCase)))
        {
            Items.Add(v);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && Items is not null)
        {
            string? match = Items.FirstOrDefault(x => string.Equals(x, value, System.StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                Items.Remove(match);
            }
        }
    }

    private void Suggest_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SuggestBox.SelectedItem is string value)
        {
            AddValue(value);
            SuggestBox.SelectedItem = null; // ready for the next pick
        }
    }

    private void NewBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddValue(NewBox.Text);
            NewBox.Clear();
            e.Handled = true;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        AddValue(NewBox.Text);
        NewBox.Clear();
    }
}
