namespace VotschVc3.Core.Calibration;

/// <summary>
/// Abstraction for enriching calibration wiring rows with production/order metadata.
/// The current application keeps these fields editable by the operator; a future SQL
/// adapter can implement this interface without coupling calibration logic to a specific
/// database schema or server.
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
    string Customer,
    string Order,
    string? Notes = null);
