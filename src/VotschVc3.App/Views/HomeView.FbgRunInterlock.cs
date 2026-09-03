using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

/// <summary>
/// Per-device dashboard interlock while FBG calibration owns the chamber.
/// Manual setpoint/profile controls are disabled, the mode badge becomes FBG CALIBRATION,
/// and the FBG button gets a slow red pulse so the active owner is unmistakable.
/// </summary>
internal static class HomeViewFbgRunInterlockBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(HomeView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded),
            handledEventsToo: true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is HomeView home) home.AttachFbgRunInterlock();
    }
}

public partial class HomeView
{
    private bool _fbgRunInterlockAttached;
    private readonly Dictionary<TextBlock, BindingBase?> _fbgModeTextBindings = new();
    private readonly Dictionary<Border, BindingBase?> _fbgModeVisibilityBindings = new();
    private readonly Dictionary<Border, Brush?> _fbgModeBackgrounds = new();
    private readonly Dictionary<Button, FbgPulseState> _fbgPulseStates = new();

    private void AttachFbgRunInterlock()
    {
        if (_fbgRunInterlockAttached) return;
        _fbgRunInterlockAttached = true;
        CalibrationStatusViewModel.Instance.PropertyChanged += OnFbgRunInterlockStatusChanged;
        Unloaded += OnFbgRunInterlockUnloaded;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(UpdateFbgRunInterlocks));
    }

    private void OnFbgRunInterlockStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(UpdateFbgRunInterlocks));
            return;
        }
        UpdateFbgRunInterlocks();
    }

    private void UpdateFbgRunInterlocks()
    {
        ChamberViewModel[] chambers = FindVisualDescendants<FrameworkElement>(this)
            .Select(element => element.DataContext)
            .OfType<ChamberViewModel>()
            .GroupBy(chamber => chamber.Id)
            .Select(group => group.First())
            .ToArray();

        foreach (ChamberViewModel chamber in chambers)
        {
            bool running = CalibrationStatusViewModel.Instance.GetWorkspace(chamber.Id).IsRunning;
            SetFbgOwnedSectionEnabled(chamber, "Rýchle ovládanie", !running);
            SetFbgOwnedSectionEnabled(chamber, "Testovací profil", !running);
            UpdateFbgModeBadge(chamber, running);
            UpdateFbgCalibrationButtonPulse(chamber, running);
        }
    }

    private void SetFbgOwnedSectionEnabled(ChamberViewModel chamber, string title, bool enabled)
    {
        foreach (TextBlock text in FindVisualDescendants<TextBlock>(this).Where(text =>
                     ReferenceEquals(text.DataContext, chamber) &&
                     string.Equals(text.Text, title, StringComparison.OrdinalIgnoreCase)))
        {
            Border? section = FindAncestorBorder(text, chamber);
            if (section is null) continue;
            if (enabled)
                section.ClearValue(UIElement.IsEnabledProperty);
            else
                section.IsEnabled = false;
        }
    }

    private void UpdateFbgModeBadge(ChamberViewModel chamber, bool running)
    {
        foreach (TextBlock text in FindVisualDescendants<TextBlock>(this).Where(text => ReferenceEquals(text.DataContext, chamber)).ToArray())
        {
            BindingBase? binding = BindingOperations.GetBindingBase(text, TextBlock.TextProperty);
            string? path = (binding as Binding)?.Path?.Path;
            bool isMode = string.Equals(path, nameof(ChamberViewModel.ControlModeBadge), StringComparison.Ordinal) ||
                          _fbgModeTextBindings.ContainsKey(text);
            if (!isMode) continue;

            Border? badge = FindAncestorBorder(text, chamber);
            if (badge is null) continue;

            if (running)
            {
                if (!_fbgModeTextBindings.ContainsKey(text))
                    _fbgModeTextBindings[text] = binding;
                if (!_fbgModeVisibilityBindings.ContainsKey(badge))
                    _fbgModeVisibilityBindings[badge] = BindingOperations.GetBindingBase(badge, UIElement.VisibilityProperty);
                if (!_fbgModeBackgrounds.ContainsKey(badge))
                    _fbgModeBackgrounds[badge] = badge.Background;

                BindingOperations.ClearBinding(text, TextBlock.TextProperty);
                text.Text = "FBG CALIBRATION";
                text.Foreground = Brushes.White;
                BindingOperations.ClearBinding(badge, UIElement.VisibilityProperty);
                badge.Visibility = Visibility.Visible;
                badge.Background = FindResource("DangerBrush") as Brush ?? Brushes.Firebrick;
            }
            else if (_fbgModeTextBindings.TryGetValue(text, out BindingBase? originalTextBinding))
            {
                if (originalTextBinding is not null)
                    BindingOperations.SetBinding(text, TextBlock.TextProperty, originalTextBinding);
                else
                    text.ClearValue(TextBlock.TextProperty);
                text.ClearValue(TextBlock.ForegroundProperty);

                if (_fbgModeVisibilityBindings.TryGetValue(badge, out BindingBase? originalVisibilityBinding) && originalVisibilityBinding is not null)
                    BindingOperations.SetBinding(badge, UIElement.VisibilityProperty, originalVisibilityBinding);
                else
                    badge.ClearValue(UIElement.VisibilityProperty);

                if (_fbgModeBackgrounds.TryGetValue(badge, out Brush? originalBackground))
                    badge.Background = originalBackground;

                _fbgModeTextBindings.Remove(text);
                _fbgModeVisibilityBindings.Remove(badge);
                _fbgModeBackgrounds.Remove(badge);
            }
        }
    }

    private void UpdateFbgCalibrationButtonPulse(ChamberViewModel chamber, bool running)
    {
        Button[] buttons = FindVisualDescendants<Button>(this)
            .Where(button => ReferenceEquals(button.DataContext, chamber) && ButtonContainsFbgCalibrationLabel(button))
            .ToArray();

        foreach (Button button in buttons)
        {
            if (running)
            {
                if (_fbgPulseStates.ContainsKey(button)) continue;

                var state = new FbgPulseState(
                    button.ReadLocalValue(Control.BackgroundProperty),
                    button.ReadLocalValue(Control.BorderBrushProperty),
                    button.ReadLocalValue(Control.ForegroundProperty),
                    button.ReadLocalValue(UIElement.OpacityProperty));
                _fbgPulseStates[button] = state;

                var pulse = new SolidColorBrush(Color.FromRgb(126, 21, 31));
                button.Background = pulse;
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 82, 94));
                button.Foreground = Brushes.White;

                pulse.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
                {
                    From = Color.FromRgb(105, 18, 28),
                    To = Color.FromRgb(224, 57, 72),
                    Duration = TimeSpan.FromSeconds(1.8),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                });
                button.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
                {
                    From = 0.82,
                    To = 1.0,
                    Duration = TimeSpan.FromSeconds(1.8),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                });
            }
            else if (_fbgPulseStates.Remove(button, out FbgPulseState? state))
            {
                if (button.Background is SolidColorBrush animated)
                    animated.BeginAnimation(SolidColorBrush.ColorProperty, null);
                button.BeginAnimation(UIElement.OpacityProperty, null);
                RestoreLocalValue(button, Control.BackgroundProperty, state.Background);
                RestoreLocalValue(button, Control.BorderBrushProperty, state.BorderBrush);
                RestoreLocalValue(button, Control.ForegroundProperty, state.Foreground);
                RestoreLocalValue(button, UIElement.OpacityProperty, state.Opacity);
            }
        }
    }

    private static bool ButtonContainsFbgCalibrationLabel(Button button)
    {
        if (button.Content is string label)
            return label.Contains("FBG", StringComparison.OrdinalIgnoreCase) && label.Contains("kalibr", StringComparison.OrdinalIgnoreCase);
        return FindVisualDescendants<TextBlock>(button).Any(text =>
            (text.Text ?? string.Empty).Contains("FBG", StringComparison.OrdinalIgnoreCase) &&
            (text.Text ?? string.Empty).Contains("kalibr", StringComparison.OrdinalIgnoreCase));
    }

    private static Border? FindAncestorBorder(DependencyObject start, ChamberViewModel chamber)
    {
        DependencyObject? node = start;
        while (node is not null)
        {
            node = VisualTreeHelper.GetParent(node);
            if (node is Border border && ReferenceEquals(border.DataContext, chamber)) return border;
        }
        return null;
    }

    private static void RestoreLocalValue(DependencyObject target, DependencyProperty property, object value)
    {
        if (value == DependencyProperty.UnsetValue)
            target.ClearValue(property);
        else
            target.SetValue(property, value);
    }

    private void OnFbgRunInterlockUnloaded(object sender, RoutedEventArgs e)
    {
        CalibrationStatusViewModel.Instance.PropertyChanged -= OnFbgRunInterlockStatusChanged;
        foreach (Button button in _fbgPulseStates.Keys.ToArray())
        {
            if (_fbgPulseStates.Remove(button, out FbgPulseState? state))
            {
                if (button.Background is SolidColorBrush animated)
                    animated.BeginAnimation(SolidColorBrush.ColorProperty, null);
                button.BeginAnimation(UIElement.OpacityProperty, null);
                RestoreLocalValue(button, Control.BackgroundProperty, state.Background);
                RestoreLocalValue(button, Control.BorderBrushProperty, state.BorderBrush);
                RestoreLocalValue(button, Control.ForegroundProperty, state.Foreground);
                RestoreLocalValue(button, UIElement.OpacityProperty, state.Opacity);
            }
        }
        Unloaded -= OnFbgRunInterlockUnloaded;
        _fbgRunInterlockAttached = false;
    }

    private sealed record FbgPulseState(object Background, object BorderBrush, object Foreground, object Opacity);
}
