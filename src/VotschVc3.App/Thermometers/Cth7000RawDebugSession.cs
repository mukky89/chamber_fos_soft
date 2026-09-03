using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.Thermometers;

/// <summary>
/// Deliberately low-level CTH7000 serial session used only by the operator debug window.
/// It does not identify, retry, reconnect or switch REMOTE/LOCAL automatically. This is
/// intentional: every transmitted byte must be attributable to one explicit operator action.
/// </summary>
internal sealed class Cth7000RawDebugSession : IAsyncDisposable
{
    private readonly string _portName;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SerialPort? _port;
    private SerialPortLease? _lease;
    private Cth7000RawSerialSettings _settings = Cth7000RawSerialSettings.Default;
    private int _disposed;

    public Cth7000RawDebugSession(string portName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        _portName = portName;
    }

    public string PortName => _portName;
    public bool IsOpen => _port?.IsOpen == true;
    public Cth7000RawSerialSettings Settings => _settings;

    public async Task OpenAsync(Cth7000RawSerialSettings settings, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_port?.IsOpen == true) return;

            SerialPortLease? lease = null;
            using var leaseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            leaseTimeout.CancelAfter(TimeSpan.FromSeconds(4));
            try
            {
                lease = await SerialPortLease.AcquireAsync(_portName, leaseTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IOException($"Port {_portName} je stále používaný normálnym klientom aplikácie. Zavri pripojenie/polling a skús Otvoriť COM znova.");
            }

            var port = new SerialPort(_portName, settings.BaudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                DtrEnable = settings.DtrEnable,
                RtsEnable = settings.RtsEnable,
                ReadTimeout = settings.ReadTimeoutMs,
                WriteTimeout = 2000,
                Encoding = Encoding.ASCII,
            };

            try
            {
                await Task.Run(() =>
                {
                    port.Open();
                    port.DiscardInBuffer();
                    port.DiscardOutBuffer();
                }, cancellationToken).ConfigureAwait(false);

                // Let the USB/UART bridge settle, but do not transmit anything automatically.
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                if (port.IsOpen) port.DiscardInBuffer();

                _settings = settings;
                _port = port;
                _lease = lease;
                lease = null;

                AppLog.Info("CTH7000 RAW", $"{_portName}: raw COM otvorený; {settings.Describe()}. Bez automatického TX.");
            }
            catch
            {
                try { if (port.IsOpen) port.Close(); } catch { }
                port.Dispose();
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

    public async Task<Cth7000RawExchange> PurgeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SerialPort port = RequireOpen();
            int before = port.BytesToRead;
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
            return Cth7000RawExchange.Info($"Purge RX/TX; RX pred vyčistením = {before} B");
        }
        finally { _gate.Release(); }
    }

    public async Task<Cth7000RawExchange> SendCommandAsync(
        string command,
        bool expectResponse,
        bool purgeBeforeSend,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SerialPort port = RequireOpen();
            if (purgeBeforeSend)
            {
                int stale = port.BytesToRead;
                if (stale > 0)
                {
                    byte[] staleBytes = ReadAvailable(port);
                    AppLog.Warn("CTH7000 RAW", $"{_portName}: pred TX zahodených {staleBytes.Length} stale RX B: {ToHex(staleBytes)}");
                }
                port.DiscardInBuffer();
            }

            string normalized = command.TrimEnd('\r', '\n');
            byte[] tx = Encoding.ASCII.GetBytes(normalized + _settings.TerminatorText);
            var sw = Stopwatch.StartNew();

            await WritePacedAsync(port, tx, _settings.InterCharacterDelayMs, cancellationToken).ConfigureAwait(false);
            AppLog.Info("CTH7000 RAW TX", $"{_portName}: ASCII='{Visible(tx)}' HEX={ToHex(tx)}");

            if (!expectResponse)
            {
                sw.Stop();
                return new Cth7000RawExchange(normalized, tx, Array.Empty<byte>(), sw.Elapsed, false, "TX dokončené; RX sa neočakáva.");
            }

            RawReadResult read = await ReadFrameAsync(port, _settings.ReadTimeoutMs, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            AppLog.Info("CTH7000 RAW RX", $"{_portName}: {read.Bytes.Length} B za {sw.Elapsed.TotalMilliseconds:F0} ms; ASCII='{Visible(read.Bytes)}' HEX={ToHex(read.Bytes)} timeout={read.TimedOut}");
            return new Cth7000RawExchange(normalized, tx, read.Bytes, sw.Elapsed, read.TimedOut,
                read.TimedOut && read.Bytes.Length == 0 ? "TIMEOUT — neprišiel ani jeden bajt." : null);
        }
        finally { _gate.Release(); }
    }

