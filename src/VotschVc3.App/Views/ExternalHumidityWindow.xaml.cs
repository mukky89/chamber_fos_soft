using System.Windows;
using Microsoft.Win32;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

public partial class ExternalHumidityWindow : Window
{
    private static readonly Dictionary<Guid, ExternalHumidityWindow> Windows = [];
    private readonly ExternalHumidityViewModel _vm;
    public ExternalHumidityWindow(ExternalHumidityViewModel vm)
    {
        InitializeComponent(); _vm = vm; DataContext = vm;
        ChamberLabel.Text = vm.ChamberName + " · " + vm.ChamberId;
        Loaded += async (_, _) => await vm.InitializeAsync();
        Closed += (_, _) => Windows.Remove(vm.ChamberId);
    }
    public static void Open(ExternalHumidityViewModel vm)
    {
        if (Windows.TryGetValue(vm.ChamberId, out var existing)) { existing.Show(); existing.Activate(); return; }
        var window = new ExternalHumidityWindow(vm) { Owner = Application.Current.MainWindow };
        Windows[vm.ChamberId] = window; window.Show();
    }
    private void NewFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Vyberte priečinok a názov nového TXT", Filter = "TXT záznam (*.txt)|*.txt", FileName = $"Testo645_{DateTime.Now:yyyyMMdd_HHmmss}.txt", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) == true) _vm.SelectFile(dialog.FileName, false);
    }
    private void AppendFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Pripisovať novú reláciu do existujúceho TXT", Filter = "TXT záznam (*.txt)|*.txt", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) _vm.SelectFile(dialog.FileName, true);
    }
}
