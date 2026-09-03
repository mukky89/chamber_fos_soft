using System.IO;
using System.Text.Json;
using VotschVc3.Core.Diagnostics;

namespace VotschVc3.App.ViewModels;

/// <summary>
/// Application-wide source of truth for the CTH7000 assigned to each FBG calibration workspace.
///
/// Assignment and connection are deliberately different concepts:
/// - assignment is persistent and exclusive across chambers;
/// - live temperature is transient and may be empty while the USB thermometer is unplugged.
///
/// This prevents the same physical reference thermometer from being accidentally used by two
/// independent FBG calibrations even when neither one is currently running.
/// </summary>
public sealed class CalibrationReferenceStatusStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private readonly Dictionary<Guid, ReferenceState> _states = new();
    private readonly string _path;

    public static CalibrationReferenceStatusStore Instance { get; } = new();

    private CalibrationReferenceStatusStore()
    {
        AppPaths.Initialize();
        _path = Path.Combine(AppPaths.SettingsDir, "fbg-reference-thermometers.json");
        LoadAssignments();
    }

    public event EventHandler<CalibrationReferenceChangedEventArgs>? Changed;

    /// <summary>
    /// Persistently assigns one physical thermometer to a chamber. A physical device is matched
    /// primarily by Windows USB serial number; COM port is the fallback and is also always
    /// checked so a device with temporarily unavailable USB metadata cannot be double-assigned.
    /// </summary>
    public bool TryAssign(
        Guid chamberId,
        string chamberName,
        string portName,
        string? usbSerialNumber,
        string channel,
        out string occupiedBy)
    {
        string port = NormalizePort(portName);
        string serial = NormalizeSerial(usbSerialNumber);
        ReferenceState updated;

        lock (_gate)
        {
            ReferenceState? conflict = _states.Values.FirstOrDefault(state =>
                state.ChamberId != chamberId && SamePhysicalThermometer(state, port, serial));
            if (conflict is not null)
            {
                occupiedBy = conflict.ChamberName;
                return false;
            }

            _states.TryGetValue(chamberId, out ReferenceState? current);
            updated = new ReferenceState
            {
                ChamberId = chamberId,
                ChamberName = string.IsNullOrWhiteSpace(chamberName) ? "Neznáme zariadenie" : chamberName.Trim(),
                PortName = port,
                UsbSerialNumber = serial,
                Channel = NormalizeChannel(channel),
                TemperatureC = current is not null && SamePhysicalThermometer(current, port, serial)
                    ? current.TemperatureC
                    : null,
                IsConnected = current is not null && SamePhysicalThermometer(current, port, serial) && current.IsConnected,
                LastUpdate = current is not null && SamePhysicalThermometer(current, port, serial)
                    ? current.LastUpdate
                    : null,
            };
            _states[chamberId] = updated;
            SaveAssignmentsUnsafe();
            occupiedBy = updated.ChamberName;
        }

        RaiseChanged(chamberId);
        return true;
    }

    public void PublishReading(
        Guid chamberId,
        string portName,
        string? usbSerialNumber,
        string channel,
        double? temperatureC,
        bool isConnected)
    {
        bool changed = false;
        lock (_gate)
        {
            if (!_states.TryGetValue(chamberId, out ReferenceState? state)) return;

            string port = NormalizePort(portName);
            string serial = NormalizeSerial(usbSerialNumber);
            string normalizedChannel = NormalizeChannel(channel);
            if (!SamePhysicalThermometer(state, port, serial)) return;

            bool persistentMetadataChanged =
                !string.Equals(state.PortName, port, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(serial) &&
                 !string.Equals(state.UsbSerialNumber, serial, StringComparison.OrdinalIgnoreCase)) ||
                !string.Equals(state.Channel, normalizedChannel, StringComparison.OrdinalIgnoreCase);

            state.PortName = port;
            if (!string.IsNullOrWhiteSpace(serial)) state.UsbSerialNumber = serial;
            state.Channel = normalizedChannel;
            state.IsConnected = isConnected;
            state.TemperatureC = isConnected ? temperatureC : null;
            state.LastUpdate = temperatureC is not null ? DateTimeOffset.Now : state.LastUpdate;
            if (persistentMetadataChanged) SaveAssignmentsUnsafe();
            changed = true;
        }

        if (changed) RaiseChanged(chamberId);
    }

    /// <summary>Marks live data unavailable while preserving the persistent assignment.</summary>
    public void MarkDisconnected(Guid chamberId)
    {
        bool changed = false;
        lock (_gate)
        {
            if (_states.TryGetValue(chamberId, out ReferenceState? state) &&
                (state.IsConnected || state.TemperatureC is not null))
            {
                state.IsConnected = false;
                state.TemperatureC = null;
                changed = true;
            }
        }

        if (changed) RaiseChanged(chamberId);
    }

    /// <summary>
    /// Explicitly frees the assignment so the thermometer can be selected by another chamber.
    /// This should only be called by an intentional operator action, never on USB unplug/read
    /// timeout/window hide/application shutdown.
    /// </summary>
    public void ReleaseAssignment(Guid chamberId)
    {
        bool removed;
        lock (_gate)
        {
            removed = _states.Remove(chamberId);
            if (removed) SaveAssignmentsUnsafe();
        }

        if (removed) RaiseChanged(chamberId);
    }

    public CalibrationReferenceSnapshot GetSnapshot(Guid chamberId)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(chamberId, out ReferenceState? state))
            {
                return CalibrationReferenceSnapshot.Empty(chamberId);
            }

            return state.ToSnapshot();
        }
    }

    public bool IsAssignedToOtherChamber(
        Guid chamberId,
        string portName,
        string? usbSerialNumber,
        out string occupiedBy)
    {
        string port = NormalizePort(portName);
        string serial = NormalizeSerial(usbSerialNumber);
        lock (_gate)
        {
            ReferenceState? conflict = _states.Values.FirstOrDefault(state =>
                state.ChamberId != chamberId && SamePhysicalThermometer(state, port, serial));
            occupiedBy = conflict?.ChamberName ?? string.Empty;
            return conflict is not null;
        }
    }

    private void LoadAssignments()
    {
        try
        {
            if (!File.Exists(_path)) return;
            string json = File.ReadAllText(_path);
            List<PersistedAssignment>? saved = JsonSerializer.Deserialize<List<PersistedAssignment>>(json, JsonOptions);
            if (saved is null) return;

            foreach (PersistedAssignment item in saved)
            {
                if (item.ChamberId == Guid.Empty || string.IsNullOrWhiteSpace(item.PortName)) continue;
                _states[item.ChamberId] = new ReferenceState
                {
                    ChamberId = item.ChamberId,
                    ChamberName = string.IsNullOrWhiteSpace(item.ChamberName) ? "Neznáme zariadenie" : item.ChamberName.Trim(),
                    PortName = NormalizePort(item.PortName),
                    UsbSerialNumber = NormalizeSerial(item.UsbSerialNumber),
                    Channel = NormalizeChannel(item.Channel),
                    // Live data intentionally does not survive app restart.
                    TemperatureC = null,
                    IsConnected = false,
                    LastUpdate = null,
                };
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("FBG referencia", $"Načítanie priradení CTH7000: {ex.Message}");
        }
    }

    private void SaveAssignmentsUnsafe()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            List<PersistedAssignment> saved = _states.Values
                .OrderBy(state => state.ChamberName, StringComparer.OrdinalIgnoreCase)
                .Select(state => new PersistedAssignment
                {
                    ChamberId = state.ChamberId,
                    ChamberName = state.ChamberName,
                    PortName = state.PortName,
                    UsbSerialNumber = state.UsbSerialNumber,
                    Channel = state.Channel,
                })
                .ToList();

            string tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(saved, JsonOptions));
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Warn("FBG referencia", $"Uloženie priradení CTH7000: {ex.Message}");
        }
    }

    private void RaiseChanged(Guid chamberId) =>
        Changed?.Invoke(this, new CalibrationReferenceChangedEventArgs(chamberId));

    private static bool SamePhysicalThermometer(ReferenceState state, string port, string serial)
    {
        if (!string.IsNullOrWhiteSpace(serial) &&
            !string.IsNullOrWhiteSpace(state.UsbSerialNumber) &&
            string.Equals(serial, state.UsbSerialNumber, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(port) &&
               string.Equals(port, state.PortName, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePort(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static string NormalizeSerial(string? value) => (value ?? string.Empty).Trim();
    private static string NormalizeChannel(string? value)
    {
        string normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized is "B" ? "B" : "A";
    }

    private sealed class ReferenceState
    {
        public Guid ChamberId { get; init; }
        public string ChamberName { get; set; } = string.Empty;
        public string PortName { get; set; } = string.Empty;
        public string UsbSerialNumber { get; set; } = string.Empty;
        public string Channel { get; set; } = "A";
        public double? TemperatureC { get; set; }
        public bool IsConnected { get; set; }
        public DateTimeOffset? LastUpdate { get; set; }

        public CalibrationReferenceSnapshot ToSnapshot() => new(
            ChamberId,
            ChamberName,
            PortName,
            UsbSerialNumber,
            Channel,
            TemperatureC,
            IsConnected,
            LastUpdate);
    }

    private sealed class PersistedAssignment
    {
        public Guid ChamberId { get; set; }
        public string ChamberName { get; set; } = string.Empty;
        public string PortName { get; set; } = string.Empty;
        public string UsbSerialNumber { get; set; } = string.Empty;
        public string Channel { get; set; } = "A";
    }
}

public sealed record CalibrationReferenceSnapshot(
    Guid ChamberId,
    string ChamberName,
    string PortName,
    string UsbSerialNumber,
    string Channel,
    double? TemperatureC,
    bool IsConnected,
    DateTimeOffset? LastUpdate)
{
    public bool IsAssigned => !string.IsNullOrWhiteSpace(PortName);
    public string TemperatureText => IsConnected && TemperatureC is { } value ? $"{value:F3}" : "—";
    public string PortText => IsAssigned ? PortName : string.Empty;

    public static CalibrationReferenceSnapshot Empty(Guid chamberId) =>
        new(chamberId, string.Empty, string.Empty, string.Empty, "A", null, false, null);
}

public sealed class CalibrationReferenceChangedEventArgs(Guid chamberId) : EventArgs
{
    public Guid ChamberId { get; } = chamberId;
}
