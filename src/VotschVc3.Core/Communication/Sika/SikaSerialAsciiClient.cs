using System.Globalization;
using System.IO.Ports;
using System.Text.RegularExpressions;

namespace VotschVc3.Core.Communication.Sika;

/// <summary>Service fallback for TP+/SIKA over RS-232/USB: 2400 8N1, CRLF, commands t/s/s=.</summary>
public sealed class SikaSerialAsciiClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SerialPort? _port;

    public bool IsOpen => _port?.IsOpen == true;

    public async Task OpenAsync(string portName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(portName)) throw new ArgumentException("Chýba COM port SIKA.", nameof(portName));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _port?.Dispose();
            _port = new SerialPort(portName.Trim(), 2400, Parity.None, 8, StopBits.One)
            {
                NewLine = "\r\n", ReadTimeout = 3000, WriteTimeout = 3000, Handshake = Handshake.None
            };
            _port.Open();
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }
        finally { _gate.Release(); }
    }

    public async Task<double> ReadTemperatureAsync(CancellationToken cancellationToken = default) =>
        ParseNumber(await QueryAsync("t", expectResponse: true, cancellationToken).ConfigureAwait(false));

    public async Task<double> ReadSetpointAsync(CancellationToken cancellationToken = default) =>
        ParseNumber(await QueryAsync("s", expectResponse: true, cancellationToken).ConfigureAwait(false));

    public Task SetSetpointAsync(double celsius, CancellationToken cancellationToken = default) =>
        QueryAsync($"s={celsius.ToString("0.######", CultureInfo.InvariantCulture)}", expectResponse: false, cancellationToken);

    public Task<string> IdentifyAsync(CancellationToken cancellationToken = default) => QueryAsync("mf", true, cancellationToken);

    private async Task<string> QueryAsync(string command, bool expectResponse, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SerialPort port = _port is { IsOpen: true } p ? p : throw new InvalidOperationException("SIKA sériové spojenie nie je otvorené.");
            return await Task.Run(() =>
            {
                port.WriteLine(command);
                return expectResponse ? port.ReadLine() : string.Empty;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public static double ParseNumber(string response)
    {
        Match m = Regex.Match(response ?? string.Empty, @"[-+]?\d+(?:[.,]\d+)?");
        if (!m.Success || !double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new InvalidOperationException($"SIKA vrátila neplatnú číselnú odpoveď: {response}");
        return value;
    }

    public ValueTask DisposeAsync()
    {
        _port?.Dispose();
        _port = null;
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
