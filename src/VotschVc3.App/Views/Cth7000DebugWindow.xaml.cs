using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VotschVc3.App.Thermometers;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.Views;

public partial class Cth7000DebugWindow : Window
{
    private readonly ThermometerDeviceViewModel _device;
    private readonly Cth7000RawDebugSession _session;
    private bool _closeInProgress;
    private bool _closeCleanupCompleted;
    private bool _busy;

    public Cth7000DebugWindow(ThermometerDeviceViewModel device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _session = new Cth7000RawDebugSession(device.PortName);
        InitializeComponent();

        DeviceText.Text = $"{device.Display} · port {device.PortName} · normálny klient: {device.ConnectionState}";
        Append("INFO", "Debug okno otvorené. Žiadny príkaz nebol odoslaný. Nastav parametre a klikni „Otvoriť COM“.");
        Append("INFO", "Pred otvorením RAW COM automaticky zastavím normálny polling/connection na tomto porte, aby sa komunikácie nemiešali.");
        Closing += OnClosing;
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        await RunUiOperationAsync(async () =>
        {
            await ReleaseNormalClientAsync();
            Cth7000RawSerialSettings settings = ReadSettings();
            Append("INFO", $"OPEN {settings.Describe()}");
            await _session.OpenAsync(settings);
            Append("OK", $"{_session.PortName} otvorený. 0 automatických TX bajtov.");
            StatusText.Text = $"RAW COM otvorený · {settings.Describe()}";
            UpdateOpenState();
        });
    }

    private async Task ReleaseNormalClientAsync()
    {
        if (!_device.IsConnected) return;

        Append("INFO", $"Normálny klient {_device.PortName} je pripojený — zastavujem polling a odpájam ho pred RAW session.");
        if (_device.DisconnectCommand.CanExecute(null))
        {
            _device.DisconnectCommand.Execute(null);
        }

        var sw = Stopwatch.StartNew();
        while (_device.IsConnected && sw.Elapsed < TimeSpan.FromSeconds(6))
        {
            await Task.Delay(50);
        }

        if (_device.IsConnected)
        {
            throw new IOException($"Normálny klient neuvoľnil {_device.PortName} do 6 s. RAW debug port neotvorím, aby sa komunikácie nemiešali.");
        }

        // Let SerialPort.Dispose/process lease complete before the dedicated raw client acquires it.
        await Task.Delay(150);
        Append("OK", "Normálny klient odpojený; RAW debug môže vlastniť port exkluzívne.");
    }

