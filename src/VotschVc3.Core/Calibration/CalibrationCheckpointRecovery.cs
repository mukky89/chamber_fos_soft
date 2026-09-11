namespace VotschVc3.Core.Calibration;

/// <summary>Repairs a setup that lost its operator wiring by using the immutable run checkpoint.</summary>
public static class CalibrationCheckpointRecovery
{
    public static CalibrationCheckpoint CreateFromHistoricalRun(
        CalibrationRunRecord run,
        CalibrationSetup setup)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(setup);
        if (run.RunId == Guid.Empty || run.ProfileId == Guid.Empty || run.ChamberId == Guid.Empty)
            throw new InvalidOperationException("Historický beh nemá platnú identitu kalibrácie.");
        if (setup.ProfileId != run.ProfileId || setup.ChamberId != run.ChamberId)
            throw new InvalidOperationException("Vybraný historický beh nepatrí k aktuálnemu profilu a komore.");
        if (run.Plateaus.Count == 0)
            throw new InvalidOperationException("Historický beh neobsahuje žiadne dokončené plato.");

        List<CalibrationSensorMapping> mappings = setup.ActiveMappings
            .Where(mapping => !setup.IsChannelIgnored(mapping.Channel) && mapping.Selected && !string.IsNullOrWhiteSpace(mapping.SerialNumber))
            .Select(CloneMapping)
            .ToList();
        if (mappings.Count == 0)
        {
            mappings = run.Plateaus
                .OrderByDescending(plateau => plateau.PlateauIndex)
                .SelectMany(plateau => plateau.Targets)
                .Where(target => !setup.IsChannelIgnored(target.Channel) && !string.IsNullOrWhiteSpace(target.SerialNumber))
                .GroupBy(
                    target => $"{target.PeakLoggerDeviceSerialNumber}|{target.Channel}|{target.PeakId}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(target => new CalibrationSensorMapping
                {
                    Selected = true,
                    SerialNumber = target.SerialNumber,
                    ChannelSerialNumber = target.SerialNumber,
                    PeakLoggerDeviceSerialNumber = target.PeakLoggerDeviceSerialNumber,
                    Channel = target.Channel,
                    PeakId = target.PeakId,
                    PeakIndex = target.PeakIndex,
                    NominalWavelengthNm = target.MeanWavelengthNm,
                    CurrentWavelengthNm = target.MeanWavelengthNm,
                })
                .ToList();
        }
        if (mappings.Count == 0)
            throw new InvalidOperationException("Pre historický beh chýba uložené zapojenie vybraných FBG peakov.");

