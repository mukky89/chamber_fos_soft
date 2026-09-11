namespace VotschVc3.Core.Calibration;

/// <summary>A partial/disconnected UI is not an instruction to erase saved source identities.</summary>
public static class CalibrationWiringPersistence
{
    public static List<CalibrationSensorMapping> MergeVisibleMappings(
        IEnumerable<CalibrationSensorMapping> saved, IEnumerable<CalibrationSensorMapping> visible) =>
        saved.Concat(visible).GroupBy(m => m.SourceIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last()).ToList();

    // Older setup files only had SerialNumber. An explicit CHAIN remains per-peak.
    public static string ChannelSerial(CalibrationSensorMapping? mapping) => mapping is null ? string.Empty :
        !string.IsNullOrWhiteSpace(mapping.ChannelSerialNumber) ? mapping.ChannelSerialNumber :
        string.IsNullOrWhiteSpace(mapping.ChainSerialNumber) ? mapping.SerialNumber ?? string.Empty : string.Empty;
}
