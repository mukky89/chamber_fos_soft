namespace VotschVc3.Core.Profiles;

/// <summary>
/// Nameplate / type-plate details of a physical chamber (from the manufacturer's
/// rating label). Purely informational – shown in the device properties so the
/// data is kept together with the chamber configuration.
/// </summary>
public sealed class ChamberNameplate
{
    public string Manufacturer { get; set; } = "Vötsch Industrietechnik";
    public string Model { get; set; } = string.Empty;            // VC³ 7034
    public string SerialNumber { get; set; } = string.Empty;     // 58566126860010
    public string OrderNumber { get; set; } = string.Empty;      // 56612686
    public string YearOfConstruction { get; set; } = string.Empty;
    public string Refrigerant1 { get; set; } = string.Empty;     // R-404A · 2,5 kg
    public string Refrigerant2 { get; set; } = string.Empty;     // R-23 · 0,55 kg
    public string SupplyVoltage { get; set; } = string.Empty;    // 3/N/PE AC 400V±10% 50Hz
    public string NominalPower { get; set; } = string.Empty;     // 4,9 kW
    public string NominalCurrent { get; set; } = string.Empty;   // 16 A
    public string SystemNumber { get; set; } = string.Empty;     // 67624021
    public string FirstCalibration { get; set; } = string.Empty;
    public string NextCalibration { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    public ChamberNameplate Clone() => (ChamberNameplate)MemberwiseClone();
}
