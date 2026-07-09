using System.Net.Sockets;

namespace VotschVc3.Core.Communication.Modbus;

/// <summary>
/// Minimal, dependency-free MODBUS TCP master. Implements the function codes
/// needed to monitor and control a POL-EKO SMART controller: read input
/// registers (0x04), read holding registers (0x03) and write a single holding
/// register (0x06). One request / response exchange runs at a time.
/// </summary>
public sealed class ModbusTcpClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _readTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private TcpClient? _client;
    private NetworkStream? _stream;
    private ushort _transactionId;

    public ModbusTcpClient(string host, int port, TimeSpan connectTimeout, TimeSpan readTimeout)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _connectTimeout = connectTimeout;
        _readTimeout = readTimeout;
    }

    /// <summary>Raised after each exchange with the hex-encoded request and response ADUs.</summary>
    public event Action<string, string>? Exchanged;

    public bool IsConnected => _client?.Connected == true;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await DisconnectAsync().ConfigureAwait(false);

        var client = new TcpClient { NoDelay = true };
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(_connectTimeout);
            await client.ConnectAsync(_host, _port, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException(
                $"Timed out connecting to {_host}:{_port} after {_connectTimeout.TotalSeconds:0.#}s.");
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        _stream = client.GetStream();
    }

    public Task DisconnectAsync()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
        return Task.CompletedTask;
    }

    /// <summary>Reads <paramref name="count"/> input registers (function 0x04).</summary>
    public Task<ushort[]> ReadInputRegistersAsync(byte unit, ushort start, ushort count, CancellationToken ct = default) =>
        ReadRegistersAsync(0x04, unit, start, count, ct);

    /// <summary>Reads <paramref name="count"/> holding registers (function 0x03).</summary>
    public Task<ushort[]> ReadHoldingRegistersAsync(byte unit, ushort start, ushort count, CancellationToken ct = default) =>
        ReadRegistersAsync(0x03, unit, start, count, ct);

    /// <summary>Writes a single holding register (function 0x06).</summary>
    public async Task WriteSingleRegisterAsync(byte unit, ushort address, ushort value, CancellationToken ct = default)
    {
        byte[] pdu = { 0x06, Hi(address), Lo(address), Hi(value), Lo(value) };
        byte[] response = await ExchangeAsync(unit, pdu, ct).ConfigureAwait(false);
        ThrowIfException(response, 0x06);
    }

    /// <summary>Sends a raw PDU (function code + data) and returns the raw response PDU.</summary>
    public Task<byte[]> SendRawPduAsync(byte unit, byte[] pdu, CancellationToken ct = default) =>
        ExchangeAsync(unit, pdu, ct);

    private async Task<ushort[]> ReadRegistersAsync(byte function, byte unit, ushort start, ushort count, CancellationToken ct)
    {
        if (count is 0 or > 125)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Register count must be 1..125.");
        }

        byte[] pdu = { function, Hi(start), Lo(start), Hi(count), Lo(count) };
        byte[] response = await ExchangeAsync(unit, pdu, ct).ConfigureAwait(false);
        ThrowIfException(response, function);

        int byteCount = response[1];
        if (response.Length < 2 + byteCount || byteCount != count * 2)
        {
            throw new ModbusException($"Malformed MODBUS response (byte count {byteCount}, expected {count * 2}).");
        }

        var registers = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            registers[i] = (ushort)((response[2 + i * 2] << 8) | response[3 + i * 2]);
        }

        return registers;
    }

    private async Task<byte[]> ExchangeAsync(byte unit, byte[] pdu, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            NetworkStream stream = _stream ?? throw new InvalidOperationException("The MODBUS transport is not connected.");

            ushort transaction = ++_transactionId;
            int length = pdu.Length + 1; // unit id + PDU
            byte[] adu = new byte[6 + length];
            adu[0] = Hi(transaction);
            adu[1] = Lo(transaction);
            adu[2] = 0; // protocol id hi
            adu[3] = 0; // protocol id lo
            adu[4] = Hi((ushort)length);
            adu[5] = Lo((ushort)length);
            adu[6] = unit;
            Array.Copy(pdu, 0, adu, 7, pdu.Length);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(_readTimeout);

            try
            {
                await stream.WriteAsync(adu, linked.Token).ConfigureAwait(false);
                await stream.FlushAsync(linked.Token).ConfigureAwait(false);

                byte[] header = await ReadExactAsync(stream, 6, linked.Token).ConfigureAwait(false);
                int respLength = (header[4] << 8) | header[5];
                if (respLength is < 2 or > 260)
                {
                    throw new ModbusException($"Invalid MODBUS response length ({respLength}).");
                }

                byte[] tail = await ReadExactAsync(stream, respLength, linked.Token).ConfigureAwait(false);
                // tail[0] = unit id, tail[1..] = PDU
                byte[] responsePdu = tail[1..];

                Exchanged?.Invoke(ToHex(adu), ToHex(Concat(header, tail)));
                return responsePdu;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Timed out waiting for a MODBUS response after {_readTimeout.TotalSeconds:0.#}s.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken ct)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new ModbusException("The MODBUS connection was closed by the device.");
            }

            offset += read;
        }

        return buffer;
    }

    private static void ThrowIfException(byte[] pdu, byte expectedFunction)
    {
        if (pdu.Length == 0)
        {
            throw new ModbusException("Empty MODBUS response.");
        }

        if ((pdu[0] & 0x80) != 0)
        {
            byte code = pdu.Length > 1 ? pdu[1] : (byte)0;
            throw new ModbusException($"MODBUS exception 0x{code:X2} ({DescribeException(code)}).");
        }

        if (pdu[0] != expectedFunction)
        {
            throw new ModbusException($"Unexpected MODBUS function 0x{pdu[0]:X2} (wanted 0x{expectedFunction:X2}).");
        }
    }

    private static string DescribeException(byte code) => code switch
    {
        0x01 => "illegal function",
        0x02 => "illegal data address",
        0x03 => "illegal data value",
        0x04 => "device failure",
        0x06 => "device busy",
        _ => "unknown",
    };

    private static byte Hi(ushort v) => (byte)(v >> 8);

    private static byte Lo(ushort v) => (byte)(v & 0xFF);

    private static byte[] Concat(byte[] a, byte[] b)
    {
        byte[] r = new byte[a.Length + b.Length];
        Array.Copy(a, r, a.Length);
        Array.Copy(b, 0, r, a.Length, b.Length);
        return r;
    }

    private static string ToHex(byte[] data) => Convert.ToHexString(data);

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}

/// <summary>Raised for MODBUS protocol / transport errors.</summary>
public sealed class ModbusException : Exception
{
    public ModbusException(string message) : base(message)
    {
    }
}
