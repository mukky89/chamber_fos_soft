namespace VotschVc3.Core.Calibration;

/// <summary>
/// Abstraction for enriching calibration wiring rows with production/order metadata.
/// Chamber FOS obtains these values through the central Sylex FOS API and never connects
/// directly to ISYS/DBFOS.
/// </summary>
public interface IProductionMetadataProvider
{
    Task<ProductionMetadata?> FindAsync(
        string serialNumber,
        string channel,
        CancellationToken cancellationToken = default);
}

public sealed record ProductionMetadata(
    string ProductDescription,
    string SensorName,
    string Order,
    string? CustomerName = null,
    string? Notes = null);
