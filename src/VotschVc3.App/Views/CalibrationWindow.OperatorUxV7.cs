using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.Views;

internal static class CalibrationWindowOperatorUxV7Bootstrap
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
        if (sender is CalibrationWindow window) window.InitializeOperatorUxV7();
    }
}

internal sealed class NotBoolConverterV7 : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public partial class CalibrationWindow
{
    private bool _operatorUxV7Initialized;
    private DataGrid? _debugProgressGridV7;
    private Border? _runnerDecisionCardV7;
    private readonly NotBoolConverterV7 _notRunningV7 = new();

    internal void InitializeOperatorUxV7()
    {
        if (_operatorUxV7Initialized) return;
        _operatorUxV7Initialized = true;

        _viewModel.PropertyChanged += OnOperatorUxV7PropertyChanged;
        _viewModel.Peaks.CollectionChanged += OnOperatorPeaksChangedV7;
        Closed += OnOperatorUxV7Closed;

        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            ReplaceStabilitySettingsV7();
            ConfigureWiringGridV7();
            ConfigureLiveDebugGridV7();
            BindConfigurationLockV7();
            RefreshChannelGroupBordersV7();
        }));
    }

    private void ReplaceStabilitySettingsV7()
    {
        _productionTabs ??= FindOperatorDescendantsV7<TabControl>(this)
            .FirstOrDefault(tab => tab.Items.OfType<TabItem>().Any(item => HeaderText(item.Header) == "Nastavenia stability"));
        if (_productionTabs is null) return;

        TabItem? settings = _productionTabs.Items.OfType<TabItem>()
            .FirstOrDefault(item => HeaderText(item.Header) == "Nastavenia stability");
        if (settings is null || settings.Content is CalibrationStabilitySettingsView) return;
        settings.Content = new CalibrationStabilitySettingsView();
    }

    private void ConfigureWiringGridV7()
    {
        _wiringGrid ??= FindOperatorDescendantsV7<DataGrid>(this)
            .FirstOrDefault(grid => IsItemsBindingV7(grid, "Peaks"));
        if (_wiringGrid is null) return;

        _wiringGrid.SelectionUnit = DataGridSelectionUnit.Cell;
        _wiringGrid.SelectionMode = DataGridSelectionMode.Extended;
        _wiringGrid.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        _wiringGrid.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);

        BindingOperations.SetBinding(
            _wiringGrid,
            DataGrid.IsReadOnlyProperty,
            new Binding(nameof(CalibrationViewModel.IsRunning)) { Mode = BindingMode.OneWay });

        _wiringGrid.BeginningEdit -= WiringGridV7_BeginningEdit;
        _wiringGrid.BeginningEdit += WiringGridV7_BeginningEdit;
        _wiringGrid.CurrentCellChanged -= WiringGridV7_CurrentCellChanged;
        _wiringGrid.CurrentCellChanged += WiringGridV7_CurrentCellChanged;
        _wiringGrid.LoadingRow -= WiringGridV7_LoadingRow;
        _wiringGrid.LoadingRow += WiringGridV7_LoadingRow;
        _wiringGrid.Sorting -= WiringGridV7_Sorting;
        _wiringGrid.Sorting += WiringGridV7_Sorting;

        ApplySelectedCellStyleV7(_wiringGrid);
        ConfigureWiringGridUxV6();
        AddEditCuesToColumnsV7(_wiringGrid);
    }

    private void WiringGridV7_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (!_viewModel.IsRunning) return;
        e.Cancel = true;
        Keyboard.ClearFocus();
    }

    private void WiringGridV7_CurrentCellChanged(object? sender, EventArgs e)
    {
        if (_wiringGrid?.CurrentCell.Item is null || _wiringGrid.CurrentCell.Column is null) return;
        _wiringGrid.ScrollIntoView(_wiringGrid.CurrentCell.Item, _wiringGrid.CurrentCell.Column);
    }

    private void WiringGridV7_LoadingRow(object? sender, DataGridRowEventArgs e) => ApplyChannelGroupBorderV7(e.Row);

    private void WiringGridV7_Sorting(object? sender, DataGridSortingEventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshChannelGroupBordersV7));

    private void ApplyChannelGroupBorderV7(DataGridRow row)
    {
        if (_wiringGrid is null || row.Item is not CalibrationPeakRowViewModel current) return;
        int index = _wiringGrid.Items.IndexOf(current);
        if (index < 0) return;

        CalibrationPeakRowViewModel? previous = index > 0 ? _wiringGrid.Items[index - 1] as CalibrationPeakRowViewModel : null;
        CalibrationPeakRowViewModel? next = index + 1 < _wiringGrid.Items.Count ? _wiringGrid.Items[index + 1] as CalibrationPeakRowViewModel : null;
        bool first = previous is null || !SameChannelV7(previous, current);
        bool last = next is null || !SameChannelV7(next, current);

        // Put grouping in style setters so validation and selection triggers retain priority.
        row.ClearValue(Control.BorderBrushProperty);
        row.ClearValue(Control.BorderThicknessProperty);
        row.ClearValue(FrameworkElement.MarginProperty);
        row.ClearValue(FrameworkElement.ToolTipProperty);
        var style = new Style(typeof(DataGridRow), _wiringGrid.RowStyle);
        if (!first || !last)
        {
            style.Setters.Add(new Setter(Control.BorderBrushProperty, TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2, first ? 2 : 0, 2, last ? 2 : 0)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, first ? 3 : 0, 0, last ? 3 : 0)));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, $"Kanál {current.Channel} · {CountChannelRowsV7(current.Channel)} peakov"));
        }
        row.Style = style;
    }

    private static bool SameChannelV7(CalibrationPeakRowViewModel left, CalibrationPeakRowViewModel right) =>
        string.Equals(left.Channel?.Trim(), right.Channel?.Trim(), StringComparison.OrdinalIgnoreCase);

    private int CountChannelRowsV7(string? channel) => _viewModel.Peaks.Count(row =>
        string.Equals(row.Channel?.Trim(), channel?.Trim(), StringComparison.OrdinalIgnoreCase));

    private void RefreshChannelGroupBordersV7()
    {
        if (_wiringGrid is null) return;
        _wiringGrid.UpdateLayout();
        foreach (object item in _wiringGrid.Items)
        {
            if (_wiringGrid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                ApplyChannelGroupBorderV7(row);
        }
    }

    private void OnOperatorPeaksChangedV7(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshChannelGroupBordersV7));
    }

    private void ApplySelectedCellStyleV7(DataGrid grid)
    {
        Style? baseStyle = TryFindResource(typeof(DataGridCell)) as Style;
        var style = new Style(typeof(DataGridCell), baseStyle);
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 0, 6, 0)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));

        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue));
        selected.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x55, 0x35, 0x58, 0x88))));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, TryFindResource("TextBrush") as Brush ?? Brushes.White));
        style.Triggers.Add(selected);

        var focused = new Trigger { Property = DataGridCell.IsKeyboardFocusWithinProperty, Value = true };
        focused.Setters.Add(new Setter(Control.BorderBrushProperty, TryFindResource("DangerBrush") as Brush ?? Brushes.Red));
        focused.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2.5)));
        focused.Setters.Add(new Setter(Panel.ZIndexProperty, 1));
        style.Triggers.Add(focused);
        grid.CellStyle = style;
    }

    private void AddEditCuesToColumnsV7(DataGrid grid)
    {
        if (grid.Tag is string tag && tag.Contains("edit-cues-v7", StringComparison.Ordinal)) return;

        for (int index = 0; index < grid.Columns.Count; index++)
        {
            if (grid.Columns[index] is not DataGridTextColumn source || source.IsReadOnly) continue;
            if (source.Binding is not Binding original || original.Path?.Path is not { Length: > 0 } path) continue;

            var replacement = new DataGridTemplateColumn
            {
                Header = source.Header,
                Width = source.Width,
                MinWidth = source.MinWidth,
                MaxWidth = source.MaxWidth,
                CanUserResize = source.CanUserResize,
                CanUserReorder = source.CanUserReorder,
                CanUserSort = source.CanUserSort,
                SortMemberPath = path,
                IsReadOnly = false,
                CellStyle = source.CellStyle,
                HeaderStyle = source.HeaderStyle,
                CellTemplate = BuildEditCueTemplateV7(original),
                CellEditingTemplate = BuildEditingTemplateV7(original),
            };

            grid.Columns.RemoveAt(index);
            grid.Columns.Insert(index, replacement);
        }

        grid.Tag = string.IsNullOrWhiteSpace(grid.Tag?.ToString())
            ? "edit-cues-v7"
            : grid.Tag + ";edit-cues-v7";
    }

    private DataTemplate BuildEditCueTemplateV7(Binding sourceBinding)
    {
        var root = new FrameworkElementFactory(typeof(Grid));
        var value = new FrameworkElementFactory(typeof(TextBlock));
        value.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        value.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 18, 0));
        value.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        value.SetBinding(TextBlock.TextProperty, CloneBindingV7(sourceBinding, forEditing: false));
        root.AppendChild(value);

        var pencil = new FrameworkElementFactory(typeof(TextBlock));
        pencil.SetValue(TextBlock.TextProperty, "✎");
        pencil.SetValue(TextBlock.FontSizeProperty, 12d);
        pencil.SetValue(UIElement.OpacityProperty, 0.62d);
        pencil.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        pencil.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        pencil.SetValue(TextBlock.ForegroundProperty, TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue);
        pencil.SetValue(FrameworkElement.ToolTipProperty, "Editovateľná bunka");
        root.AppendChild(pencil);

        return new DataTemplate { VisualTree = root };
    }

    private DataTemplate BuildEditingTemplateV7(Binding sourceBinding)
    {
        var editor = new FrameworkElementFactory(typeof(TextBox));
        if (TryFindResource("DataGridEditTextBox") is Style editStyle)
            editor.SetValue(FrameworkElement.StyleProperty, editStyle);
        editor.SetBinding(TextBox.TextProperty, CloneBindingV7(sourceBinding, forEditing: true));
        return new DataTemplate { VisualTree = editor };
    }

    private static Binding CloneBindingV7(Binding source, bool forEditing) => new(source.Path?.Path ?? string.Empty)
    {
        Mode = forEditing ? BindingMode.TwoWay : BindingMode.OneWay,
        UpdateSourceTrigger = forEditing ? source.UpdateSourceTrigger : UpdateSourceTrigger.Default,
        Converter = source.Converter,
        ConverterParameter = source.ConverterParameter,
        ConverterCulture = source.ConverterCulture,
        StringFormat = source.StringFormat,
        TargetNullValue = source.TargetNullValue,
        FallbackValue = source.FallbackValue,
        ValidatesOnDataErrors = source.ValidatesOnDataErrors,
        ValidatesOnExceptions = source.ValidatesOnExceptions,
        NotifyOnValidationError = source.NotifyOnValidationError,
    };

    private void ConfigureLiveDebugGridV7()
    {
        _productionTabs ??= FindOperatorDescendantsV7<TabControl>(this)
            .FirstOrDefault(tab => tab.Items.OfType<TabItem>().Any(item => HeaderText(item.Header) == "Live monitor"));
        _liveMonitorTab ??= _productionTabs?.Items.OfType<TabItem>()
            .FirstOrDefault(item => HeaderText(item.Header) == "Live monitor");
        if (_liveMonitorTab is null) return;

        _debugProgressGridV7 ??= FindOperatorDescendantsV7<DataGrid>(_liveMonitorTab)
            .FirstOrDefault(grid => IsItemsBindingV7(grid, "TargetProgress"));
        if (_debugProgressGridV7 is null) return;

        _debugProgressGridV7.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        _debugProgressGridV7.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        _debugProgressGridV7.FrozenColumnCount = 4;
        _debugProgressGridV7.Columns.Clear();

        _debugProgressGridV7.Columns.Add(TextColumnV7("FBG SN", "SerialNumber", 150));
        _debugProgressGridV7.Columns.Add(TextColumnV7("Kanál", "Channel", 65));
        _debugProgressGridV7.Columns.Add(TextColumnV7("Peak", "PeakId", 65));
        _debugProgressGridV7.Columns.Add(TextColumnV7("λ live [nm]", "CurrentWavelengthNm", 120, "F6"));
        _debugProgressGridV7.Columns.Add(MultiColumnV7("Fáza", 175, new CalibrationProgressPhaseConverter(), null,
            nameof(CalibrationTargetProgressViewModel.State)));
        _debugProgressGridV7.Columns.Add(MultiColumnV7("Stabilita samples", 120, new CalibrationProgressSampleLabelConverter(), "stability",
            nameof(CalibrationTargetProgressViewModel.State), nameof(CalibrationTargetProgressViewModel.StableSamples), nameof(CalibrationTargetProgressViewModel.RequiredSamples)));
        _debugProgressGridV7.Columns.Add(MultiColumnV7("Chýba stab.", 90, new CalibrationProgressRemainingConverter(), "stability",
            nameof(CalibrationTargetProgressViewModel.State), nameof(CalibrationTargetProgressViewModel.StableSamples), nameof(CalibrationTargetProgressViewModel.RequiredSamples)));
        _debugProgressGridV7.Columns.Add(MultiColumnV7("Meranie samples", 120, new CalibrationProgressSampleLabelConverter(), "measurement",
            nameof(CalibrationTargetProgressViewModel.State), nameof(CalibrationTargetProgressViewModel.StableSamples), nameof(CalibrationTargetProgressViewModel.RequiredSamples)));
        _debugProgressGridV7.Columns.Add(MultiColumnV7("Chýba mer.", 90, new CalibrationProgressRemainingConverter(), "measurement",
            nameof(CalibrationTargetProgressViewModel.State), nameof(CalibrationTargetProgressViewModel.StableSamples), nameof(CalibrationTargetProgressViewModel.RequiredSamples)));
        _debugProgressGridV7.Columns.Add(TextColumnV7("StdDev [pm]", "StandardDeviationPm", 100, "F3"));
        _debugProgressGridV7.Columns.Add(TextColumnV7("Drift [pm/min]", "DriftPmPerMinute", 110, "F3"));
        _debugProgressGridV7.Columns.Add(MultiColumnV7("Timeout ostáva", 110, new CalibrationProgressTimeoutRemainingConverter(), null,
            nameof(CalibrationTargetProgressViewModel.Elapsed), nameof(CalibrationTargetProgressViewModel.Timeout)));
        _debugProgressGridV7.Columns.Add(MultiColumnV7("Prečo čaká / čo blokuje", 340, new CalibrationProgressBlockReasonConverter(), null,
            nameof(CalibrationTargetProgressViewModel.State), nameof(CalibrationTargetProgressViewModel.StableSamples), nameof(CalibrationTargetProgressViewModel.RequiredSamples), nameof(CalibrationTargetProgressViewModel.Detail)));
        _debugProgressGridV7.Columns.Add(new DataGridTextColumn
        {
            Header = "Aktuálne dáta a kritériá",
            Binding = new Binding(nameof(CalibrationTargetProgressViewModel.Detail)) { Converter = new CalibrationProgressCriteriaConverter() },
            Width = new DataGridLength(440),
            IsReadOnly = true,
        });

        EnsureRunnerDecisionCardV7();
    }

    private void EnsureRunnerDecisionCardV7()
    {
        if (_runnerDecisionCardV7 is not null || _liveMonitorTab?.Content is not DockPanel dock) return;

        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12.5,
            Margin = new Thickness(0, 5, 0, 0),
        };
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(CalibrationViewModel.StatusMessage)));

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "DEBUG · Prečo runner nepokračuje",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Každý FBG sa vyhodnocuje samostatne. V tabuľke vidíš fázu, x/N stabilizačných samples, koľko chýba, x/N finálnych meracích samples, aktuálnu λ, StdDev, drift, timeout a presnú blokujúcu podmienku. Plato sa ukončí až keď sú hotové všetky vybrané peaky.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            Margin = new Thickness(0, 2, 0, 0),
        });
        stack.Children.Add(status);

        _runnerDecisionCardV7 = new Border
        {
            Background = TryFindResource("SurfaceAltBrush") as Brush ?? Brushes.Transparent,
            BorderBrush = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(8, 4, 8, 5),
            Child = stack,
        };
        DockPanel.SetDock(_runnerDecisionCardV7, Dock.Top);
        dock.Children.Insert(Math.Min(1, dock.Children.Count), _runnerDecisionCardV7);
    }

    private void BindConfigurationLockV7()
    {
        var inverse = _notRunningV7;

        foreach (ProfilePicker picker in FindOperatorDescendantsV7<ProfilePicker>(this))
            BindEnabledToNotRunningV7(picker, inverse);

        foreach (DataGrid grid in FindOperatorDescendantsV7<DataGrid>(this))
        {
            if (IsItemsBindingV7(grid, "CalibrationPoints"))
                BindingOperations.SetBinding(grid, DataGrid.IsReadOnlyProperty, new Binding(nameof(CalibrationViewModel.IsRunning)));
        }

        foreach (ComboBox combo in FindOperatorDescendantsV7<ComboBox>(this))
        {
            Binding? items = BindingOperations.GetBinding(combo, ItemsControl.ItemsSourceProperty);
            string path = items?.Path?.Path ?? string.Empty;
            if (path is "F100Devices" or "F100Channels" or "PeakLoggerInstances" or "SimulatorScenarios")
                BindEnabledToNotRunningV7(combo, inverse);
        }

        foreach (TextBox text in FindOperatorDescendantsV7<TextBox>(this))
        {
            BindingExpression? expression = text.GetBindingExpression(TextBox.TextProperty);
            string path = expression?.ParentBinding.Path?.Path ?? string.Empty;
            if (path is "PeakLoggerHost" or "PeakLoggerPort" or "ManualF100Port")
                BindEnabledToNotRunningV7(text, inverse);
        }

        foreach (CheckBox check in FindOperatorDescendantsV7<CheckBox>(this))
        {
            BindingExpression? expression = check.GetBindingExpression(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty);
            string path = expression?.ParentBinding.Path?.Path ?? string.Empty;
            if (path == "UseSimulator") BindEnabledToNotRunningV7(check, inverse);
        }
    }

    private static void BindEnabledToNotRunningV7(UIElement element, IValueConverter converter)
    {
        BindingOperations.SetBinding(element, UIElement.IsEnabledProperty, new Binding(nameof(CalibrationViewModel.IsRunning))
        {
            Converter = converter,
            Mode = BindingMode.OneWay,
        });
    }

    private void OnOperatorUxV7PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CalibrationViewModel.IsRunning)) return;
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => OnRunStateChangedV7(_viewModel.IsRunning)));
            return;
        }
        OnRunStateChangedV7(_viewModel.IsRunning);
    }

    private void OnRunStateChangedV7(bool running)
    {
        if (_wiringGrid is not null && running)
        {
            _wiringGrid.CancelEdit(DataGridEditingUnit.Cell);
            _wiringGrid.CancelEdit(DataGridEditingUnit.Row);
            Keyboard.ClearFocus();
        }
        RefreshChannelGroupBordersV7();
    }

    private static DataGridTextColumn TextColumnV7(string header, string path, double width, string? format = null) => new()
    {
        Header = header,
        Binding = new Binding(path) { StringFormat = string.IsNullOrWhiteSpace(format) ? null : format },
        Width = new DataGridLength(width),
        IsReadOnly = true,
    };

    private static DataGridTextColumn MultiColumnV7(string header, double width, IMultiValueConverter converter, object? parameter, params string[] paths)
    {
        var binding = new MultiBinding { Converter = converter, ConverterParameter = parameter };
        foreach (string path in paths) binding.Bindings.Add(new Binding(path));
        return new DataGridTextColumn
        {
            Header = header,
            Binding = binding,
            Width = new DataGridLength(width),
            IsReadOnly = true,
        };
    }

    private static bool IsItemsBindingV7(DataGrid grid, string path)
    {
        Binding? binding = BindingOperations.GetBinding(grid, ItemsControl.ItemsSourceProperty);
        return string.Equals(binding?.Path?.Path, path, StringComparison.Ordinal);
    }

    private static IEnumerable<T> FindOperatorDescendantsV7<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (T nested in FindOperatorDescendantsV7<T>(child)) yield return nested;
        }
    }

    private void OnOperatorUxV7Closed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnOperatorUxV7PropertyChanged;
        _viewModel.Peaks.CollectionChanged -= OnOperatorPeaksChangedV7;
        if (_wiringGrid is not null)
        {
            _wiringGrid.BeginningEdit -= WiringGridV7_BeginningEdit;
            _wiringGrid.CurrentCellChanged -= WiringGridV7_CurrentCellChanged;
            _wiringGrid.LoadingRow -= WiringGridV7_LoadingRow;
            _wiringGrid.Sorting -= WiringGridV7_Sorting;
        }
        Closed -= OnOperatorUxV7Closed;
    }
}
