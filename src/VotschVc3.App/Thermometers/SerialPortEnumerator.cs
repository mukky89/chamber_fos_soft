using System.IO;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.App.Thermometers;

/// <summary>
/// Enumerates serial / USB COM ports together with their USB serial number.
/// Uses WMI (<c>Win32_PnPEntity</c>); falls back to a plain port list when WMI is unavailable.
/// </summary>
public static class SerialPortEnumerator
{
    private static readonly Regex ComInName = new(@"\((COM\d+)\)", RegexOptions.Compiled);

    public static IReadOnlyList<SerialDeviceInfo> Enumerate()
    {
        try
        {
            return EnumerateViaWmi();
        }
        catch
        {
            return SerialPort.GetPortNames()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p => new SerialDeviceInfo(p, null, null))
                .ToList();
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
            {
                result.Add(new SerialDeviceInfo(port, null, null));
            }
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

    /// <summary>
    /// Opens candidate USB serial ports without transmitting anything and listens for
    /// a legacy talk-only frame at supported baud rates. A port already owned by an
    /// active F100Client is skipped instead of racing the live measurement connection.
    /// </summary>
    public static async Task<IReadOnlyList<string>> DiagnoseTalkOnlyAsync(
        IEnumerable<SerialDeviceInfo> devices,
        CancellationToken cancellationToken = default)
    {
        SerialDeviceInfo[] candidates = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.PortName))
            .GroupBy(device => device.PortName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (candidates.Length == 0)
        {
            return new[] { "Pasívny test: nie je dostupný žiadny kandidátsky COM port." };
        }

        Task<IReadOnlyList<string>>[] probes = candidates
            .Select(device => Task.Run(() => ProbeTalkOnly(device, cancellationToken), cancellationToken))
            .ToArray();
        IReadOnlyList<string>[] results = await Task.WhenAll(probes).ConfigureAwait(false);
        return results.SelectMany(lines => lines).ToList();
    }

    private static IReadOnlyList<string> ProbeTalkOnly(SerialDeviceInfo device, CancellationToken token)
    {
        var lines = new List<string>();

        // Never open a port that the live application connection already owns.
        if (!SerialPortLease.TryAcquire(device.PortName, out SerialPortLease? lease))
        {
            return new[] { $"{device.PortName}: OBSADENÝ aplikáciou – pasívna diagnostika ho neotvára." };
        }

        using (lease)
        {
            foreach (int baudRate in F100Protocol.BaudRates)
            {
                token.ThrowIfCancellationRequested();
                using var port = new SerialPort(device.PortName, baudRate, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    DtrEnable = true,
                    RtsEnable = true,
                    ReadTimeout = 500,
                    WriteTimeout = 500,
                };
                try
                {
                    port.Open();
                    var received = new System.Text.StringBuilder();
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (clock.Elapsed < TimeSpan.FromSeconds(3))
                    {
                        token.ThrowIfCancellationRequested();
                        string chunk = port.ReadExisting();
                        if (chunk.Length > 0) received.Append(chunk);
                        if (received.ToString().IndexOfAny(new[] { '\r', '\n' }) >= 0) break;
                        Thread.Sleep(100);
                    }

                    if (received.Length == 0)
                    {
                        lines.Add($"{device.PortName} @ {baudRate}: port otvorený, talk-only dáta neprišli.");
                        continue;
                    }

                    string sample = received.ToString()
                        .Replace("\r", "<CR>", StringComparison.Ordinal)
                        .Replace("\n", "<LF>", StringComparison.Ordinal);
                    if (sample.Length > 160) sample = sample[..160] + "…";
                    lines.Add($"{device.PortName} @ {baudRate}: DATA OK · {sample}");
                    break;
                }
                catch (UnauthorizedAccessException)
                {
                    lines.Add($"{device.PortName}: OBSADENÝ – port drží iná aplikácia alebo služba.");
                    break;
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException)
                {
                    lines.Add($"{device.PortName} @ {baudRate}: chyba portu – {ex.Message}");
                    break;
                }
            }
        }

        if (!lines.Any(line => line.Contains("DATA OK", StringComparison.Ordinal)) &&
            !lines.Any(line => line.Contains("OBSADENÝ", StringComparison.Ordinal)))
        {
            lines.Add($"{device.PortName}: žiadne pasívne dáta. Na staršom ASL F100 skontroluj Menu → Options → Talk Only → On.");
        }
        return lines;
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
                    name.Contains("F100", StringComparison.OrdinalIgnoreCase) ||
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
