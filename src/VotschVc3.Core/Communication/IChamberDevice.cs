using VotschVc3.Core.Protocol;

namespace VotschVc3.Core.Communication;

/// <summary>
/// Protocol-agnostic view of a controllable chamber / oven. Both the Vötsch
/// ASCII-2 <see cref="ChamberClient"/> and the POL-EKO MODBUS
/// <see cref="PolEko.PolEkoClient"/> implement it, so the view models and the
/// <see cref="Profiles.ProfileRunner"/> can drive either device the same way.
/// </summary>
public interface IChamberDevice : IAsyncDisposable
{
    /// <summary><c>true</c> while the underlying connection is open.</summary>
    bool IsConnected { get; }

    /// <summary>The active connection / protocol settings.</summary>
    ChamberConnectionSettings Settings { get; }

    /// <summary>Raised after every request / response exchange, for the terminal / logging.</summary>
    event EventHandler<FrameExchangedEventArgs>? FrameExchanged;

    /// <summary>Opens a connection using the supplied settings.</summary>
    Task ConnectAsync(ChamberConnectionSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Closes the connection.</summary>
    Task DisconnectAsync();

    /// <summary>Reads the current measured / set values from the device.</summary>
    Task<ChamberReading> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the supplied analog set points and digital channels. The first
    /// set point is the temperature; <paramref name="digital"/> carries the
    /// start / "system on" channel that switches the device on.
    /// </summary>
    Task WriteSetpointsAsync(
        IReadOnlyList<double> setpoints,
        DigitalChannels digital,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completely switches the device output off: stops any running program and
    /// clears the start / "system on" channel, so the chamber stops driving power.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an ad-hoc raw frame and returns the raw response, for the diagnostic
    /// terminal. The exact meaning of the frame is protocol specific.
    /// </summary>
    Task<string> SendRawAsync(string frame, CancellationToken cancellationToken = default);
}
