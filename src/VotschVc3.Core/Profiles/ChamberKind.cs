namespace VotschVc3.Core.Profiles;

/// <summary>Capabilities of a chamber, used to enable / disable humidity control.</summary>
public enum ChamberKind
{
    /// <summary>Temperature only chamber (e.g. VT3 series).</summary>
    TemperatureOnly,

    /// <summary>Temperature and humidity chamber (e.g. VC3 series).</summary>
    TemperatureHumidity,
}
