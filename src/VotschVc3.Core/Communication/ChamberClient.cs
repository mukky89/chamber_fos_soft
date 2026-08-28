using System.Text;
using VotschVc3.Core.Protocol;

namespace VotschVc3.Core.Communication;

/// <summary>
/// High level client for a Vötsch VC3 climate chamber. Wraps an
/// <see cref="ITransport"/> and the <see cref="Ascii2Protocol"/> encoder /
/// decoder, serialising all access so concurrent callers (live polling, a
/// running profile, the manual terminal) never interleave on the wire.
/// </summary>
public sealed class ChamberClient : IChamberDevice
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<ChamberConnectionSettings, ITransport> _transportFactory;

    private ITransport? _transport;

    // Per connection: does this controller answer SIMSERV GET ACTUAL VALUE for the
    // temperature / humidity control variable? Unknown until the first attempt.
    private bool _hiResTemperature = true;
    private bool _hiResHumidity = true;
    private int _hiResTemperatureMismatches;
    private int _hiResHumidityMismatches;

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
            _hiResTemperature = true;
            _hiResHumidity = true;
            _hiResTemperatureMismatches = 0;
            _hiResHumidityMismatches = 0;
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

    /// <summary>
    /// Largest difference (in the channel's unit) between the ASCII-2 value and the
    /// SIMSERV one that is still accepted as the same measurement. The two frames are
    /// read a few milliseconds apart, so a small drift is normal; anything larger means
    /// the SIMSERV control variable is not the channel we think it is, and the ASCII-2
    /// value is kept.
    /// </summary>
    public const double HighResolutionTolerance = 1.0;

    /// <summary>SIMSERV control variable that carries the temperature (index 1).</summary>
    private const int TemperatureVariable = 1;

    /// <summary>SIMSERV control variable that carries the relative humidity (index 2).</summary>
    private const int HumidityVariable = 2;

    /// <summary>Reads the current measured / set values from the chamber.</summary>
    public async Task<ChamberReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        string command = Ascii2Protocol.BuildReadCommand(Settings.Address, Settings.Terminator);
        string response = await ExchangeAsync(command, cancellationToken).ConfigureAwait(false);
        ChamberReading reading = Ascii2Protocol.ParseReading(response, Settings.StartChannelIndex);
        return await RefineAsync(reading, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces the measured values of an ASCII-2 reading with the ones the
    /// controller reports over SIMSERV.
    /// <para>
    /// The ASCII-2 read frame carries every analog value in a fixed <c>0000.0</c>
    /// field, so it can never resolve better than 0.1&#160;°C, while the controller
    /// itself measures (and Simpati displays) far more decimals. SIMSERV
    /// GET ACTUAL VALUE (11004) returns the number as text, with the controller's own
    /// resolution, so it is asked for the temperature — and, when the frame carries a
    /// humidity channel, for the humidity as well — right after every poll.
    /// </para>
    /// <para>
    /// The refinement is deliberately conservative: a value is only taken over when
    /// the answer is a SIMSERV success carrying a number that agrees with the ASCII-2
    /// one within <see cref="HighResolutionTolerance"/>. A controller that answers
    /// with an error (or with nothing usable) is not asked again on this connection,
    /// so a unit without SIMSERV support costs exactly one extra frame.
    /// </para>
    /// </summary>
    private async Task<ChamberReading> RefineAsync(ChamberReading reading, CancellationToken cancellationToken)
    {
        if (!Settings.HighResolutionRead || (!_hiResTemperature && !_hiResHumidity))
        {
            return reading;
        }

        double[] values = reading.AnalogValues.ToArray();
        var log = new StringBuilder();

        if (_hiResTemperature)
        {
            RefineOutcome outcome = await TryRefineAsync(values, 0, TemperatureVariable, log, cancellationToken)
                .ConfigureAwait(false);
            Track(outcome, ref _hiResTemperature, ref _hiResTemperatureMismatches);
        }

        // Index 2 is the measured humidity only on a chamber that reports one; a
        // temperature-only unit has something else there (or nothing at all).
        if (_hiResHumidity && values.Length > 2)
        {
            RefineOutcome outcome = await TryRefineAsync(values, 2, HumidityVariable, log, cancellationToken)
                .ConfigureAwait(false);
            Track(outcome, ref _hiResHumidity, ref _hiResHumidityMismatches);
        }

        return log.Length == 0 ? reading : reading.WithAnalogValues(values, log.ToString());
    }

    /// <summary>How many answers may disagree with the ASCII-2 value before the channel is given up on.</summary>
    private const int MaxHighResolutionMismatches = 3;

    private enum RefineOutcome
    {
        /// <summary>The controller answered with a usable, plausible value.</summary>
        Applied,

        /// <summary>The answer was a number, but not the channel we asked about.</summary>
        Mismatch,

        /// <summary>Nothing was asked / the controller cannot answer this read.</summary>
        Unsupported,

        /// <summary>Skipped this round (the ASCII-2 frame had no such channel).</summary>
        Skipped,
    }

    /// <summary>
    /// Turns one attempt into the channel's state: an unsupported read is dropped for
    /// good, a value that repeatedly disagrees with the ASCII-2 frame is dropped after
    /// <see cref="MaxHighResolutionMismatches"/> tries (the SIMSERV variable is clearly
    /// mapped elsewhere), and a good answer clears the counter.
    /// </summary>
    private static void Track(RefineOutcome outcome, ref bool enabled, ref int mismatches)
    {
        if (outcome == RefineOutcome.Unsupported)
        {
            enabled = false;
        }
        else if (outcome == RefineOutcome.Mismatch)
        {
            mismatches++;
            if (mismatches >= MaxHighResolutionMismatches)
            {
                enabled = false;
            }
        }
        else if (outcome == RefineOutcome.Applied)
        {
            mismatches = 0;
        }
    }

    /// <summary>
    /// Asks for one SIMSERV control variable and, when the answer is plausible, writes
    /// it into <paramref name="values"/>.
    /// </summary>
    private async Task<RefineOutcome> TryRefineAsync(
        double[] values,
        int analogIndex,
        int variable,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        if (analogIndex >= values.Length)
        {
            return RefineOutcome.Skipped;
        }

        string command = SimservProtocol.BuildGetActualValue(Settings.Address, variable);
        string answer;
        try
        {
            answer = await ExchangeAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A controller that does not know the command may drop the frame instead of
            // answering; one timeout per connection is the price, the poll itself stands.
            return RefineOutcome.Unsupported;
        }

        double? value = SimservProtocol.IsSuccess(answer) ? SimservProtocol.FirstValue(answer) : null;
        if (value is not { } refined)
        {
            return RefineOutcome.Unsupported;
        }

        if (Math.Abs(refined - values[analogIndex]) > HighResolutionTolerance)
        {
            // Same controller, different channel — keep the ASCII-2 value. A chamber that
            // is ramping can disagree for a moment, so this is only given up on after
            // several tries in a row.
            return RefineOutcome.Mismatch;
        }

        values[analogIndex] = refined;
        if (log.Length > 0)
        {
            log.Append(' ');
        }

        log.Append(command.TrimEnd('\r', '\n'))
           .Append(" -> ")
           .Append(answer.TrimEnd('\r', '\n'));
        return RefineOutcome.Applied;
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
        ArgumentNullException.ThrowIfNull(setpoints);
        ArgumentNullException.ThrowIfNull(digital);

        // Simpac controllers answer ASCII-2 reads ($ddI) but ignore ASCII-2
        // set-point writes ($ddE) — control has to go through SIMSERV function
        // commands, which the controller acknowledges with "1". Set each analog
        // control variable (1 = temperature, 2 = humidity, …) with SET NOMINAL
        // VALUE (11001), then the start / "system on" digital channel with SET
        // DIGITALOUT (14001). SET DIGITALOUT channel N maps to the same index N in
        // the ASCII-2 read-back block (verified: channel 2 lit bit 2), so the
        // SIMSERV channel is exactly the 0-based StartChannelIndex.
        int id = Settings.Address;
        for (int i = 0; i < setpoints.Count; i++)
        {
            string setNominal = SimservProtocol.BuildSetNominalValue(id, i + 1, setpoints[i]);
            await ExchangeAsync(setNominal, cancellationToken).ConfigureAwait(false);
        }

        string setStart = SimservProtocol.BuildSetDigitalOut(id, Settings.StartChannelIndex, digital.Start);
        await ExchangeAsync(setStart, cancellationToken).ConfigureAwait(false);
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
        var setpoints = humidity is { } h
            ? new List<double> { temperature, h }
            : new List<double> { temperature };
        return WriteSetpointsAsync(setpoints, digital, cancellationToken);
    }

    /// <summary>
    /// Completely stops the chamber: stops any running program (SET STOPZPGPRG)
    /// and clears the start / "system on" digital channel (SET DIGITALOUT = 0), so
    /// the controller stops driving power regardless of manual / program mode.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        int id = Settings.Address;
        // Stop a running program if any. Controllers without program support answer
        // with a negative error code, which is harmless here.
        await ExchangeAsync(SimservProtocol.BuildStopProgram(id), cancellationToken).ConfigureAwait(false);
        // Clear the start / "system on" channel (manual mode) -> output off.
        string off = SimservProtocol.BuildSetDigitalOut(id, Settings.StartChannelIndex, on: false);
        await ExchangeAsync(off, cancellationToken).ConfigureAwait(false);
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
