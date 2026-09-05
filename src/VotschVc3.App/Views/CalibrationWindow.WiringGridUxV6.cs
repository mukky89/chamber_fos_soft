using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace VotschVc3.App.Views;

/// <summary>
/// Focused UX layer for the Zapojenie grid.
/// All existing columns stay visible. Only the two production SN fields and Notes are text-editable;
/// the calibration checkbox remains interactive. Editable text cells enter edit mode on the first click.
/// </summary>
internal static class CalibrationWindowWiringGridUxV6Bootstrap
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
        if (sender is CalibrationWindow window) window.InitializeWiringGridUxV6();
    }
}

public partial class CalibrationWindow
{
    private static readonly HashSet<string> WiringEditableTextHeadersV6 = new(StringComparer.OrdinalIgnoreCase)
    {
        "FBG sensor SN (kanál)",
        "FBG sensor SN CHAIN",
        "Poznámky",
    };

    private bool _wiringGridUxV6Initialized;

    internal void InitializeWiringGridUxV6()
    {
        if (_wiringGridUxV6Initialized) return;
        _wiringGridUxV6Initialized = true;

        // Ensure the existing production wiring layer has already discovered/configured the grid.
        InitializeProductionWorkspaceV3();

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(ConfigureWiringGridUxV6));
    }

    private void ConfigureWiringGridUxV6()
    {
        if (_wiringGrid is null) return;

        // Keep every column that the production workspace currently exposes. Horizontal scrolling is
        // preferable to squeezing sixteen+ fields into unreadable slivers.
        _wiringGrid.RowHeight = 36;
        _wiringGrid.ColumnHeaderHeight = 46;
        _wiringGrid.MinRowHeight = 34;
        _wiringGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _wiringGrid.RowHeaderWidth = 0;
        _wiringGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _wiringGrid.SelectionUnit = DataGridSelectionUnit.Cell;
        _wiringGrid.SelectionMode = DataGridSelectionMode.Single;
        _wiringGrid.CanUserResizeColumns = true;
        _wiringGrid.CanUserReorderColumns = true;
        _wiringGrid.FrozenColumnCount = Math.Min(6, _wiringGrid.Columns.Count);
        _wiringGrid.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        _wiringGrid.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);

        Brush border = TryFindResource("BorderBrush") as Brush ?? Brushes.DimGray;
        _wiringGrid.HorizontalGridLinesBrush = border;
        _wiringGrid.VerticalGridLinesBrush = Brushes.Transparent;

        Style readOnlyCellStyle = BuildWiringCellStyleV6(editable: false);
        Style editableCellStyle = BuildWiringCellStyleV6(editable: true);

        foreach (DataGridColumn column in _wiringGrid.Columns)
        {
            string header = HeaderText(column.Header);
            bool isEditableText = WiringEditableTextHeadersV6.Contains(header);
            bool isCalibrationCheckbox =
                column is DataGridCheckBoxColumn &&
                string.Equals(header, "Kalibrovať", StringComparison.OrdinalIgnoreCase);

            // The checkbox is an action (selecting what to calibrate), not free-form cell editing.
            // Every other field is read-only except the three explicitly allowed production fields.
            column.IsReadOnly = !(isEditableText || isCalibrationCheckbox);
            column.CellStyle = isEditableText ? editableCellStyle : readOnlyCellStyle;

            ApplyReadableWiringColumnWidthV6(column, header);

            if (isEditableText)
                ApplyEditableHeaderHintV6(column, header);
        }

        _wiringGrid.PreviewMouseLeftButtonDown -= WiringGrid_PreviewMouseLeftButtonDownV6;
        _wiringGrid.PreviewMouseLeftButtonDown += WiringGrid_PreviewMouseLeftButtonDownV6;
        _wiringGrid.PreparingCellForEdit -= WiringGrid_PreparingCellForEditV6;
        _wiringGrid.PreparingCellForEdit += WiringGrid_PreparingCellForEditV6;
    }

    private Style BuildWiringCellStyleV6(bool editable)
    {
        Style? basedOn = _wiringGrid?.CellStyle ?? TryFindResource(typeof(DataGridCell)) as Style;
        var style = basedOn is null
            ? new Style(typeof(DataGridCell))
            : new Style(typeof(DataGridCell), basedOn);

        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(editable ? 7 : 6, 0, editable ? 7 : 6, 0)));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

        if (editable)
        {
            Brush surface = new SolidColorBrush(Color.FromArgb(0x35, 0x58, 0x92, 0xE8));
            Brush accent = TryFindResource("AccentBrush") as Brush ?? Brushes.CornflowerBlue;
            style.Setters.Add(new Setter(Control.BackgroundProperty, surface));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, accent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, "Klikni raz a píš. Toto pole je editovateľné."));
        }
        else
        {
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        }

        return style;
    }

    private static void ApplyReadableWiringColumnWidthV6(DataGridColumn column, string header)
    {
        double? width = header switch
        {
            "Kalibrovať" => 88,
            "Kanál" => 72,
            "Peak ID" => 72,
            "FBG index" => 82,
            "Aktuálna λ [nm]" => 128,
            "Intenzita" => 88,
            "Snímač" => 112,
            "Typ FBG" => 98,
            "Sylex SN" => 116,
            "FBG sensor SN (kanál)" => 180,
            "FBG sensor SN CHAIN" => 185,
            "Timeout [min]" => 128,
            "Max. stabilizácia [min]" => 138,
            "Poznámky" => 220,
            "Popis produktu" => 175,
            "Popis výrobku" => 175,
            "Zákazník" => 145,
            "Zákazka" => 120,
            "Názov snímača" => 170,
            _ => null,
        };

        if (!width.HasValue) return;
        column.Width = new DataGridLength(width.Value, DataGridLengthUnitType.Pixel);
        column.MinWidth = Math.Min(width.Value, 68);
    }

    private static void ApplyEditableHeaderHintV6(DataGridColumn column, string header)
    {
        if (column.Header is TextBlock existing)
        {
            existing.ToolTip = "Editovateľné · klikni raz do bunky a píš.";
            existing.FontWeight = FontWeights.SemiBold;
            return;
        }

        column.Header = new TextBlock
        {
            Text = header,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            ToolTip = "Editovateľné · klikni raz do bunky a píš.",
        };
    }

    private void WiringGrid_PreviewMouseLeftButtonDownV6(object sender, MouseButtonEventArgs e)
    {
        if (_wiringGrid is null || _viewModel.IsRunning) return;
        if (e.OriginalSource is not DependencyObject source) return;

        DataGridCell? cell = FindVisualParentWiringV6<DataGridCell>(source);
        if (cell is null || cell.IsEditing || !IsEditableWiringColumnV6(cell.Column)) return;

        // One click = focus the cell and immediately enter edit mode. WPF normally needs two clicks
        // when a DataGridTextColumn is not already current.
        cell.Focus();
        _wiringGrid.CurrentCell = new DataGridCellInfo(cell.DataContext, cell.Column);

        if (_wiringGrid.BeginEdit())
        {
            e.Handled = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (FindVisualChildWiringV6<TextBox>(cell) is not TextBox editor) return;
                editor.Focus();
                editor.CaretIndex = editor.Text?.Length ?? 0;
            }));
        }
    }

    private void WiringGrid_PreparingCellForEditV6(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (!IsEditableWiringColumnV6(e.Column)) return;

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            TextBox? editor = e.EditingElement as TextBox ?? FindVisualChildWiringV6<TextBox>(e.EditingElement);
            if (editor is null) return;
            editor.Focus();
            editor.CaretIndex = editor.Text?.Length ?? 0;
        }));
    }

    private static bool IsEditableWiringColumnV6(DataGridColumn? column) =>
        column is not null && WiringEditableTextHeadersV6.Contains(HeaderText(column.Header));

    private static T? FindVisualParentWiringV6<T>(DependencyObject? child) where T : DependencyObject
    {
        DependencyObject? current = child;
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static T? FindVisualChildWiringV6<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null) return null;
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindVisualChildWiringV6<T>(child) is T nested) return nested;
        }
        return null;
    }
}
