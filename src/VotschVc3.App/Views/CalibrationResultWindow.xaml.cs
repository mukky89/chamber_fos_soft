using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.Views;

public partial class CalibrationResultWindow : Window
{
    public CalibrationResultWindow(CalibrationRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        InitializeComponent();
        DataContext = new CalibrationResultWindowModel(run);
    }
}

public sealed class CalibrationResultWindowModel : INotifyPropertyChanged
{
    private CalibrationResultRow? _selectedResult;

    public CalibrationResultWindowModel(CalibrationRunRecord run)
    {
        Run = run;
        Results = new ObservableCollection<CalibrationResultRow>(
            run.CalculationResults.Select(result => new CalibrationResultRow(result)));
        SelectedPoints = new ObservableCollection<TemperatureCalibrationPointResult>();
        SelectedResult = Results.FirstOrDefault();
    }

    public CalibrationRunRecord Run { get; }
    public ObservableCollection<CalibrationResultRow> Results { get; }
    public ObservableCollection<TemperatureCalibrationPointResult> SelectedPoints { get; }

    public string Title => $"{Run.ProfileName} · {Run.ChamberName}";
    public string Subtitle =>
        $"Run {Run.RunId} · {Run.StartedAt:yyyy-MM-dd HH:mm:ss} · operátor {Run.Operator} · " +
        $"referencia {ReferenceLabel()}";
    public string OverallText => Run.CalculationResults.Count == 0
        ? "BEZ VÝPOČTU"
        : Run.CalculationResults.All(result => result.OverallPassed)
            ? $"PASS · {Run.CalculationResults.Count}/{Run.CalculationResults.Count}"
            : $"FAIL · {Run.CalculationResults.Count(result => result.OverallPassed)}/{Run.CalculationResults.Count}";
    public Brush ResultBrush => Run.CalculationResults.Count > 0 && Run.CalculationResults.All(result => result.OverallPassed)
        ? Brushes.SeaGreen
        : Brushes.Firebrick;

    public CalibrationResultRow? SelectedResult
    {
        get => _selectedResult;
        set
        {
            if (ReferenceEquals(_selectedResult, value)) return;
            _selectedResult = value;
            OnPropertyChanged();
            SelectedPoints.Clear();
            if (value is not null)
            {
                foreach (TemperatureCalibrationPointResult point in value.Source.Points)
                    SelectedPoints.Add(point);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private string ReferenceLabel()
    {
        if (!string.IsNullOrWhiteSpace(Run.ReferenceThermometerSerialNumber))
            return $"{Run.ReferenceThermometerSerialNumber}/{Run.ReferenceThermometerChannel}";
        if (!string.IsNullOrWhiteSpace(Run.ReferenceThermometerPort))
            return $"{Run.ReferenceThermometerPort}/{Run.ReferenceThermometerChannel}";
        return "interná sonda komory";
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CalibrationResultRow
{
    public CalibrationResultRow(TemperatureCalibrationResult source) => Source = source;

    public TemperatureCalibrationResult Source { get; }
    public string SerialNumber => Source.SerialNumber;
    public string Channel => Source.Channel;
    public string PeakId => Source.PeakId;
    public int PeakIndex => Source.PeakIndex;
    public string RecipeKey => Source.RecipeKey;
    public TemperatureCalibrationCalculationType CalculationType => Source.CalculationType;
    public double? A => Source.A;
    public double? B => Source.B;
    public double? C => Source.C;
    public double? D => Source.D;
    public double? S1 => Source.S1;
    public double? S2 => Source.S2;
    public double SensitivityPmPerC => Source.SensitivityPmPerC;
    public double TRefNm => Source.TRefNm;
    public double MaxErrorC => Source.MaxErrorC;
    public double ErrorToleranceC => Source.ErrorToleranceC;
    public double R2 => Source.R2;
    public string PassText => Source.OverallPassed ? "PASS" : "FAIL";
    public string StatusMessage => Source.StatusMessage;
}
