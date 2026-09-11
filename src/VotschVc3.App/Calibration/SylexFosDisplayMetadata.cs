using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VotschVc3.App.Calibration;

/// <summary>Transient API fields with cell-level notifications, independent of live peak samples.</summary>
public sealed class SylexFosDisplayMetadata : INotifyPropertyChanged
{
    private string _sensorName = string.Empty;
    private string _sylexSerialNumber = string.Empty;
    private string _fbgType = string.Empty;
    private string _fbgTypeDetail = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public string SensorName { get => _sensorName; set => Set(ref _sensorName, value); }
    public string SylexSerialNumber { get => _sylexSerialNumber; set => Set(ref _sylexSerialNumber, value); }
    public string FbgType { get => _fbgType; set => Set(ref _fbgType, value); }
    public string FbgTypeDetail { get => _fbgTypeDetail; set => Set(ref _fbgTypeDetail, value); }

    public void Clear()
    {
        SensorName = string.Empty;
        SylexSerialNumber = string.Empty;
        FbgType = string.Empty;
        FbgTypeDetail = string.Empty;
    }

    private void Set(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
