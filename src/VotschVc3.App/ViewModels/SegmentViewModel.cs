using VotschVc3.App.Mvvm;
using VotschVc3.Core.Profiles;

namespace VotschVc3.App.ViewModels;

/// <summary>Editable wrapper around a <see cref="ProfileSegment"/> for the profile grid.</summary>
public sealed class SegmentViewModel : ObservableObject
{
    public SegmentViewModel(ProfileSegment? model = null)
    {
        model ??= new ProfileSegment();
        _name = model.Name;
        _targetTemperature = model.TargetTemperature;
        _targetHumidity = model.TargetHumidity;
        _durationMinutes = model.Duration.TotalMinutes;
        _isRamp = model.IsRamp;
        _guaranteedSoak = model.GuaranteedSoak;
        _soakTolerance = model.SoakTolerance;
    }

    private string _name;
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private double _targetTemperature;
    public double TargetTemperature
    {
        get => _targetTemperature;
        set => SetProperty(ref _targetTemperature, value);
    }

    private double? _targetHumidity;
    public double? TargetHumidity
    {
        get => _targetHumidity;
        set => SetProperty(ref _targetHumidity, value);
    }

    private double _durationMinutes;
    public double DurationMinutes
    {
        get => _durationMinutes;
        set => SetProperty(ref _durationMinutes, Math.Max(0, value));
    }

    private bool _isRamp;
    public bool IsRamp
    {
        get => _isRamp;
        set => SetProperty(ref _isRamp, value);
    }

    private bool _guaranteedSoak;
    public bool GuaranteedSoak
    {
        get => _guaranteedSoak;
        set => SetProperty(ref _guaranteedSoak, value);
    }

    private double _soakTolerance;
    public double SoakTolerance
    {
        get => _soakTolerance;
        set => SetProperty(ref _soakTolerance, Math.Max(0, value));
    }

    /// <summary>Materialises the editable values back into a core model object.</summary>
    public ProfileSegment ToModel() => new()
    {
        Name = Name,
        TargetTemperature = TargetTemperature,
        TargetHumidity = TargetHumidity,
        Duration = TimeSpan.FromMinutes(DurationMinutes),
        IsRamp = IsRamp,
        GuaranteedSoak = GuaranteedSoak,
        SoakTolerance = SoakTolerance,
    };
}
