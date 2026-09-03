using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Diagnostics;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.Views;

/// <summary>
/// Hooks the enhancement into CalibrationWindow without adding another constructor to the
/// WPF-generated partial type. Class handlers run for every loaded calibration window.
/// </summary>
internal static class CalibrationWindowWorkflowEnhancementsBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(CalibrationWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnCalibrationWindowLoaded),
            handledEventsToo: true);
    }

    private static void OnCalibrationWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is CalibrationWindow window)
        {
            window.InitializeWorkflowEnhancements();
        }
    }
}

public partial class CalibrationWindow
{
    private bool _workflowEnhancementsInitialized;
    private Button? _selectAllPeaksButton;

    internal void InitializeWorkflowEnhancements()
    {
        if (_workflowEnhancementsInitialized) return;
        _workflowEnhancementsInitialized = true;

        RestorePersistedWorkspaceSelection();
        _viewModel.PropertyChanged += OnWorkflowViewModelPropertyChanged;
        Closing += OnWorkflowClosing;

        // The wiring grid/header is created by XAML and the existing OnLoaded handler finishes
        // its column configuration first. Add the convenience action on the next idle turn.
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureSelectAllPeaksButton));
    }

    private void RestorePersistedWorkspaceSelection()
    {
        CalibrationWorkspaceState? saved = CalibrationWorkspaceStateStore.Instance.Get(_chamberId);
        if (saved is null) return;

        if (!string.IsNullOrWhiteSpace(saved.PeakLoggerHost))
        {
            _viewModel.PeakLoggerHost = saved.PeakLoggerHost;
        }
        if (saved.PeakLoggerPort > 0)
        {
            _viewModel.PeakLoggerPort = saved.PeakLoggerPort;
        }

        TestProfile? profile = _viewModel.Profiles.FirstOrDefault(x => x.Id == saved.ProfileId);
        if (profile is not null && _viewModel.SelectedProfile?.Id != profile.Id)
        {
            _viewModel.SelectedProfile = profile;
        }

        AppLog.Info(
            "FBG kalibrácia",
            profile is null
                ? $"Uložený profil {saved.ProfileId} už nie je dostupný; použije sa aktuálny profil."
                : $"Obnovený workspace · profil „{profile.Name}“ · PeakLogger {saved.PeakLoggerHost}:{saved.PeakLoggerPort}. Zapojenie sa obnoví z uloženého setupu po načítaní PeakLogger peakov.");
    }

    private void OnWorkflowViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalibrationViewModel.SelectedProfile))
        {
            PersistWorkspaceSelection();
        }
    }

    private void OnWorkflowClosing(object? sender, CancelEventArgs e)
    {
        // The normal close button intentionally hides the reusable workspace. Force a final
        // setup save before it disappears so even a quick edit + close is restored later.
        try
        {
            if (!_viewModel.IsRunning && _viewModel.SaveSetupCommand.CanExecute(null))
            {
                _viewModel.SaveSetupCommand.Execute(null);
            }
            PersistWorkspaceSelection();
        }
        catch (Exception ex)
        {
            AppLog.Warn("FBG kalibrácia", $"Uloženie zapojenia pri zatvorení: {ex.Message}");
        }
    }

    private void PersistWorkspaceSelection()
    {
        if (_viewModel.SelectedProfile is not { } profile) return;
        CalibrationWorkspaceStateStore.Instance.Save(
            _chamberId,
            profile.Id,
            _viewModel.PeakLoggerHost,
            _viewModel.PeakLoggerPort);
    }

    private void EnsureSelectAllPeaksButton()
    {
        if (_selectAllPeaksButton is not null) return;

        Button? suggestedButton = FindButtonByContent(this, "Navrhnúť 1 peak / kanál");
        if (suggestedButton?.Parent is not DockPanel header) return;

        var button = new Button
        {
            Content = "Vybrať všetky peaky",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(10, 5, 10, 5),
            ToolTip = "Označí všetky peaky načítané z PeakLoggera. Každý vybraný peak bude mať vlastné vyhodnotenie stability a vlastný záznam vo výsledkoch.",
        };
        if (TryFindResource("GhostButton") is Style style)
        {
            button.Style = style;
        }
        DockPanel.SetDock(button, Dock.Right);
        button.Click += SelectAllPeaks_Click;

        int index = header.Children.IndexOf(suggestedButton);
        header.Children.Insert(Math.Max(0, index), button);
        _selectAllPeaksButton = button;
    }

    private void SelectAllPeaks_Click(object sender, RoutedEventArgs e)
    {
        foreach (CalibrationPeakRowViewModel peak in _viewModel.Peaks)
        {
            peak.Selected = true;
        }

        if (_viewModel.SaveSetupCommand.CanExecute(null))
        {
            _viewModel.SaveSetupCommand.Execute(null);
        }

        AppLog.Info("FBG kalibrácia", $"Vybrané všetky peaky · {_viewModel.Peaks.Count}.");
    }

    private static Button? FindButtonByContent(DependencyObject root, string content)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button && string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal))
            {
                return button;
            }

            Button? nested = FindButtonByContent(child, content);
            if (nested is not null) return nested;
        }
        return null;
    }
}
