using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;
using VotschVc3.Core.Diagnostics;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.App.Thermometers;

/// <summary>Serial client for the WIKA CTH7000 on a USB virtual COM port.</summary>
public sealed class F100Client : IAsyncDisposable
{
    private enum CommunicationMode { Unknown, TalkOnly, Query }

    private readonly SerialPort _port;
    private readonly bool _allowQueryFallback;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CommunicationMode _communicationMode;
    private bool _queryInstrumentIdentified;
    private bool _remoteActive;
    private SerialPortLease? _portLease;
    private int _disposeStarted;

    public F100Client(string portName, int baudRate = F100Protocol.DefaultBaudRate, bool allowQueryFallback = false)
    {
        _allowQueryFallback = allowQueryFallback;
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = 3500,
            WriteTimeout = 2000,
            DtrEnable = true,
            RtsEnable = true,
        };
        AppLog.Info("CTH7000 USB", $"Client vytvorený: {portName} @ {baudRate} bd, 8N1, Handshake=None, DTR=True, RTS=True, ReadTimeout=3500 ms, WriteTimeout=2000 ms.");
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
                lease = await SerialPortLease.AcquireAsync(_port.PortName, cancellationToken).ConfigureAwait(false);
                AppLog.Info("CTH7000 USB", $"{PortName}: získaný process-wide COM lease.");
                await Task.Run(() =>
                {
                    ThrowIfDisposing();
                    _port.Open();
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                }).ConfigureAwait(false);

