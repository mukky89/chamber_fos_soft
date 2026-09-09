using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VotschVc3.App.Views;

public partial class HomeView
{
    private static bool _sidebarCollapsed;
    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        ApplySidebarLayout();
    }
    private void SidebarContent_Loaded(object sender, RoutedEventArgs e) => ApplySidebarLayout();
    private void ApplySidebarLayout()
    {
        SidebarColumn.Width = new GridLength(_sidebarCollapsed ? 82 : 264);
        SidebarFrame.Padding = new Thickness(_sidebarCollapsed ? 5 : 10, 12, _sidebarCollapsed ? 5 : 10, 12);
        SidebarToggle.ToolTip = _sidebarCollapsed ? "Rozbaliť menu" : "Zúžiť menu na ikony";
        SidebarBrand.Visibility = SidebarAccount.Visibility = SidebarFolderHeading.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        foreach (var button in SidebarDescendants<Button>(SidebarContent))
        {
            foreach (var label in SidebarDescendants<TextBlock>(button))
            {
                if (!string.IsNullOrWhiteSpace(label.Text))
                {
                    if (button.ToolTip is null) button.ToolTip = label.Text;
                    System.Windows.Automation.AutomationProperties.SetName(button, label.Text);
                    label.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
                }
            }
            button.Padding = new Thickness(_sidebarCollapsed ? 9 : 12, 10, _sidebarCollapsed ? 9 : 12, 10);
        }
    }
    private static System.Collections.Generic.IEnumerable<T> SidebarDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in SidebarDescendants<T>(child)) yield return descendant;
        }
    }
}
