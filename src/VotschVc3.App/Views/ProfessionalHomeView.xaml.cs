using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

/// <summary>
/// Code-behind for the Professional dashboard. Mirrors <see cref="FleetGanttView"/>'s
/// approach: hooks the shell's chamber collection and each chamber's
/// <see cref="ChamberViewModel.PropertyChanged"/> directly, then updates a few
/// plain <see cref="TextBlock"/>s and an alarm list imperatively. Kept out of the
/// view model because this is purely a derived, read-only summary of state the
/// shell already owns — nothing here originates or mutates device state.
/// </summary>
public partial class ProfessionalHomeView : UserControl
{
    private static readonly string[] SummaryProperties =
    {
        nameof(ChamberViewModel.IsActive),
        nameof(ChamberViewModel.IsAlarm),
        nameof(ChamberViewModel.IsConnected),
        nameof(ChamberViewModel.AlarmMessage),
        nameof(ChamberViewModel.Name),
    };

    private readonly ObservableCollection<ChamberViewModel> _alarmed = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<ChamberViewModel> _hooked = new();
    private ShellViewModel? _shell;

    public ProfessionalHomeView()
    {
        InitializeComponent();
        AlarmItemsControl.ItemsSource = _alarmed;
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => { _clock.Start(); UpdateClock(); UpdateSummary(); };
        Unloaded += (_, _) => _clock.Stop();
        _clock.Tick += (_, _) => UpdateClock();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_shell is not null)
        {
            _shell.VisibleChambers.CollectionChanged -= OnChambersChanged;
        }

        _shell = e.NewValue as ShellViewModel;

        if (_shell is not null)
        {
            _shell.VisibleChambers.CollectionChanged += OnChambersChanged;
        }

        RehookItems();
        UpdateSummary();
    }

    private void OnChambersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RehookItems();
        UpdateSummary();
    }

    private void RehookItems()
    {
        foreach (ChamberViewModel vm in _hooked)
        {
            vm.PropertyChanged -= OnChamberChanged;
        }

        _hooked.Clear();
        foreach (ChamberViewModel vm in _shell?.VisibleChambers ?? Enumerable.Empty<ChamberViewModel>())
        {
            vm.PropertyChanged += OnChamberChanged;
            _hooked.Add(vm);
        }
    }

    private void OnChamberChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null && SummaryProperties.Contains(e.PropertyName))
        {
            UpdateSummary();
        }
    }

    private void UpdateClock() => ClockText.Text = DateTime.Now.ToString("HH:mm:ss");

    private void UpdateSummary()
    {
        List<ChamberViewModel> devices = _shell?.VisibleChambers.ToList() ?? new List<ChamberViewModel>();
        int active = devices.Count(c => c.IsActive);
        int alarms = devices.Count(c => c.IsAlarm);

        SummaryText.Text = $"{devices.Count} {Pluralize(devices.Count, "zariadenie", "zariadenia", "zariadení")}  ·  " +
                            $"{active} {Pluralize(active, "aktívne", "aktívne", "aktívnych")}  ·  " +
                            $"{alarms} {Pluralize(alarms, "upozornenie", "upozornenia", "upozornení")}";
        ActiveChipText.Text = $"{active} aktívne";
        AlarmChipText.Text = $"{alarms} {Pluralize(alarms, "upozornenie", "upozornenia", "upozornení")}";
        AlarmHeaderText.Text = $"⚠ {alarms} {Pluralize(alarms, "upozornenie", "upozornenia", "upozornení")}";

        _alarmed.Clear();
        foreach (ChamberViewModel c in devices.Where(c => c.IsAlarm))
        {
            _alarmed.Add(c);
        }

        AlarmItemsControl.Visibility = alarms > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoAlarmsText.Visibility = alarms > 0 ? Visibility.Collapsed : Visibility.Visible;
        AlarmPanel.BorderThickness = new Thickness(alarms > 0 ? 1 : 0);
    }

    private static string Pluralize(int n, string one, string few, string many) =>
        n == 1 ? one : n is >= 2 and <= 4 ? few : many;

    private void AlarmRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChamberViewModel chamber } && _shell is { } shell &&
            shell.OpenChamberCommand.CanExecute(chamber))
        {
            shell.OpenChamberCommand.Execute(chamber);
        }
    }
}
