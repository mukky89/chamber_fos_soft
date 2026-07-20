using System.Windows;
using System.Windows.Controls;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

public partial class QuickProfileView : UserControl
{
    public QuickProfileView() => InitializeComponent();

    /// <summary>"Počet krokov" radio: switches the step mode back to the explicit count.</summary>
    private void UseStepCount_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is QuickProfileViewModel vm)
        {
            vm.UseTemperatureStep = false;
        }
    }
}
