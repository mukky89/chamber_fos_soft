using VotschVc3.Core.Communication.Modbus;
using VotschVc3.Core.Protocol;

namespace VotschVc3.Core.Communication.PolEko;

/// <summary>
/// <see cref="IChamberDevice"/> for a POL-EKO SMART drying oven (e.g. SLN 115)
/// over MODBUS TCP. Reads the measured temperature from an input register and
/// the set point / on-off state from holding registers; writing a set point
/// updates the holding registers. Temperature only – no humidity channel.
/// </summary>
public sealed class PolEkoClient : IChamberDevice
{
    private readonly PolEkoRegisterMap _map;

    private ModbusTcpClient? _modbus;
    private string _lastResponseHex = string.Empty;
    private bool _holdingReadsDisabled;

    public PolEkoClient(PolEkoRegisterMap? map = null)
    {
        _map = map ?? PolEkoRegisterMap.SmartDefault();
        Settings = new ChamberConnectionSettings();
    }

    public ChamberConnectionSettings Settings { get; private set; }

    public bool IsConnected => _modbus?.IsConnected == true;

    public event EventHandler<FrameExchangedEventArgs>? FrameExchanged;

    private byte Unit => (byte)Math.Clamp(Settings.Address, 0, 255);

    public async Task ConnectAsync(ChamberConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await DisposeModbusAsync().ConfigureAwait(false);
        Settings = settings.Clone();
        _holdingReadsDisabled = false;

        var modbus = new ModbusTcpClient(Settings.Host, Settings.Port, Settings.ConnectTimeout, Settings.ReadTimeout);
        modbus.Exchanged += (_, resp) => _lastResponseHex = resp;
        await modbus.ConnectAsync(cancellationToken).ConfigureAwait(false);
        _modbus = modbus;
    }

    public async Task DisconnectAsync() => await DisposeModbusAsync().ConfigureAwait(false);

    public async Task<ChamberReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        ModbusTcpClient modbus = _modbus ?? throw new InvalidOperationException("Not connected to the POL-EKO device.");

        ushort[] measuredRegs = await modbus.ReadInputRegistersAsync(Unit, _map.MeasuredTemperatureInput, 1, cancellationToken)
            .ConfigureAwait(false);
        double measured = Decode(measuredRegs[0]);

        // Set point and on/off live in holding registers, which some SMART firmware
        // does not expose. Treat those reads as best-effort so monitoring keeps
        // working even when only the input register is available.
        ushort[]? setpointRegs = await TryReadHoldingAsync(modbus, _map.SetpointHolding, cancellationToken).ConfigureAwait(false);
        double? setpoint = setpointRegs is null ? null : Decode(setpointRegs[0]);

        ushort[]? onOffRegs = await TryReadHoldingAsync(modbus, _map.OnOffHolding, cancellationToken).ConfigureAwait(false);
        bool? running = onOffRegs is null ? null : onOffRegs[0] != 0;

        var analog = new List<double> { measured };
        if (setpoint is { } sp)
        {
            analog.Add(sp);
        }

        var digital = new DigitalChannels { StartChannelIndex = 0 };
        string stateText;
        if (running is { } r)
        {
            digital.Start = r;
            // Include the 32-bit digital block so the app trusts this on/off state.
            stateText = $" · {(r ? "ON" : "OFF")} · DIG={digital.ToProtocolString()}";
        }
        else
        {
            stateText = " · stav neznámy";
        }

        string raw = $"POL-EKO · T={measured:0.0} °C" +
            (setpoint is { } s ? $" · SP={s:0.0} °C" : string.Empty) + stateText;

        return new ChamberReading(DateTimeOffset.Now, raw, analog, digital);
    }

    public async Task WriteSetpointsAsync(
        IReadOnlyList<double> setpoints,
        DigitalChannels digital,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setpoints);
        ArgumentNullException.ThrowIfNull(digital);
        ModbusTcpClient modbus = _modbus ?? throw new InvalidOperationException("Not connected to the POL-EKO device.");

        double temperature = setpoints.Count > 0 ? setpoints[0] : 0d;
        await modbus.WriteSingleRegisterAsync(Unit, _map.SetpointHolding, Encode(temperature), cancellationToken)
            .ConfigureAwait(false);
        await modbus.WriteSingleRegisterAsync(Unit, _map.OnOffHolding, (ushort)(digital.Start ? 1 : 0), cancellationToken)
            .ConfigureAwait(false);

        RaiseFrame($"SET {temperature:0.0} °C · {(digital.Start ? "ON" : "OFF")}");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ModbusTcpClient modbus = _modbus ?? throw new InvalidOperationException("Not connected to the POL-EKO device.");
        // Switch the oven off (on/off holding register = 0). The set point is left
        // untouched so it is remembered for the next start.
        await modbus.WriteSingleRegisterAsync(Unit, _map.OnOffHolding, 0, cancellationToken).ConfigureAwait(false);
        RaiseFrame("STOP · OFF");
    }

    public async Task<string> SendRawAsync(string frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ModbusTcpClient modbus = _modbus ?? throw new InvalidOperationException("Not connected to the POL-EKO device.");

        byte[]? pdu = ParseHex(frame);
        if (pdu is null || pdu.Length == 0)
        {
            return "Zadaj MODBUS PDU v HEX (funkcia + dáta), napr. \"04 0000 0001\" = čítať input register 0.";
        }

        byte[] response = await modbus.SendRawPduAsync(Unit, pdu, cancellationToken).ConfigureAwait(false);
        RaiseFrame($"RAW {Convert.ToHexString(pdu)}");
        return Convert.ToHexString(response);
    }

    private async Task<ushort[]?> TryReadHoldingAsync(ModbusTcpClient modbus, ushort address, CancellationToken ct)
    {
        if (_holdingReadsDisabled)
        {
            return null;
        }

        try
        {
            return await modbus.ReadHoldingRegistersAsync(Unit, address, 1, ct).ConfigureAwait(false);
        }
        catch (ModbusException)
        {
            // Firmware that doesn't expose this holding register answers with an
            // exception (fast); keep monitoring the measured value.
            return null;
        }
        catch (TimeoutException)
        {
            // A timeout is slow to hit, so stop probing holding registers for this
            // connection to keep polling responsive.
            _holdingReadsDisabled = true;
            return null;
        }
    }

    private double Decode(ushort raw) =>
        (_map.TemperatureSigned ? (short)raw : raw) * _map.TemperatureScale;

    private ushort Encode(double celsius)
    {
        double scaled = Math.Round(celsius / _map.TemperatureScale);
        return _map.TemperatureSigned ? (ushort)(short)scaled : (ushort)Math.Clamp(scaled, 0, ushort.MaxValue);
    }

    private void RaiseFrame(string request) =>
        FrameExchanged?.Invoke(this, new FrameExchangedEventArgs(request, _lastResponseHex));

    private static byte[]? ParseHex(string text)
    {
        string clean = new(text.Where(Uri.IsHexDigit).ToArray());
        if (clean.Length == 0 || clean.Length % 2 != 0)
        {
            return null;
        }

        try
        {
            return Convert.FromHexString(clean);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private async Task DisposeModbusAsync()
    {
        if (_modbus is not null)
        {
            await _modbus.DisposeAsync().ConfigureAwait(false);
            _modbus = null;
        }
    }

    public async ValueTask DisposeAsync() => await DisposeModbusAsync().ConfigureAwait(false);
}
