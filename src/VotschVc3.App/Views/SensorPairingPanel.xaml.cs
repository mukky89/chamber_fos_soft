using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace VotschVc3.App.Views;
public partial class SensorPairingPanel : UserControl
{
    public SensorPairingPanel() { InitializeComponent(); }
    public void SetStage(int stage, string? serial = null)
    {
        var idle = new SolidColorBrush(Color.FromRgb(30, 36, 53));
        var active = new SolidColorBrush(Color.FromRgb(96, 142, 243));
        Step1.Background = stage == 1 ? active : idle;
        Step2.Background = stage == 2 ? active : idle;
        Step3.Background = stage == 3 ? new SolidColorBrush(Color.FromRgb(91, 224, 175)) : idle;
        Step3Icon.Stroke = new SolidColorBrush(stage == 3 ? Color.FromRgb(15, 55, 45) : Color.FromRgb(220, 232, 255));
        Entry.Visibility = stage == 1 ? Visibility.Visible : Visibility.Collapsed;
        Waiting.Visibility = stage == 2 ? Visibility.Visible : Visibility.Collapsed;
        PendingSerial.Text = serial ?? string.Empty;
    }
    public void ShowResult(string serial, string channel, bool apiVerified)
    {
        Result.Text = $"{serial} → Kanál {channel}";
        ResultCard.Visibility = Visibility.Visible;
        ApiStatus.Text = apiVerified ? "API overené" : "API zatiaľ neoverené";
        var color = new SolidColorBrush(apiVerified ? Color.FromRgb(91,224,175) : Color.FromRgb(234,185,71));
        ApiStatus.Foreground = color;
        ApiIcon.Stroke = color;
        ApiIcon.Data = apiVerified ? (Geometry)FindResource("CheckIcon")
            : Geometry.Parse("M 12,2 A 10,10 0 1 1 11.99,2 M 12,6 L 12,12 16,14");
        ApiBadge.BorderBrush = color;
        SetStage(3);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = System.TimeSpan.FromSeconds(1.2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!IsLoaded) return;
            SetStage(1);
            SerialInput.Focus();
        };
        timer.Start();
    }
}
