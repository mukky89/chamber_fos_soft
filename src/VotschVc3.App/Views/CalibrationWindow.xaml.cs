using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.Calibration;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.Views;

public partial class CalibrationWindow : Window
{
    /// <summary>Each chamber owns one independent, reusable calibration workspace.</summary>
    private static readonly Dictionary<Guid, CalibrationWindow> Instances = new();

    private readonly CalibrationViewModel _viewModel;
    private readonly SylexFosCalibrationIntegration _sylexFosIntegration;
    private readonly Guid _chamberId;
    private readonly Border _fosApiStatusBadge;
    private readonly TextBlock _fosApiStatusText;
    private readonly DispatcherTimer _fosApiStatusHideTimer;
    private readonly Border _duplicateSnBadge;
    private readonly TextBlock _duplicateSnText;
    private readonly Stopwatch _startupStopwatch = Stopwatch.StartNew();
    private readonly long _viewModelConstructionMs;
    private DataGrid? _wiringGrid;
    private StrictSerialValidationCommand? _strictStartCommand;
    private bool _pendingWiringGridRefresh;
    private bool _passiveHardwareRefreshStarted;
    private bool _disposing;
    private bool _shutdownRequested;

    public CalibrationWindow(Guid chamberId)
    {
        _chamberId = chamberId;

        var phase = Stopwatch.StartNew();
        _viewModel = new CalibrationViewModel(chamberId);
        _viewModelConstructionMs = phase.ElapsedMilliseconds;

        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;

        (_fosApiStatusBadge, _fosApiStatusText) = CreateFosApiStatusBadge();
        _fosApiStatusHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _fosApiStatusHideTimer.Tick += (_, _) =>
        {
            _fosApiStatusHideTimer.Stop();
            _fosApiStatusBadge.Visibility = Visibility.Collapsed;
        };

        (_duplicateSnBadge, _duplicateSnText) = CreateDuplicateSnBadge();

        if (Content is Grid rootGrid)
        {
            Grid.SetRow(_fosApiStatusBadge, 0);
            Panel.SetZIndex(_fosApiStatusBadge, 50);
            rootGrid.Children.Add(_fosApiStatusBadge);

            Grid.SetRow(_duplicateSnBadge, 0);
            Panel.SetZIndex(_duplicateSnBadge, 51);
            rootGrid.Children.Add(_duplicateSnBadge);
        }

        _sylexFosIntegration = new SylexFosCalibrationIntegration(_viewModel);
        _sylexFosIntegration.LookupStatusChanged += OnSylexFosLookupStatusChanged;

        // Network health checks are deliberately deferred until the first frame has rendered.
        // FOS metadata is optional and must never delay showing the calibration workspace.
        _ = InitializeFosApiDeferredAsync();

        Closing += OnClosing;
    }

    /// <summary>
    /// Make the first frame useful immediately. The previous startup awaited a complete hardware
    /// initialization here: WMI enumeration, active probing of every COM port as a CTH7000 and a
    /// broad PeakLogger discovery. A slow/unresponsive USB device could therefore make opening the
    /// FBG workspace feel frozen. Hardware connection is now operator-driven; only passive USB
    /// metadata enrichment is scheduled after the UI is already interactive.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ConfigureWiringGrid();

        _startupStopwatch.Stop();
        AppLog.Info(
            "FBG kalibrácia",
            $"Okno pripravené za {_startupStopwatch.ElapsedMilliseconds} ms " +
            $"(ViewModel {_viewModelConstructionMs} ms). Bez automatického COM probe/PeakLogger discovery.");

