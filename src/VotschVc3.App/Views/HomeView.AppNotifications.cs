using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using VotschVc3.App.Notifications;
using VotschVc3.App.ViewModels;

namespace VotschVc3.App.Views;

public partial class HomeView
{
    private readonly Dictionary<Guid, ChamberNotificationState> _notificationStates = new();
    private readonly Dictionary<Guid, ChamberViewModel> _notificationChambers = new();
    private bool _operationalNotificationsAttached;

    private void AttachOperationalNotifications()
    {
        if (_operationalNotificationsAttached) return;
        _operationalNotificationsAttached = true;
        Loaded += OnOperationalNotificationsLoaded;
        Unloaded += OnOperationalNotificationsUnloaded;
    }

    private void OnOperationalNotificationsLoaded(object sender, RoutedEventArgs e)
    {
        HideLegacyInlineOperationalWarnings();
        AttachVisibleChambersForNotifications();
    }

    private void HideLegacyInlineOperationalWarnings()
    {
        foreach (TextBlock text in FindNotificationDescendants<TextBlock>(this))
        {
            string value = text.Text ?? string.Empty;
            if (value.StartsWith("Zariadenie je ovládané manuálne.", StringComparison.Ordinal) ||
                value.StartsWith("Manuálne ovládanie je vypnuté, kým beží profil.", StringComparison.Ordinal))
            {
                BindingOperations.ClearBinding(text, UIElement.VisibilityProperty);
                text.Visibility = Visibility.Collapsed;
                continue;
            }

            BindingBase? textBinding = BindingOperations.GetBindingBase(text, TextBlock.TextProperty);
            if (textBinding is Binding binding &&
                string.Equals(binding.Path?.Path, nameof(ChamberViewModel.ActionInfo), StringComparison.Ordinal))
            {
                Border? banner = FindNotificationAncestor<Border>(text);
                if (banner is not null)
                {
                    BindingOperations.ClearBinding(banner, UIElement.VisibilityProperty);
                    banner.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    private void AttachVisibleChambersForNotifications()
    {
        ChamberViewModel[] chambers = FindNotificationDescendants<FrameworkElement>(this)
            .Select(element => element.DataContext)
            .OfType<ChamberViewModel>()
            .GroupBy(chamber => chamber.Id)
            .Select(group => group.First())
            .ToArray();

        foreach (ChamberViewModel chamber in chambers)
        {
            if (_notificationChambers.ContainsKey(chamber.Id)) continue;
            _notificationChambers[chamber.Id] = chamber;
            _notificationStates[chamber.Id] = new ChamberNotificationState(
                chamber.IsManualActive,
                chamber.IsProfileRunning,
                chamber.ActionInfo ?? string.Empty);
            chamber.PropertyChanged += OnNotificationChamberPropertyChanged;
        }
    }

    private void OnNotificationChamberPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ChamberViewModel chamber) return;
        if (e.PropertyName is not nameof(ChamberViewModel.IsManualActive)
            and not nameof(ChamberViewModel.IsProfileRunning)
            and not nameof(ChamberViewModel.ActionInfo))
            return;

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => OnNotificationChamberPropertyChanged(sender, e)));
            return;
        }

        ChamberNotificationState previous = _notificationStates.TryGetValue(chamber.Id, out ChamberNotificationState? state)
            ? state
            : new ChamberNotificationState(false, false, string.Empty);

        // Manual/profile-running state is already persistently visible on the chamber card. Do not
        // show transient popups for routine mode transitions; they were obscuring the operator UI.
        // Alarm/error notifications and meaningful action failures continue through the central
        // AppNotificationService below.
        string actionInfo = chamber.ActionInfo?.Trim() ?? string.Empty;
        if (e.PropertyName == nameof(ChamberViewModel.ActionInfo) &&
            !string.IsNullOrWhiteSpace(actionInfo) &&
            !string.Equals(previous.LastActionInfo, actionInfo, StringComparison.Ordinal) &&
            !IsRoutineModeMessage(actionInfo))
        {
            AppNotificationKind kind = ClassifyActionInfo(actionInfo);
            AppNotificationService.Show(
                chamber.Name,
                actionInfo,
                kind,
                dedupeKey: $"action-info:{chamber.Id}:{actionInfo}");
        }

        _notificationStates[chamber.Id] = new ChamberNotificationState(
            chamber.IsManualActive,
            chamber.IsProfileRunning,
            actionInfo);
    }

    private static bool IsRoutineModeMessage(string message)
    {
        string value = message.Trim();
        return value.StartsWith("Zariadenie je ovládané manuálne", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Testovací profil je", StringComparison.OrdinalIgnoreCase)
            || value.Contains("manuálneho behu zablokovaný", StringComparison.OrdinalIgnoreCase)
            || value.Contains("manuálne rýchle ovládanie", StringComparison.OrdinalIgnoreCase)
            || value.Contains("kým beží profil", StringComparison.OrdinalIgnoreCase);
    }

    private static AppNotificationKind ClassifyActionInfo(string message)
    {
        if (message.Contains("chyba", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("nepodar", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("zlyh", StringComparison.OrdinalIgnoreCase))
            return AppNotificationKind.Warning;

        if (message.Contains("nastav", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("spusten", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("zastaven", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("uložen", StringComparison.OrdinalIgnoreCase))
            return AppNotificationKind.Success;

        return AppNotificationKind.Info;
    }

    private void OnOperationalNotificationsUnloaded(object sender, RoutedEventArgs e)
    {
        foreach (ChamberViewModel chamber in _notificationChambers.Values)
            chamber.PropertyChanged -= OnNotificationChamberPropertyChanged;

        _notificationChambers.Clear();
        _notificationStates.Clear();
    }

    private static IEnumerable<T> FindNotificationDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (T nested in FindNotificationDescendants<T>(child)) yield return nested;
        }
    }

    private static T? FindNotificationAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        DependencyObject? current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private sealed record ChamberNotificationState(bool IsManualActive, bool IsProfileRunning, string LastActionInfo);
}
