using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace VotschVc3.App.Views;

/// <summary>
/// Runtime layout hardening for the large FBG calibration workspace.
///
/// The workspace intentionally contains a collapsible 220 px reference-temperature chart plus
/// a wide production wiring table. On 1080p/operator screens the old root Grid had no page-level
/// vertical scrolling, so expanding the chart squeezed the TabControl until the wiring table was
/// almost invisible. This partial keeps the existing XAML/business bindings untouched while
/// applying responsive sizing after the first layout pass.
/// </summary>
public partial class CalibrationWindow
{
    private bool _responsiveLayoutApplied;

    static CalibrationWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(CalibrationWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnResponsiveLayoutLoaded),
            handledEventsToo: true);
    }

    private static void OnResponsiveLayoutLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not CalibrationWindow window || window._responsiveLayoutApplied) return;

        // Do not mutate the visual tree while WPF is still routing Loaded. The existing instance
        // Loaded handler first configures the wiring grid/extra production columns; this pass then
        // sizes the final tree on the next dispatcher turn.
        _ = window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(window.ApplyResponsiveCalibrationLayout));
    }

    private void ApplyResponsiveCalibrationLayout()
    {
        if (_responsiveLayoutApplied || _disposing) return;
        _responsiveLayoutApplied = true;

        if (Content is not Grid rootGrid) return;

        // Row 3 used to be '*'. When the 220 px chart became visible WPF compressed this row,
        // effectively hiding the wiring table. Give the tab workspace a stable operator height
        // and let the page ScrollViewer handle overall overflow instead.
        if (rootGrid.RowDefinitions.Count > 3)
        {
            rootGrid.RowDefinitions[3].Height = GridLength.Auto;
        }

        TabControl? workspaceTabs = FindCalibrationTabs(rootGrid);
        if (workspaceTabs is not null)
        {
            workspaceTabs.Height = 420;
            workspaceTabs.MinHeight = 360;
        }

        DataGrid? wiringGrid = FindWiringGrid(rootGrid);
        if (wiringGrid is not null)
        {
            wiringGrid.MinHeight = 250;
            ScrollViewer.SetVerticalScrollBarVisibility(wiringGrid, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(wiringGrid, ScrollBarVisibility.Auto);
            HardenWiringColumnWidths(wiringGrid);
        }

        FixReferenceChartHeader(rootGrid);

        // The root Grid keeps all existing bindings and overlays. We only place it into a
        // page-level vertical ScrollViewer so chart expansion never makes lower content
        // unreachable. Horizontal scrolling belongs to individual wide DataGrids, not the page.
        Content = null;
        var pageScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            PanningMode = PanningMode.VerticalOnly,
            Content = rootGrid,
        };
        Content = pageScroll;
    }

    private static TabControl? FindCalibrationTabs(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is TabControl tabs && tabs.Items.OfType<TabItem>().Any(item =>
                    string.Equals(item.Header?.ToString(), "Zapojenie", StringComparison.Ordinal)))
            {
                return tabs;
            }

            TabControl? nested = FindCalibrationTabs(child);
            if (nested is not null) return nested;
        }

        return null;
    }

    private static void FixReferenceChartHeader(DependencyObject root)
    {
        TextBlock? title = FindTextBlock(root, "Priebeh USB referenčnej teploty");
        if (title?.Parent is not DockPanel header) return;

        // DockPanel.LastChildFill=True caused the right-hand Port/kanál label to occupy the
        // remaining area instead of staying docked right, producing the visible concatenation
        // "...teplotyPort: COM7...".
        header.LastChildFill = false;
        title.TextTrimming = TextTrimming.CharacterEllipsis;

        TextBlock? details = header.Children
            .OfType<TextBlock>()
            .FirstOrDefault(block => !ReferenceEquals(block, title));
        if (details is not null)
        {
            details.Margin = new Thickness(18, 0, 0, 0);
            details.TextAlignment = TextAlignment.Right;
        }
    }

    private static TextBlock? FindTextBlock(DependencyObject root, string exactText)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock text && string.Equals(text.Text, exactText, StringComparison.Ordinal))
            {
                return text;
            }

            TextBlock? nested = FindTextBlock(child, exactText);
            if (nested is not null) return nested;
        }

        return null;
    }

    private static void HardenWiringColumnWidths(DataGrid grid)
    {
        foreach (DataGridColumn column in grid.Columns)
        {
            string header = column.Header?.ToString() ?? string.Empty;
            column.MinWidth = header switch
            {
                "Kalibrovať" => 85,
                "Kanál" => 65,
                "Peak ID" => 70,
                "FBG index" => 75,
                "Aktuálna λ [nm]" => 115,
                "Intenzita" => 80,
                "Snímač" => 90,
                "Typ FBG" => 80,
                "FBG sensor SN (kanál)" => 140,
                "FBG sensor SN CHAIN" => 145,
                "Zákazka" => 90,
                "Názov snímača" => 120,
                "Popis výrobku" or "Popis produktu" => 150,
                "Zákazník" => 120,
                "Timeout [min]" => 90,
                "Poznámky" => 120,
                _ => Math.Max(column.MinWidth, 70),
            };
        }
    }
}
