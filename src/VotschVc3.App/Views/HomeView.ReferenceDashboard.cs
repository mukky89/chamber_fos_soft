using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

/// <summary>Adds a compact, always-present CTH7000 tile to every Classic dashboard device card.</summary>
public partial class HomeView
{
    private const string ReferenceMetricTag = "CTH7000_REFERENCE_METRIC";
    private bool _referenceDashboardAttached;

    private void AttachReferenceDashboard()
    {
        if (_referenceDashboardAttached) return;
        _referenceDashboardAttached = true;
        Loaded += OnReferenceDashboardLoaded;
        Unloaded += OnReferenceDashboardUnloaded;
    }

    private void OnReferenceDashboardLoaded(object sender, RoutedEventArgs e)
    {
        CalibrationReferenceStatusStore.Instance.Changed -= OnReferenceDashboardChanged;
        CalibrationReferenceStatusStore.Instance.Changed += OnReferenceDashboardChanged;
        ScheduleClassicReferenceInjection();
    }

    private void OnReferenceDashboardUnloaded(object sender, RoutedEventArgs e)
    {
        CalibrationReferenceStatusStore.Instance.Changed -= OnReferenceDashboardChanged;
    }

    private void OnReferenceDashboardChanged(object? sender, CalibrationReferenceChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => OnReferenceDashboardChanged(sender, e)));
            return;
        }

        UpdateClassicReferenceTiles(e.ChamberId);
    }

    private void ScheduleClassicReferenceInjection() =>
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(EnsureClassicReferenceTiles));

    private void EnsureClassicReferenceTiles()
    {
        var rows = new HashSet<StackPanel>();
        FindClassicMetricRows(this, rows);

        foreach (StackPanel row in rows)
        {
            if (row.DataContext is not ChamberViewModel chamber) continue;

            List<Border> existingTiles = row.Children
                .OfType<Border>()
                .Where(IsDashboardMetricTile)
                .ToList();
            if (existingTiles.Count < 2) continue;

            double compactWidth = chamber.SupportsHumidity ? 94 : 128;
            foreach (Border tile in existingTiles)
            {
                tile.Width = compactWidth;
                tile.Padding = chamber.SupportsHumidity ? new Thickness(7, 5, 7, 5) : new Thickness(9, 6, 9, 6);
            }

            Border? referenceTile = row.Children
                .OfType<Border>()
                .FirstOrDefault(border => string.Equals(border.Tag?.ToString(), ReferenceMetricTag, StringComparison.Ordinal));
            if (referenceTile is null)
            {
                referenceTile = CreateClassicReferenceTile(compactWidth);
                referenceTile.DataContext = chamber;
                row.Children.Add(referenceTile);
            }
            else
            {
                referenceTile.Width = compactWidth;
            }

            UpdateReferenceTile(referenceTile, chamber.Id);
        }
    }

    private void UpdateClassicReferenceTiles(Guid chamberId)
    {
        foreach (Border tile in FindVisualDescendants<Border>(this).Where(border =>
                     string.Equals(border.Tag?.ToString(), ReferenceMetricTag, StringComparison.Ordinal)))
        {
            if (tile.DataContext is ChamberViewModel chamber && chamber.Id == chamberId)
            {
                UpdateReferenceTile(tile, chamberId);
            }
        }
    }

    private Border CreateClassicReferenceTile(double width)
    {
        Brush surface = FindResource("SurfaceBrush") as Brush ?? Brushes.Transparent;
        Brush muted = FindResource("MutedBrush") as Brush ?? Brushes.Gray;
        Brush accent = FindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;

        var label = new TextBlock
        {
            Text = "Referencia",
            FontSize = 11.5,
            Foreground = muted,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var temperature = new TextBlock
        {
            Name = "ReferenceTemperatureText",
            Text = "—",
            FontFamily = new FontFamily("Segoe UI Semibold"),
            FontSize = 18,
            Foreground = accent,
        };
        var port = new TextBlock
        {
            Name = "ReferencePortText",
            Text = string.Empty,
            FontSize = 10.5,
            Foreground = muted,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var content = new StackPanel();
        content.Children.Add(label);
        content.Children.Add(temperature);
        content.Children.Add(port);

        return new Border
        {
            Tag = ReferenceMetricTag,
            Width = width,
            Height = 66,
            Background = surface,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 8, 8),
            Child = content,
            ToolTip = "Referenčný teplomer priradený tejto FBG kalibrácii. Ak nie je pripojený, teplota zostane prázdna.",
        };
    }

    private static void UpdateReferenceTile(Border tile, Guid chamberId)
    {
        CalibrationReferenceSnapshot snapshot = CalibrationReferenceStatusStore.Instance.GetSnapshot(chamberId);
        if (tile.Child is not StackPanel stack || stack.Children.Count < 3) return;
        if (stack.Children[1] is TextBlock temperature)
        {
            temperature.Text = snapshot.IsConnected && snapshot.TemperatureC is { } value
                ? $"{value:F3} °C"
                : "—";
        }
        if (stack.Children[2] is TextBlock port)
        {
            port.Text = snapshot.IsAssigned ? snapshot.PortName : string.Empty;
        }
    }

    private static void FindClassicMetricRows(DependencyObject root, ISet<StackPanel> result)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is Border tile && IsDashboardMetricTile(tile) &&
                tile.Parent is StackPanel { Orientation: Orientation.Horizontal } row &&
                row.DataContext is ChamberViewModel)
            {
                result.Add(row);
            }

            FindClassicMetricRows(child, result);
        }
    }

    private static bool IsDashboardMetricTile(Border border) =>
        Math.Abs(border.Height - 66) < 0.1 &&
        border.Child is StackPanel stack &&
        stack.Children.OfType<TextBlock>().Any(text =>
            text.Text is "Teplota komory" or "Teplota" or "Nastavená (setpoint)" or "Setpoint" or "Vlhkosť");

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (T nested in FindVisualDescendants<T>(child)) yield return nested;
        }
    }
}