        _ = StartPassiveHardwareMetadataRefreshAsync();
    }

    private void ConfigureWiringGrid()
    {
        _wiringGrid = FindWiringGrid(this);
        if (_wiringGrid is null)
        {
            AppLog.Warn("FBG kalibrácia", "Zapojovacia tabuľka sa pri otvorení nenašla vo visual tree.");
            return;
        }

        DataGridColumn? chainSn = FindColumn(_wiringGrid, "FBG sensor SN CHAIN");
        DataGridColumn? order = FindColumn(_wiringGrid, "Zákazka");
        DataGridColumn? customer = FindColumn(_wiringGrid, "Zákazník");
        DataGridColumn? productDescription = FindColumn(_wiringGrid, "Popis produktu")
            ?? FindColumn(_wiringGrid, "Popis výrobku");
        if (order is null || customer is null || productDescription is null)
        {
            AttachStrictSerialValidation();
            return;
        }

        // Restore the correct business meaning. Customer is customer name, never sensor name.
        customer.Header = "Zákazník";
        productDescription.Header = "Popis výrobku";

        DataGridColumn? sensorName = FindColumn(_wiringGrid, "Názov snímača");
        if (sensorName is null)
        {
            sensorName = new DataGridTextColumn
            {
                Header = "Názov snímača",
                IsReadOnly = true,
                Width = new DataGridLength(0.85, DataGridLengthUnitType.Star),
                Binding = new Binding
                {
                    Converter = new SylexFosSensorNameConverter(),
                    Mode = BindingMode.OneWay,
                },
            };
            _wiringGrid.Columns.Add(sensorName);
        }

        // Operator-facing production data stays together after the FBG SN columns.
        // FBG SN -> Zakázka -> Názov snímača -> Popis výrobku -> Zákazník.
        int firstProductionIndex = Math.Min(
            _wiringGrid.Columns.Count - 4,
            (chainSn?.DisplayIndex ?? FindColumn(_wiringGrid, "FBG sensor SN (kanál)")?.DisplayIndex ?? 0) + 1);
        order.DisplayIndex = firstProductionIndex;
        sensorName.DisplayIndex = firstProductionIndex + 1;
        productDescription.DisplayIndex = firstProductionIndex + 2;
        customer.DisplayIndex = firstProductionIndex + 3;

        AttachStrictSerialValidation();
    }

    private async Task StartPassiveHardwareMetadataRefreshAsync()
    {
        if (_passiveHardwareRefreshStarted || _disposing) return;
        _passiveHardwareRefreshStarted = true;

        // Give WPF an idle turn so maximization/layout/input are finished before any background
        // enumeration begins. RefreshF100PortsCommand performs WMI on Task.Run and does NOT open
        // a COM port or send *IDN?/REMOTE/MEASURE commands.
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        if (_disposing) return;

        try
        {
            if (_viewModel.RefreshF100PortsCommand.CanExecute(null))
            {
                _viewModel.RefreshF100PortsCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("FBG kalibrácia", $"Pasívne obnovenie USB metadata: {ex.Message}");
        }
    }

    private async Task InitializeFosApiDeferredAsync()
    {
        try
        {
            await Task.Delay(250).ConfigureAwait(true);
            if (_disposing) return;
            await _sylexFosIntegration.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Sylex FOS API", $"Odložená kontrola API pri otvorení kalibrácie: {ex.Message}");
        }
    }

    private static DataGridColumn? FindColumn(DataGrid grid, string header) =>
        grid.Columns.FirstOrDefault(column => string.Equals(column.Header?.ToString(), header, StringComparison.Ordinal));

    private static DataGrid? FindWiringGrid(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is DataGrid grid &&
                FindColumn(grid, "FBG sensor SN (kanál)") is not null &&
                FindColumn(grid, "Zákazka") is not null)
            {
                return grid;
            }

            DataGrid? nested = FindWiringGrid(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static Button? FindButtonByCommand(DependencyObject root, ICommand command)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button && ReferenceEquals(button.Command, command))
            {
                return button;
            }

            Button? nested = FindButtonByCommand(child, command);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static (Border Badge, TextBlock Text) CreateFosApiStatusBadge()
    {
        var text = new TextBlock
        {
            Text = "FOS API · kontrolujem pripojenie…",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
        };

        var badge = new Border
        {
            Child = text,
            Background = Brushes.DimGray,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "Stav načítania výrobných údajov zo Sylex FOS API. Výpadok API neblokuje samotnú kalibráciu.",
        };

        return (badge, text);
    }

    private static (Border Badge, TextBlock Text) CreateDuplicateSnBadge()
    {
        var text = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
        };

        var badge = new Border
        {
            Child = text,
            Background = Brushes.Firebrick,
            BorderBrush = Brushes.OrangeRed,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 34, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            MaxWidth = 900,
            Visibility = Visibility.Collapsed,
            ToolTip = "Rovnaké produkčné FBG SN nesmie byť priradené k viacerým PeakLogger kanálom.",
        };

        return (badge, text);
    }

    private void OnSylexFosLookupStatusChanged(object? sender, SylexFosLookupStatus status)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            ApplyFosApiStatus(status);
            if (status.State is SylexFosLookupState.Loaded or SylexFosLookupState.NotFound)
            {
                RefreshWiringGridWhenSafe();
            }
        });
    }

    /// <summary>
    /// DataGrid forbids CollectionView.Refresh while a row is in AddNew/EditItem state.
    /// API lookups complete asynchronously and can therefore arrive while the operator is still
    /// editing the serial-number cell. Defer the refresh until RowEditEnding instead of forcing
    /// the transaction to commit or crashing the calibration window.
    /// </summary>
    private void RefreshWiringGridWhenSafe()
    {
        DataGrid? grid = _wiringGrid;
        if (grid is null) return;

        if (grid.Items is IEditableCollectionView editableView &&
            (editableView.IsAddingNew || editableView.IsEditingItem))
        {
            if (!_pendingWiringGridRefresh)
            {
                _pendingWiringGridRefresh = true;
                grid.RowEditEnding += OnWiringGridRowEditEnding;
            }
            return;
        }

        _pendingWiringGridRefresh = false;
        grid.RowEditEnding -= OnWiringGridRowEditEnding;

        try
        {
            grid.Items.Refresh();
        }
        catch (InvalidOperationException ex)
        {
            // A WPF edit transaction may begin between the state check and Refresh().
            // Keep the UI usable and refresh on the next completed row edit.
            _pendingWiringGridRefresh = true;
            grid.RowEditEnding -= OnWiringGridRowEditEnding;
            grid.RowEditEnding += OnWiringGridRowEditEnding;
            AppLog.Warn("Sylex FOS API", $"Odložené obnovenie kalibračnej tabuľky: {ex.Message}");
        }
    }

    private void OnWiringGridRowEditEnding(object? sender, DataGridRowEditEndingEventArgs e)
    {
        if (!_pendingWiringGridRefresh) return;

        // RowEditEnding fires before WPF finishes the edit transaction. Run after the event
        // returns so CollectionView.Refresh is legal.
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(RefreshWiringGridWhenSafe));
    }

    private void ApplyFosApiStatus(SylexFosLookupStatus status)
    {
        _fosApiStatusHideTimer.Stop();
        _fosApiStatusBadge.Visibility = Visibility.Visible;
        _fosApiStatusText.Text = status.Message;
        _fosApiStatusBadge.Background = status.State switch
        {
            SylexFosLookupState.Loaded or SylexFosLookupState.ApiAvailable => Brushes.SeaGreen,
            SylexFosLookupState.Loading or SylexFosLookupState.CheckingApi => Brushes.DarkGoldenrod,
            SylexFosLookupState.NotFound or SylexFosLookupState.ConfigurationError => Brushes.DarkOrange,
            SylexFosLookupState.ApiUnavailable => Brushes.Firebrick,
            _ => Brushes.DimGray,
        };

        _fosApiStatusBadge.ToolTip = status.State switch
        {
            SylexFosLookupState.Loaded => "Výrobné údaje boli načítané: zakázka, popis výrobku a názov snímača. Zákazník je samostatné pole.",
            SylexFosLookupState.NotFound => "SN sa v Sylex FOS API nenašlo. Skontroluj SN; polia zostávajú ručne editovateľné.",
            SylexFosLookupState.ConfigurationError => "Skontroluj SYLEX_FOS_API_KEY a konfiguráciu klienta chamber-fos.",
            SylexFosLookupState.ApiUnavailable => "Centrálne API nie je dostupné. Kalibrácia môže pokračovať bez automatického doplnenia metadata.",
            _ => "Stav načítania výrobných údajov zo Sylex FOS API.",
        };

        // Green success/availability notifications are transient. Error states stay visible
        // until a later status replaces them so operators do not miss connectivity problems.
        if (status.State is SylexFosLookupState.Loaded or SylexFosLookupState.ApiAvailable)
        {
            _fosApiStatusHideTimer.Start();
        }
    }

    private void AttachStrictSerialValidation()
    {
        foreach (CalibrationPeakRowViewModel row in _viewModel.Peaks)
        {
            AttachStrictSerialValidationRow(row);
        }

        _viewModel.Peaks.CollectionChanged -= OnStrictValidationPeaksChanged;
        _viewModel.Peaks.CollectionChanged += OnStrictValidationPeaksChanged;

        Button? startButton = FindButtonByCommand(this, _viewModel.StartCalibrationCommand);
        if (startButton is not null)
        {
            _strictStartCommand = new StrictSerialValidationCommand(
                _viewModel.StartCalibrationCommand,
                () => !HasCrossChannelDuplicateSerialNumbers());
            startButton.Command = _strictStartCommand;
        }

        UpdateStrictSerialValidation();
    }

    private void OnStrictValidationPeaksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (CalibrationPeakRowViewModel row in e.OldItems)
            {
                row.PropertyChanged -= OnStrictValidationRowPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (CalibrationPeakRowViewModel row in e.NewItems)
            {
                AttachStrictSerialValidationRow(row);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (CalibrationPeakRowViewModel row in _viewModel.Peaks)
            {
                AttachStrictSerialValidationRow(row);
            }
        }

        UpdateStrictSerialValidation();
    }

    private void AttachStrictSerialValidationRow(CalibrationPeakRowViewModel row)
    {
        row.PropertyChanged -= OnStrictValidationRowPropertyChanged;
        row.PropertyChanged += OnStrictValidationRowPropertyChanged;
    }

    private void OnStrictValidationRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CalibrationPeakRowViewModel.ChannelSerialNumber)
            or nameof(CalibrationPeakRowViewModel.ChainSerialNumber)
            or nameof(CalibrationPeakRowViewModel.SerialNumber))
        {
            UpdateStrictSerialValidation();
        }
    }

    private void UpdateStrictSerialValidation()
    {
        List<IGrouping<string, CalibrationPeakRowViewModel>> duplicates = GetCrossChannelDuplicateSerialNumbers();
        if (duplicates.Count == 0)
        {
            _duplicateSnBadge.Visibility = Visibility.Collapsed;
            _duplicateSnText.Text = string.Empty;
            _strictStartCommand?.RaiseCanExecuteChanged();
            return;
        }

        foreach (IGrouping<string, CalibrationPeakRowViewModel> duplicate in duplicates)
        {
            string channels = string.Join(", ", duplicate
                .Select(row => row.Channel)
                .Where(channel => !string.IsNullOrWhiteSpace(channel))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            string message = $"CHYBA: FBG SN „{duplicate.Key}“ je priradené k viacerým kanálom ({channels}). Každé produkčné FBG SN môže patriť iba jednému kanálu.";
            foreach (CalibrationPeakRowViewModel row in duplicate)
            {
                row.AddSerialNumberWarning(message);
            }
        }

        string summary = string.Join("  |  ", duplicates.Select(group =>
        {
            string channels = string.Join(", ", group
                .Select(row => row.Channel)
                .Where(channel => !string.IsNullOrWhiteSpace(channel))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            return $"{group.Key}: kanály {channels}";
        }));

        _duplicateSnText.Text = $"⛔ DUPLICITNÉ FBG SN — kalibráciu nemožno spustiť. {summary}";
        _duplicateSnBadge.Visibility = Visibility.Visible;
        _strictStartCommand?.RaiseCanExecuteChanged();
    }

    private bool HasCrossChannelDuplicateSerialNumbers() => GetCrossChannelDuplicateSerialNumbers().Count > 0;

    private List<IGrouping<string, CalibrationPeakRowViewModel>> GetCrossChannelDuplicateSerialNumbers() =>
        _viewModel.Peaks
            .Where(row => !string.IsNullOrWhiteSpace(row.SerialNumber))
            .GroupBy(row => row.SerialNumber.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group
                .Select(row => $"{row.PeakLoggerDeviceSerialNumber}|{row.Channel}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1)
            .ToList();

    private sealed class StrictSerialValidationCommand : ICommand
    {
        private readonly ICommand _inner;
        private readonly Func<bool> _isValid;

        public StrictSerialValidationCommand(ICommand inner, Func<bool> isValid)
        {
            _inner = inner;
            _isValid = isValid;
            _inner.CanExecuteChanged += OnInnerCanExecuteChanged;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _isValid() && _inner.CanExecute(parameter);

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                _inner.Execute(parameter);
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        private void OnInnerCanExecuteChanged(object? sender, EventArgs e) => RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Opens (or re-activates) the shared FBG calibration workspace and preselects the
    /// device the operator pressed the button on – every chamber has its own button, and
    /// each of them should land on that chamber.
    /// </summary>
    public static void OpenFor(Window? owner, Guid chamberId)
    {
        // A window that is already tearing its devices down must not be handed back out.
        Instances.TryGetValue(chamberId, out CalibrationWindow? existing);
        if (existing is null || !existing.IsLoaded || existing._disposing)
        {
            var window = new CalibrationWindow(chamberId) { Owner = owner };
            window.Closed += (_, _) =>
            {
                if (Instances.TryGetValue(chamberId, out CalibrationWindow? current) && ReferenceEquals(current, window))
                {
                    Instances.Remove(chamberId);
                }
            };
            Instances[chamberId] = window;
            existing = window;
            existing.Show();
        }
        else
        {
            if (!existing.IsVisible)
            {
                existing.Show();
            }

            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
        }
    }

    /// <summary>Closes the workspace if one is open (called when the app shuts down).</summary>
    public static void CloseIfOpen()
    {
        foreach (CalibrationWindow window in Instances.Values.ToArray())
        {
            window._shutdownRequested = true;
            window.Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Tears the long-running calibration resources down before the window really closes.
    /// The close is cancelled, the disposal runs, and the real <see cref="Window.Close"/>
    /// is posted back to the dispatcher – calling it inline would re-enter WPF while it is
    /// still inside this Closing event ("Cannot set Visibility to Visible or call Show,
    /// ShowDialog, Close … while a Window is closing") whenever the disposal happens to
    /// complete synchronously.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_shutdownRequested)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        if (_disposing)
        {
            return;
        }

        e.Cancel = true;
        _disposing = true;
        Closing -= OnClosing;
        _ = DisposeThenCloseAsync();
    }

    private async Task DisposeThenCloseAsync()
    {
        try
        {
            _fosApiStatusHideTimer.Stop();
            _viewModel.Peaks.CollectionChanged -= OnStrictValidationPeaksChanged;
            foreach (CalibrationPeakRowViewModel row in _viewModel.Peaks)
            {
                row.PropertyChanged -= OnStrictValidationRowPropertyChanged;
            }
            _sylexFosIntegration.LookupStatusChanged -= OnSylexFosLookupStatusChanged;
            await _sylexFosIntegration.DisposeAsync();
            await _viewModel.DisposeAsync();
        }
        catch (Exception ex)
        {
            // The window must close even if a device/API integration hangs on shutdown – log and carry on.
            AppLog.Warn("FBG kalibrácia", $"Ukončenie kalibračného okna: {ex.Message}");
        }

        try
        {
            await Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Close));
        }
        catch (Exception ex)
        {
            AppLog.Warn("FBG kalibrácia", $"Zatvorenie kalibračného okna: {ex.Message}");
        }
    }
}
