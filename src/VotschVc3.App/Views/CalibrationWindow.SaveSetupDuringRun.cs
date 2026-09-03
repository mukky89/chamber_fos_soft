using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.Mvvm;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.Views;

/// <summary>
/// Keeps the calibration setup/save action available while a calibration run is active.
/// The production workspace already locks all editable setup controls during a run, so saving
/// here only persists the frozen snapshot that the runner is currently using; it cannot mutate
/// the active measurement or its geometry/state.
/// </summary>
internal static class CalibrationWindowSaveSetupDuringRunBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(CalibrationWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded),
            handledEventsToo: true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is CalibrationWindow window) window.InitializeSaveSetupDuringRun();
    }
}

public partial class CalibrationWindow
{
    private bool _saveSetupDuringRunInitialized;
    private RelayCommand? _saveSetupSnapshotCommand;
    private Button? _saveSetupSnapshotButton;

    internal void InitializeSaveSetupDuringRun()
    {
        if (_saveSetupDuringRunInitialized) return;
        _saveSetupDuringRunInitialized = true;

        _viewModel.PropertyChanged += OnSaveSetupDuringRunViewModelChanged;
        Closed += OnSaveSetupDuringRunClosed;

        // Command bindings are fully resolved only after the visual tree has loaded.
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(InstallSaveSetupSnapshotCommand));
    }

    private void InstallSaveSetupSnapshotCommand()
    {
        if (_saveSetupSnapshotButton is not null) return;

        Button? button = FindButtonForCommand(this, _viewModel.SaveSetupCommand);
        if (button is null)
        {
            AppLog.Warn("FBG kalibrácia", "Tlačidlo Uložiť zapojenie/plán sa nepodarilo nájsť vo visual tree.");
            return;
        }

        _saveSetupSnapshotCommand = new RelayCommand(
            SaveSetupSnapshotDuringRun,
            () => _viewModel.SelectedProfile is not null);

        // The original VM command intentionally disabled itself for IsRunning. Editing is already
        // locked by ProductionWorkspaceV2, therefore the save button can safely use this wrapper.
        // RelayCommand.Execute invokes the original persistence action directly and preserves the
        // existing status message, CalibrationStore format and profile execution-mode handling.
        button.Command = _saveSetupSnapshotCommand;
        button.ToolTip = "Uloží aktuálny kalibračný plán/zapojenie. Funguje aj počas behu; počas kalibrácie sa uloží iba zamknutý snapshot a prebiehajúce meranie sa nemení.";
        _saveSetupSnapshotButton = button;
        _saveSetupSnapshotCommand.RaiseCanExecuteChanged();
    }

    private void SaveSetupSnapshotDuringRun()
    {
        if (_viewModel.SelectedProfile is null) return;

        try
        {
            // Execute is deliberately called directly. The original command's CanExecute gate
            // blocks only the button during IsRunning; its persistence action is safe and is also
            // used immediately before StartCalibrationAsync starts the runner.
            _viewModel.SaveSetupCommand.Execute(null);
            AppLog.Info(
                "FBG kalibrácia",
                _viewModel.IsRunning
                    ? $"Kalibračný plán „{_viewModel.SelectedProfile.Name}“ uložený počas aktívneho behu ako zamknutý snapshot."
                    : $"Kalibračný plán „{_viewModel.SelectedProfile.Name}“ uložený.");
        }
        catch (Exception ex)
        {
            AppLog.Warn("FBG kalibrácia", $"Uloženie kalibračného plánu zlyhalo: {ex.Message}");
            MessageBox.Show(
                this,
                $"Kalibračný plán sa nepodarilo uložiť.\n\n{ex.Message}",
                "Uloženie kalibračného plánu",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnSaveSetupDuringRunViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalibrationViewModel.SelectedProfile))
            _saveSetupSnapshotCommand?.RaiseCanExecuteChanged();
    }

    private void OnSaveSetupDuringRunClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnSaveSetupDuringRunViewModelChanged;
        Closed -= OnSaveSetupDuringRunClosed;
    }

    private static Button? FindButtonForCommand(DependencyObject root, object command)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button && ReferenceEquals(button.Command, command))
                return button;

            Button? nested = FindButtonForCommand(child, command);
            if (nested is not null) return nested;
        }

        return null;
    }
}
