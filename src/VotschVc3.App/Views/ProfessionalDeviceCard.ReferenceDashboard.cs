using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

/// <summary>Compact reference-temperature readout for the Professional dashboard card.</summary>
public partial class ProfessionalDeviceCard
{
    private const string ProfessionalReferenceMetricTag = "CTH7000_REFERENCE_METRIC_PRO";
    private bool _referenceDashboardSubscribed;
    private StackPanel? _referenceMetric;

    private void AttachProfessionalReferenceDashboard()
    {
        Loaded += OnProfessionalReferenceLoaded;
        Unloaded += OnProfessionalReferenceUnloaded;
        DataContextChanged += (_, _) => ScheduleProfessionalReferenceInjection();
    }

    private void OnProfessionalReferenceLoaded(object sender, RoutedEventArgs e)
    {
        if (!_referenceDashboardSubscribed)
        {
            CalibrationReferenceStatusStore.Instance.Changed += OnProfessionalReferenceChanged;
            _referenceDashboardSubscribed = true;
        }
        ScheduleProfessionalReferenceInjection();
    }

    private void OnProfessionalReferenceUnloaded(object sender, RoutedEventArgs e)
    {
        if (_referenceDashboardSubscribed)
        {
            CalibrationReferenceStatusStore.Instance.Changed -= OnProfessionalReferenceChanged;
            _referenceDashboardSubscribed = false;
        }
    }

    private void OnProfessionalReferenceChanged(object? sender, CalibrationReferenceChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => OnProfessionalReferenceChanged(sender, e)));
            return;
        }

        if (DataContext is ChamberViewModel chamber && chamber.Id == e.ChamberId)
        {
            UpdateProfessionalReferenceMetric();
        }
    }

    private void ScheduleProfessionalReferenceInjection() =>
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(EnsureProfessionalReferenceMetric));

    private void EnsureProfessionalReferenceMetric()
    {
        if (DataContext is not ChamberViewModel chamber) return;

        TextBlock? temperatureLabel = FindProfessionalTextBlock(this, "Teplota zariadenia")
            ?? FindProfessionalTextBlock(this, "Teplota");
        if (temperatureLabel is null) return;

        WrapPanel? row = FindAncestor<WrapPanel>(temperatureLabel);
        if (row is null) return;

        // Four temperature-only values now fit the 360 px professional card on one compact row.
        // Humidity-capable devices keep the same compact widths and may wrap only the fifth value.
        foreach (StackPanel metric in row.Children.OfType<StackPanel>())
        {
            if (string.Equals(metric.Tag?.ToString(), ProfessionalReferenceMetricTag, StringComparison.Ordinal)) continue;
            metric.Width = 70;
            metric.Margin = new Thickness(0, 0, 7, 4);
            if (metric.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
            {
                label.FontSize = 10.5;
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.ToolTip ??= label.Text;
                if (label.Text == "Teplota zariadenia") label.Text = "Teplota";
                if (label.Text == "Odchýlka") label.Text = "Δ";
                if (label.Text == "Vlhkosť RH") label.Text = "Vlhkosť";
            }
        }

        _referenceMetric = row.Children
            .OfType<StackPanel>()
            .FirstOrDefault(metric => string.Equals(metric.Tag?.ToString(), ProfessionalReferenceMetricTag, StringComparison.Ordinal));

        if (_referenceMetric is null)
        {
            Brush muted = FindResource("MutedBrush") as Brush ?? Brushes.Gray;
            Brush accent = FindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;

            _referenceMetric = new StackPanel
            {
                Tag = ProfessionalReferenceMetricTag,
                Width = 78,
                Margin = new Thickness(0, 0, 0, 4),
                ToolTip = "WIKA CTH7000 priradený tejto FBG kalibrácii.",
            };
            _referenceMetric.Children.Add(new TextBlock
            {
                Text = "Referencia",
                FontSize = 10.5,
                Foreground = muted,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            _referenceMetric.Children.Add(new TextBlock
            {
                Text = "—",
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 15,
                Foreground = accent,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            _referenceMetric.Children.Add(new TextBlock
            {
                Text = string.Empty,
                FontSize = 10,
                Foreground = muted,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            row.Children.Add(_referenceMetric);
        }

        UpdateProfessionalReferenceMetric();
    }

    private void UpdateProfessionalReferenceMetric()
    {
        if (_referenceMetric is null || DataContext is not ChamberViewModel chamber) return;
        CalibrationReferenceSnapshot snapshot = CalibrationReferenceStatusStore.Instance.GetSnapshot(chamber.Id);
        if (_referenceMetric.Children.Count < 3) return;

        if (_referenceMetric.Children[1] is TextBlock temperature)
        {
            temperature.Text = snapshot.IsConnected && snapshot.TemperatureC is { } value
                ? $"{value:F3} °C"
                : "—";
        }
        if (_referenceMetric.Children[2] is TextBlock port)
        {
            port.Text = snapshot.IsAssigned ? snapshot.PortName : string.Empty;
        }
    }

    private static TextBlock? FindProfessionalTextBlock(DependencyObject root, string text)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock block && string.Equals(block.Text, text, StringComparison.Ordinal)) return block;
            TextBlock? nested = FindProfessionalTextBlock(child, text);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is T typed) return typed;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
