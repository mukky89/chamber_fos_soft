using System.Windows.Controls;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

public partial class CalibrationStabilitySettingsView : UserControl
{
    public CalibrationStabilitySettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshCurrentSettings();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) RefreshCurrentSettings();
        };
    }

    private void RefreshCurrentSettings() =>
        _ = Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
        {
            if (DataContext is CalibrationViewModel viewModel)
                viewModel.RefreshSettingsDisplay();
        }));
}
