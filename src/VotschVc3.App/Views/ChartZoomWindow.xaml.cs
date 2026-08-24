using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace VotschVc3.App.Views;

/// <summary>
/// Fullscreen (maximized, borderless) view of a <see cref="ChartView"/>. The
/// inner chart mirrors the source chart's dependency properties via one-way
/// bindings, so a live chart keeps updating while zoomed. Closed with Esc or
/// the ✕ button.
/// </summary>
public partial class ChartZoomWindow : Window
{
    private ChartZoomWindow(ChartView source, string title)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;

        Mirror(ChartView.SeriesProperty, source, nameof(ChartView.Series));
        Mirror(ChartView.UnitProperty, source, nameof(ChartView.Unit));
        Mirror(ChartView.YMinProperty, source, nameof(ChartView.YMin));
        Mirror(ChartView.YMaxProperty, source, nameof(ChartView.YMax));
        Mirror(ChartView.EmptyTextProperty, source, nameof(ChartView.EmptyText));
    }

    /// <summary>Opens the fullscreen chart modally over the window owning <paramref name="source"/>.</summary>
    public static void Show(ChartView source, string title)
    {
        var window = new ChartZoomWindow(source, title);
        Window? owner = GetWindow(source);
        if (owner is not null && owner.IsVisible)
        {
            window.Owner = owner;
        }

        window.ShowDialog();
    }

    private void Mirror(DependencyProperty property, ChartView source, string path) =>
        Chart.SetBinding(property, new Binding(path) { Source = source, Mode = BindingMode.OneWay });

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
