using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.Calibration;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.Views;

/// <summary>
/// Production hardening layered on top of ProductionWorkspaceV2.
/// Key rule: an operator typing a production SN owns the wiring grid until the edit is committed.
/// Background topology/Sylex refreshes are deferred and must never cancel the active DataGrid edit.
/// </summary>
internal static class CalibrationWindowProductionWorkspaceV3Bootstrap
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
        if (sender is CalibrationWindow window) window.InitializeProductionWorkspaceV3();
    }
}

public partial class CalibrationWindow
{
    private bool _productionWorkspaceV3Initialized;
    private bool _wiringEditActive;
    private bool _pendingWiringRefresh;
    private bool _wiringRefreshScheduled;
    private CancellationTokenSource? _silentReferenceReadCts;
    private TextBlock? _productionPlanText;
    private TextBlock? _productionStepText;
    private TextBlock? _productionWaitText;
    private TextBlock? _productionTelemetryText;

    internal void InitializeProductionWorkspaceV3()
    {
        if (_productionWorkspaceV3Initialized) return;
        _productionWorkspaceV3Initialized = true;

        // V3 depends on V2 controls/fields. Calling it is harmless if its Loaded handler ran first.
        InitializeProductionWorkspaceV2();

        // Replace V2 row refresh behavior. V2 called Items.Refresh() synchronously on every SN
        // keystroke, which throws during AddNew/EditItem and also kicks the operator out of the cell.
        foreach (CalibrationPeakRowViewModel row in _productionObservedRows.ToArray())
        {
            row.PropertyChanged -= OnProductionRowPropertyChanged;
            row.PropertyChanged -= OnProductionRowPropertyChangedV3;
            row.PropertyChanged += OnProductionRowPropertyChangedV3;
        }
        _viewModel.Peaks.CollectionChanged += OnProductionPeaksCollectionChangedV3;

        if (_sylexFosIntegration is not null)
        {
            _sylexFosIntegration.MetadataApplied -= OnSylexMetadataApplied;
            _sylexFosIntegration.MetadataApplied += OnSylexMetadataAppliedV3;
        }

        // Reuse the existing timers but replace their handlers. The 5 s WIKA refresh now reads
        // directly in the background instead of executing the UI-bound "Načítať teplotu" command.
        if (_referenceFiveSecondTimer is not null)
        {
            _referenceFiveSecondTimer.Tick -= ReferenceFiveSecondTimer_Tick;
            _referenceFiveSecondTimer.Tick += ReferenceFiveSecondTimer_TickV3;
        }
        if (_topologyTimer is not null)
        {
            _topologyTimer.Tick -= TopologyTimer_Tick;
            _topologyTimer.Tick += TopologyTimer_TickV3;
        }

        _viewModel.PropertyChanged += OnProductionWorkspaceV3PropertyChanged;
        _viewModel.CalibrationPoints.CollectionChanged += OnCalibrationPointsCollectionChangedV3;
        foreach (CalibrationPointRowViewModel point in _viewModel.CalibrationPoints)
            point.PropertyChanged += OnCalibrationPointPropertyChangedV3;

        Closed += OnProductionWorkspaceV3Closed;

        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            ConfigureWiringGridEditingV3();
            EnsurePageScrollV3();
            EnhanceRunPlanPanelV3();
            UpdateProductionPlanAndStepV3();
        }));
    }

    private void ConfigureWiringGridEditingV3()
    {
        if (_wiringGrid is null) return;

        // 16 production lines visible when the operator scrolls to the Zapojenie workspace.
        // The page itself is scrollable, so the grid no longer gets clipped to 3-4 rows by the
        // large hardware setup panel above it.
        _wiringGrid.RowHeight = 32;
        _wiringGrid.ColumnHeaderHeight = 38;
        _wiringGrid.MinHeight = (16 * 32) + 38 + 8;
        _wiringGrid.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        _wiringGrid.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        if (_productionTabs is not null)
            _productionTabs.MinHeight = _wiringGrid.MinHeight + 112;

        _wiringGrid.BeginningEdit -= WiringGrid_BeginningEditV3;
        _wiringGrid.BeginningEdit += WiringGrid_BeginningEditV3;
        _wiringGrid.CellEditEnding -= WiringGrid_CellEditEndingV3;
        _wiringGrid.CellEditEnding += WiringGrid_CellEditEndingV3;
        _wiringGrid.LostKeyboardFocus -= WiringGrid_LostKeyboardFocusV3;
        _wiringGrid.LostKeyboardFocus += WiringGrid_LostKeyboardFocusV3;
    }

    private void EnsurePageScrollV3()
    {
        if (Content is ScrollViewer viewer && Equals(viewer.Tag, "FBG_PAGE_SCROLL_V3")) return;
        if (Content is not Grid root) return;

        Content = null;
        var scroll = new ScrollViewer
        {
            Tag = "FBG_PAGE_SCROLL_V3",
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Content = root,
        };
        Content = scroll;
    }

    private void WiringGrid_BeginningEditV3(object? sender, DataGridBeginningEditEventArgs e)
    {
        _wiringEditActive = true;
    }

    private void WiringGrid_CellEditEndingV3(object? sender, DataGridCellEditEndingEventArgs e)
    {
        // CellEditEnding fires before WPF leaves the CollectionView edit transaction. Do not
        // refresh here. Return to the dispatcher first, then flush any deferred visual update.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _wiringEditActive = false;
            if (_pendingWiringRefresh) RequestWiringGridRefreshV3();

            // The VM already autosaves with a short debounce on every SN change. This final save
            // on commit is an additional persistence point and never runs while the cell is editing.
            if (!_viewModel.IsRunning && _viewModel.SaveSetupCommand.CanExecute(null))
                _viewModel.SaveSetupCommand.Execute(null);
        }));
    }

    private void WiringGrid_LostKeyboardFocusV3(object? sender, KeyboardFocusChangedEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (_wiringGrid?.IsKeyboardFocusWithin == true) return;
            _wiringEditActive = false;
            if (_pendingWiringRefresh) RequestWiringGridRefreshV3();
        }));
    }

    private bool IsWiringGridEditingV3()
    {
        if (_wiringEditActive) return true;
        if (_wiringGrid is null) return false;

        if (_wiringGrid.Items is IEditableCollectionView editable &&
            (editable.IsAddingNew || editable.IsEditingItem))
            return true;

        return _wiringGrid.IsKeyboardFocusWithin &&
               FindProductionDescendants<TextBox>(_wiringGrid).Any(box => box.IsKeyboardFocused || box.IsKeyboardFocusWithin);
    }

    private void RequestWiringGridRefreshV3()
    {
        _pendingWiringRefresh = true;
        if (_wiringRefreshScheduled || _wiringGrid is null) return;
        _wiringRefreshScheduled = true;

        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _wiringRefreshScheduled = false;
            if (_wiringGrid is null) return;
            if (IsWiringGridEditingV3()) return; // leave pending=true; edit-end will retry

            try
            {
                _wiringGrid.Items.Refresh();
                _pendingWiringRefresh = false;
            }
            catch (InvalidOperationException ex)
            {
                // Defensive guard for a CollectionView transaction that began between the check
                // and Refresh(). Never surface this to the operator; retry after the edit ends.
                _pendingWiringRefresh = true;
                AppLog.Info("FBG zapojenie", $"Refresh odložený do ukončenia editácie: {ex.Message}");
            }
        }));
    }

    private void OnProductionPeaksCollectionChangedV3(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (CalibrationPeakRowViewModel row in e.OldItems)
                row.PropertyChanged -= OnProductionRowPropertyChangedV3;
        }

        if (e.NewItems is not null)
        {
            foreach (CalibrationPeakRowViewModel row in e.NewItems)
            {
                // V2's collection handler ran first and attached its synchronous refresh handler.
                row.PropertyChanged -= OnProductionRowPropertyChanged;
                row.PropertyChanged -= OnProductionRowPropertyChangedV3;
                row.PropertyChanged += OnProductionRowPropertyChangedV3;
            }
        }

        UpdateProductionPlanAndStepV3();
    }

    private void OnProductionRowPropertyChangedV3(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CalibrationPeakRowViewModel row) return;

        if (e.PropertyName is nameof(CalibrationPeakRowViewModel.ChannelSerialNumber)
            or nameof(CalibrationPeakRowViewModel.ChainSerialNumber)
            or nameof(CalibrationPeakRowViewModel.SerialNumber))
        {
            SylexFosRowMetadataStore.SetParsedSerial(row, row.SerialNumber);
            RequestWiringGridRefreshV3();
        }

        if (e.PropertyName is nameof(CalibrationPeakRowViewModel.Selected)
            or nameof(CalibrationPeakRowViewModel.ChannelSerialNumber)
            or nameof(CalibrationPeakRowViewModel.ChainSerialNumber))
            UpdateProductionPlanAndStepV3();
    }

    private void OnSylexMetadataAppliedV3(object? sender, CalibrationPeakRowViewModel row)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => OnSylexMetadataAppliedV3(sender, row)));
            return;
        }
        RequestWiringGridRefreshV3();
    }

    private async void ReferenceFiveSecondTimer_TickV3(object? sender, EventArgs e)
    {
        if (_referenceRefreshRequested || _viewModel.IsRunning || _viewModel.SelectedF100 is null) return;

        _referenceRefreshRequested = true;
        _silentReferenceReadCts?.Cancel();
        _silentReferenceReadCts?.Dispose();
        _silentReferenceReadCts = new CancellationTokenSource(TimeSpan.FromSeconds(4.5));

        try
        {
            ThermometerDeviceViewModel device = _viewModel.SelectedF100;
            device.SelectedChannel = _viewModel.SelectedF100Channel;
            await device.ReadReferenceTemperatureAsync(_silentReferenceReadCts.Token);

            if (!string.Equals(_viewModel.SelectedF100Channel, device.SelectedChannel, StringComparison.Ordinal))
                _viewModel.SelectedF100Channel = device.SelectedChannel;

            // Temperature is a property of ThermometerDeviceViewModel, therefore all normal UI
            // bindings and the per-device dashboard reference snapshot update without executing
            // CheckF100Command and without making the button appear to click itself.
            UpdateProductionPlanAndStepV3();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Warn("WIKA background refresh", ex.Message);
        }
        finally
        {
            _referenceRefreshRequested = false;
        }
    }

    private async void TopologyTimer_TickV3(object? sender, EventArgs e)
    {
        if (_viewModel.IsRunning || !_viewModel.PeakLoggerConnected || _viewModel.UseSimulator || _extendedPeakLoggerApi is null) return;

        // Operator edit has priority over topology discovery. A sensor plug/unplug can wait until
        // the SN cell is committed; losing typed production identity is never acceptable.
        if (IsWiringGridEditingV3()) return;
        if (_topologyPollCts is not null) return;

        _topologyPollCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            IReadOnlySet<string>? topology = await _extendedPeakLoggerApi.ReadTopologyAsync(
                _viewModel.PeakLoggerHost,
                _viewModel.PeakLoggerPort,
                _topologyPollCts.Token);
            if (topology is null || IsWiringGridEditingV3()) return;

            HashSet<string> live = topology.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (live.SetEquals(CurrentPeakIdentities())) return;

            if (_viewModel.SaveSetupCommand.CanExecute(null)) _viewModel.SaveSetupCommand.Execute(null);
            ShowProductionInfo("PeakLogger hlási zmenu zapojenia – po ukončení editácie aktualizujem tabuľku…");
            if (!IsWiringGridEditingV3() && _viewModel.RefreshSensorsCommand.CanExecute(null))
                _viewModel.RefreshSensorsCommand.Execute(null);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Warn("PeakLogger topology", ex.Message);
        }
        finally
        {
            _topologyPollCts?.Dispose();
            _topologyPollCts = null;
        }
    }

    private void EnhanceRunPlanPanelV3()
    {
        if (_productionRunPanel?.Child is not StackPanel stack || _productionPlanText is not null) return;

        _productionPlanText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.82,
            Margin = new Thickness(0, 5, 0, 7),
        };
        _productionStepText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 3),
        };
        _productionWaitText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 0, 3),
        };
        _productionTelemetryText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.82,
            Margin = new Thickness(0, 0, 0, 8),
        };

        // V2 detail is superseded by the explicit plan/current-step/wait/telemetry block.
        if (_productionRunDetail is not null) _productionRunDetail.Visibility = Visibility.Collapsed;

        int insert = Math.Min(1, stack.Children.Count);
        stack.Children.Insert(insert++, _productionPlanText);
        stack.Children.Insert(insert++, _productionStepText);
        stack.Children.Insert(insert++, _productionWaitText);
        stack.Children.Insert(insert, _productionTelemetryText);
    }

    private void OnProductionWorkspaceV3PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CalibrationViewModel.IsRunning)
            or nameof(CalibrationViewModel.RunState)
            or nameof(CalibrationViewModel.PlateauLabel)
            or nameof(CalibrationViewModel.TemperatureLabel)
            or nameof(CalibrationViewModel.ReferenceTemperatureLabel)
            or nameof(CalibrationViewModel.StableLabel)
            or nameof(CalibrationViewModel.StatusMessage)
            or nameof(CalibrationViewModel.SelectedProfile)
            or nameof(CalibrationViewModel.SelectedF100)
            or nameof(CalibrationViewModel.SelectedF100Channel))
        {
            UpdateProductionPlanAndStepV3();
        }
    }

    private void OnCalibrationPointsCollectionChangedV3(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (CalibrationPointRowViewModel point in e.OldItems) point.PropertyChanged -= OnCalibrationPointPropertyChangedV3;
        if (e.NewItems is not null)
            foreach (CalibrationPointRowViewModel point in e.NewItems)
            {
                point.PropertyChanged -= OnCalibrationPointPropertyChangedV3;
                point.PropertyChanged += OnCalibrationPointPropertyChangedV3;
            }
        UpdateProductionPlanAndStepV3();
    }

    private void OnCalibrationPointPropertyChangedV3(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalibrationPointRowViewModel.Selected)) UpdateProductionPlanAndStepV3();
    }

    private void UpdateProductionPlanAndStepV3()
    {
        if (_productionPlanText is null || _productionStepText is null || _productionWaitText is null || _productionTelemetryText is null)
            return;

        CalibrationPointRowViewModel[] points = _viewModel.CalibrationPoints.Where(p => p.Selected).ToArray();
        CalibrationPeakRowViewModel[] peaks = _viewModel.Peaks.Where(p => p.Selected).ToArray();
        string temperatures = points.Length == 0
            ? "žiadne plata"
            : string.Join(" → ", points.Select(p => $"{p.TemperatureC:0.##} °C"));
        string reference = _viewModel.SelectedF100 is null
            ? "WIKA nepriradená"
            : $"WIKA {_viewModel.SelectedF100.PortName}/{_viewModel.SelectedF100Channel}";
        string profile = _viewModel.SelectedProfile?.Name ?? "profil nevybraný";

        _productionPlanText.Text =
            $"PLÁN · {profile} · {points.Length} plat: {temperatures} · {peaks.Length} FBG peakov · {reference} · " +
            $"{_viewModel.RequiredStableSamples} stabilných samples / peak";

        CalibrationTargetProgressViewModel? active = _viewModel.TargetProgress.FirstOrDefault(target =>
            target.State is not CalibrationTargetState.Stable and not CalibrationTargetState.Overridden)
            ?? _viewModel.TargetProgress.FirstOrDefault();

        string state = _viewModel.RunState ?? "Idle";
        (_productionStepText.Text, _productionWaitText.Text) = DescribeCalibrationStepV3(state, active);

        string activePeak = active is null
            ? "peak: —"
            : $"peak: {active.SerialNumber} · CH {active.Channel} · {active.PeakId} · λ {active.CurrentWavelengthNm?.ToString("F6") ?? "—"} nm · samples {active.SamplesLabel}";
        string liveReference = _viewModel.SelectedF100?.Temperature is { } t
            ? $"{t:F3} °C"
            : _viewModel.ReferenceTemperatureLabel;

        _productionTelemetryText.Text =
            $"{activePeak}\nReferencia WIKA: {liveReference} · komora (informatívne): {_viewModel.TemperatureLabel} · stabilné peaky: {_viewModel.StableLabel}";
    }

    private (string Step, string WaitingFor) DescribeCalibrationStepV3(
        string state,
        CalibrationTargetProgressViewModel? active)
    {
        string plateau = string.IsNullOrWhiteSpace(_viewModel.PlateauLabel) ? "plato" : _viewModel.PlateauLabel;
        return state switch
        {
            nameof(CalibrationRunState.Preflight) =>
                ("AKTUÁLNY KROK · Predbežná kontrola", "Kontrolujem PeakLogger, vybrané peaky, SN, profil a referenčný teplomer."),
            nameof(CalibrationRunState.Preparing) =>
                ("AKTUÁLNY KROK · Príprava zariadení", "Pripájam komoru a pripravujem kalibračný run."),
            nameof(CalibrationRunState.BaselineCollection) =>
                ("AKTUÁLNY KROK · Počiatočné wavelength dáta", "Zbieram východiskové hodnoty vybraných FBG peakov."),
            nameof(CalibrationRunState.TemperatureResponseValidation) =>
                ("AKTUÁLNY KROK · Kontrola odozvy FBG", "Overujem, že vybrané peaky reagujú na zmenu referenčnej teploty."),
            nameof(CalibrationRunState.MovingToPlateau) or nameof(CalibrationRunState.MovingToNextPlateau) =>
                ($"AKTUÁLNY KROK · Presun na {plateau}", "Komora mení setpoint. Jej teplota je informatívna; rozhodujúca stabilita sa hodnotí z WIKA referencie."),
            nameof(CalibrationRunState.WaitingForChamberStability) =>
                ($"AKTUÁLNY KROK · Stabilita WIKA · {plateau}", $"ČAKÁM NA · stabilnú referenčnú teplotu WIKA podľa tolerancie a času stability. Aktuálne {_viewModel.ReferenceTemperatureLabel}."),
            nameof(CalibrationRunState.StabilizingSensors) =>
                ($"AKTUÁLNY KROK · Stabilizácia FBG peakov · {plateau}",
                 active is null
                     ? "ČAKÁM NA · stabilitu všetkých vybraných FBG peakov."
                     : $"ČAKÁM NA · {active.SerialNumber}, CH {active.Channel}, {active.PeakId}: {active.SamplesLabel} samples, stav {active.State}."),
            nameof(CalibrationRunState.PlateauCompleted) =>
                ($"AKTUÁLNY KROK · {plateau} dokončené", "Výsledky plata sú uložené; pokračujem na ďalšie vybrané plato."),
            nameof(CalibrationRunState.Paused) =>
                ("AKTUÁLNY KROK · PAUZA", "ČAKÁM NA · pokračovanie operátorom."),
            nameof(CalibrationRunState.AwaitingOperator) =>
                ("AKTUÁLNY KROK · Zásah operátora", $"ČAKÁM NA · vyriešenie problému: {_viewModel.StatusMessage}"),
            nameof(CalibrationRunState.Completed) or nameof(CalibrationRunState.CompletedWithWarnings) =>
                ("AKTUÁLNY KROK · Kalibrácia dokončená", _viewModel.StatusMessage),
            nameof(CalibrationRunState.Aborted) or nameof(CalibrationRunState.Failed) =>
                ("AKTUÁLNY KROK · Kalibrácia ukončená", _viewModel.StatusMessage),
            _ when _viewModel.IsRunning =>
                ($"AKTUÁLNY KROK · {state}", $"Prebieha kalibrácia · {_viewModel.StatusMessage}"),
            _ =>
                ("AKTUÁLNY KROK · Pripravené na spustenie", "Po stlačení Spustiť kalibráciu pôjdem po vybraných platoch v poradí uvedenom v pláne."),
        };
    }

    private void OnProductionWorkspaceV3Closed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnProductionWorkspaceV3PropertyChanged;
        _viewModel.Peaks.CollectionChanged -= OnProductionPeaksCollectionChangedV3;
        _viewModel.CalibrationPoints.CollectionChanged -= OnCalibrationPointsCollectionChangedV3;
        foreach (CalibrationPointRowViewModel point in _viewModel.CalibrationPoints)
            point.PropertyChanged -= OnCalibrationPointPropertyChangedV3;
        foreach (CalibrationPeakRowViewModel row in _productionObservedRows.ToArray())
            row.PropertyChanged -= OnProductionRowPropertyChangedV3;
        if (_sylexFosIntegration is not null) _sylexFosIntegration.MetadataApplied -= OnSylexMetadataAppliedV3;
        if (_referenceFiveSecondTimer is not null) _referenceFiveSecondTimer.Tick -= ReferenceFiveSecondTimer_TickV3;
        if (_topologyTimer is not null) _topologyTimer.Tick -= TopologyTimer_TickV3;
        _silentReferenceReadCts?.Cancel();
        _silentReferenceReadCts?.Dispose();
        Closed -= OnProductionWorkspaceV3Closed;
    }
}
