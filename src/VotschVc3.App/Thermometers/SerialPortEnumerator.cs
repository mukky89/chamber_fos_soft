using System.IO;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.App.Thermometers;

/// <summary>
/// Enumerates serial / USB COM ports together with their USB serial number.
/// Fast UI paths only use <see cref="SerialPort.GetPortNames"/> and cached metadata;
/// WMI enrichment is reserved for explicit/background detailed scans so opening a
/// calibration window never waits on Win32_PnPEntity.
/// </summary>
public static class SerialPortEnumerator
{
    private static readonly Regex ComInName = new(@"\((COM\d+)\)", RegexOptions.Compiled);
    private static readonly object CacheGate = new();
    private static Dictionary<string, SerialDeviceInfo> _metadataCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SerialDeviceInfo> EnumerateFast()
    {
        string[] ports;
        try
        {
            ports = SerialPort.GetPortNames();
        }
        catch
        {
            return Array.Empty<SerialDeviceInfo>();
        }

        Dictionary<string, SerialDeviceInfo> cache;
        lock (CacheGate)
        {
            cache = new Dictionary<string, SerialDeviceInfo>(_metadataCache, StringComparer.OrdinalIgnoreCase);
        }

        return ports
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(port => cache.TryGetValue(port, out SerialDeviceInfo? info)
                ? info
                : new SerialDeviceInfo(port, null, null))
            .ToList();
    }

    /// <summary>
    /// Returns the latest WMI-enriched label without touching the COM port. This is used by the FBG
    /// picker so a connected WIKA can keep its serial handle while the UI still shows USB serial /
    /// PnP description learned by a background scan.
    /// </summary>
    public static bool TryGetCachedInfo(string? portName, out SerialDeviceInfo info)
    {
        if (!string.IsNullOrWhiteSpace(portName))
        {
            lock (CacheGate)
            {
                if (_metadataCache.TryGetValue(portName, out SerialDeviceInfo? cached))
                {
                    info = cached;
                    return true;
                }
            }
        }

        info = new SerialDeviceInfo(portName ?? string.Empty, null, null);
        return false;
    }

    public static IReadOnlyList<SerialDeviceInfo> Enumerate()
    {
        try
        {
            IReadOnlyList<SerialDeviceInfo> result = EnumerateViaWmi();
            UpdateCache(result);
            return result;
        }
        catch
        {
            return EnumerateFast();
        }
    }

    private static void UpdateCache(IEnumerable<SerialDeviceInfo> devices)
    {
        var next = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.PortName))
            .ToDictionary(device => device.PortName, device => device, StringComparer.OrdinalIgnoreCase);
        lock (CacheGate)
        {
            _metadataCache = next;
        }
    }

    private static List<SerialDeviceInfo> EnumerateViaWmi()
    {
        var result = new List<SerialDeviceInfo>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DeviceID, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");

        foreach (ManagementBaseObject device in searcher.Get())
        {
            string? name = device["Name"] as string;
            string? pnpId = device["PNPDeviceID"] as string ?? device["DeviceID"] as string;
            if (name is null) continue;

            Match m = ComInName.Match(name);
            if (!m.Success) continue;

            string port = m.Groups[1].Value;
            string description = name.Replace($"({port})", string.Empty).Trim();
            result.Add(new SerialDeviceInfo(port, ExtractSerial(pnpId), description));
        }

        foreach (string port in SerialPort.GetPortNames())
        {
            if (!result.Any(d => string.Equals(d.PortName, port, StringComparison.OrdinalIgnoreCase)))
                result.Add(new SerialDeviceInfo(port, null, null));
        }

        return result
            .OrderBy(d => d.PortName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ExtractSerial(string? pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId)) return null;
        string[] segments = pnpDeviceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        string last = segments.LastOrDefault() ?? string.Empty;

        if (last.All(char.IsDigit) && last.All(c => c == '0') && segments.Length >= 2)
        {
            string parent = segments[^2];
            int separator = parent.LastIndexOf('+');
            if (separator >= 0 && separator + 1 < parent.Length)
            {
                string parentIdentity = parent[(separator + 1)..].Trim();
                if (parentIdentity.Length > 0) return parentIdentity;
            }
        }

        if (last.Length == 0 || last.Contains('&')) return null;
        return last;
    }

    [Obsolete("Legacy talk-only thermometer diagnostic is retired; this method performs no probe.")]
    public static Task<IReadOnlyList<string>> DiagnoseTalkOnlyAsync(
        IEnumerable<SerialDeviceInfo> devices,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(new[]
        {
            "Pasívny test starého teplomera bol odstránený. WIKA CTH7000 používa aktívne SCPI čítanie."
        });
    }

    public static IReadOnlyList<string> DiagnoseUsb()
    {
        var lines = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Status, ConfigManagerErrorCode, PNPDeviceID, Manufacturer, Service " +
                "FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB%' OR Name LIKE '%(COM%'");

            foreach (ManagementBaseObject device in searcher.Get())
            {
                string name = device["Name"] as string ?? "Neznáme USB zariadenie";
                string pnpId = device["PNPDeviceID"] as string ?? string.Empty;
                string manufacturer = device["Manufacturer"] as string ?? "—";
                string service = device["Service"] as string ?? "—";
                uint error = device["ConfigManagerErrorCode"] is uint code ? code : 0;
                bool relevant = ComInName.IsMatch(name) ||
                    name.Contains("CTH7000", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("USB Serial", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("FTDI", StringComparison.OrdinalIgnoreCase) ||
                    manufacturer.Contains("FTDI", StringComparison.OrdinalIgnoreCase);
                if (!relevant) continue;

                string state = error == 0 ? "OK" : $"CHYBA {error}: {ConfigManagerErrorText(error)}";
                lines.Add($"{name} · {state} · ovládač {service} · {manufacturer} · {pnpId}");
            }
        }
        catch (Exception ex)
        {
            lines.Add($"Windows PnP diagnostika zlyhala: {ex.Message}");
        }

        string[] ports = SerialPort.GetPortNames().OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        lines.Insert(0, ports.Length == 0
            ? "SerialPort API: žiadny COM port"
            : $"SerialPort API: {string.Join(", ", ports)}");
        if (lines.Count == 1) lines.Add("Windows nenašiel žiadny relevantný USB sériový adaptér.");
        return lines;
    }

    private static string ConfigManagerErrorText(uint code) => code switch
    {
        1 => "zariadenie nie je správne nakonfigurované",
        10 => "zariadenie sa nedá spustiť",
        22 => "zariadenie je vypnuté",
        28 => "ovládač nie je nainštalovaný",
        31 => "Windows nevie načítať potrebný ovládač",
        43 => "zariadenie nahlásilo problém",
        45 => "zariadenie nie je momentálne pripojené",
        _ => "pozri Správcu zariadení",
    };
}
