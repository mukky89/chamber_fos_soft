using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VotschVc3.Core.Notifications;

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
/// popup above the currently visible application window. Background/tray alerts remain
/// the responsibility of DesktopNotifier and are not duplicated by a floating WPF window.
/// </summary>
public static class AppNotificationService
{
    private static readonly object Gate = new();
    private static readonly List<AppNotificationWindow> Active = new();
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastShown = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> SessionOnceShown = new(StringComparer.Ordinal);
    private static EmailSettings _preferences = new();

    public static void Configure(EmailSettings preferences) =>
        _preferences = preferences ?? new EmailSettings();

    public static void Show(
        string title,
        string message,
        AppNotificationKind kind = AppNotificationKind.Info,
        TimeSpan? duration = null,
        string? dedupeKey = null)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(message)) return;
        if (!IsEnabled(kind)) return;

        // A successful device connection is persistent state already visible on the device card.
        // Suppress routine transient connection toasts for chambers and all other devices, including
        // messages prefixed by glyphs such as "🔌 Pripojené na 10.88.5.175:2049". Warning/error
        // notifications are intentionally never filtered here.
        if (kind is AppNotificationKind.Info or AppNotificationKind.Success && IsRoutineConnectedInfo(message)) return;

        string key = dedupeKey ?? $"{kind}|{title}|{message}";
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // FBG identification validation is intentionally edge-like for the operator: a periodic
        // PeakLogger/Sylex re-validation must not keep showing the same invalid-SN popup. For FBG
        // format/duplicate warnings the typed SN itself changes on every keystroke, therefore the
        // session key is based on the warning TYPE/message, not the volatile current SN text.
        string? sessionOnceKey = GetSessionOnceKey(key, message);
        if (sessionOnceKey is not null)
        {
            if (!SessionOnceShown.TryAdd(sessionOnceKey, 0)) return;
        }
        else
        {
            if (LastShown.TryGetValue(key, out DateTimeOffset previous) && now - previous < TimeSpan.FromSeconds(2.5))
                return;
            LastShown[key] = now;
        }

        var notification = new AppNotification(
            string.IsNullOrWhiteSpace(title) ? "Upozornenie" : title.Trim(),
            message?.Trim() ?? string.Empty,
            kind,
            duration ?? ConfiguredDuration(kind));

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

    private static bool IsRoutineConnectedInfo(string? message)
    {
        string value = message?.Trim() ?? string.Empty;
        if (value.Length == 0) return false;

        return value.StartsWith("Pripojené", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Pripojené na", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Pripojené:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Connected", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Connected to", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetSessionOnceKey(string key, string message)
    {
        if (key.StartsWith("fbg-sn:", StringComparison.OrdinalIgnoreCase))
            return $"fbg-sn|{message.Trim()}";
        if (key.StartsWith("sylex:", StringComparison.OrdinalIgnoreCase))
            return $"sylex|{key}";
        return null;
    }

    private static void EnqueueOnUi(AppNotification notification)
    {
        Window? owner = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive && window.IsVisible)
            ?? (Application.Current?.MainWindow is { IsVisible: true } main ? main : null);

        // If the app is hidden/minimized to tray, DesktopNotifier owns the background
        // notification UX. Do not open a separate top-most WPF popup over other programs.
        if (owner is null)
        {
            return;
        }

        var popup = new AppNotificationWindow(notification, owner);
        popup.Closed += (_, _) =>
        {
            lock (Gate) Active.Remove(popup);
            PositionActive(owner);
        };

        lock (Gate)
        {
            Active.Add(popup);
            if (Active.Count > 5) Active[0].Dismiss();
        }
        popup.Show();
        PositionActive(owner);
    }

    private static void PositionActive(Window owner)
    {
        AppNotificationWindow[] windows;
        lock (Gate) windows = Active.Where(window => ReferenceEquals(window.Owner, owner) && window.IsVisible).ToArray();
        Rect area = GetNotificationArea(owner);
        double top = area.Top;
        for (int index = windows.Length - 1; index >= 0; index--)
        {
            AppNotificationWindow window = windows[index];
            if (top >= area.Bottom - 48) { window.Dismiss(); continue; }
            window.Position(owner, top);
            top += window.ActualHeight + 10;
        }
    }

    // Window.Left/Top can describe the restored window while maximized. Use the actual
    // client origin and the current monitor work area, converting pixels to WPF units.
    private static Rect GetNotificationArea(Window owner)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(owner).Handle;
        var screen = System.Windows.Forms.Screen.FromHandle(handle);
        var transform = PresentationSource.FromVisual(owner)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var work = screen.WorkingArea;
        Point screenStart = transform.Transform(new Point(work.Left, work.Top));
        Point screenEnd = transform.Transform(new Point(work.Right, work.Bottom));
        var area = new Rect(screenStart, screenEnd);
        if (owner.IsVisible && PresentationSource.FromVisual(owner) is not null)
        {
            Point origin = transform.Transform(owner.PointToScreen(new Point(0, 0)));
            var visibleOwner = new Rect(origin, new Size(Math.Max(1, owner.ActualWidth), Math.Max(1, owner.ActualHeight)));
            visibleOwner.Intersect(area);
            if (!visibleOwner.IsEmpty && visibleOwner.Width >= 160 && visibleOwner.Height >= 100)
                area = visibleOwner;
        }
        area.Inflate(-12, -12);
        return area;
    }
    private static TimeSpan DefaultDuration(AppNotificationKind kind) => kind switch
    {
        AppNotificationKind.Success => TimeSpan.FromSeconds(3.5),
        AppNotificationKind.Info => TimeSpan.FromSeconds(4.5),
        AppNotificationKind.Warning => TimeSpan.FromSeconds(6),
        AppNotificationKind.Error => TimeSpan.FromSeconds(8),
        _ => TimeSpan.FromSeconds(4.5),
    };

    private static bool IsEnabled(AppNotificationKind kind) => kind switch
    {
        AppNotificationKind.Info => _preferences.InfoPopupsEnabled,
        AppNotificationKind.Success => _preferences.SuccessPopupsEnabled,
        AppNotificationKind.Warning => _preferences.WarningPopupsEnabled,
        AppNotificationKind.Error => _preferences.ErrorPopupsEnabled,
        _ => true,
    };

    private static TimeSpan ConfiguredDuration(AppNotificationKind kind)
    {
        int seconds = kind switch
        {
            AppNotificationKind.Info => _preferences.InfoPopupSeconds,
            AppNotificationKind.Success => _preferences.SuccessPopupSeconds,
            AppNotificationKind.Warning => _preferences.WarningPopupSeconds,
            AppNotificationKind.Error => _preferences.ErrorPopupSeconds,
            _ => (int)DefaultDuration(kind).TotalSeconds,
        };
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 2, 120));
    }

    private sealed record AppNotification(string Title, string Message, AppNotificationKind Kind, TimeSpan Duration);

    private sealed class AppNotificationWindow : Window
    {
        private readonly DispatcherTimer _closeTimer;
        private readonly DispatcherTimer _countdownTimer;
        private readonly DateTimeOffset _expiresAt;
        private readonly TextBlock _countdown = new();
        private readonly ProgressBar _progress = new();
        private readonly AppNotification _notification;
        private bool _closingAnimated;

        public AppNotificationWindow(AppNotification notification, Window owner)
        {
            _notification = notification;
            Owner = owner;
            Width = 410;
            MaxWidth = 520;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            Opacity = 0;
            Content = new ScrollViewer { Content = BuildContent(notification), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

            Loaded += OnLoaded;
            SizeChanged += (_, _) => { if (IsLoaded) PositionActive(Owner); };
            owner.LocationChanged += OwnerMoved;
            owner.SizeChanged += OwnerMoved;
            _closeTimer = new DispatcherTimer { Interval = notification.Duration };
            _closeTimer.Tick += (_, _) => BeginClose();
            _expiresAt = DateTimeOffset.UtcNow + notification.Duration;
            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += (_, _) => UpdateCountdown();
            Closed += (_, _) =>
            {
                owner.LocationChanged -= OwnerMoved;
                owner.SizeChanged -= OwnerMoved;
                _countdownTimer.Stop();
            };
        }

        private static UIElement CreateIcon(string data, Brush color, double size)
        {
            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data), Stroke = color, StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            });
            return new Viewbox
            {
                Width = size, Height = size, Child = canvas,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        private UIElement BuildContent(AppNotification notification)
        {
            (Color background, Color border, string symbol) = notification.Kind switch
            {
                AppNotificationKind.Success => (Color.FromRgb(22, 48, 40), Color.FromRgb(76, 217, 151), "M5,12 L10,17 L19,7"),
                AppNotificationKind.Warning => (Color.FromRgb(53, 43, 26), Color.FromRgb(255, 195, 77), "M12,3 L22,21 L2,21 Z M12,9 L12,14 M12,17 L12,17.2"),
                AppNotificationKind.Error => (Color.FromRgb(57, 30, 37), Color.FromRgb(255, 112, 129), "M8,2 L16,2 L22,8 L22,16 L16,22 L8,22 L2,16 L2,8 Z M12,7 L12,13 M12,17 L12,17.2"),
                _ => (Color.FromRgb(26, 40, 61), Color.FromRgb(109, 176, 255), "M12,2 A10,10 0 1 1 11.99,2 M12,11 L12,17 M12,7 L12,7.2"),
            };

            var glyphText = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(35, border.R, border.G, border.B)),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = CreateIcon(symbol, new SolidColorBrush(border), 24),
            };
            System.Windows.Automation.AutomationProperties.SetName(glyphText, notification.Kind switch
            {
                AppNotificationKind.Error => "Alarm",
                AppNotificationKind.Warning => "Varovanie",
                AppNotificationKind.Success => "Úspech",
                _ => "Informácia",
            });
            var title = new TextBlock
            {
                Text = notification.Title.TrimStart('⚠', '✓', '✔', 'ℹ', '!', '×', ' ', '\uFE0F'),
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
                Content = CreateIcon("M6,6 L18,18 M18,6 L6,18", Brushes.White, 14),
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
            System.Windows.Automation.AutomationProperties.SetName(close, "Zavrieť upozornenie");
            close.Click += (_, _) => BeginClose();

            var copy = new Button
            {
                Content = CreateIcon("M8,8 L20,8 L20,21 L8,21 Z M16,8 L16,3 L3,3 L3,16 L8,16", Brushes.White, 16), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = Brushes.White, FontSize = 15, Width = 28, Height = 28, Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Top, Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Kopírovať upozornenie",
            };
            System.Windows.Automation.AutomationProperties.SetName(copy, "Kopírovať upozornenie");
            copy.Click += async (_, _) =>
            {
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        Clipboard.SetText($"{notification.Title}\r\n{notification.Message}".Trim());
                        return;
                    }
                    catch (System.Runtime.InteropServices.COMException)
                    {
                        await Task.Delay(100);
                    }
                }
                copy.ToolTip = "Schránka je práve obsadená. Skúste kopírovanie znova.";
            };

            _countdown.Foreground = new SolidColorBrush(Color.FromRgb(235, 239, 248));
            _countdown.FontSize = 10.5;
            _countdown.HorizontalAlignment = HorizontalAlignment.Right;
            _countdown.Margin = new Thickness(0, 5, 2, 2);
            _progress.Height = 8;
            _progress.Minimum = 0;
            _progress.Maximum = notification.Duration.TotalSeconds;
            _progress.Value = notification.Duration.TotalSeconds;
            _progress.Foreground = new SolidColorBrush(border);
            _progress.Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(glyphText, 0);
            Grid.SetColumn(textStack, 1);
            Grid.SetColumn(copy, 2);
            Grid.SetColumn(close, 3);
            grid.Children.Add(glyphText);
            grid.Children.Add(textStack);
            grid.Children.Add(copy);
            grid.Children.Add(close);
            Grid.SetRow(_countdown, 1); Grid.SetColumn(_countdown, 1); Grid.SetColumnSpan(_countdown, 3);
            Grid.SetRow(_progress, 2); Grid.SetColumn(_progress, 0); Grid.SetColumnSpan(_progress, 4);
            grid.Children.Add(_countdown);
            grid.Children.Add(_progress);

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
            PositionActive(Owner);
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            _closeTimer.Start();
            _countdownTimer.Start();
            UpdateCountdown();
            _progress.BeginAnimation(ProgressBar.ValueProperty,
                new DoubleAnimation(_notification.Duration.TotalSeconds, 0, _notification.Duration));
        }

        public void Position(Window owner, double top)
        {
            Rect area = GetNotificationArea(owner);
            Width = Math.Min(410, area.Width);
            MaxHeight = Math.Max(1, area.Bottom - Math.Max(area.Top, top));
            Left = area.Right - Width;
            Top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - Math.Min(ActualHeight, MaxHeight)));
        }
        public void Dismiss() => BeginClose();

        private void OwnerMoved(object? sender, EventArgs e) => PositionActive(Owner);

        private void UpdateCountdown()
        {
            int seconds = Math.Max(0, (int)Math.Ceiling((_expiresAt - DateTimeOffset.UtcNow).TotalSeconds));
            _countdown.Text = $"{seconds} s";
        }

        private void BeginClose()
        {
            if (_closingAnimated) return;
            _closingAnimated = true;
            _closeTimer.Stop();
            _countdownTimer.Stop();
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(150));
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        }
    }
}
