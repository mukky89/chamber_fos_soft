using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.Calibration;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.Views;

internal static class CalibrationReferenceControlSettingsBootstrap
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
        if (sender is CalibrationWindow window) window.InitializeReferenceControlSettings();
    }
}

public partial class CalibrationWindow
{
    private bool _referenceControlSettingsInitialized;
    private readonly CalibrationDeviceOptionsStore _calibrationDeviceOptionsStore = new();
    private CalibrationDeviceOptions? _calibrationDeviceOptions;
    private CheckBox? _referenceControlToggle;

    internal void InitializeReferenceControlSettings()
    {
        if (_referenceControlSettingsInitialized) return;
        _referenceControlSettingsInitialized = true;

        _calibrationDeviceOptions = _calibrationDeviceOptionsStore.Load(_chamberId);
        CalibrationReferenceControlRegistry.Configure(_chamberId, _calibrationDeviceOptions.ToCoreOptions());
        _viewModel.PropertyChanged += OnReferenceControlViewModelChanged;
        Closed += OnReferenceControlSettingsClosed;

        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(InjectReferenceControlSettings));
    }

    private void InjectReferenceControlSettings()
    {
        if (_referenceControlToggle is not null || _productionTabs is null || _calibrationDeviceOptions is null) return;
        TabItem? settingsTab = _productionTabs.Items.OfType<TabItem>()
            .FirstOrDefault(item => HeaderText(item.Header) == "Nastavenia stability");
        if (settingsTab?.Content is not ScrollViewer scroll || scroll.Content is not StackPanel stack) return;

        _referenceControlToggle = new CheckBox
        {
            Content = "Riadiť kalibračnú teplotu podľa WIKA referencie (zariadenie)",
            IsChecked = _calibrationDeviceOptions.ControlTemperatureByReference,
            FontWeight = FontWeights.SemiBold,
            IsEnabled = !_viewModel.IsRunning,
            ToolTip = "Voliteľný pomalý vonkajší regulačný okruh. Komora naďalej používa vlastný regulátor, aplikácia iba bezpečne dorovnáva jej setpoint tak, aby fyzická WIKA referencia dosiahla cieľ kalibračného plata.",
        };
        _referenceControlToggle.Checked += ReferenceControlToggleChanged;
        _referenceControlToggle.Unchecked += ReferenceControlToggleChanged;

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = "Riadenie teploty zariadenia",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        });
        body.Children.Add(_referenceControlToggle);
        body.Children.Add(new TextBlock
        {
            Text = "Vypnuté: profil zapisuje svoj cieľ priamo ako setpoint komory. Zapnuté: počas kalibračného plata sa setpoint komory pomaly a obmedzene koriguje podľa odchýlky WIKA. Stabilitu stále určuje iba WIKA referencia a následne každý FBG peak.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(22, 5, 0, 0),
            Opacity = 0.78,
            MaxWidth = 820,
        });
        body.Children.Add(new TextBlock
        {
            Text = "Bezpečné limity: krok max. 0,30 °C / 10 s, celková korekcia max. ±3,0 °C, deadband 0,05 °C. Nastavenie je uložené samostatne pre toto zariadenie.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(22, 3, 0, 0),
            Opacity = 0.68,
            MaxWidth = 820,
        });

        var card = new Border
        {
            Background = TryFindResource("SurfaceAltBrush") as Brush ?? Brushes.Transparent,
            BorderBrush = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 16),
            Child = body,
        };
        stack.Children.Insert(0, card);
    }

    private void ReferenceControlToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_calibrationDeviceOptions is null || _referenceControlToggle is null) return;
        _calibrationDeviceOptions.ControlTemperatureByReference = _referenceControlToggle.IsChecked == true;
        _calibrationDeviceOptionsStore.Save(_chamberId, _calibrationDeviceOptions);
        CalibrationReferenceControlRegistry.Configure(_chamberId, _calibrationDeviceOptions.ToCoreOptions());
    }

    private void OnReferenceControlViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.CalibrationViewModel.IsRunning) && _referenceControlToggle is not null)
            _referenceControlToggle.IsEnabled = !_viewModel.IsRunning;
    }

    private void OnReferenceControlSettingsClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnReferenceControlViewModelChanged;
        Closed -= OnReferenceControlSettingsClosed;
    }
}
