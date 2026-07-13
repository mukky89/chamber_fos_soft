using System.Net.Sockets;
using System.Text;

namespace VotschVc3.Core.Communication;

/// <summary>
/// Ethernet (TCP/IP) transport for the ASCII-2 interface. Keeps a single
/// persistent socket open and performs one request / response exchange at a
/// time. The chamber accepts at most five simultaneous connections, so reusing
/// one socket is the friendly behaviour.
/// </summary>
public sealed class TcpTransport : ITransport
{
    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _readTimeout;
    private readonly char _responseTerminator;

    private TcpClient? _client;
    private NetworkStream? _stream;

    /// <param name="host">Chamber IP address or host name.</param>
    /// <param name="port">TCP port of the ASCII-2 interface.</param>
    /// <param name="connectTimeout">Timeout for opening the socket.</param>
    /// <param name="readTimeout">Timeout for a complete response.</param>
    /// <param name="responseTerminator">
    /// Character that marks the end of a response. The controller terminates
    /// responses with a carriage return.
    /// </param>
    public TcpTransport(
        string host,
        int port,
        TimeSpan connectTimeout,
        TimeSpan readTimeout,
        char responseTerminator = '\r')
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _connectTimeout = connectTimeout;
        _readTimeout = readTimeout;
        _responseTerminator = responseTerminator;
    }

    /// <inheritdoc />
    public bool IsConnected => _client?.Connected == true;

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task DisconnectAsync()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<string> SendReceiveAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_stream is null)
        {
            throw new InvalidOperationException("The transport is not connected.");
        }

        // Latin-1 (ISO-8859-1) is a superset of ASCII for the ASCII-2 protocol
        // (all its characters are < 128, so nothing changes there) and, unlike
        // Encoding.ASCII, it transmits the SIMSERV separator '¶' (0xB6) intact.
        byte[] payload = Encoding.Latin1.GetBytes(command);
        await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        return await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ReadResponseAsync(CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException("The transport is not connected.");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_readTimeout);

        var builder = new StringBuilder();
        byte[] buffer = new byte[256];

        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    // Remote closed the connection.
                    break;
                }

                for (int i = 0; i < read; i++)
                {
                    char c = (char)buffer[i];
                    if (c == _responseTerminator)
                    {
                        return builder.ToString();
                    }

                    // Ignore a line feed paired with the carriage return.
                    if (c != '\n' || _responseTerminator == '\n')
                    {
                        builder.Append(c);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out waiting for a response after {_readTimeout.TotalSeconds:0.#}s. " +
                $"Received so far: \"{builder}\".");
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
