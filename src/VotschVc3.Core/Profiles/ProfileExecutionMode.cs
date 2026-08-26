namespace VotschVc3.Core.Profiles;

/// <summary>Defines how a stored profile is executed.</summary>
public enum ProfileExecutionMode
{
    /// <summary>Ordinary chamber profile; no PeakLogger dependency.</summary>
    Normal,

    /// <summary>FBG temperature calibration; selected hold segments act as calibration plateaus.</summary>
    TemperatureCalibration,
}
