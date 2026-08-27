using System.Diagnostics;
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
    private readonly SerialPort _port;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public F100Client(string portName, int baudRate = F100Protocol.DefaultBaudRate)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = 2000,
            WriteTimeout = 2000,
            DtrEnable = true,
            RtsEnable = true,
        };
    }

    public bool IsOpen => _port.IsOpen;

    public string PortName => _port.PortName;

    public Task OpenAsync() => Task.Run(() =>
    {
        if (!_port.IsOpen)
        {
            _port.Open();
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }
    });

    /// <summary>Sends a non-query command. No read is attempted, so commands such as SYSTEM:REMOTE do not time out.</summary>
    public async Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                WriteWithDelay(F100Protocol.Frame(command));
                return ReadLine();
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
        string directCommand = F100Protocol.BuildMeasureChannelCommand(normalized);
        string response = await SendReceiveAsync(directCommand, cancellationToken).ConfigureAwait(false);
        ThermometerReading direct = F100Protocol.ParseReading(response);
        if (!F100Protocol.IsErrorResponse(response) && direct.Temperature is not null)
        {
            return direct;
        }

        await SendAsync(F100Protocol.BuildConfigureChannelCommand(normalized), cancellationToken).ConfigureAwait(false);
        // Community integrations of the F100 report that the first conversion after an A/B
        // change can take several seconds; use five seconds to avoid returning the old channel.
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        return await ReadAsync(fallbackReadCommand, cancellationToken).ConfigureAwait(false);
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

    private string ReadLine()
    {
        var sb = new StringBuilder();
        var clock = Stopwatch.StartNew();

        while (clock.ElapsedMilliseconds <= _port.ReadTimeout)
        {
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

    public async ValueTask DisposeAsync()
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
                // ignore close errors
            }

            _port.Dispose();
        }).ConfigureAwait(false);

        _gate.Dispose();
    }
}
