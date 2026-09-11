using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.Views;

public partial class CalibrationWindow
{
    private void AddSensorLinksColumn()
    {
        if (_wiringGrid is null || _wiringGrid.Columns.Any(c => HeaderText(c.Header) == "Otvoriť")) return;
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        foreach (string system in new[] { "ISYS", "DBFOS" })
        {
            var button = new FrameworkElementFactory(typeof(Button));
            button.SetValue(FrameworkElement.TagProperty, system);
            button.SetValue(FrameworkElement.ToolTipProperty, $"Otvoriť snímač v {system}");
            button.SetValue(Control.PaddingProperty, new Thickness(5, 2, 5, 2));
            button.SetValue(Control.FontSizeProperty, 10.0);
            button.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            button.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            button.SetValue(Control.ForegroundProperty, new SolidColorBrush(system == "ISYS" ? Color.FromRgb(137,180,255) : Color.FromRgb(91,224,175)));
            button.AddHandler(Button.ClickEvent, new RoutedEventHandler(OpenSensorSystem_Click));
            var content = new FrameworkElementFactory(typeof(StackPanel));
            content.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var label = new FrameworkElementFactory(typeof(TextBlock));
            label.SetValue(TextBlock.TextProperty, system);
            content.AppendChild(label);
            var icon = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            icon.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 3,5 L 3,13 11,13 11,10 M 7,2 L 14,2 14,9 M 14,2 L 6,10"));
            icon.SetBinding(System.Windows.Shapes.Path.StrokeProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
            icon.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.5);
            icon.SetValue(FrameworkElement.MarginProperty, new Thickness(4,0,0,0));
            content.AppendChild(icon);
            button.AppendChild(content);
            panel.AppendChild(button);
        }
        _wiringGrid.Columns.Add(new DataGridTemplateColumn { Header = "Otvoriť", Width = 148, IsReadOnly = true,
            CellTemplate = new DataTemplate { VisualTree = panel } });
    }

    private void OpenSensorSystem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { DataContext: CalibrationPeakRowViewModel row, Tag: string system }) return;
        string id = FbgSerialParser.Parse(row.SerialNumber).Split('/')[0];
        if (id.Length != 6 || id.Any(c => c < '0' || c > '9'))
        {
            ShowProductionInfo("Najprv priraď platné SN snímača (šesť číslic pred lomkou).");
            return;
        }
        string? root = system switch { "ISYS" => "https://isys.sylex.sk/io/", "DBFOS" => "https://dbfos.sylex.sk/objednavky/", _ => null };
        if (root is null) return;
        try { Process.Start(new ProcessStartInfo(root + id) { UseShellExecute = true }); }
        catch (System.Exception ex) { ShowProductionInfo($"Nepodarilo sa otvoriť {system}: {ex.Message}"); }
    }
}
