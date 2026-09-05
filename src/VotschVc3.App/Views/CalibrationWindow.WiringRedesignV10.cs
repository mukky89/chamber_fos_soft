using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VotschVc3.App.Calibration;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

public partial class CalibrationWindow
{
    private CancellationTokenSource? _wiringSearchCtsV10;

    private async void WiringSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _wiringSearchCtsV10?.Cancel();
        _wiringSearchCtsV10?.Dispose();
        _wiringSearchCtsV10 = new CancellationTokenSource();
        CancellationToken token = _wiringSearchCtsV10.Token;
        try
        {
            await Task.Delay(180, token);
            if (token.IsCancellationRequested) return;
            ApplyWiringSearchV10((sender as TextBox)?.Text ?? string.Empty);
        }
        catch (OperationCanceledException) { }
    }

    private void ApplyWiringSearchV10(string query)
    {
        if (_wiringGrid is null) return;
        if (IsWiringGridEditingV3())
        {
            _ = RetryWiringSearchAfterEditV10Async(query);
            return;
        }

        string needle = query.Trim();
        ICollectionView view = CollectionViewSource.GetDefaultView(_viewModel.Peaks);
        view.Filter = item => item is CalibrationPeakRowViewModel row &&
            (needle.Length == 0 || WiringSearchTextV10(row).Contains(needle, StringComparison.CurrentCultureIgnoreCase));
        view.Refresh();
        if (_viewModel.SelectedWiringPeak is null || !view.Cast<object>().Contains(_viewModel.SelectedWiringPeak))
            _viewModel.SelectedWiringPeak = view.Cast<CalibrationPeakRowViewModel>().FirstOrDefault();
    }

    private async Task RetryWiringSearchAfterEditV10Async(string query)
    {
        await Task.Delay(200);
        if (!Dispatcher.HasShutdownStarted)
            await Dispatcher.InvokeAsync(() => ApplyWiringSearchV10(query));
    }

    private static string WiringSearchTextV10(CalibrationPeakRowViewModel row) => string.Join('|',
        row.Channel, row.PeakId, row.PeakIndex, row.ChannelSerialNumber, row.ChainSerialNumber,
        row.SerialNumber, SylexFosRowMetadataStore.GetSerialNumber(row), SylexFosSensorNameStore.Get(row),
        row.Order, row.ProductDescription, row.Customer, row.Notes);

    private void SelectAllWiringPeaks_Click(object sender, RoutedEventArgs e) => SelectAllPeaks_Click(sender, e);

    private void ValidateWiringSerials_Click(object sender, RoutedEventArgs e)
    {
        if (_wiringGrid?.CommitEdit(DataGridEditingUnit.Cell, true) == true)
            _wiringGrid.CommitEdit(DataGridEditingUnit.Row, true);
        _viewModel.ValidateWiringSerialNumbers();
    }
}
