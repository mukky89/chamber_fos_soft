using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.Views;

public partial class CalibrationWindow
{
    private bool _paliParityUiInitialized;
    private Button? _calibrationResultButton;
    private CalibrationRunRecord? _latestCalculatedRun;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_paliParityUiInitialized) return;
        _paliParityUiInitialized = true;

        ReplaceLegacyStabilitySettings();
        InstallCalibrationResultButton();
        _viewModel.PropertyChanged += OnPaliParityViewModelPropertyChanged;
        _viewModel.History.CollectionChanged += OnPaliParityHistoryChanged;
        Closed += OnPaliParityWindowClosed;
        RefreshCalibrationResultButton();
    }

    private void ReplaceLegacyStabilitySettings()
    {
        TabItem? tab = FindTabItemByHeader(this, "Nastavenia stability");
        if (tab is null) return;
        tab.Content = new CalibrationStabilitySettingsView
        {
            DataContext = _viewModel,
        };
    }

    private void InstallCalibrationResultButton()
    {
        if (Content is not Grid rootGrid || _calibrationResultButton is not null) return;

        var button = new Button
        {
            Content = "Výsledok kalibrácie",
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 18, 0),
            Padding = new Thickness(13, 6, 13, 6),
            Foreground = Brushes.White,
            Background = Brushes.SeaGreen,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            FontWeight = FontWeights.SemiBold,
            ToolTip = "Otvorí finálne TEMP/FBGS koeficienty, sensitivity, TRef, MaxError, R², PASS/FAIL a chybu jednotlivých kalibračných bodov.",
        };
        button.Click += (_, _) => OpenLatestCalibrationResult();
        Grid.SetRow(button, 0);
        Panel.SetZIndex(button, 60);
        rootGrid.Children.Add(button);
        _calibrationResultButton = button;
    }

    private void OnPaliParityViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_viewModel.IsRunning) && !_viewModel.IsRunning)
        {
            // RefreshHistory runs at the end of the calibration finally block. Defer one UI turn so
            // the latest persisted run is already in the collection when we evaluate the badge.
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(RefreshCalibrationResultButton));
        }
    }

    private void OnPaliParityHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshCalibrationResultButton();

    private void RefreshCalibrationResultButton()
    {
        if (_calibrationResultButton is null) return;

        _latestCalculatedRun = _viewModel.History
            .Where(run => run.CalculationResults.Count > 0)
            .OrderByDescending(run => run.StartedAt)
            .FirstOrDefault();

        if (_latestCalculatedRun is null)
        {
            _calibrationResultButton.Visibility = Visibility.Collapsed;
            return;
        }

        int total = _latestCalculatedRun.CalculationResults.Count;
        int passed = _latestCalculatedRun.CalculationResults.Count(result => result.OverallPassed);
        bool allPassed = passed == total;
        _calibrationResultButton.Content = allPassed
            ? $"✓ PASS · {passed}/{total} · zobraziť výsledok"
            : $"✕ FAIL · {passed}/{total} PASS · zobraziť výsledok";
        _calibrationResultButton.Background = allPassed ? Brushes.SeaGreen : Brushes.Firebrick;
        _calibrationResultButton.Visibility = Visibility.Visible;
    }

    private void OpenLatestCalibrationResult()
    {
        if (_latestCalculatedRun is null) return;
        var window = new CalibrationResultWindow(_latestCalculatedRun)
        {
            Owner = this,
        };
        window.ShowDialog();
    }

    private void OnPaliParityWindowClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnPaliParityViewModelPropertyChanged;
        _viewModel.History.CollectionChanged -= OnPaliParityHistoryChanged;
        Closed -= OnPaliParityWindowClosed;
    }

    private static TabItem? FindTabItemByHeader(DependencyObject root, string header)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is TabItem tab && string.Equals(tab.Header?.ToString(), header, StringComparison.Ordinal))
                return tab;
            if (child is DependencyObject dependencyObject)
            {
                TabItem? nested = FindTabItemByHeader(dependencyObject, header);
                if (nested is not null) return nested;
            }
        }
        return null;
    }
}
