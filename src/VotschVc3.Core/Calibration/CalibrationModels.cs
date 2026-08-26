using System.Text.Json.Serialization;

namespace VotschVc3.Core.Calibration;

public enum CalibrationRunState
{
    Idle,
    Preflight,
    Preparing,
    BaselineCollection,
    TemperatureResponseValidation,
    MovingToPlateau,
    WaitingForChamberStability,
    StabilizingSensors,
    PlateauCompleted,
    MovingToNextPlateau,
    Paused,
    AwaitingOperator,
    Completed,
    CompletedWithWarnings,
    Aborted,
    Failed,
}

public enum CalibrationTargetState
{
    Waiting,
    Missing,
    Live,
    WaitingForTemperature,
    Stabilizing,
    Stable,
    NoTemperatureResponse,
    TimedOut,
    PeakLost,
    Disconnected,
    Overridden,
    Failed,
}

public enum CalibrationFailurePolicy
{
    ContinueAndFlag,
    PauseForOperator,
    AbortCalibration,
}

public enum ExpectedResponseDirection
{
    Any,
    Positive,
    Negative,
}

public sealed class PeakLoggerSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; }
    public string? AuthenticationToken { get; set; }
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan StaleDataTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public int MaxReconnectAttempts { get; set; }
    public bool UseSimulator { get; set; } = true;
}

public sealed class CalibrationProfileSettings
{
    public int RequiredStableSamples { get; set; } = 50;

    /// <summary>0 disables the criterion. Unit: pm.</summary>
    public double MaxWavelengthRangePm { get; set; } = 5.0;

    /// <summary>0 disables the criterion. Unit: pm.</summary>
    public double MaxWavelengthStdDevPm { get; set; } = 1.5;

    /// <summary>0 disables the criterion. Unit: pm/min.</summary>
    public double MaxWavelengthDriftPmPerMinute { get; set; } = 1.0;

