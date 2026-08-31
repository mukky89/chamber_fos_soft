using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
    private bool _disposing;
    private bool _shutdownRequested;

    public CalibrationWindow(Guid chamberId)
    {
        _chamberId = chamberId;
        _viewModel = new CalibrationViewModel(chamberId);
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;

        (_fosApiStatusBadge, _fosApiStatusText) = CreateFosApiStatusBadge();
        if (Content is Grid rootGrid)
        {
            Grid.SetRow(_fosApiStatusBadge, 0);
            Panel.SetZIndex(_fosApiStatusBadge, 50);
            rootGrid.Children.Add(_fosApiStatusBadge);
        }

        _sylexFosIntegration = new SylexFosCalibrationIntegration(_viewModel);
        _sylexFosIntegration.LookupStatusChanged += OnSylexFosLookupStatusChanged;
        _ = _sylexFosIntegration.InitializeAsync();

        Closing += OnClosing;
    }

    /// <summary>
    /// Keeps the operator-facing production metadata next to the entered FBG SN. The persisted
    /// property is still named Customer for backwards compatibility with existing setup files,
    /// but its business meaning is SensorName and the UI must never present it as a customer.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        DataGrid? wiringGrid = FindWiringGrid(this);
        if (wiringGrid is null) return;

        DataGridColumn? chainSn = FindColumn(wiringGrid, "FBG sensor SN CHAIN");
        DataGridColumn? order = FindColumn(wiringGrid, "Zákazka");
        DataGridColumn? sensorName = FindColumn(wiringGrid, "Zákazník");
        DataGridColumn? productDescription = FindColumn(wiringGrid, "Popis produktu");
        if (order is null || sensorName is null || productDescription is null) return;

        sensorName.Header = "Názov snímača";
        productDescription.Header = "Popis výrobku";

        // Put API-enriched production fields directly after the effective FBG SN columns:
        // FBG SN -> Zakázka -> Názov snímača -> Popis výrobku.
        int firstProductionIndex = Math.Min(
            wiringGrid.Columns.Count - 3,
            (chainSn?.DisplayIndex ?? FindColumn(wiringGrid, "FBG sensor SN (kanál)")?.DisplayIndex ?? 0) + 1);
        order.DisplayIndex = firstProductionIndex;
        sensorName.DisplayIndex = firstProductionIndex + 1;
        productDescription.DisplayIndex = firstProductionIndex + 2;
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

    private void OnSylexFosLookupStatusChanged(object? sender, SylexFosLookupStatus status)
    {
        _ = Dispatcher.InvokeAsync(() => ApplyFosApiStatus(status));
    }

    private void ApplyFosApiStatus(SylexFosLookupStatus status)
    {
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
            SylexFosLookupState.Loaded => "Výrobné údaje boli načítané: zakázka, popis výrobku a názov snímača.",
            SylexFosLookupState.NotFound => "SN sa v Sylex FOS API nenašlo. Skontroluj SN; polia zostávajú ručne editovateľné.",
            SylexFosLookupState.ConfigurationError => "Skontroluj SYLEX_FOS_API_KEY a konfiguráciu klienta chamber-fos.",
            SylexFosLookupState.ApiUnavailable => "Centrálne API nie je dostupné. Kalibrácia môže pokračovať bez automatického doplnenia údajov.",
            _ => "Stav načítania výrobných údajov zo Sylex FOS API.",
        };
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
