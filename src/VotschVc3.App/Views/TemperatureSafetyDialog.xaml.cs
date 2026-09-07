using System.Globalization;
using System.Windows;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

public partial class TemperatureSafetyDialog : Window
{
    private readonly ChamberViewModel _chamber;

    public TemperatureSafetyDialog(ChamberViewModel chamber)
    {
        InitializeComponent();
        _chamber = chamber;
        DataContext = chamber;
        MinimumBox.Text = chamber.SafetyTempMin.ToString("G", CultureInfo.CurrentCulture);
        MaximumBox.Text = chamber.SafetyTempMax.ToString("G", CultureInfo.CurrentCulture);
        Loaded += (_, _) => { MinimumBox.Focus(); MinimumBox.SelectAll(); };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Draft text stays local: Cancel, Escape and closing the window never re-arm the interlock.
        if (!TryParse(MinimumBox.Text, out double minimum) ||
            !TryParse(MaximumBox.Text, out double maximum) || minimum >= maximum)
        {
            ValidationMessage.Text = "Zadaj platné čísla. Minimum musí byť menšie ako maximum.";
            return;
        }

        if (!_chamber.TryApplyTemperatureSafetyLimits(minimum, maximum))
        {
            ValidationMessage.Text = "Limity nemožno zmeniť: zariadenie je zamknuté alebo prebieha FBG kalibrácia.";
            return;
        }
        DialogResult = true;
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);
}
