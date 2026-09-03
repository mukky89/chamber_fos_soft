using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;
using VotschVc3.Core.Diagnostics;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.App.Thermometers;

/// <summary>
/// Serial client for the WIKA CTH7000 on a USB virtual COM port.
/// Every channel measurement is an atomic SCPI session:
/// *IDN? (once) -> SYSTEM:REMOTE -> MEASURE:CHANNEL? -> SYSTEM:LOCAL.
/// SYSTEM:LOCAL is attempted from a finally block so the front panel is not left locked
/// after a timeout, cancellation, parser failure or disconnect.
/// </summary>
public sealed class F100Client : IAsyncDisposable
{
    private enum CommunicationMode { Unknown, Query }

    private readonly SerialPort _port;
    private readonly bool _allowQueryFallback;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CommunicationMode _communicationMode;
    private bool _queryInstrumentIdentified;
    private bool _remoteActive;
    private SerialPortLease? _portLease;
    private int _disposeStarted;

    public F100Client(
        string portName,
        int baudRate = F100Protocol.DefaultBaudRate,
        bool allowQueryFallback = false)
    {
        _allowQueryFallback = allowQueryFallback;
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = 8000,
            WriteTimeout = 2000,
            DtrEnable = true,
            RtsEnable = true,
            Encoding = Encoding.ASCII,
        };

