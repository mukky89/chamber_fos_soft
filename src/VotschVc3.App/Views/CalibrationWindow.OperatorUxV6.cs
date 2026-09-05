using System.ComponentModel;
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

internal static class CalibrationWindowOperatorUxV6Bootstrap
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
        if (sender is CalibrationWindow window) window.InitializeOperatorUxV6();
    }
}

public partial class CalibrationWindow
{
    private bool _operatorUxV6Initialized;
    private DataGrid? _debugProgressGridV6;
    private Border? _runnerDecisionCardV6;

    internal void InitializeOperatorUxV6()
    {
        if (_operatorUxV6Initialized) return;
        _operatorUxV6Initialized = true;

        _viewModel.PropertyChanged += OnOperatorUxV6PropertyChanged;
        Closed += OnOperatorUxV6Closed;

        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            ReplaceStabilitySettingsV6();
            ConfigureWiringGridV6();
            ConfigureLiveDebugGridV6();
            ApplyHardRunLockV6(_viewModel.IsRunning);
        }));
    }

    private void ReplaceStabilitySettingsV6()
    {
        _productionTabs ??= FindOperatorDescendants<TabControl>(this)
            .FirstOrDefault(tab => tab.Items.OfType<TabItem>().Any(item => HeaderText(item.Header) == "Nastavenia stability"));
        if (_productionTabs is null) return;

        TabItem? settings = _productionTabs.Items.OfType<TabItem>()
            .FirstOrDefault(item => HeaderText(item.Header) == "Nastavenia stability");
        if (settings is null || settings.Content is CalibrationStabilitySettingsView) return;
        settings.Content = new CalibrationStabilitySettingsView { DataContext = _viewModel };
    }

    private void ConfigureWiringGridV6()
    {
        _wiringGrid ??= FindOperatorDescendants<DataGrid>(this)
            .FirstOrDefault(grid => IsItemsBinding(grid, "Peaks"));
        if (_wiringGrid is null) return;

        _wiringGrid.SelectionUnit = DataGridSelectionUnit.Cell;
        _wiringGrid.SelectionMode = DataGridSelectionMode.Extended;
        _wiringGrid.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        _wiringGrid.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);

        // This is the hard run-time lock. Unlike a one-time IsEnabled snapshot it follows IsRunning
        // for the complete lifetime of the window, including after tab/template re-realization.
        BindingOperations.SetBinding(
            _wiringGrid,
            DataGrid.IsReadOnlyProperty,
            new Binding(nameof(CalibrationViewModel.IsRunning)) { Mode = BindingMode.OneWay });

        _wiringGrid.BeginningEdit -= WiringGridV6_BeginningEdit;
        _wiringGrid.BeginningEdit += WiringGridV6_BeginningEdit;
        _wiringGrid.CurrentCellChanged -= WiringGridV6_CurrentCellChanged;
        _wiringGrid.CurrentCellChanged += WiringGridV6_CurrentCellChanged;

        ApplySelectedCellStyleV6(_wiringGrid);
        AddEditCuesToColumnsV6(_wiringGrid);
    }

    private void WiringGridV6_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (!_viewModel.IsRunning) return;
        e.Cancel = true;
        Keyboard.ClearFocus();
    }

    private void WiringGridV6_CurrentCellChanged(object? sender, EventArgs e)
    {
        if (_wiringGrid?.CurrentCell.Item is null || _wiringGrid.CurrentCell.Column is null) return;
        _wiringGrid.ScrollIntoView(_wiringGrid.CurrentCell.Item, _wiringGrid.CurrentCell.Column);
    }

    private void ApplySelectedCellStyleV6(DataGrid grid)
    {
        Style? baseStyle = TryFindResource(typeof(DataGridCell)) as Style;
        var style = new Style(typeof(DataGridCell), baseStyle);
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 0)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));

        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue));
        selected.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x55, 0x35, 0x58, 0x88))));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, TryFindResource("TextBrush") as Brush ?? Brushes.White));
        style.Triggers.Add(selected);
        grid.CellStyle = style;
    }

    private void AddEditCuesToColumnsV6(DataGrid grid)
    {
        if (grid.Tag is string tag && tag.Contains("edit-cues-v6", StringComparison.Ordinal)) return;

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
                CellTemplate = BuildEditCueTemplateV6(original),
                CellEditingTemplate = BuildEditingTemplateV6(original),
            };

            grid.Columns.RemoveAt(index);
            grid.Columns.Insert(index, replacement);
        }

        grid.Tag = string.IsNullOrWhiteSpace(grid.Tag?.ToString())
            ? "edit-cues-v6"
            : grid.Tag + ";edit-cues-v6";
    }

    private DataTemplate BuildEditCueTemplateV6(Binding sourceBinding)
    {
        var grid = new FrameworkElementFactory(typeof(Grid));
        var value = new FrameworkElementFactory(typeof(TextBlock));
        value.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        value.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 18, 0));
        value.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        value.SetBinding(TextBlock.TextProperty, CloneBindingV6(sourceBinding, forEditing: false));
        grid.AppendChild(value);

        var pencil = new FrameworkElementFactory(typeof(TextBlock));
        pencil.SetValue(TextBlock.TextProperty, "✎");
        pencil.SetValue(TextBlock.FontSizeProperty, 12d);
        pencil.SetValue(UIElement.OpacityProperty, 0.58d);
        pencil.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        pencil.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        pencil.SetValue(TextBlock.ForegroundProperty, TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue);
        pencil.SetValue(FrameworkElement.ToolTipProperty, "Editovateľná bunka");
        grid.AppendChild(pencil);

        return new DataTemplate { VisualTree = grid };
    }

    private DataTemplate BuildEditingTemplateV6(Binding sourceBinding)
    {
        var editor = new FrameworkElementFactory(typeof(TextBox));
        if (TryFindResource("DataGridEditTextBox") is Style editStyle)
            editor.SetValue(FrameworkElement.StyleProperty, editStyle);
        editor.SetBinding(TextBox.TextProperty, CloneBindingV6(sourceBinding, forEditing: true));
        return new DataTemplate { VisualTree = editor };
    }

    private static Binding CloneBindingV6(Binding source, bool forEditing)
    {
        return new Binding(source.Path?.Path ?? string.Empty)
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
    }

    private void ConfigureLiveDebugGridV6()
    {
        _productionTabs ??= FindOperatorDescendants<TabControl>(this)
            .FirstOrDefault(tab => tab.Items.OfType<TabItem>().Any(item => HeaderText(item.Header) == "Live monitor"));
        _liveMonitorTab ??= _productionTabs?.Items.OfType<TabItem>()
            .FirstOrDefault(item => HeaderText(item.Header) == "Live monitor");
        if (_liveMonitorTab is null) return;

        _debugProgressGridV6 ??= FindOperatorDescendants<DataGrid>(_liveMonitorTab)
            .FirstOrDefault(grid => IsItemsBinding(grid, "TargetProgress"));
        if (_debugProgressGridV6 is null) return;

        _debugProgressGridV6.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        _debugProgressGridV6.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        _debugProgressGridV6.FrozenColumnCount = 4;
        _debugProgressGridV6.Columns.Clear();

        _debugProgressGridV6.Columns.Add(TextColumnV6("FBG SN", "SerialNumber", 150));
        _debugProgressGridV6.Columns.Add(TextColumnV6("Kanál", "Channel", 65));
        _debugProgressGridV6.Columns.Add(TextColumnV6("Peak", "PeakId", 65));
        _debugProgressGridV6.Columns.Add(TextColumnV6("λ live [nm]", "CurrentWavelengthNm", 120, "F6"));
        _debugProgressGridV6.Columns.Add(MultiColumnV6("Fáza", 175, new CalibrationProgressPhaseConverter(), null,
            nameof(CalibrationTargetProgressViewModel.State)));
        _debugProgressGridV6.Columns.Add(MultiColumnV6("Stabilita samples", 115, new CalibrationProgressSampleLabelConverter(), "stability",
            nameof(CalibrationTargetProgressViewModel.State), nameof(CalibrationTargetProgressViewModel.StableSamples), nameof(CalibrationTargetProgressViewModel.RequiredSamples)));
        _debugProgressGridV6.Columns.Add(MultiColumnV6("Chýba stab.", 90, new CalibrationProgressRemainingConverter(), "stability",
            nameof(CalibrationTargetProgressViewModel.State), nameof(CalibrationTargetProgressViewModel.StableSamples), nameof(CalibrationTargetProgressViewModel.RequiredSamples)));
        _debugProgressGridV6.Columns.Add(MultiColumnV6("Meranie samples", 115, new CalibrationProgressSampleLabelConverter(), "measurement",
            nameof(CalibrationTargetProgressViewModel.State), nameof(CalibrationTargetProgressViewModel.StableSamples), nameof(CalibrationTargetProgressViewModel.RequiredSamples)));
        _debugProgressGridV6.Columns.Add(MultiColumnV6("Chýba mer.", 90, new CalibrationProgressRemainingConverter(), "measurement",
            nameof(CalibrationTargetProgressViewModel.State), nameof(CalibrationTargetProgressViewModel.StableSamples), nameof(CalibrationTargetProgressViewModel.RequiredSamples)));
        _debugProgressGridV6.Columns.Add(TextColumnV6("StdDev [pm]", "StandardDeviationPm", 95, "F3"));
        _debugProgressGridV6.Columns.Add(TextColumnV6("Drift [pm/min]", "DriftPmPerMinute", 105, "F3"));
        _debugProgressGridV6.Columns.Add(MultiColumnV6("Timeout ostáva", 105, new CalibrationProgressTimeoutRemainingConverter(), null,
            nameof(CalibrationTargetProgressViewModel.Elapsed), nameof(CalibrationTargetProgressViewModel.Timeout)));
        _debugProgressGridV6.Columns.Add(MultiColumnV6("Prečo čaká / čo blokuje", 330, new CalibrationProgressBlockReasonConverter(), null,
            nameof(CalibrationTargetProgressViewModel.State), nameof(CalibrationTargetProgressViewModel.StableSamples), nameof(CalibrationTargetProgressViewModel.RequiredSamples), nameof(CalibrationTargetProgressViewModel.Detail)));
        _debugProgressGridV6.Columns.Add(new DataGridTextColumn
        {
            Header = "Aktuálne kritériá",
            Binding = new Binding(nameof(CalibrationTargetProgressViewModel.Detail)) { Converter = new CalibrationProgressCriteriaConverter() },
            Width = new DataGridLength(430),
            IsReadOnly = true,
        });

        EnsureRunnerDecisionCardV6();
    }

    private void EnsureRunnerDecisionCardV6()
    {
        if (_runnerDecisionCardV6 is not null || _liveMonitorTab?.Content is not DockPanel dock) return;

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
            Text = "DEBUG · Prečo runner nepokračuje na ďalší krok",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Hľadaj konkrétny blokujúci FBG v tabuľke: chýbajúce samples, range, StdDev alebo drift označený ×. Po fáze MERANIE musí každý peak nazbierať celé finálne okno; jeden peak môže byť hotový, kým iný stále stabilizuje.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78,
            Margin = new Thickness(0, 2, 0, 0),
        });
        stack.Children.Add(status);

        _runnerDecisionCardV6 = new Border
        {
            Background = TryFindResource("SurfaceAltBrush") as Brush ?? Brushes.Transparent,
            BorderBrush = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(8, 4, 8, 5),
            Child = stack,
        };
        DockPanel.SetDock(_runnerDecisionCardV6, Dock.Top);
        dock.Children.Insert(Math.Min(1, dock.Children.Count), _runnerDecisionCardV6);
    }

    private static DataGridTextColumn TextColumnV6(string header, string path, double width, string? format = null) => new()
    {
        Header = header,
        Binding = new Binding(path) { StringFormat = string.IsNullOrWhiteSpace(format) ? null : format },
        Width = new DataGridLength(width),
        IsReadOnly = true,
    };

    private static DataGridTextColumn MultiColumnV6(string header, double width, IMultiValueConverter converter, object? parameter, params string[] paths)
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

    private void OnOperatorUxV6PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CalibrationViewModel.IsRunning)) return;
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => ApplyHardRunLockV6(_viewModel.IsRunning)));
            return;
        }
        ApplyHardRunLockV6(_viewModel.IsRunning);
    }

    private void ApplyHardRunLockV6(bool running)
    {
        if (_wiringGrid is not null && running)
        {
            _wiringGrid.CancelEdit(DataGridEditingUnit.Cell);
            _wiringGrid.CancelEdit(DataGridEditingUnit.Row);
            Keyboard.ClearFocus();
        }

        _productionTabs ??= FindOperatorDescendants<TabControl>(this)
            .FirstOrDefault(tab => tab.Items.OfType<TabItem>().Any(item => HeaderText(item.Header) == "Zapojenie"));
        if (_productionTabs is not null)
        {
            foreach (TabItem item in _productionTabs.Items.OfType<TabItem>())
            {
                string header = HeaderText(item.Header);
                if (header is "Zapojenie" or "Kalibračné plata" or "Nastavenia stability")
                    item.IsEnabled = !running;
            }
        }

        foreach (ProfilePicker picker in FindOperatorDescendants<ProfilePicker>(this)) picker.IsEnabled = !running;

        foreach (Button button in FindOperatorDescendants<Button>(this))
        {
            ICommand? command = button.Command;
            if (command is null) continue;
            bool configurationCommand = ReferenceEquals(command, _viewModel.SaveSetupCommand)
                || ReferenceEquals(command, _viewModel.ConnectPeakLoggerCommand)
                || ReferenceEquals(command, _viewModel.DiscoverPeakLoggerApisCommand)
                || ReferenceEquals(command, _viewModel.RefreshSensorsCommand)
                || ReferenceEquals(command, _viewModel.SelectSuggestedPeaksCommand)
                || ReferenceEquals(command, _viewModel.MarkAllPlateausCommand)
                || ReferenceEquals(command, _viewModel.RefreshF100PortsCommand)
                || ReferenceEquals(command, _viewModel.CheckF100Command)
                || ReferenceEquals(command, _viewModel.DiagnoseF100TalkOnlyCommand)
                || ReferenceEquals(command, _viewModel.AddManualF100PortCommand)
                || ReferenceEquals(command, _viewModel.ForceReconnectF100Command);
            if (configurationCommand) button.IsEnabled = !running && command.CanExecute(button.CommandParameter);
        }
    }

    private static bool IsItemsBinding(DataGrid grid, string path)
    {
        Binding? binding = BindingOperations.GetBinding(grid, ItemsControl.ItemsSourceProperty);
        return string.Equals(binding?.Path?.Path, path, StringComparison.Ordinal);
    }

    private static IEnumerable<T> FindOperatorDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (T nested in FindOperatorDescendants<T>(child)) yield return nested;
        }
    }

    private void OnOperatorUxV6Closed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnOperatorUxV6PropertyChanged;
        if (_wiringGrid is not null)
        {
            _wiringGrid.BeginningEdit -= WiringGridV6_BeginningEdit;
            _wiringGrid.CurrentCellChanged -= WiringGridV6_CurrentCellChanged;
        }
        Closed -= OnOperatorUxV6Closed;
    }
}
