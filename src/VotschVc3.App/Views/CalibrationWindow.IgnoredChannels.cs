using System.Windows;
using System.Windows.Controls;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.Views;

public partial class CalibrationWindow
{
    private void IgnoredChannels_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsRunning) { ShowProductionInfo("Kanály možno meniť iba mimo bežiacej kalibrácie."); return; }
        if (_viewModel.SelectedProfile is null) { ShowProductionInfo("Najprv vyber kalibračný profil."); return; }
        if (IsWiringGridEditingV3()) { ShowProductionInfo("Najprv dokonči editáciu bunky klávesom Enter."); return; }
        CloseSequentialWiringV9();
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "Zaškrtni kanály používané na iné meranie. Nebudú ovplyvňovať zapojenie ani kalibráciu v tejto komore a profile.\nOdškrtnutím ich znovu zapneš; uložené SN zostanú zachované.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16),
        });
        var choices = new WrapPanel();
        var boxes = new List<CheckBox>();
        foreach (string channel in CalibrationWlFormat.Channels.Concat(_viewModel.Peaks.Select(p => p.Channel))
                     .Concat(_viewModel.IgnoredPeakLoggerChannels).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c))
        {
            var box = new CheckBox
            {
                Content = $"Ignorovať {channel}", Tag = channel, Width = 145,
                Margin = new Thickness(0, 0, 0, 12), IsChecked = _viewModel.IsPeakLoggerChannelIgnored(channel),
            };
            boxes.Add(box);
            choices.Children.Add(box);
        }
        panel.Children.Add(choices);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Zrušiť", IsCancel = true, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "Uložiť", IsDefault = true, Padding = new Thickness(14, 7, 14, 7) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        var dialog = new Window
        {
            Title = "Ignorované kanály PeakLoggera", Owner = this, Width = 650, SizeToContent = SizeToContent.Height,
            MaxHeight = 650, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Background, Foreground = Foreground,
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        };
        dialog.SourceInitialized += (_, _) => EnableDarkTitleBarV9(dialog);
        save.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() != true) return;
        try
        {
            _viewModel.SetIgnoredPeakLoggerChannels(boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag));
            _knownPeakIdentities = CurrentPeakIdentities();
            _pendingTopologyChange = null;
            _pendingTopologyChangeConfirmations = 0;
            ShowProductionInfo(_viewModel.IgnoredPeakLoggerChannels.Count == 0
                ? "Sledovanie všetkých kanálov je zapnuté."
                : "Ignorované kanály: " + string.Join(", ", _viewModel.IgnoredPeakLoggerChannels));
        }
        catch (Exception ex) { ShowProductionInfo(ex.Message); }
    }
}
