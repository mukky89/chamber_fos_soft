using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.Views;

public partial class WiringSetupPickerWindow : Window
{
    private IReadOnlyList<CalibrationSetupCatalogEntry> _items = [];
    private bool _closed;
    public string? SelectedFilePath { get; private set; }
    public string SourceDescription { get; private set; } = "";
    public WiringSetupPickerWindow()
    {
        InitializeComponent();
        Loaded += LoadCatalog;
        Closed += (_, _) => _closed = true;
    }
    private async void LoadCatalog(object sender, RoutedEventArgs e)
    {
        Loaded -= LoadCatalog;
        try
        {
            var items = await Task.Run(() => CalibrationSetupCatalog.Read(Path.Combine(AppPaths.CalibrationDir, "Setups"),
                new ProfileStore(AppPaths.ProfilesDir).LoadAll().ToDictionary(x => x.Id, x => x.Name),
                new ChamberConfigStore(Path.Combine(AppPaths.SettingsDir, "chambers.json")).LoadAll().ToDictionary(x => x.Id, x => x.Name)));
            if (_closed) return;
            _items = items; Filter();
        }
        catch (Exception ex) { if (!_closed) Status.Text = "Zapojenia sa nepodarilo načítať: " + ex.Message + " Použite Iný súbor."; }
    }
    private void Filter()
    {
        if (Entries is null || Search is null || Backups is null || Status is null) return;
        string query = Search.Text.Trim();
        var matches = _items.Where(x => (Backups.IsChecked == true || x.Kind == "Uložené zapojenie") &&
            (query.Length == 0 || x.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        Entries.ItemsSource = matches;
        Status.Text = matches.Length == 0 ? "Žiadne zodpovedajúce zapojenie. Zmeňte vyhľadávanie alebo vyberte Iný súbor." : $"Nájdené záznamy: {matches.Length}. Vyberte riadok; podrobnosti sa zobrazia nižšie.";
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) => Filter();
    private void FilterChanged(object sender, RoutedEventArgs e) => Filter();
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Detail is null) return;
        var selected = Entries.SelectedItem as CalibrationSetupCatalogEntry;
        Detail.Text = selected is null ? "" : selected.Description + (selected.Error.Length > 0 ? "\nNedá sa načítať: " + selected.Error : "");
        SourcePath.Text = selected?.Path ?? "";
        LoadButton.IsEnabled = ExportButton.IsEnabled = selected?.CanLoad == true;
    }
    private void Load_Click(object sender, RoutedEventArgs e)
    {
        if (Entries.SelectedItem is not CalibrationSetupCatalogEntry { CanLoad: true } selected) return;
        SelectedFilePath = selected.Path; SourceDescription = selected.Description; DialogResult = true;
    }
    private void LoadDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject target && ItemsControl.ContainerFromElement(Entries, target) is DataGridRow)
            Load_Click(sender, e);
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Iné uložené zapojenie alebo export z kalibrácie", Filter = "Zapojenie (*.xlsx;*.json;*.bak)|*.xlsx;*.json;*.bak", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        SelectedFilePath = dialog.FileName; DialogResult = true;
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (Entries.SelectedItem is not CalibrationSetupCatalogEntry { CanLoad: true } selected) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Uložiť kópiu zapojenia s čitateľným názvom", Filter = "Zapojenie (*.json)|*.json", FileName = selected.SuggestedFileName, AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        ExportButton.IsEnabled = false;
        try
        {
            await Task.Run(() => CalibrationSetupCatalog.ExportCopy(selected, dialog.FileName));
            if (!_closed) Status.Text = "Kópia uložená: " + dialog.FileName;
        }
        catch (Exception ex) { if (!_closed) Status.Text = "Kópiu sa nepodarilo uložiť: " + ex.Message; }
        finally { if (!_closed) ExportButton.IsEnabled = (Entries.SelectedItem as CalibrationSetupCatalogEntry)?.CanLoad == true; }
    }
}
