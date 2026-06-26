namespace VotschVc3.Core.Communication;

/// <summary>
/// Low level transport abstraction for the chamber link. Implemented by
/// <see cref="TcpTransport"/> for the Ethernet interface; the abstraction also
/// makes it possible to plug in a serial or simulated transport for testing.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary><c>true</c> while the underlying connection is open.</summary>
    bool IsConnected { get; }

    /// <summary>Opens the connection.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the connection.</summary>
    Task DisconnectAsync();

    /// <summary>
    /// Sends a fully formatted command frame (terminator included) and returns
    /// the response frame, reading until the response terminator is seen or the
    /// read timeout elapses.
    /// </summary>
    Task<string> SendReceiveAsync(string command, CancellationToken cancellationToken = default);
}
