using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

/// <summary>
/// Keeps active FBG run status where the operator works: as a separate block above
/// the complete Quick control section. Inactive cards are collapsed so they do not
/// steal space or collide with the Quick control / Edit presets header.
/// </summary>
public partial class HomeView
{
    private const string CalibrationStatusCardTag = "FBG_DEVICE_STATUS_V3";
    private bool _calibrationStatusDashboardAttached;

    private void AttachCalibrationStatusDashboard()
    {
        if (_calibrationStatusDashboardAttached) return;
        _calibrationStatusDashboardAttached = true;
        Loaded += OnCalibrationStatusDashboardLoaded;
        Unloaded += OnCalibrationStatusDashboardUnloaded;
    }

    private void OnCalibrationStatusDashboardLoaded(object sender, RoutedEventArgs e)
    {
        CalibrationStatusViewModel.Instance.PropertyChanged -= OnCalibrationDashboardPropertyChanged;
        CalibrationStatusViewModel.Instance.PropertyChanged += OnCalibrationDashboardPropertyChanged;
        ScheduleCalibrationStatusInjection();
    }

    private void OnCalibrationStatusDashboardUnloaded(object sender, RoutedEventArgs e)
    {
        CalibrationStatusViewModel.Instance.PropertyChanged -= OnCalibrationDashboardPropertyChanged;
    }

    private void OnCalibrationDashboardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => OnCalibrationDashboardPropertyChanged(sender, e)));
            return;
        }
        EnsureCalibrationStatusCards();
        UpdateCalibrationStatusCards();
    }

    private void ScheduleCalibrationStatusInjection() =>
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            HideLegacySidebarCalibrationStatus();
            EnsureCalibrationStatusCards();
            UpdateCalibrationStatusCards();
        }));

    private void HideLegacySidebarCalibrationStatus()
    {
        foreach (TextBlock text in FindVisualDescendants<TextBlock>(this)
                     .Where(t => string.Equals(t.Text, "FBG KALIBRÁCIA", StringComparison.Ordinal)))
        {
            DependencyObject? node = text;
            while (node is not null && !ReferenceEquals(node, this))
            {
                if (node is Border border)
                {
                    border.Visibility = Visibility.Collapsed;
                    break;
                }
                node = VisualTreeHelper.GetParent(node);
            }
        }
    }

    private void EnsureCalibrationStatusCards()
    {
        foreach (TextBlock quickTitle in FindVisualDescendants<TextBlock>(this)
                     .Where(t => string.Equals(t.Text, "Rýchle ovládanie", StringComparison.OrdinalIgnoreCase)))
        {
            if (quickTitle.DataContext is not ChamberViewModel chamber) continue;
            if (!TryFindQuickControlSection(quickTitle, chamber, out Panel? parent, out Border? quickSection)) continue;
            if (parent.Children.OfType<FrameworkElement>().Any(x => string.Equals(x.Tag?.ToString(), CalibrationStatusCardTag, StringComparison.Ordinal)))
                continue;

            Border status = CreateCalibrationStatusCard(chamber);
            int index = parent.Children.IndexOf(quickSection);
            parent.Children.Insert(Math.Max(0, index), status);
        }
    }

    /// <summary>
    /// Find the entire bordered Quick control section, not the inner DockPanel that owns
    /// the title. The old implementation stopped at the first Panel ancestor and inserted
    /// the FBG card inside the header row, which caused the status text to overlap
    /// "Rýchle ovládanie" and "Upraviť predvoľby".
    /// </summary>
    private static bool TryFindQuickControlSection(
        DependencyObject start,
        ChamberViewModel chamber,
        out Panel? parent,
        out Border? section)
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is Border border &&
                border.DataContext is ChamberViewModel borderChamber && borderChamber.Id == chamber.Id)
            {
                DependencyObject? visualParent = VisualTreeHelper.GetParent(border);
                if (visualParent is Panel panel &&
                    panel.DataContext is ChamberViewModel panelChamber && panelChamber.Id == chamber.Id)
                {
                    parent = panel;
                    section = border;
                    return true;
                }
            }
            current = VisualTreeHelper.GetParent(current);
        }

        parent = null;
        section = null;
        return false;
    }

    private Border CreateCalibrationStatusCard(ChamberViewModel chamber)
    {
        Brush surfaceAlt = FindResource("SurfaceAltBrush") as Brush ?? Brushes.Transparent;
        Brush borderBrush = FindResource("BorderBrush") as Brush ?? Brushes.Gray;
        Brush muted = FindResource("MutedBrush") as Brush ?? Brushes.Gray;

        var title = new TextBlock
        {
            Text = "FBG kalibrácia",
            FontFamily = new FontFamily("Segoe UI Semibold"),
            FontSize = 12.5,
        };
        var state = new TextBlock
        {
            Tag = "state",
            Text = string.Empty,
            FontFamily = new FontFamily("Segoe UI Semibold"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = muted,
        };
        var header = new DockPanel();
        DockPanel.SetDock(state, Dock.Right);
        header.Children.Add(state);
        header.Children.Add(title);

        var detail = new TextBlock
        {
            Tag = "detail",
            Text = string.Empty,
            FontSize = 11.5,
            Foreground = muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 6),
        };
        var progress = new ProgressBar
        {
            Tag = "progress",
            Height = 6,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
        };

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(detail);
        stack.Children.Add(progress);

        return new Border
        {
            Tag = CalibrationStatusCardTag,
            DataContext = chamber,
            Background = surfaceAlt,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10),
            Visibility = Visibility.Collapsed,
            Child = stack,
        };
    }

    private void UpdateCalibrationStatusCards()
    {
        Brush ok = FindResource("OkBrush") as Brush ?? Brushes.Green;
        Brush muted = FindResource("MutedBrush") as Brush ?? Brushes.Gray;

        foreach (Border card in FindVisualDescendants<Border>(this)
                     .Where(x => string.Equals(x.Tag?.ToString(), CalibrationStatusCardTag, StringComparison.Ordinal)))
        {
            if (card.DataContext is not ChamberViewModel chamber || card.Child is not StackPanel stack) continue;
            CalibrationWorkspaceStatusSnapshot snapshot = CalibrationStatusViewModel.Instance.GetWorkspace(chamber.Id);
            TextBlock? state = FindTagged<TextBlock>(stack, "state");
            TextBlock? detail = FindTagged<TextBlock>(stack, "detail");
            ProgressBar? progress = FindTagged<ProgressBar>(stack, "progress");

            // The main card already has an FBG button and control-mode chip. An idle status card
            // adds no useful information and unnecessarily pushes Quick control downward.
            card.Visibility = snapshot.IsRunning ? Visibility.Visible : Visibility.Collapsed;
            if (!snapshot.IsRunning) continue;

            if (state is not null)
            {
                state.Text = snapshot.RunState;
                state.Foreground = ok;
            }
            if (detail is not null)
                detail.Text = $"{snapshot.ProfileName} · {snapshot.Plateau}";
            if (progress is not null)
                progress.Value = snapshot.ProgressPercent;
        }
    }

    private static T? FindTagged<T>(DependencyObject root, string tag) where T : FrameworkElement =>
        FindVisualDescendants<T>(root).FirstOrDefault(x => string.Equals(x.Tag?.ToString(), tag, StringComparison.Ordinal));
}
