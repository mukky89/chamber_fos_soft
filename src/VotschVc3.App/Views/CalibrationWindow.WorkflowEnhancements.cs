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
    private CalibrationWorkspaceState? _restoredWorkspaceState;
    private HashSet<int>? _restoredCalibrationPointSelection;
    private readonly HashSet<CalibrationPointRowViewModel> _workspaceObservedPoints = new();
    private bool _restoringWorkspace;
    private bool _workspaceSetupSaveQueued;

    internal void InitializeWorkflowEnhancements()
    {
        if (_workflowEnhancementsInitialized) return;
        _workflowEnhancementsInitialized = true;

        RestorePersistedWorkspaceSelection();
        AttachCalibrationPointAutosave();
        _viewModel.PropertyChanged += OnWorkflowViewModelPropertyChanged;
        Closing += OnWorkflowClosing;
        Closed += OnWorkflowClosed;

        // The first frame stays UI-first. Restore after the other production layout passes have
        // finished so the remembered tab/plateau selection wins over startup defaults. No broad
        // PeakLogger discovery is started here; only the exact remembered endpoint is attempted.
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                EnsureSelectAllPeaksButton();
                RestoreSavedCalibrationPointSelection();
                RestoreSelectedTab();
                PersistWorkspaceSelection();
                _ = RestoreKnownPeakLoggerConnectionAsync();
            }));
    }

    private void RestorePersistedWorkspaceSelection()
    {
        CalibrationWorkspaceState? saved = CalibrationWorkspaceStateStore.Instance.Get(_chamberId);
        _restoredWorkspaceState = saved;
        if (saved is null) return;

        _restoringWorkspace = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(saved.PeakLoggerHost))
            {
                _viewModel.PeakLoggerHost = saved.PeakLoggerHost;
            }
            if (saved.PeakLoggerPort > 0)
            {
                _viewModel.PeakLoggerPort = saved.PeakLoggerPort;
            }

            _viewModel.UseSimulator = saved.UseSimulator;
            _viewModel.SimulatorScenario = saved.SimulatorScenario;
            _viewModel.ShowF100Chart = saved.ShowF100Chart;

            TestProfile? profile = _viewModel.Profiles.FirstOrDefault(x => x.Id == saved.ProfileId);
            if (profile is not null && _viewModel.SelectedProfile?.Id != profile.Id)
            {
                _viewModel.SelectedProfile = profile;
            }

            // LoadProfileSetup has already reconstructed the persisted CalibrationStore setup at
            // this point. Capture the exact plateau subset before the production "new profile =>
            // select all" convenience behavior gets an idle turn and can overwrite it.
            _restoredCalibrationPointSelection = _viewModel.CalibrationPoints
                .Where(point => point.Selected)
                .Select(point => point.SegmentIndex)
                .ToHashSet();

            AppLog.Info(
                "FBG kalibrácia",
                profile is null
                    ? $"Uložený profil {saved.ProfileId} už nie je dostupný; použije sa aktuálny profil."
                    : $"Obnovený workspace · profil „{profile.Name}“ · PeakLogger {saved.PeakLoggerHost}:{saved.PeakLoggerPort} · " +
                      $"simulátor={(saved.UseSimulator ? saved.SimulatorScenario.ToString() : "nie")} · karta {saved.SelectedTabHeader}. " +
                      "Zapojenie, SN/CHAIN, peaky, plata, timeouty a stability sa obnovujú z CalibrationStore.");
        }
        finally
        {
            _restoringWorkspace = false;
        }
    }

    private void RestoreSavedCalibrationPointSelection()
    {
        HashSet<int>? selected = _restoredCalibrationPointSelection;
        if (selected is null || selected.Count == 0) return;

        _restoringWorkspace = true;
        try
        {
            foreach (CalibrationPointRowViewModel point in _viewModel.CalibrationPoints)
            {
                point.Selected = selected.Contains(point.SegmentIndex);
            }
        }
        finally
        {
            _restoringWorkspace = false;
        }

        ScheduleWorkspaceSetupSave();
    }

    private void RestoreSelectedTab()
    {
        string? header = _restoredWorkspaceState?.SelectedTabHeader;
        if (string.IsNullOrWhiteSpace(header) || _productionTabs is null) return;

        TabItem? tab = _productionTabs.Items.OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(HeaderText(item.Header), header, StringComparison.Ordinal));
        if (tab is not null)
        {
            _productionTabs.SelectedItem = tab;
        }
    }

    private async Task RestoreKnownPeakLoggerConnectionAsync()
    {
        CalibrationWorkspaceState? saved = _restoredWorkspaceState;
        if (saved is null || _disposing || _viewModel.PeakLoggerConnected || _viewModel.IsRunning)
        {
            return;
        }

        // Do not reintroduce the old slow startup discovery. This is one exact endpoint (or the
        // remembered simulator) from the previous session. A missing PeakLogger therefore leaves
        // the workspace usable and merely reports the ordinary connection error.
        await Task.Delay(150).ConfigureAwait(true);
        if (_disposing || _viewModel.PeakLoggerConnected) return;

        try
        {
            if (_viewModel.ConnectPeakLoggerCommand.CanExecute(null))
            {
                AppLog.Info(
                    "FBG kalibrácia",
                    saved.UseSimulator
                        ? $"Obnovujem uložený PeakLogger simulátor {saved.SimulatorScenario}…"
                        : $"Obnovujem posledný PeakLogger {saved.PeakLoggerHost}:{saved.PeakLoggerPort} bez discovery scanu…");
                _viewModel.ConnectPeakLoggerCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("FBG kalibrácia", $"Automatická obnova uloženého PeakLogger zapojenia: {ex.Message}");
        }
    }

    private void OnWorkflowViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_restoringWorkspace) return;

        if (e.PropertyName == nameof(CalibrationViewModel.SelectedProfile))
        {
            AttachCalibrationPointAutosave();
            PersistWorkspaceSelection();
            return;
        }

        if (e.PropertyName is nameof(CalibrationViewModel.PeakLoggerHost)
            or nameof(CalibrationViewModel.PeakLoggerPort)
            or nameof(CalibrationViewModel.UseSimulator)
            or nameof(CalibrationViewModel.SimulatorScenario)
            or nameof(CalibrationViewModel.ShowF100Chart))
        {
            PersistWorkspaceSelection();
            return;
        }

        if (IsPersistedCalibrationSetting(e.PropertyName))
        {
            ScheduleWorkspaceSetupSave();
        }
    }

    private static bool IsPersistedCalibrationSetting(string? propertyName) => propertyName is
        nameof(CalibrationViewModel.RequiredStableSamples)
        or nameof(CalibrationViewModel.EnableWavelengthAveraging)
        or nameof(CalibrationViewModel.WavelengthAveragingSamples)
        or nameof(CalibrationViewModel.EnableWavelengthTraceLogging)
        or nameof(CalibrationViewModel.WavelengthTraceIntervalSeconds)
        or nameof(CalibrationViewModel.MaxRangePm)
        or nameof(CalibrationViewModel.MaxStdDevPm)
        or nameof(CalibrationViewModel.MaxDriftPmPerMinute)
        or nameof(CalibrationViewModel.ChamberToleranceC)
        or nameof(CalibrationViewModel.ChamberStableMinutes)
        or nameof(CalibrationViewModel.SensorTimeoutMinutes)
        or nameof(CalibrationViewModel.ValidationMinimumDeltaTemperatureC)
        or nameof(CalibrationViewModel.ValidationMinimumResponsePm)
        or nameof(CalibrationViewModel.AllowValidationOverride)
        or nameof(CalibrationViewModel.ValidationOverrideReason);

    private void AttachCalibrationPointAutosave()
    {
        foreach (CalibrationPointRowViewModel point in _workspaceObservedPoints)
        {
            point.PropertyChanged -= OnWorkspaceCalibrationPointChanged;
        }
        _workspaceObservedPoints.Clear();

        foreach (CalibrationPointRowViewModel point in _viewModel.CalibrationPoints)
        {
            point.PropertyChanged += OnWorkspaceCalibrationPointChanged;
            _workspaceObservedPoints.Add(point);
        }
    }

    private void OnWorkspaceCalibrationPointChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_restoringWorkspace || e.PropertyName != nameof(CalibrationPointRowViewModel.Selected)) return;
        ScheduleWorkspaceSetupSave();
    }

    private void ScheduleWorkspaceSetupSave()
    {
        if (_workspaceSetupSaveQueued || _viewModel.IsRunning) return;
        _workspaceSetupSaveQueued = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _workspaceSetupSaveQueued = false;
            try
            {
                if (!_viewModel.IsRunning && _viewModel.SaveSetupCommand.CanExecute(null))
                {
                    _viewModel.SaveSetupCommand.Execute(null);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("FBG kalibrácia", $"Priebežné uloženie workspace: {ex.Message}");
            }
        }));
    }

    private void OnWorkflowClosing(object? sender, CancelEventArgs e)
    {
        // This handler runs synchronously for both the normal Hide behavior and the real app
        // shutdown. Persist before asynchronous COM/API disposal begins so even a fast Windows/app
        // exit cannot lose the operator's last FBG workspace edits.
        try
        {
            if (!_viewModel.IsRunning && _viewModel.SaveSetupCommand.CanExecute(null))
            {
                _viewModel.SaveSetupCommand.Execute(null);
            }
            PersistWorkspaceSelection();
            AppLog.Info("FBG kalibrácia", $"Workspace {_chamberId} uložený pred zatvorením aplikácie/okna.");
        }
        catch (Exception ex)
        {
            AppLog.Warn("FBG kalibrácia", $"Uloženie zapojenia pri zatvorení: {ex.Message}");
        }
    }

    private void OnWorkflowClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnWorkflowViewModelPropertyChanged;
        foreach (CalibrationPointRowViewModel point in _workspaceObservedPoints)
        {
            point.PropertyChanged -= OnWorkspaceCalibrationPointChanged;
        }
        _workspaceObservedPoints.Clear();
        Closing -= OnWorkflowClosing;
        Closed -= OnWorkflowClosed;
    }

    private void PersistWorkspaceSelection()
    {
        if (_viewModel.SelectedProfile is not { } profile) return;

        string selectedTab = (_productionTabs?.SelectedItem as TabItem) is { } tab
            ? HeaderText(tab.Header)
            : _restoredWorkspaceState?.SelectedTabHeader ?? "Zapojenie";

        CalibrationWorkspaceStateStore.Instance.Save(new CalibrationWorkspaceState(
            _chamberId,
            profile.Id,
            _viewModel.PeakLoggerHost,
            _viewModel.PeakLoggerPort,
            DateTimeOffset.Now)
        {
            UseSimulator = _viewModel.UseSimulator,
            SimulatorScenario = _viewModel.SimulatorScenario,
            ShowF100Chart = _viewModel.ShowF100Chart,
            SelectedTabHeader = selectedTab,
        });
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
