using System.IO;
using System.Windows;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.Views;

public partial class CalibrationWindow
{
    private bool _wiringImportPending;
    private async void ImportWiring_Click(object sender, RoutedEventArgs e)
    {
        if (_wiringImportPending) return;
        if (!_viewModel.CanImportWiring) { ShowProductionInfo("Najprv vyber profil a dokonči prebiehajúce načítanie alebo kalibráciu."); return; }
        if (IsWiringGridEditingV3()) { ShowProductionInfo("Najprv potvrď editovanú bunku klávesom Enter."); return; }
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Načítať zapojenie uložené aplikáciou",
            Filter = "Zapojenie aplikácie (*.xlsx;*.json;*.bak)|*.xlsx;*.json;*.bak|Zapojenie z kalibrácie (*.xlsx)|*.xlsx|Uložené zapojenie a zálohy (*.json;*.bak)|*.json;*.bak",
            InitialDirectory = Path.Combine(AppPaths.CalibrationDir, "Setups"), CheckFileExists = true, Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        var profileId = _viewModel.SelectedProfile!.Id;
        var chamberId = _viewModel.SelectedChamber?.Config.Id;
        _wiringImportPending = true;
        try
        {
            var mappings = await Task.Run(() => CalibrationWiringImporter.Load(dialog.FileName));
            if (_viewModel.SelectedProfile?.Id != profileId || _viewModel.SelectedChamber?.Config.Id != chamberId || !_viewModel.CanImportWiring)
                throw new InvalidOperationException("Počas načítania sa zmenil profil alebo stav kalibrácie. Vyber súbor znova.");
            var connected = _viewModel.Peaks.Where(p => !p.IsDisconnected).Select(p => p.ToMapping().SourceIdentity).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int matched = mappings.Count(m => connected.Contains(m.SourceIdentity));
            int ignored = mappings.Count(m => _viewModel.IsPeakLoggerChannelIgnored(m.Channel));
            if (!ConfirmDialog.Ask($"Súbor: {Path.GetFileName(dialog.FileName)}\nProfil: {_viewModel.SelectedProfile.Name}\n\n" +
                $"Načítať {mappings.Count} peakov ({mappings.Count(m => m.Selected)} vybraných na kalibráciu)?\n" +
                $"Zhodné pripojené peaky: {matched}; ostatné zostanú označené ako nepripojené.\n" +
                (ignored > 0 ? $"Aktuálne ignorované kanály skryjú {ignored} importovaných peakov.\n" : "") +
                "\nNahradia sa aktuálne SN, CHAIN, výber peakov a údaje zapojenia. Profil, teplotné body, kritériá stability a ignorované kanály sa nemenia. Pôvodné zapojenie sa zálohuje.",
                "Načítať zapojenie zo súboru", "Načítať zapojenie")) return;
            CloseSequentialWiringV9();
            _viewModel.ImportWiring(mappings);
            _knownPeakIdentities = CurrentPeakIdentities();
            _pendingTopologyChange = null;
            _pendingTopologyChangeConfirmations = 0;
            ShowProductionInfo(_viewModel.StatusMessage);
        }
        catch (Exception ex) { ShowProductionInfo("Zapojenie sa nepodarilo načítať: " + ex.Message); }
        finally { _wiringImportPending = false; }
    }
}
