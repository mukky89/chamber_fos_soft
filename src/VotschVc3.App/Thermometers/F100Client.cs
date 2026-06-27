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

    /// <summary>Sends a command (terminator added if missing) and returns the response line.</summary>
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

    /// <summary>Sends the read command and decodes the reading.</summary>
    public async Task<ThermometerReading> ReadAsync(string readCommand, CancellationToken cancellationToken = default)
    {
        string response = await SendReceiveAsync(readCommand, cancellationToken).ConfigureAwait(false);
        return F100Protocol.ParseReading(response);
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
