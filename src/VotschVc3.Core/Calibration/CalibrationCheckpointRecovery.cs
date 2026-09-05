namespace VotschVc3.Core.Calibration;

/// <summary>Repairs a setup that lost its operator wiring by using the immutable run checkpoint.</summary>
public static class CalibrationCheckpointRecovery
{
    public static bool RestoreMappingsIfMissing(CalibrationSetup setup, CalibrationCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(checkpoint);

        bool setupHasUsableSelection = setup.Mappings.Any(mapping =>
            mapping.Selected && !string.IsNullOrWhiteSpace(mapping.SerialNumber));
        List<CalibrationSensorMapping> checkpointMappings = checkpoint.Mappings
            .Where(mapping => mapping.Selected && !string.IsNullOrWhiteSpace(mapping.SerialNumber))
            .Select(CloneMapping)
            .ToList();
        if (setupHasUsableSelection || checkpointMappings.Count == 0) return false;

        setup.Mappings = checkpointMappings;
        return true;
    }

    private static CalibrationSensorMapping CloneMapping(CalibrationSensorMapping mapping) => new()
    {
        Channel = mapping.Channel,
        Core1 = mapping.Core1,
        Core2 = mapping.Core2,
        SerialNumber = mapping.SerialNumber,
        ChannelSerialNumber = mapping.ChannelSerialNumber,
        ChainSerialNumber = mapping.ChainSerialNumber,
        PeakLoggerDeviceSerialNumber = mapping.PeakLoggerDeviceSerialNumber,
        PeakId = mapping.PeakId,
        PeakIndex = mapping.PeakIndex,
        NominalWavelengthNm = mapping.NominalWavelengthNm,
        CurrentWavelengthNm = mapping.CurrentWavelengthNm,
        Selected = mapping.Selected,
        Notes = mapping.Notes,
        ProductDescription = mapping.ProductDescription,
        Customer = mapping.Customer,
        Order = mapping.Order,
        StabilizationTimeoutOverride = mapping.StabilizationTimeoutOverride,
    };
}
