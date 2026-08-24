using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.Agent;

public sealed class F100Client : IAsyncDisposable
{
    private readonly SerialPort _port;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public F100Client(string portName, int baudRate)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        { Handshake = Handshake.None, ReadTimeout = 2000, WriteTimeout = 2000, DtrEnable = true, RtsEnable = true };
    }
    public bool IsOpen => _port.IsOpen;
    public Task OpenAsync() => Task.Run(() => { if (!_port.IsOpen) { _port.Open(); _port.DiscardInBuffer(); _port.DiscardOutBuffer(); } });
    public async Task<ThermometerReading> ReadAsync(string command, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            string response = await Task.Run(() =>
            {
                foreach (char c in F100Protocol.Frame(command)) { _port.Write(c.ToString()); Thread.Sleep(F100Protocol.InterCharacterDelayMs); }
                var sb = new StringBuilder(); var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds <= _port.ReadTimeout)
                {
                    int b; try { b = _port.ReadByte(); } catch (TimeoutException) { break; }
                    if (b < 0) break; char ch = (char)b;
                    if (ch is '\r' or '\n') { if (sb.Length > 0) break; continue; }
                    sb.Append(ch);
                }
                return sb.ToString();
            }, ct);
            return F100Protocol.ParseReading(response);
        }
        finally { _gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        await Task.Run(() => { try { if (_port.IsOpen) _port.Close(); } catch { } _port.Dispose(); });
        _gate.Dispose();
    }
}
