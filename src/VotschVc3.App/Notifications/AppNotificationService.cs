using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace VotschVc3.App.Notifications;

public enum AppNotificationKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// Single in-app notification pipeline for operator-facing transient messages.
/// Notifications are queued, de-duplicated and rendered as a small non-activating
/// popup above the currently active application window. This keeps warnings out of
/// permanent card layouts and gives the whole desktop app one consistent UX.
/// </summary>
public static class AppNotificationService
{
    private static readonly object Gate = new();
    private static readonly Queue<AppNotification> Queue = new();
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastShown = new(StringComparer.Ordinal);
    private static AppNotificationWindow? _active;

    public static void Show(
        string title,
        string message,
        AppNotificationKind kind = AppNotificationKind.Info,
        TimeSpan? duration = null,
        string? dedupeKey = null)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(message)) return;

        string key = dedupeKey ?? $"{kind}|{title}|{message}";
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (LastShown.TryGetValue(key, out DateTimeOffset previous) && now - previous < TimeSpan.FromSeconds(2.5))
            return;
        LastShown[key] = now;

        var notification = new AppNotification(
            string.IsNullOrWhiteSpace(title) ? "Upozornenie" : title.Trim(),
            message?.Trim() ?? string.Empty,
            kind,
            duration ?? DefaultDuration(kind));

        Application? app = Application.Current;
        if (app?.Dispatcher is null) return;
        _ = app.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => EnqueueOnUi(notification)));
    }

    public static void Info(string title, string message, string? key = null) =>
        Show(title, message, AppNotificationKind.Info, dedupeKey: key);

    public static void Success(string title, string message, string? key = null) =>
        Show(title, message, AppNotificationKind.Success, dedupeKey: key);

    public static void Warning(string title, string message, string? key = null) =>
        Show(title, message, AppNotificationKind.Warning, dedupeKey: key);

    public static void Error(string title, string message, string? key = null) =>
        Show(title, message, AppNotificationKind.Error, dedupeKey: key);

    private static void EnqueueOnUi(AppNotification notification)
    {
        lock (Gate)
        {
            Queue.Enqueue(notification);
            if (_active is not null) return;
        }
        ShowNextOnUi();
    }

    private static void ShowNextOnUi()
    {
        AppNotification next;
        lock (Gate)
        {
            if (_active is not null || Queue.Count == 0) return;
            next = Queue.Dequeue();
        }

        Window? owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
            ?? Application.Current?.MainWindow;

        var popup = new AppNotificationWindow(next, owner);
        popup.Closed += (_, _) =>
        {
            lock (Gate) _active = null;
            _ = Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ShowNextOnUi));
        };

        lock (Gate) _active = popup;
        popup.Show();
    }

    private static TimeSpan DefaultDuration(AppNotificationKind kind) => kind switch
    {
        AppNotificationKind.Success => TimeSpan.FromSeconds(3.5),
        AppNotificationKind.Info => TimeSpan.FromSeconds(4.5),
        AppNotificationKind.Warning => TimeSpan.FromSeconds(6),
        AppNotificationKind.Error => TimeSpan.FromSeconds(8),
        _ => TimeSpan.FromSeconds(4.5),
    };

    private sealed record AppNotification(string Title, string Message, AppNotificationKind Kind, TimeSpan Duration);

    private sealed class AppNotificationWindow : Window
    {
        private readonly AppNotification _notification;
        private readonly DispatcherTimer _closeTimer;
        private bool _closingAnimated;

        public AppNotificationWindow(AppNotification notification, Window? owner)
        {
            _notification = notification;
            Owner = owner is { IsVisible: true } ? owner : null;
            Width = 520;
            MaxWidth = 720;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = Owner is null;
            Focusable = false;
            Opacity = 0;
            Content = BuildContent(notification);

            Loaded += OnLoaded;
            _closeTimer = new DispatcherTimer { Interval = notification.Duration };
            _closeTimer.Tick += (_, _) => BeginClose();
        }

        private UIElement BuildContent(AppNotification notification)
        {
            (Color background, Color border, string glyph) = notification.Kind switch
            {
                AppNotificationKind.Success => (Color.FromRgb(29, 103, 73), Color.FromRgb(70, 196, 134), "✓"),
                AppNotificationKind.Warning => (Color.FromRgb(126, 82, 17), Color.FromRgb(239, 177, 67), "!"),
                AppNotificationKind.Error => (Color.FromRgb(126, 34, 43), Color.FromRgb(241, 91, 104), "×"),
                _ => (Color.FromRgb(42, 74, 126), Color.FromRgb(92, 150, 244), "i"),
            };

            var glyphText = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 19,
                Foreground = Brushes.White,
                Width = 26,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var title = new TextBlock
            {
                Text = notification.Title,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 13.5,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
            };
            var message = new TextBlock
            {
                Text = notification.Message,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(235, 239, 248)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
                Visibility = string.IsNullOrWhiteSpace(notification.Message) ? Visibility.Collapsed : Visibility.Visible,
            };
            var textStack = new StackPanel();
            textStack.Children.Add(title);
            textStack.Children.Add(message);

            var close = new Button
            {
                Content = "×",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                FontSize = 17,
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Margin = new Thickness(8, -4, -4, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Zavrieť upozornenie",
            };
            close.Click += (_, _) => BeginClose();

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(glyphText, 0);
            Grid.SetColumn(textStack, 1);
            Grid.SetColumn(close, 2);
            grid.Children.Add(glyphText);
            grid.Children.Add(textStack);
            grid.Children.Add(close);

            return new Border
            {
                Background = new SolidColorBrush(background),
                BorderBrush = new SolidColorBrush(border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 9, 10, 9),
                Child = grid,
            };
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PositionNearOwner();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            _closeTimer.Start();
        }

        private void PositionNearOwner()
        {
            Window? owner = Owner;
            if (owner is { IsVisible: true })
            {
                double width = ActualWidth > 0 ? ActualWidth : Width;
                Left = owner.Left + Math.Max(12, (owner.ActualWidth - width) / 2);
                Top = owner.Top + 48;
                return;
            }

            SystemParameters.WorkArea.ToString(); // force WorkArea initialization on UI thread
            Rect work = SystemParameters.WorkArea;
            double popupWidth = ActualWidth > 0 ? ActualWidth : Width;
            Left = work.Left + (work.Width - popupWidth) / 2;
            Top = work.Top + 48;
        }

        private void BeginClose()
        {
            if (_closingAnimated) return;
            _closingAnimated = true;
            _closeTimer.Stop();
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(150));
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        }
    }
}