        AppLog.Info(
            "CTH7000 USB",
            $"Client vytvorený: {portName} @ {baudRate} bd, 8N1, Handshake=None, " +
            "DTR=True, RTS=True, ASCII, ReadTimeout=8000 ms, WriteTimeout=2000 ms.");
    }

    public bool IsOpen => _port.IsOpen;
    public string InstrumentIdentity { get; private set; } = string.Empty;
    public string PortName => _port.PortName;

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposing();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposing();
            if (_port.IsOpen)
            {
                AppLog.Info("CTH7000 USB", $"{PortName}: OpenAsync – port už je otvorený.");
                return;
            }

            AppLog.Info("CTH7000 USB", $"{PortName}: pokus o otvorenie portu.");
            SerialPortLease? lease = null;
            try
            {
                lease = await SerialPortLease.AcquireAsync(_port.PortName, cancellationToken)
                    .ConfigureAwait(false);
                AppLog.Info("CTH7000 USB", $"{PortName}: získaný process-wide COM lease.");

                await Task.Run(() =>
                {
                    _port.Open();
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                }).ConfigureAwait(false);

                AppLog.Info(
                    "CTH7000 USB",
                    $"{PortName}: SerialPort.Open OK; RX/TX buffre vyčistené. " +
                    "Čakám 350 ms na inicializáciu WIKA.");

                await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken)
                    .ConfigureAwait(false);
                if (_port.IsOpen) _port.DiscardInBuffer();

                _portLease = lease;
                lease = null;
                ResetProtocolState();
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLog.Error(
                    "CTH7000 USB",
                    $"{PortName}: prístup k COM portu odmietnutý – port je pravdepodobne obsadený. {ex.Message}");
                throw new SerialPortBusyException(_port.PortName, ex);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                AppLog.Error(
                    "CTH7000 USB",
                    $"{PortName}: OpenAsync zlyhalo – {ex.GetType().Name}: {ex.Message}");
                throw;
            }
            finally
            {
                lease?.Dispose();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> IdentifyInstrumentAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposing();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposing();
            EnsurePortOpen();
            return await IdentifyUnderGateAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfDisposing();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposing();
            EnsurePortOpen();
            WriteCommandLogged(command, "command");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            AppLog.Error(
                "CTH7000 USB",
                $"{PortName}: SendAsync '{command}' FAILED → {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> SendReceiveAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfDisposing();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposing();
            EnsurePortOpen();
            return await SendReceiveWithRetryUnderGateAsync(command, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ThermometerReading> ReadAsync(
        string readCommand,
        CancellationToken cancellationToken = default)
    {
        AppLog.Info("CTH7000 USB", $"{PortName}: ReadAsync '{readCommand}'.");
        string response = await SendReceiveAsync(readCommand, cancellationToken).ConfigureAwait(false);
        ThermometerReading reading = F100Protocol.ParseReading(response);
        AppLog.Info(
            "CTH7000 USB",
            $"{PortName}: ParseReading → temperature={reading.Temperature?.ToString() ?? "null"}, " +
            $"unit='{reading.Unit}', raw='{FormatLog(response)}'.");
        return reading;
    }

    /// <summary>
    /// Reads one CTH7000 input and guarantees a best-effort SYSTEM:LOCAL before returning.
    /// This method is safe for continuous polling; it does not leave the physical front panel
    /// in REMOTE between samples.
    /// </summary>
    public async Task<ThermometerReading> ReadChannelAsync(
        string channel,
        string fallbackReadCommand = F100Protocol.DefaultReadCommand,
        CancellationToken cancellationToken = default)
    {
        _ = fallbackReadCommand; // retained for source compatibility; CTH7000 never sends READ?.
        string normalized = F100Protocol.NormalizeChannel(channel);

        ThrowIfDisposing();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposing();
            EnsurePortOpen();
            AppLog.Info(
                "CTH7000 USB",
                $"{PortName}: ReadChannel START channel={normalized}, mode={_communicationMode}.");

            if (!_queryInstrumentIdentified)
            {
                string identity = await IdentifyUnderGateAsync(cancellationToken).ConfigureAwait(false);
                if (!_queryInstrumentIdentified && !_allowQueryFallback)
                {
                    AppLog.Error(
                        "CTH7000 USB",
                        $"{PortName}: *IDN? nevrátilo podporovaný CTH7000. Identity='{identity.Trim()}'.");
                    return new ThermometerReading(
                        DateTimeOffset.Now,
                        null,
                        string.Empty,
                        $"{PortName}: WIKA CTH7000 neodpovedal na *IDN?.");
                }
            }

            Exception? last = null;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    if (!_queryInstrumentIdentified)
                    {
                        await IdentifyUnderGateAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (!_queryInstrumentIdentified && !_allowQueryFallback)
                    {
                        return new ThermometerReading(
                            DateTimeOffset.Now,
                            null,
                            string.Empty,
                            $"{PortName}: zariadenie neodpovedalo ako WIKA CTH7000.");
                    }

                    await EnterRemoteUnderGateAsync(cancellationToken).ConfigureAwait(false);

                    string measureCommand = F100Protocol.BuildMeasureChannelCommand(normalized);
                    AppLog.Info(
                        "CTH7000 USB",
                        $"{PortName}: meriam kanál {normalized} → '{measureCommand}' [pokus {attempt}/2].");

                    string response = SendReceiveOnceUnderGate(measureCommand, cancellationToken, $"measure {attempt}/2");
                    ThermometerReading reading = F100Protocol.ParseReading(response);
                    AppLog.Info(
                        "CTH7000 USB",
                        $"{PortName}: výsledok kanál {normalized} → " +
                        $"temperature={reading.Temperature?.ToString() ?? "null"}, " +
                        $"unit='{reading.Unit}', raw='{FormatLog(response)}'.");
                    return reading;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
                {
                    last = ex;
                    AppLog.Error(
                        "CTH7000 USB",
                        $"{PortName}: meranie kanála {normalized}, pokus {attempt}/2 FAILED → " +
                        $"{ex.GetType().Name}: {ex.Message}");

                    if (attempt == 2) break;

                    // Release REMOTE before touching the COM handle. If the instrument has
                    // already disappeared this is harmless and the reconnect continues.
                    TryReturnToLocalUnderGate();
                    AppLog.Warn(
                        "CTH7000 USB",
                        $"{PortName}: dočasný USB výpadok, pred retry robím bezpečný reconnect.");
                    await ReopenUnderGateAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    // Critical lifecycle guarantee: never intentionally keep the CTH7000
                    // front panel in REMOTE after a measurement attempt.
                    TryReturnToLocalUnderGate();
                }
            }

            throw last ?? new IOException($"USB čítanie {PortName} zlyhalo.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(string Channel, ThermometerReading Reading)> ReadAvailableChannelAsync(
        string preferredChannel,
        string fallbackReadCommand = F100Protocol.DefaultReadCommand,
        CancellationToken cancellationToken = default)
    {
        string preferred = F100Protocol.NormalizeChannel(preferredChannel) == "B" ? "B" : "A";
        AppLog.Info("CTH7000 USB", $"{PortName}: ReadAvailableChannel preferred={preferred}.");

        ThermometerReading first = await ReadChannelAsync(
            preferred,
            fallbackReadCommand,
            cancellationToken).ConfigureAwait(false);
        if (first.Temperature is not null) return (preferred, first);

        string alternate = preferred == "A" ? "B" : "A";
        AppLog.Warn(
            "CTH7000 USB",
            $"{PortName}: kanál {preferred} nedal teplotu, skúšam alternatívu {alternate}.");

        ThermometerReading second = await ReadChannelAsync(
            alternate,
            fallbackReadCommand,
            cancellationToken).ConfigureAwait(false);
        return second.Temperature is not null ? (alternate, second) : (preferred, first);
    }

    public async Task ReturnToLocalIfSupportedAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposeStarted) != 0) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_remoteActive || !_port.IsOpen) return;
            SendLocalUnderGate(throwOnError: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> IdentifyUnderGateAsync(CancellationToken cancellationToken)
    {
        AppLog.Info(
            "CTH7000 USB",
            $"{PortName}: identifikácia START (*IDN?, inter-character {F100Protocol.InterCharacterDelayMs} ms).");

        try
        {
            string identity = SendReceiveOnceUnderGate(
                F100Protocol.IdentifyCommand,
                cancellationToken,
                "identify");
            InstrumentIdentity = identity.Trim();
            _queryInstrumentIdentified = IsSupportedQueryInstrument(identity);
            _communicationMode = _queryInstrumentIdentified
                ? CommunicationMode.Query
                : CommunicationMode.Unknown;

            AppLog.Info(
                "CTH7000 USB",
                $"{PortName}: identifikácia OK → '{InstrumentIdentity}', query={_queryInstrumentIdentified}.");
            return identity;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
        {
            AppLog.Error(
                "CTH7000 USB",
                $"{PortName}: identifikácia FAILED → {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    private async Task EnterRemoteUnderGateAsync(CancellationToken cancellationToken)
    {
        if (_remoteActive) return;
        EnsurePortOpen();
        AppLog.Info("CTH7000 USB", $"{PortName}: SYSTEM:REMOTE.");
        WriteCommandLogged(F100Protocol.RemoteCommand, "remote");
        _remoteActive = true;
        await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken).ConfigureAwait(false);
    }

    private void TryReturnToLocalUnderGate()
    {
        if (!_remoteActive) return;
        try
        {
            SendLocalUnderGate(throwOnError: false);
        }
        catch
        {
            // SendLocalUnderGate(false) already logs. Measurement errors must not be hidden
            // by a secondary LOCAL error.
        }
    }

    private void SendLocalUnderGate(bool throwOnError)
    {
        if (!_remoteActive) return;
        try
        {
            if (_port.IsOpen)
            {
                AppLog.Info("CTH7000 USB", $"{PortName}: SYSTEM:LOCAL.");
                WriteCommandLogged(F100Protocol.LocalCommand, "local");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            AppLog.Error(
                "CTH7000 USB",
                $"{PortName}: SYSTEM:LOCAL FAILED → {ex.GetType().Name}: {ex.Message}");
            if (throwOnError) throw;
        }
        finally
        {
            _remoteActive = false;
        }
    }

    private async Task<string> SendReceiveWithRetryUnderGateAsync(
        string command,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                return SendReceiveOnceUnderGate(command, cancellationToken, $"query {attempt}/2");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
            {
                last = ex;
                AppLog.Error(
                    "CTH7000 USB",
                    $"{PortName}: príkaz '{command}' pokus {attempt}/2 FAILED → " +
                    $"{ex.GetType().Name}: {ex.Message}");
                if (attempt == 2) break;
                await ReopenUnderGateAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        throw last ?? new IOException($"USB čítanie {PortName} zlyhalo.");
    }

    private string SendReceiveOnceUnderGate(
        string command,
        CancellationToken cancellationToken,
        string context)
    {
        EnsurePortOpen();
        _port.DiscardInBuffer();
        string frame = F100Protocol.Frame(command);
        AppLog.Info(
            "CTH7000 USB TX",
            $"{PortName} [{context}]: {FormatLog(frame)} " +
            $"[pacing={F100Protocol.InterCharacterDelayMs} ms/char]");

        WriteCommand(frame);
        string response = ReadLine(cancellationToken);
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new TimeoutException(
                $"WIKA CTH7000 na {PortName} neposlal odpoveď na '{command}' v časovom limite.");
        }

        AppLog.Info("CTH7000 USB RX", $"{PortName} [{context}]: {FormatLog(response)}");
        return response;
    }

    private void WriteCommandLogged(string command, string context)
    {
        string frame = F100Protocol.Frame(command);
        AppLog.Info(
            "CTH7000 USB TX",
            $"{PortName} [{context}]: {FormatLog(frame)} " +
            $"[pacing={F100Protocol.InterCharacterDelayMs} ms/char]");
        WriteCommand(frame);
    }

    private async Task ReopenUnderGateAsync(CancellationToken cancellationToken)
    {
        try
        {
            AppLog.Warn("CTH7000 USB", $"{PortName}: reconnect START.");
            _remoteActive = false;

            if (_port.IsOpen)
            {
                try { _port.DiscardInBuffer(); } catch { }
                try { _port.DiscardOutBuffer(); } catch { }
                try { _port.Close(); } catch { }
            }

            await Task.Run(() =>
            {
                _port.Open();
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();
            }).ConfigureAwait(false);

            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
            if (_port.IsOpen) _port.DiscardInBuffer();
            ResetProtocolState();
            AppLog.Info("CTH7000 USB", $"{PortName}: reconnect DONE.");
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error(
                "CTH7000 USB",
                $"{PortName}: reconnect FAILED – COM port obsadený. {ex.Message}");
            throw new SerialPortBusyException(_port.PortName, ex);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            AppLog.Error(
                "CTH7000 USB",
                $"{PortName}: reconnect FAILED → {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    private void WriteCommand(string frame)
    {
        foreach (char c in frame)
        {
            EnsurePortOpen();
            _port.Write(c.ToString());
            if (F100Protocol.InterCharacterDelayMs > 0)
            {
                Thread.Sleep(F100Protocol.InterCharacterDelayMs);
            }
        }
    }

    private string ReadLine(CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();
        const int pollDelayMs = 25;
        const int overallTimeoutMs = 8000;

        while (stopwatch.ElapsedMilliseconds < overallTimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsurePortOpen();

            string chunk = _port.ReadExisting();
            if (!string.IsNullOrEmpty(chunk))
            {
                builder.Append(chunk);
                if (builder.ToString().Contains('\r') || builder.ToString().Contains('\n'))
                {
                    break;
                }
            }

            Thread.Sleep(pollDelayMs);
        }

        string result = builder.ToString().TrimEnd('\r', '\n');
        AppLog.Info(
            "CTH7000 USB RX",
            $"{PortName}: ReadExisting dokončené po {stopwatch.ElapsedMilliseconds} ms, " +
            $"bytes={Encoding.ASCII.GetByteCount(result)}, raw='{FormatLog(result)}'.");
        return result;
    }

    private void ResetProtocolState()
    {
        _communicationMode = CommunicationMode.Unknown;
        _queryInstrumentIdentified = false;
        _remoteActive = false;
        InstrumentIdentity = string.Empty;
    }

    private static bool IsSupportedQueryInstrument(string identity) =>
        !string.IsNullOrWhiteSpace(identity) &&
        !F100Protocol.IsErrorResponse(identity) &&
        identity.Contains("CTH7000", StringComparison.OrdinalIgnoreCase);

    private static string FormatLog(string value) => value
        .Replace("\r", "<CR>", StringComparison.Ordinal)
        .Replace("\n", "<LF>", StringComparison.Ordinal);

    private void EnsurePortOpen()
    {
        if (!_port.IsOpen)
        {
            throw new IOException($"COM port {PortName} nie je otvorený.");
        }
    }

    private void ThrowIfDisposing()
    {
        if (Volatile.Read(ref _disposeStarted) != 0)
        {
            throw new ObjectDisposedException(nameof(F100Client));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            AppLog.Info("CTH7000 USB", $"{PortName}: DisposeAsync START.");

            // Do not call the public ReturnToLocalIfSupportedAsync here: disposal has already
            // started. Send the command directly while we still own the serial gate.
            TryReturnToLocalUnderGate();

            try
            {
                if (_port.IsOpen) _port.Close();
            }
            catch (Exception ex)
            {
                AppLog.Warn(
                    "CTH7000 USB",
                    $"{PortName}: Close počas Dispose zlyhal – {ex.GetType().Name}: {ex.Message}");
            }

            _port.Dispose();
            _portLease?.Dispose();
            _portLease = null;
            AppLog.Info("CTH7000 USB", $"{PortName}: DisposeAsync DONE.");
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
