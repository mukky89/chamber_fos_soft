using VotschVc3.Core.Protocol;

namespace VotschVc3.Core.Communication;

/// <summary>
/// High level client for a Vötsch VC3 climate chamber. Wraps an
/// <see cref="ITransport"/> and the <see cref="Ascii2Protocol"/> encoder /
/// decoder, serialising all access so concurrent callers (live polling, a
/// running profile, the manual terminal) never interleave on the wire.
/// </summary>
public sealed class ChamberClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<ChamberConnectionSettings, ITransport> _transportFactory;

    private ITransport? _transport;

    /// <summary>Creates a client that opens TCP transports for the given settings.</summary>
    public ChamberClient()
        : this(static s => new TcpTransport(
            s.Host, s.Port, s.ConnectTimeout, s.ReadTimeout,
            responseTerminator: s.Terminator.Length > 0 ? s.Terminator[^1] : '\r'))
    {
    }

    /// <summary>Creates a client with a custom transport factory (used for tests / simulation).</summary>
    public ChamberClient(Func<ChamberConnectionSettings, ITransport> transportFactory)
    {
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        Settings = new ChamberConnectionSettings();
    }

    /// <summary>The active connection / protocol settings.</summary>
    public ChamberConnectionSettings Settings { get; private set; }

    /// <summary><c>true</c> while a transport is connected.</summary>
    public bool IsConnected => _transport?.IsConnected == true;

    /// <summary>Raised after every frame is exchanged, for the raw terminal / logging.</summary>
    public event EventHandler<FrameExchangedEventArgs>? FrameExchanged;

    /// <summary>Opens a connection using the supplied settings.</summary>
    public async Task ConnectAsync(ChamberConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisposeTransportAsync().ConfigureAwait(false);
            Settings = settings.Clone();
            _transport = _transportFactory(Settings);
            await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Closes the connection.</summary>
    public async Task DisconnectAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeTransportAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads the current measured / set values from the chamber.</summary>
    public async Task<ChamberReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        string command = Ascii2Protocol.BuildReadCommand(Settings.Address, Settings.Terminator);
        string response = await ExchangeAsync(command, cancellationToken).ConfigureAwait(false);
        return Ascii2Protocol.ParseReading(response, Settings.StartChannelIndex);
    }

    /// <summary>
    /// Writes the supplied analog set points and digital channels. The
    /// <paramref name="digital"/> argument carries the start / "system on"
    /// channel; remember to set it so the chamber actually approaches the set
    /// point.
    /// </summary>
    public async Task WriteSetpointsAsync(
        IReadOnlyList<double> setpoints,
        DigitalChannels digital,
        CancellationToken cancellationToken = default)
    {
        string command = Ascii2Protocol.BuildWriteCommand(
            Settings.Address, setpoints, digital, Settings.AnalogChannelCount, Settings.Terminator);
        await ExchangeAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Convenience overload that sets temperature and (optionally) humidity and
    /// toggles the start channel.
    /// </summary>
    public Task SetTemperatureAndHumidityAsync(
        double temperature,
        double? humidity,
        bool start,
        CancellationToken cancellationToken = default)
    {
        var digital = new DigitalChannels { StartChannelIndex = Settings.StartChannelIndex, Start = start };
        var setpoints = new List<double> { temperature, humidity ?? 0d };
        return WriteSetpointsAsync(setpoints, digital, cancellationToken);
    }

    /// <summary>
    /// Sends an already formatted, raw frame (terminator optional – it is added
    /// when missing) and returns the raw response. For ad-hoc / vendor commands.
    /// </summary>
    public async Task<string> SendRawAsync(string frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!frame.EndsWith(Settings.Terminator, StringComparison.Ordinal))
        {
            frame += Settings.Terminator;
        }

        return await ExchangeAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ExchangeAsync(string command, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transport = _transport ?? throw new InvalidOperationException("Not connected to a chamber.");
            string response = await transport.SendReceiveAsync(command, cancellationToken).ConfigureAwait(false);
            FrameExchanged?.Invoke(this, new FrameExchangedEventArgs(command, response));
            return response;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DisposeTransportAsync()
    {
        if (_transport is not null)
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
            _transport = null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisposeTransportAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}

/// <summary>Event payload describing one request / response exchange.</summary>
public sealed class FrameExchangedEventArgs : EventArgs
{
    public FrameExchangedEventArgs(string request, string response)
    {
        Request = request;
        Response = response;
        Timestamp = DateTimeOffset.Now;
    }

    public DateTimeOffset Timestamp { get; }

    /// <summary>The frame that was sent (terminator included).</summary>
    public string Request { get; }

    /// <summary>The frame that was received (terminator stripped).</summary>
    public string Response { get; }
}
