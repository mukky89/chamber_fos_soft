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
            if (!value.StartsWith("Zariadenie je ovládané manuálne.", StringComparison.Ordinal) &&
                !value.StartsWith("Manuálne ovládanie je vypnuté, kým beží profil.", StringComparison.Ordinal))
                continue;

            // A popup notification now carries these messages. Keep them out of the card layout
            // even when their old Visibility binding changes state later.
            BindingOperations.ClearBinding(text, UIElement.VisibilityProperty);
            text.Visibility = Visibility.Collapsed;
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
            _notificationStates[chamber.Id] = new ChamberNotificationState(chamber.IsManualActive, chamber.IsProfileRunning);
            chamber.PropertyChanged += OnNotificationChamberPropertyChanged;
        }
    }

    private void OnNotificationChamberPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ChamberViewModel chamber) return;
        if (e.PropertyName is not nameof(ChamberViewModel.IsManualActive) and not nameof(ChamberViewModel.IsProfileRunning)) return;

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => OnNotificationChamberPropertyChanged(sender, e)));
            return;
        }

        ChamberNotificationState previous = _notificationStates.TryGetValue(chamber.Id, out ChamberNotificationState? state)
            ? state
            : new ChamberNotificationState(false, false);

        if (!previous.IsManualActive && chamber.IsManualActive)
        {
            AppNotificationService.Warning(
                chamber.Name,
                "Zariadenie je ovládané manuálne. Testovací profil je do zastavenia manuálneho behu zablokovaný.",
                $"manual-active:{chamber.Id}");
        }

        if (!previous.IsProfileRunning && chamber.IsProfileRunning)
        {
            AppNotificationService.Info(
                chamber.Name,
                "Beží testovací profil. Manuálne rýchle ovládanie je do ukončenia profilu zablokované.",
                $"profile-running:{chamber.Id}");
        }

        _notificationStates[chamber.Id] = new ChamberNotificationState(chamber.IsManualActive, chamber.IsProfileRunning);
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

    private sealed record ChamberNotificationState(bool IsManualActive, bool IsProfileRunning);
}
