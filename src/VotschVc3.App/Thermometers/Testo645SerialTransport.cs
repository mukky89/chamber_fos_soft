using System.IO;
using System.IO.Ports;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.App.Thermometers;

internal sealed class Testo645SerialTransport(string portName) : ITesto645Transport
{
    private SerialPort? _port;
    private SerialPortLease? _lease;
    public void Open()
    {
        string name = portName.Trim().ToUpperInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^COM[1-9][0-9]*$"))
            throw new ArgumentException("Zadajte COM port vo formáte COM1, COM2, …");
        if (!SerialPortLease.TryAcquire(name, out _lease))
            throw new IOException($"Port {name} je obsadený iným meraním alebo diagnostikou.");
        try
        {
            _port = new SerialPort(name, 9600, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.RequestToSend, ReadTimeout = 500, WriteTimeout = 500,
            };
            _port.Open();
        }
        catch { Dispose(); throw; }
    }
    public void ClearInput() => _port!.DiscardInBuffer();
    public void Write(byte[] request) => _port!.Write(request, 0, request.Length);
    public int Read(byte[] buffer) => _port!.Read(buffer, 0, buffer.Length);
    public void Dispose()
    {
        try { _port?.Dispose(); }
        finally { _port = null; _lease?.Dispose(); _lease = null; }
    }
}
