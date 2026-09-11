using System.ComponentModel;
using System.Runtime.CompilerServices;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.Calibration;

/// <summary>Saved production metadata with cell-level notifications, independent of live peak samples.</summary>
public sealed class SylexFosDisplayMetadata : INotifyPropertyChanged
{
    private string _sensorName = string.Empty;
    private string _sylexSerialNumber = string.Empty;
    private string _fbgType = string.Empty;
    private string _wavelengthEvaluation = "Nevyhodnotené";
    private string _wavelengthEvaluationDetail = string.Empty;
    public string WavelengthEvaluation { get => _wavelengthEvaluation; set => Set(ref _wavelengthEvaluation, value); }
    public string WavelengthEvaluationDetail { get => _wavelengthEvaluationDetail; set => Set(ref _wavelengthEvaluationDetail, value); }
    private string _fbgTypeDetail = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public string SensorName { get => _sensorName; set => Set(ref _sensorName, value); }
    public string SylexSerialNumber { get => _sylexSerialNumber; set => Set(ref _sylexSerialNumber, value); }
    public string FbgType { get => _fbgType; set => Set(ref _fbgType, value is "T" or "S" or "" ? value : "—"); }
    public string FbgTypeDetail { get => _fbgTypeDetail; set => Set(ref _fbgTypeDetail, value); }

    public void Restore(CalibrationSensorMapping? mapping)
    {
        if (mapping is null) return;
        string serial = FbgSerialParser.Parse(mapping.SerialNumber);
        if (!string.Equals(SylexSerialNumber, serial, StringComparison.OrdinalIgnoreCase)) Clear();
        SylexSerialNumber = serial;
        if (string.IsNullOrWhiteSpace(serial)) return;
        if (!string.IsNullOrWhiteSpace(mapping.SensorName)) SensorName = mapping.SensorName;
        if (!string.IsNullOrWhiteSpace(mapping.ProductionFbgType)) FbgType = mapping.ProductionFbgType;
        if (!string.IsNullOrWhiteSpace(mapping.ProductionFbgTypeDetail)) FbgTypeDetail = mapping.ProductionFbgTypeDetail;
    }

    public void Clear()
    {
        SensorName = string.Empty;
        SylexSerialNumber = string.Empty;
        FbgType = string.Empty;
        FbgTypeDetail = string.Empty;
        WavelengthEvaluation = "Nevyhodnotené";
        WavelengthEvaluationDetail = string.Empty;
    }

    private void Set(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
