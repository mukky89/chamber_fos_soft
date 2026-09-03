using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Input;
using VotschVc3.Core.Calibration;

namespace VotschVc3.App.Views;

/// <summary>
/// The legacy CalibrationViewModel intentionally stays stable because it is shared by several
/// production-workspace partials. New Pali-parity settings are proxied here to the private
/// CalibrationSetup so the existing settings page can expose them without another large VM rewrite.
/// </summary>
public partial class CalibrationStabilitySettingsView : UserControl, INotifyPropertyChanged
{
    public CalibrationStabilitySettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RefreshProxyBindings();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int RequiredMeasurementSamples
    {
        get => Settings?.RequiredMeasurementSamples ?? 30;
        set => Update(settings => settings.RequiredMeasurementSamples = Math.Clamp(value, 2, 10000));
    }

    public double ChamberMaxDriftCPerMinute
    {
        get => Settings?.MaxChamberDriftCPerMinute ?? 0.1;
        set => Update(settings => settings.MaxChamberDriftCPerMinute = Math.Max(0, value));
    }

    public double ChamberStabilityTimeoutMinutes
    {
        get => Settings?.ChamberStabilityTimeout.TotalMinutes ?? 30;
        set => Update(settings => settings.ChamberStabilityTimeout = TimeSpan.FromMinutes(Math.Max(0.1, value)));
    }

    public double ReferenceToleranceC
    {
        get => Settings?.ReferenceToleranceC ?? 0.5;
        set => Update(settings => settings.ReferenceToleranceC = Math.Abs(value));
    }

    public double ReferenceStableMinutes
    {
        get => Settings?.ReferenceStableDuration.TotalMinutes ?? 1;
        set => Update(settings => settings.ReferenceStableDuration = TimeSpan.FromMinutes(Math.Max(0, value)));
    }

    public double ReferenceMaxDriftCPerMinute
    {
        get => Settings?.MaxReferenceDriftCPerMinute ?? 0.1;
        set => Update(settings => settings.MaxReferenceDriftCPerMinute = Math.Max(0, value));
    }

    public double ReferenceStabilityTimeoutMinutes
    {
        get => Settings?.ReferenceStabilityTimeout.TotalMinutes ?? 30;
        set => Update(settings => settings.ReferenceStabilityTimeout = TimeSpan.FromMinutes(Math.Max(0.1, value)));
    }

    public double MinimumCalibrationPointMinutes
    {
        get => Settings?.MinimumCalibrationPointDuration.TotalMinutes ?? 0;
        set => Update(settings => settings.MinimumCalibrationPointDuration = TimeSpan.FromMinutes(Math.Max(0, value)));
    }

    public double DeviceRecoveryTimeoutMinutes
    {
        get => Settings?.DeviceRecoveryTimeout.TotalMinutes ?? 15;
        set => Update(settings => settings.DeviceRecoveryTimeout = TimeSpan.FromMinutes(Math.Max(0.1, value)));
    }

    public double DeviceRecoveryPollSeconds
    {
        get => Settings?.DeviceRecoveryPollInterval.TotalSeconds ?? 5;
        set => Update(settings => settings.DeviceRecoveryPollInterval = TimeSpan.FromSeconds(Math.Max(1, value)));
    }

    private CalibrationProfileSettings? Settings
    {
        get
        {
            object? vm = DataContext;
            if (vm is null) return null;
            FieldInfo? field = vm.GetType().GetField("_setup", BindingFlags.Instance | BindingFlags.NonPublic);
            return (field?.GetValue(vm) as CalibrationSetup)?.Settings;
        }
    }

    private void Update(Action<CalibrationProfileSettings> apply, [CallerMemberName] string? propertyName = null)
    {
        CalibrationProfileSettings? settings = Settings;
        if (settings is null) return;
        apply(settings);
        OnPropertyChanged(propertyName);
        PersistThroughViewModel();
    }

    private void PersistThroughViewModel()
    {
        object? vm = DataContext;
        if (vm is null) return;
        PropertyInfo? property = vm.GetType().GetProperty("SaveSetupCommand", BindingFlags.Instance | BindingFlags.Public);
        if (property?.GetValue(vm) is ICommand command && command.CanExecute(null)) command.Execute(null);
    }

    private void RefreshProxyBindings()
    {
        OnPropertyChanged(nameof(RequiredMeasurementSamples));
        OnPropertyChanged(nameof(ChamberMaxDriftCPerMinute));
        OnPropertyChanged(nameof(ChamberStabilityTimeoutMinutes));
        OnPropertyChanged(nameof(ReferenceToleranceC));
        OnPropertyChanged(nameof(ReferenceStableMinutes));
        OnPropertyChanged(nameof(ReferenceMaxDriftCPerMinute));
        OnPropertyChanged(nameof(ReferenceStabilityTimeoutMinutes));
        OnPropertyChanged(nameof(MinimumCalibrationPointMinutes));
        OnPropertyChanged(nameof(DeviceRecoveryTimeoutMinutes));
        OnPropertyChanged(nameof(DeviceRecoveryPollSeconds));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
