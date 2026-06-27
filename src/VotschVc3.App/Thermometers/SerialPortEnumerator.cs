using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using VotschVc3.Core.Thermometers;

namespace VotschVc3.App.Thermometers;

/// <summary>
/// Enumerates serial / USB COM ports together with their USB serial number, so
/// several identical ASL F100 units can be told apart. Uses WMI
/// (<c>Win32_PnPEntity</c>); falls back to a plain port list when WMI is
/// unavailable.
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
            // WMI can be disabled or unavailable – degrade gracefully.
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
            if (name is null)
            {
                continue;
            }

            Match m = ComInName.Match(name);
            if (!m.Success)
            {
                continue;
            }

            string port = m.Groups[1].Value;
            string description = name.Replace($"({port})", string.Empty).Trim();
            result.Add(new SerialDeviceInfo(port, ExtractSerial(pnpId), description));
        }

        // Make sure ports without a friendly PnP entry are still listed.
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

    /// <summary>
    /// Pulls the device serial number out of a USB PnP id such as
    /// <c>USB\VID_0403&amp;PID_6001\FT3AB12X</c> – the last path segment.
    /// </summary>
    private static string? ExtractSerial(string? pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            return null;
        }

        string last = pnpDeviceId.Split('\\').LastOrDefault() ?? string.Empty;

        // Composite-device entries use "&" (e.g. "6&1abc&0&2"); those are not real
        // serial numbers, so ignore them.
        if (last.Length == 0 || last.Contains('&'))
        {
            return null;
        }

        return last;
    }
}
