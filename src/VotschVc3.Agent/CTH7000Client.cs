using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.Agent;

/// <summary>
/// Bridge-side WIKA CTH7000 USB client.
/// The legacy bridge setting READ? is accepted only as a compatibility alias for channel A;
/// READ? itself is never sent to a CTH7000.
///
/// Production sequence validated against the installed V1.0 unit:
/// SYSTEM:REMOTE -> 1 s settle -> *IDN? (first session only)
/// -> MEASURE:CHANNEL? 1/2 -> SYSTEM:LOCAL.
/// </summary>
public sealed class F100Client : IAsyncDisposable
{
    private readonly SerialPort _port;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _identified;
    private bool _remoteActive;
    private int _disposeStarted;

    public F100Client(string portName, int baudRate)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = 8000,
            WriteTimeout = 2000,
            DtrEnable = true,
            RtsEnable = true,
            Encoding = Encoding.ASCII,
        };
    }

    public bool IsOpen => _port.IsOpen;

    public async Task OpenAsync()
    {
        ThrowIfDisposing();
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposing();
            if (_port.IsOpen) return;

            await Task.Run(() =>
            {
                _port.Open();
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();
            }).ConfigureAwait(false);

            await Task.Delay(TimeSpan.FromMilliseconds(350)).ConfigureAwait(false);
            if (_port.IsOpen) _port.DiscardInBuffer();
            _identified = false;
            _remoteActive = false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reads the configured reference channel. For backward compatibility, READ? means
    /// channel A. MEASURE:CHANNEL? 2 selects channel B. Other legacy values also default
    /// to A so the bridge never sends undocumented CTH7000 query commands.
    /// </summary>
    public async Task<ThermometerReading> ReadAsync(string command, CancellationToken ct)
    {
        ThrowIfDisposing();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposing();
            EnsureOpen();

            string channel = ResolveChannel(command);
            Exception? last = null;

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    // Fresh-open IDN before REMOTE timed out with zero bytes on the production
                    // V1.0 unit. Always establish REMOTE and let the instrument settle first.
                    await EnterRemoteUnderGateAsync(ct).ConfigureAwait(false);
                    await EnsureIdentifiedUnderGateAsync(ct).ConfigureAwait(false);

                    string response = SendReceiveUnderGate(
                        F100Protocol.BuildMeasureChannelCommand(channel),
                        ct);
                    return F100Protocol.ParseReading(response);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
                {
                    last = ex;
                    if (attempt == 2) break;
                    TryReturnLocalUnderGate();
                    await ReopenUnderGateAsync(ct).ConfigureAwait(false);
                }
                finally
                {
                    TryReturnLocalUnderGate();
                }
            }

            throw last ?? new IOException($"CTH7000 USB read failed on {_port.PortName}.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task EnsureIdentifiedUnderGateAsync(CancellationToken ct)
    {
        if (_identified) return Task.CompletedTask;
        ct.ThrowIfCancellationRequested();

        string identity = SendReceiveUnderGate(F100Protocol.IdentifyCommand, ct);
        if (F100Protocol.IsErrorResponse(identity) ||
            !identity.Contains("CTH7000", StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                $"Zariadenie na {_port.PortName} sa po SYSTEM:REMOTE neidentifikovalo ako WIKA CTH7000. IDN='{identity}'.");
        }

        _identified = true;
        return Task.CompletedTask;
    }

    private async Task EnterRemoteUnderGateAsync(CancellationToken ct)
    {
        if (_remoteActive) return;
        EnsureOpen();
        WriteCommand(F100Protocol.Frame(F100Protocol.RemoteCommand));
        _remoteActive = true;
        await Task.Delay(
            TimeSpan.FromMilliseconds(F100Protocol.RemoteSettleDelayMs),
            ct).ConfigureAwait(false);
    }

    private void TryReturnLocalUnderGate()
    {
        if (!_remoteActive) return;
        try
        {
            if (_port.IsOpen)
            {
                WriteCommand(F100Protocol.Frame(F100Protocol.LocalCommand));
            }
        }
        catch
        {
            // Best effort. The original read/reconnect error is more useful to the bridge.
        }
        finally
        {
            _remoteActive = false;
        }
    }

    private string SendReceiveUnderGate(string command, CancellationToken ct)
    {
        EnsureOpen();
        ct.ThrowIfCancellationRequested();
        _port.DiscardInBuffer();
        WriteCommand(F100Protocol.Frame(command));

        var response = new StringBuilder();
        var clock = Stopwatch.StartNew();
        const int pollDelayMs = 25;
        const int overallTimeoutMs = 8000;

        while (clock.ElapsedMilliseconds < overallTimeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            EnsureOpen();

            string chunk = _port.ReadExisting();
            if (!string.IsNullOrEmpty(chunk))
            {
                response.Append(chunk);
                string current = response.ToString();
                if (current.Contains('\r') || current.Contains('\n'))
                {
                    break;
                }
            }

            Thread.Sleep(pollDelayMs);
        }

        string result = response.ToString().TrimEnd('\r', '\n');
        if (string.IsNullOrWhiteSpace(result))
        {
            throw new TimeoutException(
                $"WIKA CTH7000 na {_port.PortName} neposlal odpoveď na '{command}' do 8 s.");
        }

        return result;
    }

    private async Task ReopenUnderGateAsync(CancellationToken ct)
    {
        _remoteActive = false;
        _identified = false;

        try
        {
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

            await Task.Delay(TimeSpan.FromMilliseconds(350), ct).ConfigureAwait(false);
            if (_port.IsOpen) _port.DiscardInBuffer();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException($"COM port {_port.PortName} je obsadený.", ex);
        }
    }

    private void WriteCommand(string frame)
    {
        foreach (char c in frame)
        {
            EnsureOpen();
            _port.Write(c.ToString());
            if (F100Protocol.InterCharacterDelayMs > 0)
            {
                Thread.Sleep(F100Protocol.InterCharacterDelayMs);
            }
        }
    }

    private static string ResolveChannel(string? command)
    {
        string text = (command ?? string.Empty).Trim();
        if (text.Equals("B", StringComparison.OrdinalIgnoreCase) ||
            text.EndsWith(" 2", StringComparison.OrdinalIgnoreCase) ||
            text.EndsWith(":2", StringComparison.OrdinalIgnoreCase))
        {
            return "B";
        }

        return "A";
    }

    private void EnsureOpen()
    {
        if (!_port.IsOpen)
        {
            throw new IOException($"COM port {_port.PortName} nie je otvorený.");
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
            TryReturnLocalUnderGate();
            try
            {
                if (_port.IsOpen) _port.Close();
            }
            catch
            {
            }

            _port.Dispose();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