                AppLog.Info("CTH7000 USB", $"{PortName}: SerialPort.Open OK; RX/TX buffre vyčistené. Čakám 350 ms na inicializáciu WIKA.");
                await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
                ThrowIfDisposing();
                if (_port.IsOpen) _port.DiscardInBuffer();
                _portLease = lease;
                lease = null;
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLog.Error("CTH7000 USB", $"{PortName}: prístup k COM portu odmietnutý – port je pravdepodobne obsadený. {ex.Message}");
                throw new SerialPortBusyException(_port.PortName, ex);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                AppLog.Error("CTH7000 USB", $"{PortName}: OpenAsync zlyhalo – {ex.GetType().Name}: {ex.Message}");
                throw;
            }
            finally
            {
                lease?.Dispose();
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<string> IdentifyInstrumentAsync(CancellationToken cancellationToken = default)
    {
        AppLog.Info("CTH7000 USB", $"{PortName}: identifikácia START (*IDN?, inter-character {F100Protocol.InterCharacterDelayMs} ms).");
        try
        {
            string identity = await SendReceiveAtomicAsync(F100Protocol.IdentifyCommand, cancellationToken).ConfigureAwait(false);
            InstrumentIdentity = identity.Trim();
            _queryInstrumentIdentified = IsSupportedQueryInstrument(identity);
            if (_queryInstrumentIdentified) _communicationMode = CommunicationMode.Query;
            AppLog.Info("CTH7000 USB", $"{PortName}: identifikácia OK → '{InstrumentIdentity}', query={_queryInstrumentIdentified}.");
            return identity;
        }
        catch (Exception ex)
        {
            AppLog.Error("CTH7000 USB", $"{PortName}: identifikácia FAILED → {ex.GetType().Name}: {ex.Message}");
            throw;
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
            string frame = F100Protocol.Frame(command);
            AppLog.Info("CTH7000 USB TX", $"{PortName}: {FormatLog(frame)} [pacing={F100Protocol.InterCharacterDelayMs} ms/char]");
            await Task.Run(() => WriteCommand(frame)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            AppLog.Error("CTH7000 USB", $"{PortName}: SendAsync '{command}' FAILED → {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task<string> SendReceiveAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfDisposing();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Exception? last = null;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    ThrowIfDisposing();
                    EnsurePortOpen();
                    string frame = F100Protocol.Frame(command);
                    AppLog.Info("CTH7000 USB TX", $"{PortName} [pokus {attempt}/2]: {FormatLog(frame)} [pacing={F100Protocol.InterCharacterDelayMs} ms/char]");
                    string response = await Task.Run(() =>
                    {
                        ThrowIfDisposing();
                        EnsurePortOpen();
                        WriteCommand(frame);
                        return ReadLine(cancellationToken);
                    }).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(response))
                    {
                        throw new TimeoutException($"WIKA CTH7000 na {PortName} neposlal odpoveď v časovom limite.");
                    }
                    AppLog.Info("CTH7000 USB RX", $"{PortName} [pokus {attempt}/2]: {FormatLog(response)}");
                    return response;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    last = new IOException($"USB čítanie {PortName} bolo prerušené.");
                }
                catch (TimeoutException ex)
                {
                    last = ex;
                }
                catch (SerialPortBusyException)
                {
                    throw;
                }
                catch (IOException ex)
                {
                    last = ex;
                }
                catch (InvalidOperationException ex)
                {
                    last = ex;
                }

                AppLog.Error("CTH7000 USB", $"{PortName}: príkaz '{command}' pokus {attempt}/2 FAILED → {last?.GetType().Name}: {last?.Message}");
                if (attempt == 2) break;
                AppLog.Warn("CTH7000 USB", $"{PortName}: dočasný výpadok, pred retry robím bezpečný reconnect.");
                await ReopenUnderGateAsync(cancellationToken).ConfigureAwait(false);
            }

            AppLog.Error("CTH7000 USB", $"{PortName}: príkaz '{command}' definitívne FAILED po 2 pokusoch.");
            throw last ?? new IOException($"USB čítanie {PortName} zlyhalo.");
        }
        finally { _gate.Release(); }
    }

    private async Task<string> SendReceiveAtomicAsync(string command, CancellationToken cancellationToken)
    {
        ThrowIfDisposing();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposing();
            EnsurePortOpen();
            string frame = F100Protocol.Frame(command);
            AppLog.Info("CTH7000 USB TX", $"{PortName} [scan]: {FormatLog(frame)} [pacing={F100Protocol.InterCharacterDelayMs} ms/char]");
            string response = await Task.Run(() =>
            {
                ThrowIfDisposing();
                EnsurePortOpen();
                WriteCommand(frame);
                return ReadLine(cancellationToken);
            }).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(response))
            {
                throw new TimeoutException($"WIKA CTH7000 na {PortName} neposlal odpoveď na {command}.");
            }
            AppLog.Info("CTH7000 USB RX", $"{PortName} [scan]: {FormatLog(response)}");
            return response;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
        {
            AppLog.Error("CTH7000 USB", $"{PortName}: scan '{command}' FAILED → {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task<ThermometerReading> ReadAsync(string readCommand, CancellationToken cancellationToken = default)
    {
        AppLog.Info("CTH7000 USB", $"{PortName}: ReadAsync '{readCommand}'.");
        string response = await SendReceiveAsync(readCommand, cancellationToken).ConfigureAwait(false);
        ThermometerReading reading = F100Protocol.ParseReading(response);
        AppLog.Info("CTH7000 USB", $"{PortName}: ParseReading → temperature={reading.Temperature?.ToString() ?? "null"}, unit='{reading.Unit}', raw='{FormatLog(response)}'.");
        return reading;
    }

    public async Task<ThermometerReading> ReadChannelAsync(string channel, string fallbackReadCommand = F100Protocol.DefaultReadCommand, CancellationToken cancellationToken = default)
    {
        string normalized = F100Protocol.NormalizeChannel(channel);
        AppLog.Info("CTH7000 USB", $"{PortName}: ReadChannel START channel={normalized}, mode={_communicationMode}.");

        if (_communicationMode == CommunicationMode.Unknown)
        {
            string identity = await IdentifyInstrumentAsync(cancellationToken).ConfigureAwait(false);
            if (_queryInstrumentIdentified)
            {
                AppLog.Info("CTH7000 USB", $"{PortName}: CTH7000 potvrdený, prepínam na SYSTEM:REMOTE.");
                await SendAsync(F100Protocol.RemoteCommand, cancellationToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken).ConfigureAwait(false);
                _remoteActive = true;
                _communicationMode = CommunicationMode.Query;
                AppLog.Info("CTH7000 USB", $"{PortName}: SYSTEM:REMOTE pripravené.");
            }
            else
            {
                AppLog.Error("CTH7000 USB", $"{PortName}: *IDN? nevrátilo podporovaný CTH7000. Identity='{identity.Trim()}'.");
                return new ThermometerReading(DateTimeOffset.Now, null, string.Empty, $"{PortName}: WIKA CTH7000 neodpovedal na *IDN?.");
            }
        }

        if (_communicationMode is CommunicationMode.Unknown or CommunicationMode.TalkOnly)
        {
            // Legacy passive/talk-only branch retained only for source compatibility; CTH7000 uses Query mode.
            ThermometerReading passive = await ReadTalkOnlyAsync(normalized, TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            if (passive.Temperature is not null)
            {
                _communicationMode = CommunicationMode.TalkOnly;
                return passive;
            }
            if (_communicationMode == CommunicationMode.TalkOnly) return passive;
        }

        bool queryCapable = _queryInstrumentIdentified;
        if (!_allowQueryFallback && !queryCapable)
        {
            AppLog.Error("CTH7000 USB", $"{PortName}: query režim nie je dostupný – identifikácia CTH7000 zlyhala.");
            return new ThermometerReading(DateTimeOffset.Now, null, string.Empty, $"{PortName}: zariadenie neodpovedalo na *IDN?. Skontroluj USB spojenie a WIKA CTH7000.");
        }

        if (queryCapable) await EnsureRemoteAsync(cancellationToken).ConfigureAwait(false);
        _communicationMode = CommunicationMode.Query;

        string measureCommand = F100Protocol.BuildMeasureChannelCommand(normalized);
        AppLog.Info("CTH7000 USB", $"{PortName}: meriam kanál {normalized} → '{measureCommand}'. Čakám na odpoveď do 3.5 s.");
        string response = await SendReceiveAsync(measureCommand, cancellationToken).ConfigureAwait(false);
        ThermometerReading direct = F100Protocol.ParseReading(response);
        AppLog.Info("CTH7000 USB", $"{PortName}: výsledok kanál {normalized} → temperature={direct.Temperature?.ToString() ?? "null"}, unit='{direct.Unit}', raw='{FormatLog(response)}'.");
        if (!F100Protocol.IsErrorResponse(response) && direct.Temperature is not null) return direct;
        if (_queryInstrumentIdentified) return direct;

        AppLog.Warn("CTH7000 USB", $"{PortName}: priame meranie kanála {normalized} nevrátilo platnú teplotu, skúšam fallback konfiguráciu.");
        await SendAsync(F100Protocol.BuildConfigureChannelCommand(normalized), cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        return await ReadAsync(fallbackReadCommand, cancellationToken).ConfigureAwait(false);
    }

    public async Task<(string Channel, ThermometerReading Reading)> ReadAvailableChannelAsync(string preferredChannel, string fallbackReadCommand = F100Protocol.DefaultReadCommand, CancellationToken cancellationToken = default)
    {
        try
        {
            string preferred = F100Protocol.NormalizeChannel(preferredChannel) == "B" ? "B" : "A";
            AppLog.Info("CTH7000 USB", $"{PortName}: ReadAvailableChannel preferred={preferred}.");
            ThermometerReading first = await ReadChannelAsync(preferred, fallbackReadCommand, cancellationToken).ConfigureAwait(false);
            if (first.Temperature is not null || _communicationMode != CommunicationMode.Query) return (preferred, first);

            string alternate = preferred == "A" ? "B" : "A";
            AppLog.Warn("CTH7000 USB", $"{PortName}: kanál {preferred} nedal teplotu, skúšam alternatívu {alternate}.");
            ThermometerReading second = await ReadChannelAsync(alternate, fallbackReadCommand, cancellationToken).ConfigureAwait(false);
            return second.Temperature is not null ? (alternate, second) : (preferred, first);
        }
        finally
        {
            await ReturnToLocalIfSupportedAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task ReturnToLocalIfSupportedAsync(CancellationToken cancellationToken = default)
    {
        if (_communicationMode != CommunicationMode.Query || !_remoteActive) return;
        try
        {
            AppLog.Info("CTH7000 USB", $"{PortName}: posielam SYSTEM:LOCAL.");
            await SendAsync(F100Protocol.LocalCommand, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error("CTH7000 USB", $"{PortName}: SYSTEM:LOCAL FAILED → {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally { _remoteActive = false; }
    }

    private async Task EnsureRemoteAsync(CancellationToken cancellationToken)
    {
        if (_remoteActive) return;
        AppLog.Info("CTH7000 USB", $"{PortName}: EnsureRemote → SYSTEM:REMOTE.");
        await SendAsync(F100Protocol.RemoteCommand, cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken).ConfigureAwait(false);
        _remoteActive = true;
    }

    private async Task ReopenUnderGateAsync(CancellationToken cancellationToken)
    {
        try
        {
            AppLog.Warn("CTH7000 USB", $"{PortName}: reconnect START.");
            if (_port.IsOpen)
            {
                try { _port.DiscardInBuffer(); } catch { }
                try { _port.DiscardOutBuffer(); } catch { }
                try { _port.Close(); } catch { }
            }

            await Task.Run(() =>
            {
                ThrowIfDisposing();
                _port.Open();
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();
            }).ConfigureAwait(false);
            AppLog.Info("CTH7000 USB", $"{PortName}: reconnect Open OK; RX/TX buffre vyčistené.");
            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
            if (_port.IsOpen) _port.DiscardInBuffer();
            AppLog.Info("CTH7000 USB", $"{PortName}: reconnect DONE.");
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("CTH7000 USB", $"{PortName}: reconnect FAILED – COM port obsadený. {ex.Message}");
            throw new SerialPortBusyException(_port.PortName, ex);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            AppLog.Error("CTH7000 USB", $"{PortName}: reconnect FAILED – {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    private static bool IsSupportedQueryInstrument(string identity) =>
        !string.IsNullOrWhiteSpace(identity) && !F100Protocol.IsErrorResponse(identity) &&
        identity.Contains("CTH7000", StringComparison.OrdinalIgnoreCase);

    private async Task<ThermometerReading> ReadTalkOnlyAsync(string channel, TimeSpan timeout, CancellationToken cancellationToken)
    {
        AppLog.Warn("CTH7000 USB", $"{PortName}: legacy talk-only branch invoked unexpectedly; channel={channel}.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposing();
            EnsurePortOpen();
            return await Task.Run(() =>
            {
                var clock = Stopwatch.StartNew();
                string lastRaw = string.Empty;
                while (clock.Elapsed < timeout)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string raw = ReadLine(cancellationToken);
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    lastRaw = raw;
                    AppLog.Info("Legacy USB RX", $"{PortName} [talk-only]: {FormatLog(raw)}");
                    ThermometerReading reading = F100Protocol.ParseReading(raw);
                    string? frameChannel = F100Protocol.DetectTalkOnlyChannel(raw);
                    if (reading.Temperature is not null && (frameChannel is null || string.Equals(frameChannel, channel, StringComparison.Ordinal))) return reading;
                }
                return new ThermometerReading(DateTimeOffset.Now, null, string.Empty, lastRaw);
            }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private void WriteCommand(string text)
    {
        EnsurePortOpen();
        foreach (char c in text)
        {
            EnsurePortOpen();
            _port.Write(c.ToString());
            if (F100Protocol.InterCharacterDelayMs > 0) Thread.Sleep(F100Protocol.InterCharacterDelayMs);
        }
    }

    private string ReadLine(CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds <= _port.ReadTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int b;
            try { b = _port.ReadByte(); }
            catch (TimeoutException) { break; }
            if (b < 0) break;
            char c = (char)b;
            if (c is '\r' or '\n')
            {
                if (sb.Length > 0) break;
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string FormatLog(string value)
    {
        if (string.IsNullOrEmpty(value)) return "<EMPTY>";
        string normalized = value.Replace("\r", "<CR>", StringComparison.Ordinal).Replace("\n", "<LF>", StringComparison.Ordinal);
        return normalized.Length > 300 ? normalized[..300] + "…" : normalized;
    }

    private void EnsurePortOpen()
    {
        if (!_port.IsOpen) throw new IOException($"USB COM port {_port.PortName} je zatvorený alebo bol odpojený.");
    }

    private void ThrowIfDisposing()
    {
        if (Volatile.Read(ref _disposeStarted) != 0) throw new ObjectDisposedException(nameof(F100Client), "Komunikácia s WIKA CTH7000 sa zatvára.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            AppLog.Info("CTH7000 USB", $"{PortName}: Dispose START.");
            await Task.Run(() =>
            {
                try
                {
                    if (_port.IsOpen) _port.Close();
                }
                catch (Exception ex)
                {
                    AppLog.Error("CTH7000 USB", $"{PortName}: Close počas Dispose zlyhal – {ex.GetType().Name}: {ex.Message}");
                }
                _port.Dispose();
            }).ConfigureAwait(false);

            _portLease?.Dispose();
            _portLease = null;
            AppLog.Info("CTH7000 USB", $"{PortName}: Dispose DONE.");
        }
        finally { _gate.Release(); }

        _gate.Dispose();
    }
}
