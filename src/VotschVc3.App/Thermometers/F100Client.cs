using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.App.Thermometers;

/// <summary>
/// Serial client for an ASL F100 thermometer on a USB virtual COM port. Wraps
/// the synchronous <see cref="SerialPort"/> in async methods, honours the
/// 1–2&#160;ms inter-character gap and reads a carriage-return terminated line.
/// Access is serialised so polling and the raw terminal never overlap.
/// </summary>
public sealed class F100Client : IAsyncDisposable
{
    private enum CommunicationMode { Unknown, TalkOnly, Query }

    private readonly SerialPort _port;
    private readonly bool _allowQueryFallback;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CommunicationMode _communicationMode;
    private bool _queryInstrumentIdentified;
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
            // CTH7000 needs about 2.1–2.3 s for a fresh channel conversion.
            // Two seconds loses the response just before it arrives and shifts it to
            // the next query, so keep a small but safe margin.
            ReadTimeout = 3500,
            WriteTimeout = 2000,
            DtrEnable = true,
            RtsEnable = true,
        };
    }

    public bool IsOpen => _port.IsOpen;
    public string InstrumentIdentity { get; private set; } = string.Empty;

    /// <summary>Returns a confirmed query-capable instrument to local control. The original
    /// talk-only F100 must never receive speculative SCPI commands.</summary>
    public Task ReturnToLocalIfSupportedAsync(CancellationToken cancellationToken = default) =>
        _communicationMode == CommunicationMode.Query
            ? SendAsync(F100Protocol.LocalCommand, cancellationToken)
            : Task.CompletedTask;

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
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    _port.Open();
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                }, cancellationToken).ConfigureAwait(false);

                // The CTH7000's FTDI interface resets when DTR/RTS and the COM handle
                // are opened. Queries sent immediately after Open() are silently lost.
                await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
                _port.DiscardInBuffer();
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException(
                    $"Port {_port.PortName} je obsadený. Zatvor FOS4X, inú inštanciu aplikácie alebo inú diagnostiku, ktorá používa tento port, a skús pripojenie znova.",
                    ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Sends a non-query command. No read is attempted, so commands such as SYSTEM:REMOTE do not time out.</summary>
    public async Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfDisposing();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposing();
            await Task.Run(() => WriteWithDelay(F100Protocol.Frame(command)), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Sends a query (terminator added if missing) and returns the response line.</summary>
    public async Task<string> SendReceiveAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfDisposing();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposing();
            return await Task.Run(() =>
            {
                WriteWithDelay(F100Protocol.Frame(command));
                return ReadLine(cancellationToken);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Sends the configured read command and decodes the reading.</summary>
    public async Task<ThermometerReading> ReadAsync(string readCommand, CancellationToken cancellationToken = default)
    {
        string response = await SendReceiveAsync(readCommand, cancellationToken).ConfigureAwait(false);
        return F100Protocol.ParseReading(response);
    }

    /// <summary>
    /// Reads one explicitly selected F100 probe input. The ASL SCPI family supports
    /// MEASURE:CHANNEL?; if an older F100 firmware rejects that form, the method falls
    /// back to CONFIGURE:CHANNEL + READ?. The fallback waits for a fresh conversion after
    /// changing channels so a value from the previous input is not accidentally logged.
    /// </summary>
    public async Task<ThermometerReading> ReadChannelAsync(
        string channel,
        string fallbackReadCommand = F100Protocol.DefaultReadCommand,
        CancellationToken cancellationToken = default)
    {
        string normalized = F100Protocol.NormalizeChannel(channel);

        // CTH7000 answers *IDN? immediately. Try identification first so the operator
        // does not wait four seconds for a talk-only stream that this model never emits.
        if (_communicationMode == CommunicationMode.Unknown)
        {
            string initialIdentity = await SendReceiveAsync(F100Protocol.IdentifyCommand, cancellationToken).ConfigureAwait(false);
            InstrumentIdentity = initialIdentity.Trim();
            _queryInstrumentIdentified = IsSupportedQueryInstrument(initialIdentity);
            if (_queryInstrumentIdentified)
            {
                await SendAsync(F100Protocol.RemoteCommand, cancellationToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken).ConfigureAwait(false);
                _communicationMode = CommunicationMode.Query;
            }
        }

        // The original F100 USB interface normally emits measurements as a
        // continuous talk-only stream. It does not necessarily implement the
        // SCPI query set used by later ASL models. Detect that stream first and
        // remember the result so subsequent reads do not send unsupported commands.
        if (_communicationMode is CommunicationMode.Unknown or CommunicationMode.TalkOnly)
        {
            ThermometerReading passive = await ReadTalkOnlyAsync(
                normalized,
                _communicationMode == CommunicationMode.Unknown ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(2),
                cancellationToken).ConfigureAwait(false);
            if (passive.Temperature is not null)
            {
                _communicationMode = CommunicationMode.TalkOnly;
                return passive;
            }

            if (_communicationMode == CommunicationMode.TalkOnly)
            {
                return passive;
            }
        }

        // Both instruments use an FTDI virtual COM port. Identify the query-capable
        // CTH7000 before sending its numeric-channel measurement command; a silent
        // original F100 continues to require Talk Only and receives no further command.
        string identity = _queryInstrumentIdentified
            ? string.Empty
            : await SendReceiveAsync(F100Protocol.IdentifyCommand, cancellationToken).ConfigureAwait(false);
        bool queryCapable = _queryInstrumentIdentified || IsSupportedQueryInstrument(identity);
        if (!_allowQueryFallback && !queryCapable)
        {
            return new ThermometerReading(
                DateTimeOffset.Now,
                null,
                string.Empty,
                $"{PortName}: bez talk-only dát. Na ASL F100 zapni Menu → Options → Talk Only → On.");
        }

        if (queryCapable)
        {
            await SendAsync(F100Protocol.RemoteCommand, cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken).ConfigureAwait(false);
        }
        _communicationMode = CommunicationMode.Query;
        string directCommand = F100Protocol.BuildMeasureChannelCommand(normalized);
        string response = await SendReceiveAsync(directCommand, cancellationToken).ConfigureAwait(false);
        ThermometerReading direct = F100Protocol.ParseReading(response);
        if (!F100Protocol.IsErrorResponse(response) && direct.Temperature is not null)
        {
            return direct;
        }

        // CTH7000 explicitly reports NoProbe for an empty input. That is a valid,
        // immediate result; legacy READ? fallback is unsupported and only adds delay.
        if (_queryInstrumentIdentified)
        {
            return direct;
        }

        await SendAsync(F100Protocol.BuildConfigureChannelCommand(normalized), cancellationToken).ConfigureAwait(false);
        // Community integrations of the F100 report that the first conversion after an A/B
        // change can take several seconds; use five seconds to avoid returning the old channel.
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        return await ReadAsync(fallbackReadCommand, cancellationToken).ConfigureAwait(false);
    }

    public async Task<(string Channel, ThermometerReading Reading)> ReadAvailableChannelAsync(
        string preferredChannel,
        string fallbackReadCommand = F100Protocol.DefaultReadCommand,
        CancellationToken cancellationToken = default)
    {
        string preferred = F100Protocol.NormalizeChannel(preferredChannel) == "B" ? "B" : "A";
        ThermometerReading first = await ReadChannelAsync(preferred, fallbackReadCommand, cancellationToken).ConfigureAwait(false);
        if (first.Temperature is not null || _communicationMode != CommunicationMode.Query)
        {
            return (preferred, first);
        }

        string alternate = preferred == "A" ? "B" : "A";
        ThermometerReading second = await ReadChannelAsync(alternate, fallbackReadCommand, cancellationToken).ConfigureAwait(false);
        return second.Temperature is not null ? (alternate, second) : (preferred, first);
    }

    private static bool IsSupportedQueryInstrument(string identity) =>
        !string.IsNullOrWhiteSpace(identity) &&
        !F100Protocol.IsErrorResponse(identity) &&
        (identity.Contains("CTH7000", StringComparison.OrdinalIgnoreCase) ||
         identity.Contains("F150", StringComparison.OrdinalIgnoreCase) ||
         identity.Contains("F250", StringComparison.OrdinalIgnoreCase));

    private async Task<ThermometerReading> ReadTalkOnlyAsync(
        string channel,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposing();
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
                    if (reading.Temperature is not null &&
                        (frameChannel is null || string.Equals(frameChannel, channel, StringComparison.Ordinal)))
                    {
                        return reading;
                    }
                }

                return new ThermometerReading(DateTimeOffset.Now, null, string.Empty, lastRaw);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void WriteWithDelay(string text)
    {
        foreach (char c in text)
        {
            _port.Write(c.ToString());
            if (F100Protocol.InterCharacterDelayMs > 0)
            {
                Thread.Sleep(F100Protocol.InterCharacterDelayMs);
            }
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
            try
            {
                b = _port.ReadByte();
            }
            catch (TimeoutException)
            {
                break;
            }

            if (b < 0)
            {
                break;
            }

            char c = (char)b;
            if (c is '\r' or '\n')
            {
                if (sb.Length > 0)
                {
                    break;
                }

                continue; // skip leading terminators
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private void ThrowIfDisposing()
    {
        if (Volatile.Read(ref _disposeStarted) != 0)
        {
            throw new ObjectDisposedException(nameof(F100Client), "Komunikácia ASL F100 sa zatvára.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        // Never close SerialPort while another thread is blocked in ReadByte(). Closing a
        // live handle makes System.IO.Ports cancel that read with OperationCanceledException.
        // Waiting for the shared gate lets the read finish (or hit its short timeout) first.
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                try
                {
                    if (_port.IsOpen)
                    {
                        _port.Close();
                    }
                }
                catch
                {
                    // A disappearing USB device may reject close; Dispose still releases it.
                }

                _port.Dispose();
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        _gate.Dispose();
    }
}