    public double ChamberToleranceC { get; set; } = 0.5;
    public TimeSpan ChamberStableDuration { get; set; } = TimeSpan.FromMinutes(1);
    public double MaxChamberDriftCPerMinute { get; set; } = 0.1;
    public TimeSpan ChamberStabilityTimeout { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan DefaultSensorStabilizationTimeout { get; set; } = TimeSpan.FromMinutes(60);
    public CalibrationFailurePolicy SensorTimeoutPolicy { get; set; } = CalibrationFailurePolicy.ContinueAndFlag;
    public CalibrationFailurePolicy PeakLostPolicy { get; set; } = CalibrationFailurePolicy.PauseForOperator;
    public CalibrationFailurePolicy PeakLoggerDisconnectPolicy { get; set; } = CalibrationFailurePolicy.PauseForOperator;

    public double ValidationMinimumDeltaTemperatureC { get; set; } = 5.0;
    public double ValidationMinimumWavelengthResponsePm { get; set; } = 5.0;
    public ExpectedResponseDirection ExpectedResponseDirection { get; set; } = ExpectedResponseDirection.Any;
    public CalibrationFailurePolicy ValidationFailurePolicy { get; set; } = CalibrationFailurePolicy.PauseForOperator;
    public bool AllowValidationOverride { get; set; }
    public string ValidationOverrideReason { get; set; } = string.Empty;

    public TimeSpan PeakLostGracePeriod { get; set; } = TimeSpan.FromMinutes(2);
}

public sealed class CalibrationSensorMapping
{
    public string Channel { get; set; } = string.Empty;
    public int? Core1 { get; set; }
    public int? Core2 { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string PeakId { get; set; } = string.Empty;
    public int PeakIndex { get; set; }
    public double? NominalWavelengthNm { get; set; }
    public double? CurrentWavelengthNm { get; set; }
    public bool Selected { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string ProductDescription { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public string Order { get; set; } = string.Empty;
    public TimeSpan? StabilizationTimeoutOverride { get; set; }

    [JsonIgnore]
    public string Identity => $"{SerialNumber}|{Channel}|{PeakId}";
}

public sealed class CalibrationSetup
{
    public Guid ProfileId { get; set; }
    public List<CalibrationSensorMapping> Mappings { get; set; } = new();
    public CalibrationProfileSettings Settings { get; set; } = new();
}

public sealed record PeakLoggerPeak(
    string PeakId,
    int PeakIndex,
    double WavelengthNm,
    double? Intensity = null);

public sealed record PeakLoggerSensor(
    string SerialNumber,
    string Channel,
    IReadOnlyList<PeakLoggerPeak> Peaks);

public sealed record PeakLoggerMeasurement(
    DateTimeOffset Timestamp,
    string SerialNumber,
    string Channel,
    string PeakId,
    int PeakIndex,
    double WavelengthNm,
    double? Intensity = null);

public sealed class CalibrationRawSample
{
    public Guid RunId { get; set; }
    public Guid ProfileId { get; set; }
    public int PlateauIndex { get; set; }
    public double TargetTemperatureC { get; set; }
    public double ActualTemperatureC { get; set; }
    public double? ReferenceTemperatureC { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string PeakId { get; set; } = string.Empty;
    public int PeakIndex { get; set; }
    public double WavelengthNm { get; set; }
    public double? Intensity { get; set; }
}

public sealed class CalibrationMeasurementResult
{
    public string SerialNumber { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string PeakId { get; set; } = string.Empty;
    public int PeakIndex { get; set; }
    public CalibrationTargetState Status { get; set; }
    public int SampleCount { get; set; }
    public double MeanWavelengthNm { get; set; }
    public double MedianWavelengthNm { get; set; }
    public double MinWavelengthNm { get; set; }
    public double MaxWavelengthNm { get; set; }
    public double RangePm { get; set; }
    public double StandardDeviationPm { get; set; }
    public double DriftPmPerMinute { get; set; }
    public TimeSpan StabilizationTime { get; set; }
    public string? Problem { get; set; }
    public List<CalibrationRawSample> StableSamples { get; set; } = new();

    [JsonIgnore]
    public string Identity => $"{SerialNumber}|{Channel}|{PeakId}";
}

public sealed class CalibrationPlateauResult
{
    public int PlateauIndex { get; set; }
    public double TargetTemperatureC { get; set; }
    public double ActualTemperatureC { get; set; }
    public double? ReferenceTemperatureC { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public List<CalibrationMeasurementResult> Targets { get; set; } = new();
}

public sealed class CalibrationWarning
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? PlateauIndex { get; set; }
    public string? SerialNumber { get; set; }
    public string? PeakId { get; set; }
    public bool Overridden { get; set; }
    public string? OverrideReason { get; set; }
}

public sealed class CalibrationRunRecord
{
    public Guid RunId { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public Guid ChamberId { get; set; }
    public string ChamberName { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public CalibrationRunState State { get; set; } = CalibrationRunState.Idle;
    public List<CalibrationPlateauResult> Plateaus { get; set; } = new();
    public List<CalibrationWarning> Warnings { get; set; } = new();
}

public sealed class CalibrationCheckpoint
{
    public Guid RunId { get; set; }
    public Guid ProfileId { get; set; }
    public Guid ChamberId { get; set; }
    public int CurrentPlateauIndex { get; set; }
    public double? CurrentTargetTemperatureC { get; set; }
    public CalibrationRunState State { get; set; }
    public List<CalibrationPlateauResult> CompletedPlateaus { get; set; } = new();
    public List<CalibrationSensorMapping> Mappings { get; set; } = new();
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
}

public sealed record CalibrationTargetProgress(
    string SerialNumber,
    string Channel,
    string PeakId,
    int PeakIndex,
    double? CurrentWavelengthNm,
    int StableSamples,
    int RequiredSamples,
    double? StandardDeviationPm,
    double? DriftPmPerMinute,
    TimeSpan Elapsed,
    TimeSpan Timeout,
    CalibrationTargetState State,
    string? Detail);

public sealed record CalibrationProgressSnapshot(
    CalibrationRunState State,
    int PlateauIndex,
    int PlateauCount,
    double TargetTemperatureC,
    double? ActualTemperatureC,
    double? ReferenceTemperatureC,
    int StableTargets,
    int TotalTargets,
    TimeSpan PlateauElapsed,
    IReadOnlyList<CalibrationTargetProgress> Targets,
    string Message);

public sealed class CalibrationOperatorActionRequiredException : Exception
{
    public CalibrationOperatorActionRequiredException(string message, CalibrationWarning warning)
        : base(message)
    {
        Warning = warning;
    }

    public CalibrationWarning Warning { get; }
}