        return new CalibrationCheckpoint
        {
            RunId = run.RunId,
            ProfileId = run.ProfileId,
            ChamberId = run.ChamberId,
            CurrentPlateauIndex = run.Plateaus.Count,
            CurrentTargetTemperatureC = run.Plateaus.LastOrDefault()?.TargetTemperatureC,
            State = CalibrationRunState.Aborted,
            CompletedPlateaus = run.Plateaus.ToList(),
            Mappings = mappings,
            SettingsSnapshot = CloneSettings(setup.Settings),
            CalibrationSegmentIndices = setup.CalibrationSegmentIndices.ToList(),
        };
    }

    public static bool RestoreRunConfiguration(CalibrationSetup setup, CalibrationCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.SettingsSnapshot is null) return false;

        setup.Settings = CloneSettings(checkpoint.SettingsSnapshot);
        if (checkpoint.CalibrationSegmentIndices.Count > 0)
            setup.CalibrationSegmentIndices = checkpoint.CalibrationSegmentIndices.ToList();
        return true;
    }

    /// <summary>
    /// Explicitly replaces the interrupted run's decision settings with the current administrator
    /// defaults. Completed plateaus, wiring and the selected calibration plan remain untouched.
    /// </summary>
    public static void ApplyCurrentDefaults(
        CalibrationSetup setup,
        CalibrationCheckpoint checkpoint,
        CalibrationProfileSettings currentDefaults)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(currentDefaults);

        setup.Settings = CloneSettings(currentDefaults);
        checkpoint.SettingsSnapshot = CloneSettings(currentDefaults);
    }

    public static CalibrationProfileSettings CloneSettings(CalibrationProfileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new CalibrationProfileSettings
        {
            EnableSetpointRamp = settings.EnableSetpointRamp,
            SetpointRampCPerMinute = settings.SetpointRampCPerMinute,
            EnableWavelengthAveraging = settings.EnableWavelengthAveraging,
            WavelengthAveragingSamples = settings.WavelengthAveragingSamples,
            EnableWavelengthTraceLogging = settings.EnableWavelengthTraceLogging,
            WavelengthTraceIntervalSeconds = settings.WavelengthTraceIntervalSeconds,
            SampleAcquisitionIntervalSeconds = settings.SampleAcquisitionIntervalSeconds,
            RequiredStableSamples = settings.RequiredStableSamples,
            RequiredMeasurementSamples = settings.RequiredMeasurementSamples,
            MaxWavelengthRangePm = settings.MaxWavelengthRangePm,
            MaxWavelengthStdDevPm = settings.MaxWavelengthStdDevPm,
            MaxWavelengthDriftPmPerMinute = settings.MaxWavelengthDriftPmPerMinute,
            AppliedChamberEntryDefaults = settings.AppliedChamberEntryDefaults,
            ChamberEntryEnabled = settings.ChamberEntryEnabled,
            ChamberEntryToleranceC = settings.ChamberEntryToleranceC,
            ChamberEntryStableSeconds = settings.ChamberEntryStableSeconds,
            ChamberEntryRangeC = settings.ChamberEntryRangeC,
            ChamberEntryDriftCPerMinute = settings.ChamberEntryDriftCPerMinute,
            ChamberToleranceC = settings.ChamberToleranceC,
            ChamberStableDuration = settings.ChamberStableDuration,
            MaxChamberDriftCPerMinute = settings.MaxChamberDriftCPerMinute,
            MaxChamberRangeC = settings.MaxChamberRangeC,
            MaxChamberStdDevC = settings.MaxChamberStdDevC,
            ChamberStabilityTimeout = settings.ChamberStabilityTimeout,
            ChamberStabilityExtensionStep = settings.ChamberStabilityExtensionStep,
            MaxAutomaticChamberStabilityExtension = settings.MaxAutomaticChamberStabilityExtension,
            FinalConditioningTemperatureC = settings.FinalConditioningTemperatureC,
            FinalConditioningDuration = settings.FinalConditioningDuration,
            FinalConditioningToleranceC = settings.FinalConditioningToleranceC,
            OperatorSupervisionEnabled = settings.OperatorSupervisionEnabled,
            DefaultSensorStabilizationTimeout = settings.DefaultSensorStabilizationTimeout,
            SensorTimeoutPolicy = settings.SensorTimeoutPolicy,
            PeakLostPolicy = settings.PeakLostPolicy,
            PeakLoggerDisconnectPolicy = settings.PeakLoggerDisconnectPolicy,
            ValidationMinimumDeltaTemperatureC = settings.ValidationMinimumDeltaTemperatureC,
            ValidationMinimumWavelengthResponsePm = settings.ValidationMinimumWavelengthResponsePm,
            ExpectedResponseDirection = settings.ExpectedResponseDirection,
            ValidationFailurePolicy = settings.ValidationFailurePolicy,
            AllowValidationOverride = settings.AllowValidationOverride,
            ValidationOverrideReason = settings.ValidationOverrideReason,
            PeakLostGracePeriod = settings.PeakLostGracePeriod,
        };
    }

    public static bool RestoreMappingsIfMissing(CalibrationSetup setup, CalibrationCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(checkpoint);

        bool setupHasUsableSelection = setup.ActiveMappings.Any(mapping =>
            mapping.Selected && !string.IsNullOrWhiteSpace(mapping.SerialNumber));
        List<CalibrationSensorMapping> checkpointMappings = checkpoint.Mappings
            .Where(mapping => !setup.IsChannelIgnored(mapping.Channel) && mapping.Selected && !string.IsNullOrWhiteSpace(mapping.SerialNumber))
            .Select(CloneMapping)
            .ToList();
        if (setupHasUsableSelection || checkpointMappings.Count == 0) return false;

        setup.Mappings = setup.Mappings.Where(m => setup.IsChannelIgnored(m.Channel)).Concat(checkpointMappings).ToList();
        return true;
    }

    private static CalibrationSensorMapping CloneMapping(CalibrationSensorMapping mapping) => new()
    {
        Channel = mapping.Channel,
        Core1 = mapping.Core1,
        Core2 = mapping.Core2,
        SerialNumber = mapping.SerialNumber,
        SensorName = mapping.SensorName,
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