    private void PaliPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _session.IsOpen) return;

        SelectComboValue(BaudCombo, "9600");
        DtrCheck.IsChecked = true;
        RtsCheck.IsChecked = true;
        SelectComboValue(TerminatorCombo, "CR");
        SelectComboValue(InterDelayCombo, "25");
        SelectComboValue(TimeoutCombo, "8000");
        PurgeBeforeSendCheck.IsChecked = true;

        Append("PRESET", "AutoOptical/Pali: 9600 bd, 8N1, flow=None, CR, 25 ms medzi každým bajtom. Pôvodný driver po open posielal SYSTEM:REMOTE a až potom MEASURE:CHANNEL?.");
        Append("INFO", "Pre porovnávací test po fyzickom resete: Open → SYSTEM:REMOTE → počkaj aspoň 1 s → MEASURE:CHANNEL? 1 → SYSTEM:LOCAL. Pred týmto testom neposielaj *IDN?.");
        StatusText.Text = "Pali / AutoOptical preset nastavený · 25 ms pacing.";
    }

    private async void Purge_Click(object sender, RoutedEventArgs e) =>
        await RunExchangeAsync(() => _session.PurgeAsync());

    private async void Listen_Click(object sender, RoutedEventArgs e) =>
        await RunExchangeAsync(() => _session.ListenAsync(2000));

    private async void Idn_Click(object sender, RoutedEventArgs e) =>
        await SendPresetAsync("*IDN?", expectResponse: true);

    private async void Remote_Click(object sender, RoutedEventArgs e) =>
        await SendPresetAsync("SYSTEM:REMOTE", expectResponse: false);

    private async void MeasureA_Click(object sender, RoutedEventArgs e) =>
        await SendPresetAsync("MEASURE:CHANNEL? 1", expectResponse: true);

    private async void MeasureB_Click(object sender, RoutedEventArgs e) =>
        await SendPresetAsync("MEASURE:CHANNEL? 2", expectResponse: true);

    private async void Local_Click(object sender, RoutedEventArgs e) =>
        await SendPresetAsync("SYSTEM:LOCAL", expectResponse: false);

    private async void CustomSend_Click(object sender, RoutedEventArgs e)
    {
        string command = CustomCommandBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(command)) return;
        await SendPresetAsync(command, CustomExpectResponseCheck.IsChecked == true);
    }

    private async Task SendPresetAsync(string command, bool expectResponse)
    {
        bool purge = PurgeBeforeSendCheck.IsChecked == true && expectResponse;
        await RunExchangeAsync(() => _session.SendCommandAsync(command, expectResponse, purge));
    }

    private async void Emergency_Click(object sender, RoutedEventArgs e)
    {
        await RunExchangeAsync(() => _session.EmergencyLocalAndCloseAsync(), updateOpenStateAfter: true);
        StatusText.Text = "Emergency LOCAL dokončený; COM je zavretý.";
    }

    private async void HardClose_Click(object sender, RoutedEventArgs e)
    {
        await RunExchangeAsync(() => _session.HardCloseAsync(), updateOpenStateAfter: true);
        StatusText.Text = "COM zavretý bez ďalšieho TX.";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(TranscriptBox.Text);
            StatusText.Text = "RAW transcript skopírovaný do schránky.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Kopírovanie zlyhalo: {ex.Message}";
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        TranscriptBox.Clear();
        Append("INFO", "Transcript vyčistený.");
    }

    private async Task RunExchangeAsync(Func<Task<Cth7000RawExchange>> action, bool updateOpenStateAfter = false)
    {
        await RunUiOperationAsync(async () =>
        {
            Cth7000RawExchange exchange = await action();
            AppendExchange(exchange);
            if (updateOpenStateAfter) UpdateOpenState();
        });
    }

    private async Task RunUiOperationAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        UpdateEnabledState();
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Append("ERROR", $"{ex.GetType().Name}: {ex.Message}");
            StatusText.Text = $"Chyba: {ex.Message}";
            AppLog.Error("CTH7000 RAW", $"{_device.PortName}: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _busy = false;
            UpdateOpenState();
        }
    }

    private Cth7000RawSerialSettings ReadSettings()
    {
        int baud = ReadComboInt(BaudCombo, 9600);
        int delay = ReadComboInt(InterDelayCombo, 2);
        int timeout = Math.Clamp(ReadComboInt(TimeoutCombo, 8000), 100, 60000);
        string terminator = ReadComboText(TerminatorCombo, "CR");
        return new Cth7000RawSerialSettings(
            baud,
            DtrCheck.IsChecked == true,
            RtsCheck.IsChecked == true,
            terminator,
            Math.Clamp(delay, 0, 100),
            timeout);
    }

    private static int ReadComboInt(ComboBox box, int fallback) =>
        int.TryParse(ReadComboText(box, fallback.ToString()), out int value) ? value : fallback;

    private static string ReadComboText(ComboBox box, string fallback) =>
        box.SelectedItem is ComboBoxItem { Content: not null } item ? item.Content.ToString() ?? fallback : fallback;

    private static void SelectComboValue(ComboBox box, string value)
    {
        foreach (object item in box.Items)
        {
            if (item is ComboBoxItem { Content: not null } comboItem &&
                string.Equals(comboItem.Content.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = comboItem;
                return;
            }
        }
    }

    private void AppendExchange(Cth7000RawExchange exchange)
    {
        string elapsed = exchange.Elapsed == TimeSpan.Zero ? "" : $" · {exchange.Elapsed.TotalMilliseconds:F0} ms";
        if (exchange.Tx.Length > 0)
        {
            Append("TX ASCII", Cth7000RawDebugSession.ToVisibleAscii(exchange.Tx));
            Append("TX HEX", Cth7000RawDebugSession.ToHex(exchange.Tx));
        }
        if (exchange.Rx.Length > 0)
        {
            Append("RX ASCII", Cth7000RawDebugSession.ToVisibleAscii(exchange.Rx));
            Append("RX HEX", Cth7000RawDebugSession.ToHex(exchange.Rx));
        }
        else if (exchange.Command != "(info)" && exchange.Tx.Length > 0)
        {
            Append(exchange.TimedOut ? "RX TIMEOUT" : "RX", $"0 B{elapsed}");
        }
        if (!string.IsNullOrWhiteSpace(exchange.Note))
        {
            Append(exchange.TimedOut ? "WARN" : "INFO", exchange.Note + elapsed);
        }
        if (exchange.Rx.Length > 0)
        {
            Append("RESULT", $"RX={exchange.Rx.Length} B{elapsed}; timeout={exchange.TimedOut}");
        }
    }

    private void Append(string kind, string text)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff}  {kind,-10}  {text}";
        TranscriptBox.AppendText(line + Environment.NewLine);
        TranscriptBox.ScrollToEnd();
        AppLog.Info("CTH7000 RAW UI", $"{_device.PortName}: {kind}: {text}");
    }

    private void UpdateOpenState()
    {
        bool open = _session.IsOpen;
        OpenButton.IsEnabled = !_busy && !open;
        PaliPresetButton.IsEnabled = !_busy && !open;
        PurgeButton.IsEnabled = !_busy && open;
        ListenButton.IsEnabled = !_busy && open;
        IdnButton.IsEnabled = !_busy && open;
        RemoteButton.IsEnabled = !_busy && open;
        MeasureAButton.IsEnabled = !_busy && open;
        MeasureBButton.IsEnabled = !_busy && open;
        LocalButton.IsEnabled = !_busy && open;
        EmergencyButton.IsEnabled = !_busy && open;
        HardCloseButton.IsEnabled = !_busy && open;
        CustomSendButton.IsEnabled = !_busy && open;

        BaudCombo.IsEnabled = !open && !_busy;
        DtrCheck.IsEnabled = !open && !_busy;
        RtsCheck.IsEnabled = !open && !_busy;
        TerminatorCombo.IsEnabled = !open && !_busy;
        InterDelayCombo.IsEnabled = !open && !_busy;
        TimeoutCombo.IsEnabled = !open && !_busy;
    }

    private void UpdateEnabledState()
    {
        OpenButton.IsEnabled = false;
        PaliPresetButton.IsEnabled = false;
        PurgeButton.IsEnabled = false;
        ListenButton.IsEnabled = false;
        IdnButton.IsEnabled = false;
        RemoteButton.IsEnabled = false;
        MeasureAButton.IsEnabled = false;
        MeasureBButton.IsEnabled = false;
        LocalButton.IsEnabled = false;
        EmergencyButton.IsEnabled = false;
        HardCloseButton.IsEnabled = false;
        CustomSendButton.IsEnabled = false;
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // The second Close() is intentionally allowed only after async COM cleanup has
        // completed and has been posted to a later Dispatcher turn. Calling Close() directly
        // from this async Closing continuation can hit Window.VerifyNotClosing().
        if (_closeCleanupCompleted)
        {
            return;
        }

        e.Cancel = true;
        if (_closeInProgress)
        {
            return;
        }

        _closeInProgress = true;
        UpdateEnabledState();
        try
        {
            if (_session.IsOpen)
            {
                Append("INFO", "Okno sa zatvára — best-effort SYSTEM:LOCAL + close.");
                Cth7000RawExchange exchange = await _session.EmergencyLocalAndCloseAsync();
                AppendExchange(exchange);
            }
            await _session.DisposeAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("CTH7000 RAW", $"{_device.PortName}: shutdown debug session FAILED: {ex.Message}");
        }
        finally
        {
            _closeCleanupCompleted = true;
            _closeInProgress = false;

            // Do not call Close() inline while WPF is unwinding the original Closing event.
            // Posting it ensures the first close has been fully cancelled before the final close.
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
            {
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Normal,
                    new Action(() =>
                    {
                        try
                        {
                            Close();
                        }
                        catch (InvalidOperationException ex)
                        {
                            AppLog.Warn(
                                "CTH7000 RAW",
                                $"{_device.PortName}: deferred Close preskočený: {ex.Message}");
                        }
                    }));
            }
        }
    }
}
