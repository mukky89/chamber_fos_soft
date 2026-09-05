using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        Brush borderBrush = FindResource("BorderBrush") as Brush ?? Brushes.Gray;
        Brush muted = FindResource("MutedBrush") as Brush ?? Brushes.Gray;

        var liveDot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Brushes.MediumSeaGreen, Margin = new Thickness(0, 0, 7, 0) };
        var title = new TextBlock
        {
            Text = "FBG KALIBRÁCIA · LIVE",
            FontFamily = new FontFamily("Segoe UI Semibold"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(139, 186, 255)),
        };
        var liveTitle = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        liveTitle.Children.Add(liveDot);
        liveTitle.Children.Add(title);
        var state = new TextBlock
        {
            Tag = "state",
            Text = string.Empty,
            FontFamily = new FontFamily("Segoe UI Semibold"),
            FontSize = 10.5,
            Foreground = Brushes.MediumSeaGreen,
        };
        var statePill = new Border { Background = new SolidColorBrush(Color.FromRgb(21, 53, 46)), BorderBrush = Brushes.MediumSeaGreen, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4, 8, 4), Child = state };
        var header = new DockPanel();
        DockPanel.SetDock(statePill, Dock.Right);
        header.Children.Add(statePill);
        header.Children.Add(liveTitle);

        var profile = new TextBlock { Tag = "profile", FontSize = 16, FontFamily = new FontFamily("Segoe UI Semibold"), Margin = new Thickness(0, 10, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis };
        var runId = new TextBlock
        {
            Tag = "runId",
            FontSize = 11,
            FontFamily = new FontFamily("Segoe UI Semibold"),
            Foreground = new SolidColorBrush(Color.FromRgb(187, 216, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var openRunFolder = new Button
        {
            Tag = "openRunFolder",
            Content = "📁 Otvoriť súbory",
            Style = FindResource("AccentOutlineButton") as Style,
            FontSize = 10.5,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(8, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = "Otvorí priečinok so summary, výsledkami, raw samples, wavelength trace a diagnostickým logom tejto kalibrácie.",
        };
        openRunFolder.Click += OpenCalibrationRunFolder_Click;
        var runMeta = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(openRunFolder, Dock.Right);
        runMeta.Children.Add(openRunFolder);
        runMeta.Children.Add(runId);
        var plateau = new TextBlock { Tag = "plateau", FontSize = 11, Foreground = muted };
        var eta = new TextBlock
        {
            Tag = "eta",
            FontSize = 11,
            FontFamily = new FontFamily("Segoe UI Semibold"),
            Foreground = new SolidColorBrush(Color.FromRgb(139, 186, 255)),
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };

        var detail = new TextBlock
        {
            Tag = "detail",
            Text = string.Empty,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 221, 250)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 10),
        };
        var metrics = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        metrics.ColumnDefinitions.Add(new ColumnDefinition()); metrics.ColumnDefinitions.Add(new ColumnDefinition()); metrics.ColumnDefinitions.Add(new ColumnDefinition());
        metrics.Children.Add(CreateStatusMetric("CIEĽ", "target", 0));
        metrics.Children.Add(CreateStatusMetric("WIKA", "reference", 1));
        metrics.Children.Add(CreateStatusMetric("FBG PEAKY", "peaks", 2));
        var progress = new ProgressBar
        {
            Tag = "progress",
            Height = 6,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
        };
        var progressText = new TextBlock { Tag = "progressText", FontSize = 10.5, Foreground = muted, Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(profile);
        stack.Children.Add(runMeta);
        stack.Children.Add(plateau);
        stack.Children.Add(eta);
        stack.Children.Add(detail);
        stack.Children.Add(metrics);
        stack.Children.Add(progress);
        stack.Children.Add(progressText);

        return new Border
        {
            Tag = CalibrationStatusCardTag,
            DataContext = chamber,
            Background = new LinearGradientBrush(Color.FromRgb(23, 40, 62), Color.FromRgb(15, 27, 43), 35),
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 90, 130)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 14, 11),
            Margin = new Thickness(0, 0, 0, 10),
            Visibility = Visibility.Collapsed,
            Child = stack,
        };
    }

    private Border CreateStatusMetric(string label, string valueTag, int column)
    {
        Brush muted = FindResource("MutedBrush") as Brush ?? Brushes.Gray;
        var value = new TextBlock { Tag = valueTag, FontSize = 14, FontFamily = new FontFamily("Segoe UI Semibold"), Margin = new Thickness(0, 3, 0, 0) };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, FontSize = 9.5, Foreground = muted });
        panel.Children.Add(value);
        var border = new Border { Background = new SolidColorBrush(Color.FromRgb(13, 25, 40)), BorderBrush = new SolidColorBrush(Color.FromRgb(41, 65, 93)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(column == 0 ? 0 : 4, 0, column == 2 ? 0 : 4, 0), Child = panel };
        Grid.SetColumn(border, column);
        return border;
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
            TextBlock? profile = FindTagged<TextBlock>(stack, "profile");
            TextBlock? runId = FindTagged<TextBlock>(stack, "runId");
            Button? openRunFolder = FindTagged<Button>(stack, "openRunFolder");
            TextBlock? plateau = FindTagged<TextBlock>(stack, "plateau");
            TextBlock? eta = FindTagged<TextBlock>(stack, "eta");
            TextBlock? detail = FindTagged<TextBlock>(stack, "detail");
            TextBlock? target = FindTagged<TextBlock>(stack, "target");
            TextBlock? reference = FindTagged<TextBlock>(stack, "reference");
            TextBlock? peaks = FindTagged<TextBlock>(stack, "peaks");
            TextBlock? progressText = FindTagged<TextBlock>(stack, "progressText");
            ProgressBar? progress = FindTagged<ProgressBar>(stack, "progress");

            // The main card already has an FBG button and control-mode chip. An idle status card
            // adds no useful information and unnecessarily pushes Quick control downward.
            card.Visibility = snapshot.IsRunning ? Visibility.Visible : Visibility.Collapsed;
            if (!snapshot.IsRunning) continue;

            if (state is not null)
            {
                state.Text = snapshot.DisplayState;
                state.Foreground = ok;
            }
            if (profile is not null) profile.Text = snapshot.ProfileName;
            if (runId is not null) runId.Text = $"ID kalibrácie: {snapshot.RunId}";
            if (openRunFolder is not null)
            {
                openRunFolder.CommandParameter = snapshot.RunDirectory;
                openRunFolder.IsEnabled = !string.IsNullOrWhiteSpace(snapshot.RunDirectory);
            }
            if (plateau is not null) plateau.Text = $"{snapshot.Plateau} · čas fázy {snapshot.PhaseElapsed}";
            if (eta is not null)
            {
                eta.Text = snapshot.EstimatedFinish is not "—" and not ""
                    ? $"Odhad konca: {snapshot.EstimatedFinish} · zostáva {snapshot.Eta}"
                    : $"Odhad konca: {snapshot.Eta}";
                eta.ToolTip = snapshot.EtaBasis;
            }
            if (detail is not null)
                detail.Text = snapshot.CurrentActivity;
            if (target is not null) target.Text = snapshot.Target;
            if (reference is not null) reference.Text = snapshot.Reference;
            if (peaks is not null) peaks.Text = snapshot.PeakSummary;
            if (progress is not null)
                progress.Value = snapshot.ProgressPercent;
            if (progressText is not null) progressText.Text = snapshot.ProgressLabel;
        }
    }

    private static T? FindTagged<T>(DependencyObject root, string tag) where T : FrameworkElement =>
        FindVisualDescendants<T>(root).FirstOrDefault(x => string.Equals(x.Tag?.ToString(), tag, StringComparison.Ordinal));

    private static void OpenCalibrationRunFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string path } || string.IsNullOrWhiteSpace(path)) return;
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Notifications.AppNotificationService.Warning("Súbory kalibrácie", $"Priečinok sa nepodarilo otvoriť: {ex.Message}", $"calibration-folder:{path}");
        }
    }
}