    public async Task<Cth7000RawExchange> ListenAsync(int durationMs, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SerialPort port = RequireOpen();
            var sw = Stopwatch.StartNew();
            var bytes = new List<byte>();
            while (sw.ElapsedMilliseconds < Math.Max(50, durationMs))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (port.BytesToRead > 0)
                {
                    bytes.AddRange(ReadAvailable(port));
                }
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
            if (port.BytesToRead > 0) bytes.AddRange(ReadAvailable(port));
            sw.Stop();
            byte[] rx = bytes.ToArray();
            AppLog.Info("CTH7000 RAW RX", $"{_portName}: listen {durationMs} ms → {rx.Length} B; ASCII='{Visible(rx)}' HEX={ToHex(rx)}");
            return new Cth7000RawExchange("(listen only)", Array.Empty<byte>(), rx, sw.Elapsed, rx.Length == 0, rx.Length == 0 ? "Počas listen okna neprišli dáta." : null);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Best-effort escape hatch. Sends SYSTEM:LOCAL using the exact currently selected serial
    /// settings, then closes the COM handle and releases the process-wide lease.
    /// </summary>
    public async Task<Cth7000RawExchange> EmergencyLocalAndCloseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_port?.IsOpen != true)
            {
                ReleasePortResources();
                return Cth7000RawExchange.Info("COM už je zatvorený; LOCAL sa neposielal.");
            }

