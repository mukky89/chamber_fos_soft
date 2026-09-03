using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using VotschVc3.App.Thermometers;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

internal static class CalibrationComPortUxBootstrap
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
        if (sender is CalibrationWindow window) window.InitializeComPortUx();
    }
}

internal sealed class CachedSerialDeviceDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ThermometerDeviceViewModel device) return "—";
        return SerialPortEnumerator.TryGetCachedInfo(device.PortName, out var cached)
            ? cached.Display
            : device.Display;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public partial class CalibrationWindow
{
    private bool _comPortUxInitialized;
    private ComboBox? _referencePortPicker;
    private bool _comMetadataRefreshRunning;

    internal void InitializeComPortUx()
    {
        if (_comPortUxInitialized) return;
        _comPortUxInitialized = true;

        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
        {
            _referencePortPicker = FindComUxDescendants<ComboBox>(this)
                .FirstOrDefault(combo =>
                {
                    Binding? binding = BindingOperations.GetBinding(combo, ItemsControl.ItemsSourceProperty);
                    return string.Equals(binding?.Path?.Path, "F100Devices", StringComparison.Ordinal);
                });
            if (_referencePortPicker is null) return;

            _referencePortPicker.ClearValue(ItemsControl.DisplayMemberPathProperty);
            _referencePortPicker.ItemTemplate = BuildComItemTemplate();
            _referencePortPicker.DropDownOpened -= ReferencePortPicker_DropDownOpened;
            _referencePortPicker.DropDownOpened += ReferencePortPicker_DropDownOpened;
            _ = RefreshComMetadataAsync();
        }));
    }

    private DataTemplate BuildComItemTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe UI"));
        text.SetBinding(TextBlock.TextProperty, new Binding(".") { Converter = new CachedSerialDeviceDisplayConverter() });
        panel.AppendChild(text);
        return new DataTemplate { VisualTree = panel };
    }

    private void ReferencePortPicker_DropDownOpened(object? sender, EventArgs e) => _ = RefreshComMetadataAsync();

    private async Task RefreshComMetadataAsync()
    {
        if (_comMetadataRefreshRunning) return;
        _comMetadataRefreshRunning = true;
        try
        {
            // WMI only. SerialPortEnumerator never opens the COM port and therefore cannot disturb
            // a connected CTH7000 session.
            await Task.Run(SerialPortEnumerator.Enumerate);
            if (_referencePortPicker is not null)
            {
                _referencePortPicker.Items.Refresh();
                BindingExpression? selectedBinding = _referencePortPicker.GetBindingExpression(Selector.SelectedItemProperty);
                selectedBinding?.UpdateTarget();
            }
        }
        catch
        {
            // The picker keeps the fast COM label if Windows WMI metadata is temporarily unavailable.
        }
        finally
        {
            _comMetadataRefreshRunning = false;
        }
    }

    private static IEnumerable<T> FindComUxDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (T nested in FindComUxDescendants<T>(child)) yield return nested;
        }
    }
}
