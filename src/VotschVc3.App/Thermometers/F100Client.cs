using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.App.Thermometers;

/// <summary>
/// Serial client for the WIKA CTH7000 on a USB virtual COM port.
/// The class name is kept as F100Client for source compatibility with the existing app.
/// All access to the physical SerialPort is serialized so scanning, polling and disposal
/// can never close a port while another operation is writing to it.
/// </summary>
public sealed class F100Client : IAsyncDisposable
{
    private enum CommunicationMode { Unknown, TalkOnly, Query }

    private readonly SerialPort _port;
    private readonly bool _allowQueryFallback;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CommunicationMode _communicationMode;
    private bool _queryInstrumentIdentified;
    private bool _remoteActive;
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
            if (_port.IsOpen) return;

            try
            {
                await Task.Run(() =>
                {
                    ThrowIfDisposing();
                    _port.Open();
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                }).ConfigureAwait(false);

                await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
                ThrowIfDisposing();
                if (_port.IsOpen) _port.DiscardInBuffer();
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException($"Port {_port.PortName} je obsadený. Zatvor FOS4X, inú inštanciu aplikácie alebo inú diagnostiku, ktorá používa tento port, a skús pripojenie znova.", ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> IdentifyInstrumentAsync(CancellationToken cancellationToken = default)
    {
        string identity = await SendReceiveAtomicAsync(F100Protocol.IdentifyCommand, cancellationToken).ConfigureAwait(false);
        InstrumentIdentity = identity.Trim();
        _queryInstrumentIdentified = IsSupportedQueryInstrument(identity);
        if (_queryInstrumentIdentified) _communicationMode = CommunicationMode.Query;
        return identity;
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
            await Task.Run(() => WriteCommand(F100Protocol.Frame(command))).ConfigureAwait(false);
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
            ThrowIfDisposing();
            EnsurePortOpen();
            return await Task.Run(() =>
            {
                ThrowIfDisposing();
                EnsurePortOpen();
                WriteCommand(F100Protocol.Frame(command));
                return ReadLine(cancellationToken);
            }).ConfigureAwait(false);
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
            return await Task.Run(() =>
            {
                // Critical race fix: scan/identify and DisposeAsync cannot use/close the
                // SerialPort simultaneously because both operations own the same gate.
                ThrowIfDisposing();
                EnsurePortOpen();
                _port.Write(F100Protocol.Frame(command));
                return ReadLine(cancellationToken);
            }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<ThermometerReading> ReadAsync(string readCommand, CancellationToken cancellationToken = default)
    {
        string response = await SendReceiveAsync(readCommand, cancellationToken).ConfigureAwait(false);
        return F100Protocol.ParseReading(response);
    }

    public async Task<ThermometerReading> ReadChannelAsync(string channel, string fallbackReadCommand = F100Protocol.DefaultReadCommand, CancellationToken cancellationToken = default)
    {
        string normalized = F100Protocol.NormalizeChannel(channel);

        if (_communicationMode == CommunicationMode.Unknown)
        {
            string identity = await IdentifyInstrumentAsync(cancellationToken).ConfigureAwait(false);
            if (_queryInstrumentIdentified)
            {
                await SendAsync(F100Protocol.RemoteCommand, cancellationToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken).ConfigureAwait(false);
                _remoteActive = true;
                _communicationMode = CommunicationMode.Query;
            }
            else
            {
                return new ThermometerReading(DateTimeOffset.Now, null, string.Empty, $"{PortName}: WIKA CTH7000 neodpovedal na *IDN?.");
            }
        }

        if (_communicationMode is CommunicationMode.Unknown or CommunicationMode.TalkOnly)
        {
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
            return new ThermometerReading(DateTimeOffset.Now, null, string.Empty, $"{PortName}: zariadenie neodpovedalo na *IDN?. Skontroluj USB spojenie a WIKA CTH7000.");
        }

        if (queryCapable) await EnsureRemoteAsync(cancellationToken).ConfigureAwait(false);
        _communicationMode = CommunicationMode.Query;

        string response = await SendReceiveAsync(F100Protocol.BuildMeasureChannelCommand(normalized), cancellationToken).ConfigureAwait(false);
        ThermometerReading direct = F100Protocol.ParseReading(response);
        if (!F100Protocol.IsErrorResponse(response) && direct.Temperature is not null) return direct;
        if (_queryInstrumentIdentified) return direct;

        await SendAsync(F100Protocol.BuildConfigureChannelCommand(normalized), cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        return await ReadAsync(fallbackReadCommand, cancellationToken).ConfigureAwait(false);
    }

    public async Task<(string Channel, ThermometerReading Reading)> ReadAvailableChannelAsync(string preferredChannel, string fallbackReadCommand = F100Protocol.DefaultReadCommand, CancellationToken cancellationToken = default)
    {
        try
        {
            string preferred = F100Protocol.NormalizeChannel(preferredChannel) == "B" ? "B" : "A";
            ThermometerReading first = await ReadChannelAsync(preferred, fallbackReadCommand, cancellationToken).ConfigureAwait(false);
            if (first.Temperature is not null || _communicationMode != CommunicationMode.Query) return (preferred, first);

            string alternate = preferred == "A" ? "B" : "A";
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
        try { await SendAsync(F100Protocol.LocalCommand, cancellationToken).ConfigureAwait(false); }
        finally { _remoteActive = false; }
    }

    private async Task EnsureRemoteAsync(CancellationToken cancellationToken)
    {
        if (_remoteActive) return;
        await SendAsync(F100Protocol.RemoteCommand, cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken).ConfigureAwait(false);
        _remoteActive = true;
    }

    private static bool IsSupportedQueryInstrument(string identity) =>
        !string.IsNullOrWhiteSpace(identity) && !F100Protocol.IsErrorResponse(identity) &&
        (identity.Contains("CTH7000", StringComparison.OrdinalIgnoreCase) ||
         identity.Contains("F150", StringComparison.OrdinalIgnoreCase) ||
         identity.Contains("F250", StringComparison.OrdinalIgnoreCase));

    private async Task<ThermometerReading> ReadTalkOnlyAsync(string channel, TimeSpan timeout, CancellationToken cancellationToken)
    {
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
        if (_queryInstrumentIdentified)
        {
            _port.Write(text);
            return;
        }
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
            await Task.Run(() =>
            {
                try
                {
                    if (_port.IsOpen) _port.Close();
                }
                catch { }
                _port.Dispose();
            }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }

        _gate.Dispose();
    }
}