            byte[] tx = Encoding.ASCII.GetBytes("SYSTEM:LOCAL" + _settings.TerminatorText);
            Exception? writeError = null;
            var sw = Stopwatch.StartNew();
            try
            {
                await WritePacedAsync(_port, tx, _settings.InterCharacterDelayMs, cancellationToken).ConfigureAwait(false);
                await Task.Delay(80, CancellationToken.None).ConfigureAwait(false);
                AppLog.Warn("CTH7000 RAW", $"{_portName}: EMERGENCY SYSTEM:LOCAL TX HEX={ToHex(tx)}");
            }
            catch (Exception ex)
            {
                writeError = ex;
                AppLog.Error("CTH7000 RAW", $"{_portName}: emergency LOCAL write FAILED: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                sw.Stop();
                ReleasePortResources();
            }

            return new Cth7000RawExchange("SYSTEM:LOCAL (emergency)", tx, Array.Empty<byte>(), sw.Elapsed,
                writeError is not null, writeError?.Message ?? "SYSTEM:LOCAL odoslané; COM zavretý.");
        }
        finally { _gate.Release(); }
    }

    public async Task<Cth7000RawExchange> HardCloseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReleasePortResources();
            AppLog.Warn("CTH7000 RAW", $"{_portName}: HARD CLOSE bez ďalšieho TX.");
            return Cth7000RawExchange.Info("COM zatvorený bez odoslania ďalšieho príkazu.");
        }
        finally { _gate.Release(); }
    }

    private async Task<RawReadResult> ReadFrameAsync(SerialPort port, int timeoutMs, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var sw = Stopwatch.StartNew();
        long lastByteAt = -1;
        bool sawTerminator = false;

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (port.BytesToRead > 0)
            {
                byte[] chunk = ReadAvailable(port);
                if (chunk.Length > 0)
                {
                    bytes.AddRange(chunk);
                    lastByteAt = sw.ElapsedMilliseconds;
                    sawTerminator |= chunk.Any(b => b is 0x0D or 0x0A);
                }
            }
            else if (lastByteAt >= 0)
            {
                long quietMs = sw.ElapsedMilliseconds - lastByteAt;
                // A terminated reply only needs a short drain window. A reply without CR/LF is
                // considered complete after 180 ms of silence so we can inspect non-standard frames.
                if ((sawTerminator && quietMs >= 40) || quietMs >= 180)
                {
                    return new RawReadResult(bytes.ToArray(), false);
                }
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }

        if (port.BytesToRead > 0) bytes.AddRange(ReadAvailable(port));
        return new RawReadResult(bytes.ToArray(), true);
    }

    private static byte[] ReadAvailable(SerialPort port)
    {
        int count = port.BytesToRead;
        if (count <= 0) return Array.Empty<byte>();
        byte[] buffer = new byte[count];
        int read = port.Read(buffer, 0, buffer.Length);
        return read == buffer.Length ? buffer : buffer[..read];
    }

    private static async Task WritePacedAsync(SerialPort port, byte[] bytes, int interCharacterDelayMs, CancellationToken cancellationToken)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            port.Write(bytes, i, 1);
            if (interCharacterDelayMs > 0 && i < bytes.Length - 1)
            {
                await Task.Delay(interCharacterDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private SerialPort RequireOpen() => _port?.IsOpen == true
        ? _port
        : throw new InvalidOperationException($"Port {_portName} nie je otvorený v RAW debug režime.");

    private void ReleasePortResources()
    {
        SerialPort? port = _port;
        _port = null;
        try { if (port?.IsOpen == true) port.Close(); } catch { }
        try { port?.Dispose(); } catch { }
        _lease?.Dispose();
        _lease = null;
    }

    private static string Visible(byte[] bytes)
    {
        if (bytes.Length == 0) return "";
        var sb = new StringBuilder();
        foreach (byte b in bytes)
        {
            sb.Append(b switch
            {
                0x0D => "<CR>",
                0x0A => "<LF>",
                0x09 => "<TAB>",
                >= 0x20 and <= 0x7E => ((char)b).ToString(),
                _ => $"<0x{b:X2}>",
            });
        }
        return sb.ToString();
    }

    public static string ToVisibleAscii(byte[] bytes) => Visible(bytes);
    public static string ToHex(byte[] bytes) => bytes.Length == 0 ? "—" : string.Join(' ', bytes.Select(b => b.ToString("X2")));

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_port?.IsOpen == true)
            {
                try
                {
                    byte[] tx = Encoding.ASCII.GetBytes("SYSTEM:LOCAL" + _settings.TerminatorText);
                    await WritePacedAsync(_port, tx, _settings.InterCharacterDelayMs, CancellationToken.None).ConfigureAwait(false);
                    await Task.Delay(80).ConfigureAwait(false);
                }
                catch { }
            }
            ReleasePortResources();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private readonly record struct RawReadResult(byte[] Bytes, bool TimedOut);
}

internal sealed record Cth7000RawSerialSettings(
    int BaudRate,
    bool DtrEnable,
    bool RtsEnable,
    string TerminatorName,
    int InterCharacterDelayMs,
    int ReadTimeoutMs)
{
    public static Cth7000RawSerialSettings Default { get; } = new(9600, true, true, "CR", 2, 8000);

    public string TerminatorText => TerminatorName.ToUpperInvariant() switch
    {
        "LF" => "\n",
        "CRLF" => "\r\n",
        _ => "\r",
    };

    public string Describe() =>
        $"{BaudRate} bd, 8N1, flow=None, DTR={DtrEnable}, RTS={RtsEnable}, term={TerminatorName}, pacing={InterCharacterDelayMs} ms, timeout={ReadTimeoutMs} ms";
}

internal sealed record Cth7000RawExchange(
    string Command,
    byte[] Tx,
    byte[] Rx,
    TimeSpan Elapsed,
    bool TimedOut,
    string? Note)
{
    public static Cth7000RawExchange Info(string note) =>
        new("(info)", Array.Empty<byte>(), Array.Empty<byte>(), TimeSpan.Zero, false, note);
}
